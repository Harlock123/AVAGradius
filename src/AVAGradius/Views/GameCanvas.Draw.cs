using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAGradius.Engine;
using Effect = AVAGradius.Engine.Effect;

namespace AVAGradius.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAGradius/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;

    /// <summary>Each stage's colours: the space behind, the rock, its lit edge, and a glow.</summary>
    private static readonly (uint Space, uint Haze, uint Rock, uint Edge, uint Glow)[] Themes =
    [
        (0xFF04040E, 0xFF2A0C10, 0xFF4A2418, 0xFFC0602A, 0xFFFF7A20),   // the fire ridges
        (0xFF020A12, 0xFF0A2A3A, 0xFF16303C, 0xFF5AC8D8, 0xFF9AF0FF),   // the glass reef
        (0xFF0A0410, 0xFF2A0A24, 0xFF4A1830, 0xFFC05A80, 0xFFFF80B0),   // the hive
        (0xFF04060C, 0xFF101828, 0xFF2E3644, 0xFF8A9AB0, 0xFFFFD040),   // the citadel
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame) DrawStage(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Bottom - 16), 24, Yellow, center: true);
    }

    // ───────────────────────────── the stage ─────────────────────────────

    private void DrawStage(DrawingContext dc)
    {
        var w = Engine.World;
        var th = Themes[w.Stage.Theme];
        using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Bottom - Field.Top)))
        {
            DrawSpace(dc, w.CamX, w.Stage.Theme, th);
            using (dc.PushTransform(Matrix.CreateTranslation(-Math.Round(w.CamX), 0)))
            {
                int c0 = (int)(w.CamX / T) - 1, c1 = c0 + Field.ViewCols + 3;
                for (int c = Math.Max(0, c0); c < Math.Min(w.Stage.Cols, c1); c++)
                for (int r = 0; r < Field.Rows; r++)
                    DrawTile(dc, w, c, r, th);
                foreach (var v in w.Stage.Volcanoes) DrawVolcano(dc, v.Col, v.Row);
                foreach (var cap in w.Capsules) DrawCapsule(dc, cap);
                foreach (var e in w.Enemies) DrawEnemy(dc, e, w);
                foreach (var m in w.Missiles) DrawMissile(dc, m);
                foreach (var s in w.Shots) DrawShot(dc, s);
                DrawShip(dc, w);
                foreach (var e in w.Effects) DrawEffect(dc, e);
            }
        }
    }

    /// <summary>Stars drifting at three depths, and something of each stronghold far off.</summary>
    private void DrawSpace(DrawingContext dc, double camX, int theme, (uint Space, uint Haze, uint Rock, uint Edge, uint Glow) th)
    {
        // Haze toward the ground and the roof, dark in the middle.
        for (int i = 0; i < 40; i++)
        {
            double f = Math.Pow(Math.Abs(i / 39.0 - 0.5) * 2, 2);
            uint a = th.Space, b = th.Haze;
            uint Mix(int shift) => (uint)((((a >> shift) & 0xFF) * (1 - f) + ((b >> shift) & 0xFF) * f)) << shift;
            dc.FillRectangle(Brush(0xFF000000 | Mix(16) | Mix(8) | Mix(0)), new Rect(0, Field.Top + i * 16, Field.Width, 17));
        }
        for (int layer = 0; layer < 3; layer++)
        {
            double speed = 0.08 + layer * 0.14;
            for (int i = 0; i < 40; i++)
            {
                uint h = Hash(i, layer, 7);
                double x = ((h % 1100) - camX * speed) % 1100;
                if (x < 0) x += 1100;
                double y = Field.Top + (h >> 12) % 640;
                double tw = 0.5 + 0.5 * Math.Sin(_clock * (2 + h % 5) + i);
                dc.FillRectangle(Brush(Alpha(layer == 2 ? 0xFFFFFFFF : 0xFFA0B0FF, 0.35 + layer * 0.25 + tw * 0.2)), new Rect(x - 20, y, 1 + layer, 1 + layer));
            }
        }
        double far = -camX * 0.12;
        switch (theme)
        {
            case 0:
                // A red giant sun, low.
                dc.FillEllipse(760, 560, 120, 0x40FF4020);
                dc.FillEllipse(760, 560, 80, 0x60FF6030);
                break;
            case 1:
                for (int i = 0; i < 5; i++)
                {
                    double x = ((far * 2 + i * 260) % 1300 + 1300) % 1300 - 140;
                    Poly(dc, 0x2060E0FF, null, 0, new Point(x, Field.Bottom), new Point(x + 40, Field.Top + 200 + i * 30), new Point(x + 80, Field.Bottom));
                }
                break;
            case 2:
                for (int i = 0; i < 6; i++)
                {
                    double x = ((far * 1.5 + i * 220) % 1320 + 1320) % 1320 - 150;
                    dc.FillEllipse(x, Field.Top + 120 + (Hash(i, 2, 2) % 400), 60 + Hash(i, 3, 3) % 50, 0x30C04080);
                }
                break;
            case 3:
                for (int i = 0; i < 14; i++)
                {
                    double x = ((far * 2 + i * 80) % 1120 + 1120) % 1120 - 40;
                    dc.DrawLine(Pen(0x203A5A8A, 2), new Point(x, Field.Top), new Point(x, Field.Bottom));
                }
                for (int j = 0; j < 8; j++) dc.DrawLine(Pen(0x183A5A8A, 2), new Point(0, Field.Top + 40 + j * 80), new Point(Field.Width, Field.Top + 40 + j * 80));
                break;
        }
    }

    private void DrawTile(DrawingContext dc, World w, int c, int r, (uint Space, uint Haze, uint Rock, uint Edge, uint Glow) th)
    {
        var t = w.At(c, r);
        if (t == Tile.Empty) return;
        double x = c * T, y = Field.Top + r * T;
        uint h = Hash(c, r, w.Stage.Theme);
        switch (t)
        {
            case Tile.Rock:
            {
                dc.FillRectangle(Brush(th.Rock), new Rect(x, y, T + 0.5, T + 0.5));
                if (w.Stage.Theme == 3)
                {
                    // Steel panels and rivets.
                    dc.DrawRectangle(null, Pen(0xFF20262E, 2), new Rect(x + 1, y + 1, T - 2, T - 2));
                    dc.FillEllipse(x + 5, y + 5, 1.5, 0xFF6A7488);
                    dc.FillEllipse(x + T - 5, y + T - 5, 1.5, 0xFF6A7488);
                }
                else
                {
                    for (int k = 0; k < 3; k++)
                    {
                        uint hk = h >> (k * 8);
                        dc.FillEllipse(x + 4 + hk % 24, y + 4 + (hk >> 4) % 24, 2 + hk % 3, Alpha(0xFF000000, 0.22));
                    }
                }
                // The lit edge where it meets the open.
                bool up = w.At(c, r - 1) == Tile.Empty && r > 0, down = w.At(c, r + 1) == Tile.Empty && r < Field.Rows - 1;
                bool left = w.At(c - 1, r) == Tile.Empty, right = w.At(c + 1, r) == Tile.Empty;
                if (up) dc.FillRectangle(Brush(th.Edge), new Rect(x, y, T + 0.5, 4));
                if (down) dc.FillRectangle(Brush(th.Edge), new Rect(x, y + T - 4, T + 0.5, 4));
                if (left) dc.FillRectangle(Brush(Alpha(th.Edge, 0.7)), new Rect(x, y, 3, T));
                if (right) dc.FillRectangle(Brush(Alpha(th.Edge, 0.7)), new Rect(x + T - 3, y, 3, T));
                if (w.Stage.Theme == 0 && up && h % 5 == 0) dc.FillEllipse(x + 8 + h % 16, y + 2, 3, 0xFFFFA040);
                if (w.Stage.Theme == 2 && (up || down) && h % 3 == 0) dc.FillEllipse(x + 6 + h % 20, up ? y + 3 : y + T - 3, 4, 0xFFE070A0);
                break;
            }
            case Tile.Crystal when w.Stage.Theme == 3:
                // A shutter: hazard stripes.
                dc.FillRectangle(Brush(0xFF2A2A1A), new Rect(x + 2, y, T - 4, T + 0.5));
                for (int k = 0; k < 4; k++)
                    Poly(dc, 0xFFE0B020, null, 0, new Point(x + 2, y + k * 8), new Point(x + 2, y + k * 8 + 4), new Point(x + T - 2, y + k * 8 + 8), new Point(x + T - 2, y + k * 8 + 4));
                dc.DrawRectangle(null, Pen(0xFF8A7A30, 2), new Rect(x + 2, y, T - 4, T));
                break;
            case Tile.Crystal:
            {
                double gl = 0.55 + 0.25 * Math.Sin(_clock * 3 + c * 0.7 + r);
                Poly(dc, Alpha(0xFF60D8F0, gl), 0xFFC0F8FF, 1.5, new Point(x + T / 2, y + 1), new Point(x + T - 2, y + T * 0.35), new Point(x + T - 3, y + T - 2), new Point(x + 3, y + T - 2), new Point(x + 2, y + T * 0.35));
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.6), 2), new Point(x + 9, y + 9), new Point(x + 13, y + 22));
                break;
            }
            case Tile.Cell:
            {
                double pulse = 1 + 0.06 * Math.Sin(_clock * 4 + h % 7);
                dc.FillEllipse(x + T / 2, y + T / 2, 15 * pulse, 0xFFB04A78);
                dc.FillEllipse(x + T / 2 - 3, y + T / 2 - 3, 8 * pulse, 0xFFE07AA8);
                dc.FillEllipse(x + T / 2 + 4, y + T / 2 + 3, 3, 0xFF5A1A38);
                break;
            }
        }
    }

    private void DrawVolcano(DrawingContext dc, int col, int row)
    {
        double x = (col + 0.5) * T, y = Field.Top + row * T;
        double flick = 0.6 + 0.4 * Math.Sin(_clock * 9 + col);
        dc.DrawEllipse(Brush(Alpha(0xFFFF6020, 0.5 * flick)), null, new Point(x, y), 30, 12);
        dc.DrawEllipse(Brush(Alpha(0xFFFFC040, 0.8 * flick)), null, new Point(x, y + 2), 14, 5);
    }

    // ───────────────────────────── the Halcyon ─────────────────────────────

    private void DrawShip(DrawingContext dc, World w)
    {
        var s = w.Ship;
        if (s.Dead) return;
        // The drones, furthest first.
        for (int i = s.Drones - 1; i >= 0; i--)
        {
            var (dx, dy) = s.Drone(i);
            DrawDrone(dc, dx + w.CamX, dy, i);
        }
        if (s.Safe > 0 && (int)(_clock * 14) % 2 == 0) return;
        DrawHalcyon(dc, s.X, s.Y, s.Bank, s.Guard);
    }

    private void DrawHalcyon(DrawingContext dc, double x, double y, double bank, int guard = 0)
    {
        double b = bank * 3;
        // The engine's flame.
        double fl = 10 + 6 * Math.Sin(_clock * 40);
        Poly(dc, 0xFFFF8A20, null, 0, new Point(x - 18, y - 4), new Point(x - 18 - fl, y), new Point(x - 18, y + 4));
        Poly(dc, 0xFFFFF0A0, null, 0, new Point(x - 18, y - 2), new Point(x - 18 - fl * 0.5, y), new Point(x - 18, y + 2));
        // The wings, swept back; the near one leans as it climbs or dives.
        Poly(dc, 0xFF4A6AB0, 0xFF203060, 1, new Point(x - 4, y - 3), new Point(x - 16, y - 16 + b), new Point(x - 10, y - 16 + b), new Point(x + 6, y - 3));
        Poly(dc, 0xFF4A6AB0, 0xFF203060, 1, new Point(x - 4, y + 3), new Point(x - 16, y + 16 + b), new Point(x - 10, y + 16 + b), new Point(x + 6, y + 3));
        // The hull, long and pointed.
        Poly(dc, 0xFFE8ECF8, 0xFF5A6A8A, 1.5, new Point(x + 24, y), new Point(x + 6, y - 6), new Point(x - 18, y - 6), new Point(x - 20, y), new Point(x - 18, y + 6), new Point(x + 6, y + 6));
        dc.FillRectangle(Brush(0xFFE04030), new Rect(x - 14, y - 1.5, 22, 3));
        // The canopy.
        dc.DrawEllipse(Brush(0xFF40C8FF), Pen(0xFF205A8A, 1), new Point(x + 2, y - 4), 7, 3.5);
        if (guard > 0)
        {
            double a = 0.35 + guard * 0.05 + 0.15 * Math.Sin(_clock * 12);
            dc.DrawEllipse(null, Pen(Alpha(0xFF60C0FF, a), 3), new Point(x + 26, y), 9, 22);
            dc.DrawEllipse(null, Pen(Alpha(0xFFC0F0FF, a), 1.5), new Point(x + 26, y), 6, 18);
        }
    }

    private void DrawDrone(DrawingContext dc, double x, double y, int i)
    {
        double p = 1 + 0.15 * Math.Sin(_clock * 10 + i);
        dc.FillEllipse(x, y, 13 * p, 0x50FF6010);
        dc.FillEllipse(x, y, 9, 0xFFFF7A20);
        dc.FillEllipse(x - 2, y - 2, 4.5, 0xFFFFE0A0);
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        switch (s.Kind)
        {
            case ShotKind.Bullet:
                dc.FillRectangle(Brush(0xFFFFF0A0), new Rect(s.X - 9, s.Y - 2, 18, 4), 2);
                break;
            case ShotKind.Twin:
                dc.DrawLine(Pen(0xFFFFE070, 4), new Point(s.X - 6, s.Y + 6), new Point(s.X + 6, s.Y - 6));
                break;
            case ShotKind.Laser:
                dc.FillRectangle(Brush(0x6040E0FF), new Rect(s.X - s.Length, s.Y - 5, s.Length, 10), 4);
                dc.FillRectangle(Brush(0xFF80F0FF), new Rect(s.X - s.Length, s.Y - 2.5, s.Length, 5), 2);
                dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(s.X - s.Length, s.Y - 1, s.Length, 2));
                break;
            case ShotKind.Missile:
            {
                double a = s.Running ? 0 : Math.Atan2(s.Vy, s.Vx);
                using (dc.PushTransform(Matrix.CreateRotation(a) * Matrix.CreateTranslation(s.X, s.Y)))
                {
                    dc.FillRectangle(Brush(0xFFE04040), new Rect(-7, -2.5, 12, 5), 2);
                    Poly(dc, 0xFFFFFFFF, null, 0, new Point(5, -2.5), new Point(9, 0), new Point(5, 2.5));
                    dc.FillEllipse(-9, 0, 2.5 + Math.Sin(_clock * 50), 0xFFFFC040);
                }
                break;
            }
        }
    }

    private void DrawMissile(DrawingContext dc, Missile m)
    {
        if (m.Length > 0)
        {
            dc.FillRectangle(Brush(0x70FF40C0), new Rect(m.X, m.Y - 5, m.Length, 10), 5);
            dc.FillRectangle(Brush(0xFFFFB0F0), new Rect(m.X, m.Y - 2, m.Length, 4), 2);
            return;
        }
        double p = 4.5 + Math.Sin(_clock * 20 + m.X) * 1.2;
        dc.FillEllipse(m.X, m.Y, p + 2, 0x60FF6080);
        dc.FillEllipse(m.X, m.Y, p, 0xFFFF7A9A);
        dc.FillEllipse(m.X, m.Y, 2, 0xFFFFFFFF);
    }

    private void DrawCapsule(DrawingContext dc, Capsule c)
    {
        double glow = 0.5 + 0.5 * Math.Sin(_clock * 8);
        dc.DrawEllipse(Brush(Alpha(0xFFFF6020, 0.3 + glow * 0.3)), null, new Point(c.X, c.Y), 22, 15);
        dc.DrawRectangle(Brush(0xFFE03020), Pen(0xFFFFC080, 2), new Rect(c.X - 14, c.Y - 9, 28, 18), 9, 9);
        dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.5)), new Rect(c.X - 8, c.Y - 6, 16, 3), 1.5f);
    }

    // ───────────────────────────── the Varkh ─────────────────────────────

    private void DrawEnemy(DrawingContext dc, Enemy e, World w)
    {
        double x = e.X, y = e.Y;
        bool flash = e.Hurt > 0 && (int)(_clock * 30) % 2 == 0;
        uint W(uint c) => flash ? 0xFFFFFFFF : c;
        double flip = e.Roof ? -1 : 1;
        switch (e.Kind)
        {
            case Foe.Darter:
            {
                double a = Math.Atan2(e.Pattern == 1 ? 64 * 3.2 * Math.Cos(e.T * 3.2) : e.Vy, e.Vx == 0 ? -1 : e.Vx);
                uint body = e.Red ? 0xFFE04A30 : 0xFF5A8AE0, trim = e.Red ? 0xFFFFC060 : 0xFFC0E0FF;
                using (dc.PushTransform(Matrix.CreateRotation(a) * Matrix.CreateTranslation(x, y)))
                {
                    Poly(dc, W(body), 0xFF101828, 1.2, new Point(16, 0), new Point(-10, -11), new Point(-4, 0), new Point(-10, 11));
                    dc.FillEllipse(4, 0, 3.5, W(trim));
                }
                break;
            }
            case Foe.Spinner:
            {
                uint body = e.Red ? 0xFFE04A30 : 0xFF9A6AD0;
                using (dc.PushTransform(Matrix.CreateRotation(e.T * 8) * Matrix.CreateTranslation(x, y)))
                {
                    for (int k = 0; k < 4; k++)
                    {
                        double a = k * Math.PI / 2;
                        Poly(dc, W(body), null, 0, new Point(0, 0), new Point(Math.Cos(a) * 16, Math.Sin(a) * 16), new Point(Math.Cos(a + 0.6) * 10, Math.Sin(a + 0.6) * 10));
                    }
                }
                dc.FillEllipse(x, y, 6, W(e.Red ? 0xFFFFD060 : 0xFFE0D0FF));
                break;
            }
            case Foe.Hatch:
            {
                double open = e.FireIn < 0.6 ? 1 - e.FireIn / 0.6 : 0.15;
                double baseY = y + 18 * flip;
                dc.DrawEllipse(Brush(W(0xFF5A6A7A)), Pen(0xFF20262E, 2), new Point(x, baseY), 32, 22);
                dc.FillRectangle(Brush(0xFF2A3038), new Rect(x - 34, e.Roof ? baseY - 4 : baseY, 68, 4));
                dc.DrawEllipse(Brush(0xFF100808), null, new Point(x, baseY - 8 * flip), 18, 8 * open + 1);
                dc.FillEllipse(x, baseY - 8 * flip, 4 * open, 0xFFFF6040);
                break;
            }
            case Foe.Walker:
            {
                double sw = Math.Sin(e.T * 9) * 6;
                dc.DrawLine(Pen(W(0xFF8A9A6A), 4), new Point(x - 4, y), new Point(x - 8 + sw, y + 16 * flip));
                dc.DrawLine(Pen(W(0xFF8A9A6A), 4), new Point(x + 4, y), new Point(x + 8 - sw, y + 16 * flip));
                dc.DrawRectangle(Brush(W(0xFF6A7A4A)), Pen(0xFF2A3020, 1.5), new Rect(x - 13, e.Roof ? y - 2 : y - 14, 26, 14), 4, 4);
                double gx = w.Ship.X - x, gy = w.Ship.Y - y, d = Math.Max(1, Math.Sqrt(gx * gx + gy * gy));
                dc.DrawLine(Pen(W(0xFF3A4428), 4), new Point(x, y - 7 * flip), new Point(x + gx / d * 18, y - 7 * flip + gy / d * 18));
                dc.FillEllipse(x + 5, y - 9 * flip, 2.5, 0xFFFF4030);
                break;
            }
            case Foe.Turret:
            {
                double baseY = y + 14 * flip;
                double gx = w.Ship.X - x, gy = w.Ship.Y - y, d = Math.Max(1, Math.Sqrt(gx * gx + gy * gy));
                dc.DrawLine(Pen(W(0xFF2A3040), 6), new Point(x, y), new Point(x + gx / d * 22, y + gy / d * 22));
                dc.DrawEllipse(Brush(W(0xFF8A7A9A)), Pen(0xFF2A2030, 1.5), new Point(x, y + 2 * flip), 14, 12);
                dc.FillRectangle(Brush(W(0xFF5A4A6A)), new Rect(x - 18, e.Roof ? baseY - 14 : baseY - 6, 36, 6), 2);
                dc.FillEllipse(x, y, 3.5, 0xFFFFD040);
                break;
            }
            case Foe.Boulder:
            {
                using (dc.PushTransform(Matrix.CreateRotation(e.T * 4) * Matrix.CreateTranslation(x, y)))
                {
                    Poly(dc, W(0xFF6A3A20), 0xFFFF8030, 2, new Point(14, 0), new Point(8, -12), new Point(-6, -14), new Point(-14, -2), new Point(-10, 10), new Point(4, 13));
                    dc.FillEllipse(-3, -2, 4, 0xFFFFA040);
                }
                break;
            }
            case Foe.Sentinel:
            {
                double gl = 0.6 + 0.4 * Math.Sin(_clock * 4 + x);
                double tip = y - 40 * flip, foot = y + 40 * flip;
                Poly(dc, W(Alpha(0xFF50C8E8, 0.85)), 0xFFC0F8FF, 2, new Point(x, tip - 6 * flip), new Point(x + 16, tip + 18 * flip), new Point(x + 14, foot), new Point(x - 14, foot), new Point(x - 16, tip + 18 * flip));
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, gl), 3), new Point(x - 4, tip + 14 * flip), new Point(x - 6, foot - 10 * flip));
                dc.FillEllipse(x, tip + 14 * flip, 6 * gl + 2, Alpha(0xFFFFFFFF, gl));
                break;
            }
            case Foe.Ring:
            {
                double sq = Math.Abs(Math.Sin(e.T * 5));
                dc.DrawEllipse(null, Pen(W(0xFF80F0FF), 4), new Point(x, y), 12 * (0.3 + 0.7 * sq), 12);
                dc.DrawEllipse(null, Pen(Alpha(0xFFFFFFFF, 0.6), 1.5), new Point(x, y), 12 * (0.3 + 0.7 * sq), 12);
                break;
            }
            case Foe.Polyp:
            {
                double open = e.FireIn < 0.5 ? 1 - e.FireIn / 0.5 : 0.1;
                dc.DrawEllipse(Brush(W(0xFFC0508A)), Pen(0xFF5A1A38, 2), new Point(x, y + 4 * flip), 22, 16);
                dc.FillEllipse(x - 8, y + 2 * flip, 5, W(0xFFE080B0));
                dc.DrawEllipse(Brush(0xFF2A0812), null, new Point(x, y - 8 * flip), 9, 3 + 6 * open);
                break;
            }
            case Foe.Spore:
                dc.FillEllipse(x, y, 9, W(0xFF80D040));
                dc.FillEllipse(x - 2, y - 2, 4, W(0xFFD0FF90));
                break;
            case Foe.Bastion: DrawBastion(dc, e, W); break;
            case Foe.Queen: DrawQueen(dc, e, W); break;
            case Foe.Mind: DrawMind(dc, e, W); break;
        }
    }

    private void DrawBastion(DrawingContext dc, Enemy e, Func<uint, uint> W)
    {
        double x = e.X, y = e.Y;
        uint hull = e.Level > 1 ? 0xFF6A5A8A : 0xFF5A6A8A, dark = 0xFF2A3044;
        // The two arms carrying the beam guns, above and below.
        foreach (double s in new[] { -1.0, 1.0 })
        {
            Poly(dc, W(hull), dark, 2, new Point(x - 110, y + s * 46), new Point(x - 60, y + s * 30), new Point(x + 80, y + s * 30), new Point(x + 110, y + s * 70), new Point(x - 40, y + s * 70), new Point(x - 70, y + s * 84));
            dc.FillRectangle(Brush(0xFF1A1E2A), new Rect(x - 104, y + s * 46 - 4, 20, 8), 3);
            dc.FillRectangle(Brush(0xFF1A1E2A), new Rect(x - 58, y + s * 76 - 4, 20, 8), 3);
            bool charge = e.FireIn < 0.35 && e.State == 1;
            if (charge)
            {
                dc.FillEllipse(x - 102, y + s * 46, 6, 0xFFFF80E0);
                dc.FillEllipse(x - 56, y + s * 76, 6, 0xFFFF80E0);
            }
        }
        // The body, with the channel to the core.
        dc.DrawRectangle(Brush(W(0xFF7A8AA8)), Pen(dark, 2), new Rect(x - 70, y - 34, 170, 68), 10, 10);
        dc.FillRectangle(Brush(0xFF0A0C14), new Rect(x - 112, y - 13, 80, 26));
        // The core, glowing blue (red when bare).
        double gl = 0.7 + 0.3 * Math.Sin(_clock * 6);
        uint core = e.Plates == 0 ? 0xFFFF5040 : 0xFF40A0FF;
        dc.FillEllipse(x - 30, y, 20, Alpha(core, 0.35 * gl));
        dc.FillEllipse(x - 30, y, 13, W(core));
        dc.FillEllipse(x - 33, y - 3, 5, 0xFFFFFFFF);
        // The plates still standing in the channel.
        for (int i = 0; i < e.Plates; i++)
        {
            double px = x - 52 - i * 9;
            dc.FillRectangle(Brush(i == e.Plates - 1 && e.PlateHp < Sim.PlateHp ? 0xFFB0C8E0 : 0xFF8AA0C0), new Rect(px - 3, y - 12, 6, 24), 2);
        }
        // Engines.
        for (int k = -1; k <= 1; k += 2)
            Poly(dc, 0xFFFF8A20, null, 0, new Point(x + 100, y + k * 16 - 6), new Point(x + 120 + 8 * Math.Sin(_clock * 30), y + k * 16), new Point(x + 100, y + k * 16 + 6));
    }

    private void DrawQueen(DrawingContext dc, Enemy e, Func<uint, uint> W)
    {
        double x = e.X, y = e.Y;
        // The mass, pulsing.
        double p = 1 + 0.04 * Math.Sin(_clock * 3);
        dc.DrawEllipse(Brush(W(0xFF8A2A5A)), Pen(0xFF3A0A20, 3), new Point(x + 10, y), 100 * p, 124 * p);
        for (int i = 0; i < 7; i++)
        {
            double a = i * 0.9 + _clock * 0.3;
            dc.FillEllipse(x + 30 + Math.Cos(a) * 60, y + Math.Sin(a) * 90, 14, W(0xFFB04A80));
        }
        // The eye: open, it can be hurt.
        bool open = Sim.QueenOpen(e);
        double opening = e.State != 1 ? 0 : open ? Math.Min(1, (e.T % 6 - 3) * 4) : Math.Max(0, 1 - (e.T % 6) * 4);
        dc.DrawEllipse(Brush(0xFF3A0A1A), Pen(0xFF2A0612, 3), new Point(x - 60, y), 38, 34);
        if (opening > 0.05)
        {
            dc.DrawEllipse(Brush(W(0xFFF0F0D0)), null, new Point(x - 60, y), 34, 30 * opening);
            dc.FillEllipse(x - 68, y, 13 * Math.Min(1, opening * 1.5), 0xFFE03020);
            dc.FillEllipse(x - 68, y, 6 * Math.Min(1, opening * 1.5), 0xFF100000);
        }
        else dc.DrawLine(Pen(0xFF5A1A30, 3), new Point(x - 94, y), new Point(x - 26, y));
    }

    private void DrawMind(DrawingContext dc, Enemy e, Func<uint, uint> W)
    {
        double x = e.X, y = e.Y;
        double p = 1 + 0.06 * Math.Sin(_clock * 5);
        dc.FillEllipse(x, y, 70 * p, 0x40FF4060);
        dc.DrawEllipse(Brush(W(0xFFD05A8A)), Pen(0xFF5A1A38, 3), new Point(x, y), 52 * p, 48 * p);
        // The folds.
        for (int i = 0; i < 5; i++)
        {
            double yy = y - 32 + i * 16;
            dc.DrawLine(Pen(0xFF8A2A5A, 3), new Point(x - 36 + (i % 2) * 6, yy), new Point(x + 34 - (i % 2) * 8, yy + Math.Sin(_clock * 2 + i) * 4));
        }
        dc.DrawLine(Pen(0xFF6A1A40, 3), new Point(x, y - 48), new Point(x, y + 48));
        // Its cables, into the walls.
        for (int k = -1; k <= 1; k += 2)
            dc.DrawLine(Pen(0xFF4A5468, 8), new Point(x + 30, y + k * 34), new Point(x + 120, y + k * 140));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double a = 1 - e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 40), 22, Alpha(e.Colour, a), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 30), 26, Alpha(e.Colour, Math.Min(1, a * 2)), center: true);
                break;
            case EffectKind.Burst:
            {
                int n = int.TryParse(e.Text, out int v) ? v : 10;
                dc.FillEllipse(e.X, e.Y, 18 * (1 - a) + 4, Alpha(0xFFFFFFFF, a * 0.6));
                for (int i = 0; i < n; i++)
                {
                    double ang = i * Math.PI * 2 / n + i, r = 6 + e.Age * 140;
                    dc.FillEllipse(e.X + Math.Cos(ang) * r, e.Y + Math.Sin(ang) * r, 3.5 * a + 1, Alpha(e.Colour, a));
                }
                break;
            }
            case EffectKind.Blast:
            {
                double r = 10 + e.Age * 120;
                dc.FillEllipse(e.X, e.Y, r * 0.6, Alpha(0xFFFFF0C0, a * 0.8));
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, a), 6 * a + 1), new Point(e.X, e.Y), r, r);
                for (int i = 0; i < 14; i++)
                {
                    double ang = i * 0.45 + e.X, rr = r * (0.6 + (i % 3) * 0.25);
                    dc.FillEllipse(e.X + Math.Cos(ang) * rr, e.Y + Math.Sin(ang) * rr, 4 * a + 1, Alpha(0xFFFF8030, a));
                }
                break;
            }
            case EffectKind.Spark:
                dc.FillEllipse(e.X, e.Y, 10 * a + 2, Alpha(e.Colour, a));
                break;
        }
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private static readonly string[] GaugeNames = ["SPEED", "MISSILE", "TWIN", "LASER", "DRONE", "GUARD"];

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, Field.Bottom, Field.Width, Field.Height - Field.Bottom));
        dc.DrawLine(Pen(0xFF2A3A6A, 2), new Point(0, Field.Hud), new Point(Field.Width, Field.Hud));
        Text(dc, "1UP", new Point(20, 24), 22, Red);
        Text(dc, g.Score.ToString("0000000"), new Point(70, 24), 32, White);
        Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("0000000"), new Point(Field.Width / 2, 24), 28, Yellow, center: true);
        if (!InGame) return;
        dc.DrawLine(Pen(0xFF2A3A6A, 2), new Point(0, Field.Bottom), new Point(Field.Width, Field.Bottom));
        Text(dc, w.Stage.Name, new Point(Field.Width - 20, 24), 22, 0xFF9AB0E0, right: true);
        // The ships left.
        for (int i = 0; i < Math.Min(g.Lives - 1, 6); i++)
        {
            using (dc.PushTransform(Matrix.CreateScale(0.6, 0.6) * Matrix.CreateTranslation(30 + i * 30, Field.Bottom + 40)))
                DrawHalcyon(dc, 0, 0, 0);
        }
        // The power gauge.
        var s = w.Ship;
        double bx = 230, by = Field.Bottom + 18, bw = 118, bh = 40;
        for (int i = 0; i < 6; i++)
        {
            bool lit = s.Gauge == i, can = s.CanTake((Power)i);
            double x = bx + i * (bw + 6);
            uint fill = lit ? (can ? 0xFFE0A020 : 0xFF6A5A3A) : 0xFF141A30;
            dc.DrawRectangle(Brush(fill), Pen(lit ? 0xFFFFF0A0 : 0xFF3A4A8A, 2), new Rect(x, by, bw, bh), 4, 4);
            string label = GaugeNames[i];
            if (i == 0 && s.Speed > 0) label += " " + new string('+', s.Speed);
            if (i == 4 && s.Drones > 0) label += " " + s.Drones;
            uint tc = lit ? 0xFF101010 : !can ? 0xFF4A5A80 : 0xFFB0C8FF;
            Text(dc, label, new Point(x + bw / 2, by + bh / 2), 24, tc, center: true, shadow: false);
        }
        if (s.Guard > 0)
        {
            dc.FillRectangle(Brush(0xFF203060), new Rect(bx, by + bh + 6, 6 * (bw + 6) - 6, 5));
            dc.FillRectangle(Brush(0xFF60C0FF), new Rect(bx, by + bh + 6, (6 * (bw + 6) - 6) * s.Guard / Ship.GuardHits, 5));
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 230;
        dc.FillRectangle(Brush(0xA0000000), new Rect(cx - 400, cy - 52, 800, 108), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 18 ? 48 : 60, 0xFFFFC040, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawSpace(dc, _clock * 120, 0, Themes[0]);
        // The Halcyon and two drones, weaving across.
        double sx = (_clock * 160) % 1300 - 120, sy = 600 + Math.Sin(_clock * 1.6) * 40;
        DrawDrone(dc, sx - 50, 600 + Math.Sin((_clock - 0.25) * 1.6) * 40, 0);
        DrawDrone(dc, sx - 96, 600 + Math.Sin((_clock - 0.5) * 1.6) * 40, 1);
        DrawHalcyon(dc, sx, sy, Math.Cos(_clock * 1.6) * 0.6);
        string title = "AVA GRADIUS";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 32 + 32 + i * 64;
            double ty = 160 + Math.Sin(_clock * 2.4 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(tx + 3, ty + 3), 120, 0xFF401010, center: true, shadow: false);
            Text(dc, title[i].ToString(), new Point(tx, ty), 120, i < 3 ? Cyan : (i % 2 == 0 ? 0xFFFF6A30 : 0xFFFFB040), center: true, shadow: false);
        }
        Text(dc, "AFTER KONAMI'S 1985 ARCADE GAME", new Point(cx, 236), 24, Dim, center: true);
        Text(dc, "THE VARKH ARMADA HAS RISEN FROM THE DEEP STARS.", new Point(cx, 300), 28, White, center: true);
        Text(dc, "FLY THE HALCYON THROUGH FOUR OF THEIR STRONGHOLDS AND DESTROY THE MIND.", new Point(cx, 332), 28, White, center: true);
        Text(dc, "SHOOT DOWN RED FLIERS (OR A WHOLE RED LINE) FOR CAPSULES. EACH LIGHTS THE NEXT", new Point(cx, 380), 24, 0xFFFFA070, center: true);
        Text(dc, "BOX OF THE GAUGE: SPEED, MISSILE, TWIN, LASER, DRONE, GUARD. TAKE IT WITH Z.", new Point(cx, 406), 24, 0xFFFFA070, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO LAUNCH", new Point(cx, 476), 44, Yellow, center: true);
        Text(dc, "ARROWS: FLY    X OR CTRL: FIRE    Z OR SPACE: POWER UP    P: PAUSE    M: SOUND", new Point(cx, 530), 20, 0xFFB0A0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawSpace(dc, _clock * 60, 3, Themes[3]);
        Text(dc, "THE ACE PILOTS", new Point(cx, 120), 64, Cyan, center: true);
        Text(dc, "RANK NAME     SCORE  STAGES", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawSpace(dc, _clock * 60, 3, Themes[3]);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
