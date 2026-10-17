using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAGradius.Engine;
using AVAGradius.Views;
using Controls = AVAGradius.Engine.Controls;

namespace AVAGradius.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GRADIUS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avagradius-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GRADIUS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAGradius-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.6);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 312400, 8);
        g.HighScores.Insert("AVA", 98100, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A stage flown by the computer pilot (with some arms, so there's something to see) until something worth showing.</summary>
    private static void Play(int stage, string name, Func<GameEngine, bool> moment, int seconds = 200, Action<Ship>? arm = null, int checkpoint = 0)
    {
        var (w, c, g) = Open();
        g.Go(stage, checkpoint: checkpoint);
        g.SetLives(20);
        arm?.Invoke(g.World.Ship);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static void Armed(Ship s)
    {
        s.Speed = 1;
        s.Missile = true;
        s.Laser = true;
        s.Drones = 2;
    }

    [AvaloniaFact]
    public void A_red_line() => Play(0, "red-line", g => g.World.Enemies.Count(e => e.Kind == Foe.Darter && e.Red && e.X < g.World.CamX + Field.Width - 40) >= 3 && g.World.Shots.Count >= 2);

    [AvaloniaFact]
    public void The_ridges() => Play(0, "ridges", g => g.World.CamX > 70 * Field.Tile && g.World.Enemies.Any(e => e.Kind == Foe.Walker) && g.World.Missiles.Count >= 1 && g.World.Ship.Drones >= 2,
        arm: s => { s.Speed = 1; s.Missile = true; s.Drones = 2; s.Gauge = 2; });

    [AvaloniaFact]
    public void A_volcano() => Play(0, "volcano", g => g.World.Enemies.Count(e => e.Kind == Foe.Boulder) >= 4 && g.World.Ship.Drones >= 1, arm: Armed, checkpoint: 122);

    [AvaloniaFact]
    public void The_Bastion() => Play(0, "bastion", g => g.World.Enemies.Any(e => e.Kind == Foe.Bastion && e.State == 1 && e.Plates < 4 && e.Plates > 0) && g.World.Missiles.Count(m => m.Length > 0) >= 2,
        arm: Armed, checkpoint: 202);

    [AvaloniaFact]
    public void The_glass_reef() => Play(1, "glass-reef", g => g.World.CamX > 22 * Field.Tile && g.World.Enemies.Any(e => e.Kind == Foe.Ring) && g.World.Ship.Gauge >= 0, arm: s => { Armed(s); s.Gauge = 3; s.Guard = 7; });

    [AvaloniaFact]
    public void The_hive() => Play(2, "hive", g => g.World.CamX > 88 * Field.Tile && g.World.Regrow.Count >= 2 && g.World.Missiles.Count >= 2, arm: Armed, checkpoint: 68);

    [AvaloniaFact]
    public void The_Queen() => Play(2, "queen", g => g.World.Enemies.Any(e => e.Kind == Foe.Queen && Sim.QueenOpen(e) && e.T % 6 > 4) && g.World.Missiles.Count >= 3, arm: Armed, checkpoint: 200);

    [AvaloniaFact]
    public void The_citadel() => Play(3, "citadel", g => g.World.CamX > 70 * Field.Tile && g.World.CamX < 100 * Field.Tile && g.World.Missiles.Count >= 1 && g.World.Enemies.Any(e => e.Kind is Foe.Turret or Foe.Hatch), arm: Armed, checkpoint: 59);

    [AvaloniaFact]
    public void The_Mind() => Play(3, "mind", g => g.World.Enemies.Any(e => e.Kind == Foe.Mind && e.Hp < 30) && g.World.Shots.Count >= 3, arm: Armed, checkpoint: 167);

    [AvaloniaFact]
    public void Stage_clear() => Play(0, "clear", g => g.Phase == Phase.Clear && g.PhaseTime > 1, arm: Armed, checkpoint: 202);
}
