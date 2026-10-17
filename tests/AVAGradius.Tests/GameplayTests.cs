using Avalonia.Input;
using AVAGradius.Engine;

namespace AVAGradius.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avagradius-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A stage at a column with nothing coming (no lines, no ground guns, no volcanoes), and the ship safe to hit.</summary>
    private static World Empty(int stage = 0, int col = 0)
    {
        var w = new World();
        Sim.Start(w, stage, 1, col, null, 1);
        w.NextSpawn = w.Stage.Spawns.Count;
        w.VolcanoIn = 1e9;
        w.Ship.Safe = 0;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static Enemy Put(World w, Foe kind, double dx, double dy, int hp = 1)
    {
        var e = new Enemy { Kind = kind, X = w.Ship.X + dx, Y = w.Ship.Y + dy, Hp = hp, Id = w.NextId++, Entered = true, FireIn = 99 };
        w.Enemies.Add(e);
        return e;
    }

    private static readonly Controls Fire = new(0, 0, false, true);

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void The_stage_goes_by_and_carries_the_ship_and_the_ship_flies_within_the_screen()
    {
        var w = Empty();
        double x = w.Ship.X, cam = w.CamX;
        Run(w, 60);
        Assert.Equal(Field.Scroll, w.CamX - cam, 1);
        Assert.Equal(x - cam, w.Ship.X - w.CamX, 1);
        Run(w, 60, new Controls(1, -1));
        Assert.True(w.Ship.X - w.CamX > x - cam + 150);
        Run(w, 600, new Controls(1, -1));
        Assert.Equal(Field.Top + 10, w.Ship.Y);
        Assert.True(w.Ship.X < w.CamX + Field.Width - 50);
    }

    [Fact]
    public void Each_speed_up_makes_the_ship_quicker()
    {
        var w = Empty();
        double y = w.Ship.Y;
        Run(w, 30, new Controls(0, 1));
        double slow = w.Ship.Y - y;
        w = Empty();
        w.Ship.Speed = 2;
        Run(w, 30, new Controls(0, 1));
        Assert.True(w.Ship.Y - y > slow * 1.5);
    }

    [Fact]
    public void Touching_the_rock_is_the_end_of_the_ship()
    {
        var w = Empty(0, 64);
        int col = (int)(w.Ship.X / Field.Tile);
        Run(w, 200, new Controls(0, 1));
        Assert.True(w.Ship.Dead);
        Assert.Equal("the rock", w.Cause);
        Assert.True(w.Ship.Y > w.Stage.FloorY(col) - 40);
    }

    // ───────────────────────────── the guns ─────────────────────────────

    [Fact]
    public void Held_fire_streams_shots_but_only_two_at_a_time_and_they_bring_things_down()
    {
        var w = Empty();
        Run(w, 40, Fire);
        Assert.InRange(w.Shots.Count(s => s.Kind == ShotKind.Bullet), 1, 2);
        var e = Put(w, Foe.Darter, 300, 0);
        Run(w, 90, Fire);
        Assert.True(e.Dead);
        Assert.Equal(100, w.Score);
    }

    [Fact]
    public void The_laser_goes_through_one_thing_into_the_next()
    {
        var w = Empty();
        w.Ship.Laser = true;
        var a = Put(w, Foe.Darter, 200, 0);
        var b = Put(w, Foe.Darter, 260, 0);
        for (int i = 0; i < 12 && !(a.Dead && b.Dead); i++) Sim.Step(w, Fire);
        Assert.True(a.Dead && b.Dead);
    }

    [Fact]
    public void The_twin_fires_up_and_ahead_too()
    {
        var w = Empty();
        w.Ship.Twin = true;
        Run(w, 3, Fire);
        var twin = Assert.Single(w.Shots, s => s.Kind == ShotKind.Twin);
        Assert.True(twin.Vy < 0 && twin.Vx > 0);
    }

    [Fact]
    public void A_missile_drops_onto_the_ground_and_runs_along_it()
    {
        var w = Empty(0, 64);
        w.Ship.Missile = true;
        Sim.Step(w, Fire);
        var m = Assert.Single(w.Shots, s => s.Kind == ShotKind.Missile);
        bool ran = false;
        for (int i = 0; i < 90 && !m.Dead; i++) { Sim.Step(w, default); ran |= m.Running; }
        Assert.True(ran, "the missile should have come down and run along the ground");
        // On flat ground, a missile along it gets a walker the shots go over.
        w = Empty(0, 202);
        w.Ship.Missile = true;
        var walker = Put(w, Foe.Walker, 260, 0);
        walker.Y = w.Stage.FloorY((int)(walker.X / Field.Tile)) - 18;
        for (int i = 0; i < 120 && !walker.Dead; i++) Sim.Step(w, new Controls(0, -1, false, true));
        Assert.True(walker.Dead);
    }

    [Fact]
    public void The_drones_follow_where_the_ship_has_been_and_fire_with_it()
    {
        var w = Empty();
        w.Ship.Drones = 2;
        Run(w, 40, new Controls(0, 1));
        var (dx, dy) = w.Ship.Drone(0);
        Assert.True(dy < w.Ship.Y - 20, "the first drone trails behind, above where the ship is now");
        Assert.True(w.Ship.Drone(1).Y < dy);
        Run(w, 4, Fire);
        Assert.Contains(w.Shots, s => s.Gun == 1);
        Assert.Contains(w.Shots, s => s.Gun == 2);
    }

    // ───────────────────────────── capsules and the gauge ─────────────────────────────

    [Fact]
    public void A_whole_red_line_shot_down_leaves_a_capsule_but_not_if_one_gets_away()
    {
        var w = Empty();
        w.Lines.Add(3);
        var line = Enumerable.Range(0, 3).Select(i => { var e = Put(w, Foe.Darter, 200 + i * 40, 0); e.Wave = 0; e.Red = true; return e; }).ToList();
        foreach (var e in line.Take(2)) Sim.Destroy(w, e);
        Assert.Empty(w.Capsules);
        Sim.Destroy(w, line[2]);
        Assert.Single(w.Capsules);
        // One gets away: no capsule.
        w.Lines.Add(2);
        var two = Enumerable.Range(0, 2).Select(i => { var e = Put(w, Foe.Darter, 200, 100 + i * 40); e.Wave = 1; e.Red = true; return e; }).ToList();
        two[0].X = w.CamX - 200;
        Sim.Step(w, default);
        Sim.Destroy(w, two[1]);
        Assert.Single(w.Capsules);
    }

    [Fact]
    public void Capsules_move_the_gauge_on_and_the_lit_box_is_taken_with_the_button()
    {
        var w = Empty();
        foreach (int i in new[] { 0, 1 })
        {
            w.Capsules.Add(new Capsule { X = w.Ship.X, Y = w.Ship.Y });
            Sim.Step(w, default);
        }
        Assert.Equal((int)Power.Missile, w.Ship.Gauge);
        Assert.Equal(1000, w.Score);
        Sim.Step(w, new Controls(0, 0, true));
        Assert.True(w.Ship.Missile);
        Assert.Equal(-1, w.Ship.Gauge);
        // Taken already: the box can't be taken again.
        w.Ship.Gauge = (int)Power.Missile;
        Sim.Step(w, default);
        Sim.Step(w, new Controls(0, 0, true));
        Assert.Equal((int)Power.Missile, w.Ship.Gauge);
        // Past the guard, the gauge comes round to the start.
        w.Ship.Gauge = (int)Power.Guard;
        w.Capsules.Add(new Capsule { X = w.Ship.X, Y = w.Ship.Y });
        Sim.Step(w, default);
        Assert.Equal((int)Power.Speed, w.Ship.Gauge);
    }

    [Fact]
    public void The_laser_and_the_twin_take_each_others_place()
    {
        var w = Empty();
        w.Ship.Gauge = (int)Power.Twin;
        Sim.TakePower(w);
        w.Ship.Gauge = (int)Power.Laser;
        Sim.TakePower(w);
        Assert.True(w.Ship.Laser);
        Assert.False(w.Ship.Twin);
    }

    [Fact]
    public void The_guard_takes_hits_until_it_wears_away()
    {
        var w = Empty();
        w.Ship.Guard = 2;
        for (int i = 0; i < 3; i++)
        {
            w.Missiles.Add(new Missile { X = w.Ship.X + 2, Y = w.Ship.Y });
            Sim.Step(w, default);
        }
        Assert.True(w.Ship.Dead);
        Assert.Equal(0, w.Ship.Guard);
    }

    // ───────────────────────────── walls ─────────────────────────────

    [Fact]
    public void Glass_shot_away_stays_gone_but_cells_grow_back()
    {
        var w = Empty(1, 20);
        Sim.Break(w, 30, 8);
        Assert.Equal(Tile.Empty, w.Grid[30, 8]);
        var h = Empty(2, 20);
        Sim.Break(h, 28, 8);
        Assert.Equal(Tile.Empty, h.Grid[28, 8]);
        Run(h, 60 * 8);
        Assert.Equal(Tile.Cell, h.Grid[28, 8]);
    }

    [Fact]
    public void Shooting_a_way_through_glass_gets_the_ship_past()
    {
        var w = Empty(1, 18);
        // In line with the middle of a row, firing as the wall comes.
        w.Ship.Y = Field.Top + 9.5 * Field.Tile;
        Run(w, 60 * 12, Fire);
        Assert.False(w.Ship.Dead, w.Cause);
        Assert.True(w.Ship.X > 32 * Field.Tile);
        Assert.Equal(Tile.Empty, w.Grid[30, 9]);
        Assert.Equal(Tile.Empty, w.Grid[31, 9]);
    }

    // ───────────────────────────── the Varkh ─────────────────────────────

    [Fact]
    public void Guns_on_the_ground_fire_at_the_ship_and_a_shot_is_the_end_of_it()
    {
        var w = Empty(0, 30);
        var gun = Put(w, Foe.Turret, 300, 200);
        gun.FireIn = 0.1;
        for (int i = 0; i < 120 && !w.Ship.Dead; i++) Sim.Step(w, default);
        Assert.True(w.Ship.Dead);
        Assert.Equal("a shot", w.Cause);
    }

    [Fact]
    public void A_hatch_lets_out_fliers_that_come_for_the_ship()
    {
        var w = Empty(0, 40);
        var hatch = Put(w, Foe.Hatch, 400, 200, 8);
        hatch.FireIn = 0.1;
        Run(w, 60);
        var out1 = w.Enemies.Where(e => e.Kind == Foe.Darter && e.Pattern == 3).ToList();
        Assert.InRange(out1.Count, 2, 5);
        Assert.Contains(out1, d => d.State == 1 && d.Vx < 0);
    }

    [Fact]
    public void The_volcanoes_throw_up_boulders()
    {
        var w = Empty(0, 122);
        w.VolcanoIn = 0.1;
        Run(w, 120);
        Assert.Contains(w.Enemies, e => e.Kind == Foe.Boulder);
    }

    // ───────────────────────────── the ends of the stages ─────────────────────────────

    /// <summary>The first stage, stopped for its fight, with the Bastion in place.</summary>
    private static (World W, Enemy Boss) Bastion()
    {
        var w = Empty(0, 202);
        w.CamX = w.Stage.BossCol * Field.Tile - Field.Width - 1;
        Run(w, 2);
        Assert.True(w.BossLock);
        for (int i = 0; i < 600 && !w.Enemies.Any(e => e.Boss && e.State == 1); i++) Sim.Step(w, default);
        var boss = w.Enemies.Single(e => e.Boss);
        w.Ship.Safe = 99;
        return (w, boss);
    }

    [Fact]
    public void The_Bastion_core_is_bare_only_when_its_plates_are_shot_away_and_off_the_line_shots_glance_off()
    {
        var (w, boss) = Bastion();
        int plates = boss.Plates, hp = boss.Hp;
        Assert.Equal(4, plates);
        w.Shots.Add(new Shot { X = boss.X - 150, Y = boss.Y - 40, Vx = Sim.BulletSpeed, Kind = ShotKind.Bullet });
        Run(w, 10);
        Assert.Equal(plates, boss.Plates);
        Assert.Equal(Sim.PlateHp, boss.PlateHp);
        for (int i = 0; i < plates * Sim.PlateHp; i++)
        {
            w.Shots.Add(new Shot { X = boss.X - 150, Y = boss.Y, Vx = Sim.BulletSpeed, Kind = ShotKind.Bullet });
            Run(w, 6);
        }
        Assert.Equal(0, boss.Plates);
        Assert.Equal(hp, boss.Hp);
        for (int i = 0; i < hp; i++)
        {
            w.Shots.Add(new Shot { X = boss.X - 150, Y = boss.Y, Vx = Sim.BulletSpeed, Kind = ShotKind.Bullet });
            Run(w, 6);
        }
        Assert.True(w.BossBeaten);
        Assert.True(w.Score >= Sim.Points(Foe.Bastion));
        Run(w, 60 * 4);
        Assert.True(w.Done);
    }

    [Fact]
    public void The_Bastion_beams_miss_a_ship_in_line_with_its_core()
    {
        var (w, boss) = Bastion();
        w.Ship.Safe = 0;
        w.Ship.Y = boss.Y;
        // Keeping in line with the core as it moves: four volleys go by.
        for (int i = 0; i < 60 * 7 && !w.Ship.Dead; i++) Sim.Step(w, new Controls(0, Math.Sign(boss.Y - w.Ship.Y)));
        Assert.False(w.Ship.Dead, w.Cause);
        Assert.True(w.Missiles.Count(m => m.Length > 0) + 1 > 0);
    }

    [Fact]
    public void A_boss_not_beaten_in_time_goes_and_the_stage_ends_without_its_points()
    {
        var (w, boss) = Bastion();
        Run(w, (int)(60 * (Sim.BossPatience + 8)));
        Assert.True(w.Done);
        Assert.False(w.BossBeaten);
        Assert.True(boss.Dead);
    }

    [Fact]
    public void The_Queen_is_hurt_only_with_her_eye_open()
    {
        var w = Empty(2, 200);
        w.CamX = w.Stage.BossCol * Field.Tile - Field.Width - 1;
        for (int i = 0; i < 600 && !w.Enemies.Any(e => e.Boss && e.State == 1); i++) Sim.Step(w, default);
        var q = w.Enemies.Single(e => e.Boss);
        w.Ship.Safe = 999;
        int hp = q.Hp;
        while (Sim.QueenOpen(q)) Sim.Step(w, default);
        w.Shots.Add(new Shot { X = q.X - 140, Y = q.Y, Vx = Sim.BulletSpeed, Kind = ShotKind.Bullet });
        Run(w, 8);
        Assert.Equal(hp, q.Hp);
        while (!Sim.QueenOpen(q)) Sim.Step(w, default);
        w.Shots.Add(new Shot { X = q.X - 140, Y = q.Y, Vx = Sim.BulletSpeed, Kind = ShotKind.Bullet });
        Run(w, 8);
        Assert.Equal(hp - 1, q.Hp);
    }

    // ───────────────────────────── copies ─────────────────────────────

    [Fact]
    public void A_copy_of_the_world_plays_out_exactly_as_the_world_does()
    {
        var a = new World();
        Sim.Start(a, 0, 1, 64, null, 7);
        Run(a, 120, Fire);
        var b = a.Copy();
        var c = new Controls(0, -1, false, true);
        Run(a, 400, c);
        Run(b, 400, c);
        Assert.Equal(a.Ship.X, b.Ship.X);
        Assert.Equal(a.Ship.Y, b.Ship.Y);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Enemies.Count, b.Enemies.Count);
        Assert.Equal(a.Ship.Dead, b.Ship.Dead);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_ship_lost_starts_again_at_the_last_checkpoint_with_nothing()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0, checkpoint: 64);
        g.World.Ship.Laser = true;
        g.World.Ship.Drones = 2;
        g.Tick(default);
        Sim.Kill(g.World, "test");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(64 * Field.Tile, g.World.CamX, 1);
        Assert.False(g.World.Ship.Laser);
        Assert.Equal(0, g.World.Ship.Drones);
    }

    [Fact]
    public void The_last_ship_lost_ends_the_game_and_a_good_score_goes_on_the_table()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 90000;
        Sim.Kill(g.World, "test");
        g.Run((int)(60 * (GameEngine.DyingSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void A_stage_cleared_goes_on_to_the_next_with_the_ship_still_armed()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        g.World.Ship.Laser = true;
        g.World.Done = true;
        g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Run((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(1, g.World.StageIndex);
        Assert.True(g.World.Ship.Laser);
        Assert.Equal(1, g.Cleared);
    }

    [Fact]
    public void The_computer_pilot_flies_the_whole_game_round_to_the_start_again()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 4) { Silent = true };
        g.Go(0);
        g.SetLives(20);
        g.Pilot.Reset();
        for (int t = 0; t < 60 * 900 && g.World.Round == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.World.Round);
        Assert.Equal(4, g.Cleared);
        Assert.True(g.Lives >= 18, $"{20 - g.Lives} ships lost");
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_fly_fire_and_power_up()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyDown(Key.Z);
        Assert.True(input.Player.Power);
        Assert.False(input.Player.Fire);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Fire);
        input.Dispose();
        // Up on the keys flies up the screen.
        var w = Empty();
        double y = w.Ship.Y;
        Run(w, 20, new Controls(0, -1));
        Assert.True(w.Ship.Y < y);
    }
}
