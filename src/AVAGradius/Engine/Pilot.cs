namespace AVAGradius.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. Every few steps it tries each of the nine ways to
/// steer, each followed by the best of a few ways on, on copies of the world, firing all the while, and takes the
/// one that keeps the ship alive, clear of the rock and of what's flying, lined up with something to shoot, and
/// near the capsules. It takes the gauge's lit box when it's one it wants: two speed-ups, the missiles, the laser,
/// drones, and the guard.
/// </summary>
public sealed class Pilot
{
    private Controls _choice;
    private int _held;
    private static readonly (int Dx, int Dy)[] Moves = [(0, 0), (1, 0), (-1, 0), (0, -1), (0, 1), (1, -1), (1, 1), (-1, -1), (-1, 1)];
    private static readonly (int Dx, int Dy)[] Then = [(0, 0), (0, -1), (0, 1), (-1, 0)];
    public const int Hold = 8, Ahead = 26;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _held = 0;
        _choice = default;
        Doing = "";
    }

    /// <summary>Whether the pilot wants what the gauge has lit.</summary>
    public static bool Wants(Ship s)
    {
        if (s.Gauge < 0) return false;
        return (Power)s.Gauge switch
        {
            Power.Speed => s.Speed < 2,
            Power.Missile => !s.Missile,
            Power.Twin => false,
            Power.Laser => !s.Laser,
            Power.Drone => s.Drones < Ship.MaxDrones,
            _ => s.Guard <= 0,
        };
    }

    public Controls Decide(GameEngine g)
    {
        var w = g.World;
        if (g.Phase != Phase.Play || w.Ship.Dead) { _held = 0; return default; }
        bool power = Wants(w.Ship) && !w.Ship.PowerPrev;
        if (_held > 0)
        {
            _held--;
            return _choice with { Power = power, Fire = true };
        }
        double best = double.MinValue;
        var pick = (0, 0);
        foreach (var m in Moves)
        {
            double value = Try(w, m);
            if (value > best) { best = value; pick = m; }
        }
        _choice = new Controls(pick.Item1, pick.Item2, false, true);
        _held = 3;
        Doing = pick switch { (0, 0) => "hold", (1, 0) => "on", (-1, 0) => "back", (0, -1) => "up", (0, 1) => "down", (1, _) => "on and over", _ => "back and over" };
        return _choice with { Power = power };
    }

    private static double Try(World start, (int Dx, int Dy) m)
    {
        var w = start.Copy();
        Run(w, m, Hold);
        if (w.Ship.Dead) return Value(start, w);
        double best = double.MinValue;
        foreach (var next in Then)
        {
            var w2 = w.Copy();
            Run(w2, next, Ahead);
            best = Math.Max(best, Value(start, w2));
        }
        // Holding the same way on.
        var w3 = w.Copy();
        Run(w3, m, Ahead);
        return Math.Max(best, Value(start, w3));
    }

    private static void Run(World w, (int Dx, int Dy) m, int steps)
    {
        var c = new Controls(m.Dx, m.Dy, false, true);
        for (int i = 0; i < steps && !w.Ship.Dead; i++) Sim.Step(w, c);
    }

    private static double Value(World before, World after)
    {
        var s = after.Ship;
        if (s.Dead) return -1e6 + after.Clock * 1000;
        double v = (after.Score - before.Score) * 1.0;
        if (s.Gauge != before.Ship.Gauge) v += 2500;
        v -= (before.Ship.Guard - s.Guard) * 300;
        // Room above and below.
        v += Math.Min(Clearance(after, s.X, s.Y, -1), 100) * 2.5 + Math.Min(Clearance(after, s.X, s.Y, 1), 100) * 2.5;
        v += Math.Min(Clearance(after, s.X + 60, s.Y, -1), 80) + Math.Min(Clearance(after, s.X + 60, s.Y, 1), 80);
        // Clear of what's flying.
        foreach (var m in after.Missiles)
        {
            double d = m.Length > 0 ? Math.Abs(m.Y - s.Y) + Math.Max(0, m.X - s.X - 200) : Math.Sqrt((m.X - s.X) * (m.X - s.X) + (m.Y - s.Y) * (m.Y - s.Y));
            if (d < 80) v -= (80 - d) * 8;
        }
        Enemy? boss = null, target = null;
        double near = double.MaxValue;
        foreach (var e in after.Enemies)
        {
            if (e.Boss) { boss = e; continue; }
            double dx = e.X - s.X, dy = e.Y - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 90) v -= (90 - d) * 6;
            if (dx > 30 && dx < 650 && e.Kind is not Foe.Boulder && dx < near) { near = dx; target = e; }
        }
        double sx = s.X - after.CamX;
        if (boss != null)
        {
            // The fight at the end: in line with what can be hurt, at a distance.
            double aimY = boss.Y;
            v -= Math.Abs(s.Y - aimY) * (boss.Kind == Foe.Queen && !Sim.QueenOpen(boss) ? 0.5 : 4);
            v -= Math.Abs(sx - (boss.Kind == Foe.Mind ? 330 : 220)) * 1.5;
        }
        else
        {
            v -= Math.Abs(sx - 260) * 1.5;
            if (target != null) v -= Math.Min(Math.Abs(target.Y - s.Y), 220) * 0.8;
        }
        foreach (var cap in after.Capsules)
            v -= Math.Min(600, Math.Sqrt((cap.X - s.X) * (cap.X - s.X) + (cap.Y - s.Y) * (cap.Y - s.Y))) * 2;
        return v;
    }

    /// <summary>How far from a point to the rock (or anything solid) straight up or down, in pixels.</summary>
    private static double Clearance(World w, double x, double y, int dir)
    {
        for (double d = 0; d < 160; d += 8)
        {
            double yy = y + dir * (Ship.HalfHeight + d);
            if (yy < Field.Top || yy > Field.Bottom) return d;
            if (w.AtPoint(x, yy) != Tile.Empty || w.AtPoint(x + Ship.HalfWidth, yy) != Tile.Empty) return d;
        }
        return 160;
    }
}
