namespace AVAGradius.Engine;

/// <summary>
/// A stage, step by step: the scrolling, the Halcyon and its guns and drones, everything the Varkh send, the
/// volcanoes, the hits, and the fight at the end. Works on a <see cref="World"/>, so the computer pilot can run
/// it on copies.
/// </summary>
public static class Sim
{
    public const double BulletSpeed = 960, LaserSpeed = 1400, LaserLength = 150, TwinSpeed = 700;
    private const double Dt = Field.Step, T = Field.Tile;

    // ───────────────────────────── starting ─────────────────────────────

    /// <summary>A stage begun at a checkpoint column; <paramref name="carry"/> is the ship whose arms come with it (none after a loss).</summary>
    public static void Start(World w, int stage, int round, int checkpoint, Ship? carry, int seed)
    {
        var st = StageBook.All[stage];
        w.Stage = st;
        w.StageIndex = stage;
        w.Round = round;
        w.Grid = (Tile[,])st.Grid.Clone();
        w.Regrow.Clear();
        w.Shots.Clear();
        w.Enemies.Clear();
        w.Missiles.Clear();
        w.Capsules.Clear();
        w.Effects.Clear();
        w.Lines.Clear();
        w.Heard.Clear();
        w.CamX = Math.Min(checkpoint * T, st.BossCol * T - Field.Width);
        w.Clock = w.BossTime = 0;
        w.VolcanoIn = 1;
        w.BossLock = w.BossUp = w.BossDown = w.Done = w.BossBeaten = false;
        w.Dice = new Dice(seed);
        w.Cause = "";
        // What would have come into view already is passed by.
        w.NextSpawn = 0;
        while (w.NextSpawn < st.Spawns.Count && st.Spawns[w.NextSpawn].Col * T < w.CamX + Field.Width) w.NextSpawn++;
        var s = new Ship();
        if (carry != null)
        {
            s.Speed = carry.Speed;
            s.Missile = carry.Missile;
            s.Twin = carry.Twin;
            s.Laser = carry.Laser;
            s.Drones = carry.Drones;
            s.Guard = carry.Guard;
            s.Gauge = carry.Gauge;
        }
        int col = (int)((w.CamX + 160) / T);
        s.X = w.CamX + 160;
        s.Y = Math.Round((st.RoofY(col) + st.FloorY(col)) / 2);
        s.Safe = 2;
        for (int i = 0; i < Ship.TrailLength; i++) { s.TrailX[i] = s.X - w.CamX; s.TrailY[i] = s.Y; }
        w.Ship = s;
    }

    // ───────────────────────────── a step ─────────────────────────────

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        double scroll = 0;
        if (!w.BossLock)
        {
            scroll = w.Speed * Dt;
            w.CamX += scroll;
            double stop = w.Stage.BossCol * T - Field.Width;
            if (w.CamX >= stop)
            {
                scroll -= w.CamX - stop;
                w.CamX = stop;
                w.BossLock = true;
            }
        }
        var s = w.Ship;
        if (s.Dead) s.DeadTime += Dt;
        else MoveShip(w, c, scroll);
        Arrivals(w);
        Volcanoes(w);
        MoveShots(w);
        for (int i = 0, n = w.Enemies.Count; i < n; i++)
            if (!w.Enemies[i].Dead) Behave(w, w.Enemies[i]);
        MoveMissiles(w, scroll);
        Hits(w);
        Ending(w);
        Regrowth(w);
        foreach (var cap in w.Capsules)
        {
            cap.T += Dt;
            if (cap.X < w.CamX - 40) cap.Dead = true;
        }
        foreach (var e in w.Effects) e.Age += Dt;
        w.Shots.RemoveAll(x => x.Dead);
        w.Enemies.RemoveAll(x => x.Dead);
        w.Missiles.RemoveAll(x => x.Dead);
        w.Capsules.RemoveAll(x => x.Dead);
        w.Effects.RemoveAll(x => x.Done);
    }

    // ───────────────────────────── the Halcyon ─────────────────────────────

    private static void MoveShip(World w, Controls c, double scroll)
    {
        var s = w.Ship;
        double v = s.Speed0;
        s.X += scroll + c.Dx * v * Dt;
        s.Y += c.Dy * v * Dt;
        s.X = Math.Clamp(s.X, w.CamX + 30, w.CamX + Field.Width - 60);
        s.Y = Math.Clamp(s.Y, Field.Top + 10, Field.Bottom - 10);
        s.Bank += (c.Dy - s.Bank) * Math.Min(1, Dt * 10);
        s.Safe -= Dt;
        // The drones follow where the ship has been on the screen.
        if (c.Dx != 0 || c.Dy != 0)
        {
            s.TrailHead = (s.TrailHead + 1) % Ship.TrailLength;
            s.TrailX[s.TrailHead] = s.X - w.CamX;
            s.TrailY[s.TrailHead] = s.Y;
        }
        if (c.Power && !s.PowerPrev) TakePower(w);
        s.PowerPrev = c.Power;
        s.FireCool -= Dt;
        s.MissileCool -= Dt;
        if (c.Fire) Fire(w);
        // Rock, glass and cells are all the end of it.
        foreach (var (px, py) in new[] { (s.X + Ship.HalfWidth, s.Y), (s.X - Ship.HalfWidth * 0.8, s.Y - Ship.HalfHeight), (s.X - Ship.HalfWidth * 0.8, s.Y + Ship.HalfHeight), (s.X, s.Y - Ship.HalfHeight * 0.7), (s.X, s.Y + Ship.HalfHeight * 0.7) })
        {
            var t = w.AtPoint(px, py);
            if (t != Tile.Empty)
            {
                Kill(w, t switch { Tile.Crystal => w.Stage.Theme == 3 ? "a shutter" : "the glass", Tile.Cell => "the cells", _ => "the rock" });
                return;
            }
        }
    }

    /// <summary>The lit box of the gauge, taken (if it can be).</summary>
    public static void TakePower(World w)
    {
        var s = w.Ship;
        if (s.Gauge < 0) return;
        var p = (Power)s.Gauge;
        if (!s.CanTake(p)) { w.Heard.Add(Sfx.Denied); return; }
        switch (p)
        {
            case Power.Speed: s.Speed++; w.Heard.Add(Sfx.SaySpeed); break;
            case Power.Missile: s.Missile = true; w.Heard.Add(Sfx.SayMissile); break;
            case Power.Twin: s.Twin = true; s.Laser = false; w.Heard.Add(Sfx.SayTwin); break;
            case Power.Laser: s.Laser = true; s.Twin = false; w.Heard.Add(Sfx.SayLaser); break;
            case Power.Drone:
                // A new drone starts where the ship is, and drifts back into place along the trail.
                s.Drones++;
                w.Heard.Add(Sfx.SayDrone);
                break;
            case Power.Guard: s.Guard = Ship.GuardHits; w.Heard.Add(Sfx.SayGuard); break;
        }
        s.Gauge = -1;
        w.Heard.Add(Sfx.PowerUp);
    }

    /// <summary>Where each gun is: the ship, then its drones.</summary>
    public static IEnumerable<(double X, double Y)> Guns(World w)
    {
        var s = w.Ship;
        yield return (s.X, s.Y);
        for (int i = 0; i < s.Drones; i++)
        {
            var (dx, dy) = s.Drone(i);
            yield return (dx + w.CamX, dy);
        }
    }

    private static void Fire(World w)
    {
        var s = w.Ship;
        int gun = 0;
        bool fired = false, missile = false;
        foreach (var (gx, gy) in Guns(w))
        {
            if (s.FireCool <= 0)
            {
                if (s.Laser)
                {
                    if (Count(w, gun, ShotKind.Laser) < 2)
                    {
                        w.Shots.Add(new Shot { X = gx + 24 + LaserLength * 0.4, Y = gy, Vx = LaserSpeed, Length = LaserLength * 0.4, Kind = ShotKind.Laser, Gun = gun });
                        fired = true;
                    }
                }
                else if (Count(w, gun, ShotKind.Bullet) < 2)
                {
                    w.Shots.Add(new Shot { X = gx + 22, Y = gy, Vx = BulletSpeed, Kind = ShotKind.Bullet, Gun = gun });
                    fired = true;
                    if (s.Twin && Count(w, gun, ShotKind.Twin) < 1)
                        w.Shots.Add(new Shot { X = gx + 10, Y = gy - 6, Vx = TwinSpeed, Vy = -TwinSpeed, Kind = ShotKind.Twin, Gun = gun });
                }
            }
            if (s.Missile && s.MissileCool <= 0 && Count(w, gun, ShotKind.Missile) < 1)
            {
                w.Shots.Add(new Shot { X = gx + 4, Y = gy + 10, Vx = 70 + w.Speed, Vy = 300, Kind = ShotKind.Missile, Gun = gun });
                missile = true;
            }
            gun++;
        }
        if (fired)
        {
            s.FireCool = s.Laser ? 0.12 : 0.085;
            w.Heard.Add(s.Laser ? Sfx.Laser : Sfx.Fire);
        }
        if (missile)
        {
            s.MissileCool = 0.3;
            w.Heard.Add(Sfx.MissileLaunch);
        }
    }

    private static int Count(World w, int gun, ShotKind kind)
    {
        int n = 0;
        foreach (var x in w.Shots) if (x.Gun == gun && x.Kind == kind && !x.Dead) n++;
        return n;
    }

    private static void MoveShots(World w)
    {
        foreach (var x in w.Shots)
        {
            if (x.Kind == ShotKind.Missile) { MoveMissile(w, x); continue; }
            x.X += x.Vx * Dt;
            x.Y += x.Vy * Dt;
            if (x.Kind == ShotKind.Laser) x.Length = Math.Min(LaserLength, x.Length + x.Vx * Dt);
            if (x.X - x.Length > w.CamX + Field.Width + 10 || x.Y < Field.Top - 10 || x.Y > Field.Bottom + 10) { x.Dead = true; continue; }
            if (HitTerrain(w, x.X, x.Y)) x.Dead = true;
        }
    }

    /// <summary>A missile drops ahead and down; on the ground it runs along it, until a wall or something to hit.</summary>
    private static void MoveMissile(World w, Shot x)
    {
        if (!x.Running)
        {
            x.X += x.Vx * Dt;
            x.Y += x.Vy * Dt;
            if (w.AtPoint(x.X, x.Y + 6) is var t && t != Tile.Empty)
            {
                if (t != Tile.Rock) { HitTerrain(w, x.X, x.Y + 6); x.Dead = true; return; }
                x.Running = true;
                x.Y = Field.Top + Math.Floor((x.Y + 6 - Field.Top) / T) * T - 6;
            }
            if (x.Y > Field.Bottom - 6) { x.Running = true; x.Y = Field.Bottom - 6; }
        }
        else
        {
            x.X += (w.Speed + 260) * Dt;
            if (w.AtPoint(x.X + 8, x.Y) != Tile.Empty) { HitTerrain(w, x.X + 8, x.Y); x.Dead = true; return; }
            if (x.Y < Field.Bottom - 6 && w.AtPoint(x.X, x.Y + 8) == Tile.Empty) { x.Running = false; x.Vy = 300; x.Vx = 70 + w.Speed; }
        }
        if (x.X > w.CamX + Field.Width + 10) x.Dead = true;
    }

    /// <summary>Whether a point is in something solid; glass and cells there are broken.</summary>
    private static bool HitTerrain(World w, double x, double y)
    {
        int c = (int)Math.Floor(x / T), r = (int)Math.Floor((y - Field.Top) / T);
        var t = w.At(c, r);
        if (t == Tile.Empty) return false;
        if (t != Tile.Rock) Break(w, c, r);
        return true;
    }

    public static void Break(World w, int c, int r)
    {
        var t = w.Grid[c, r];
        w.Grid[c, r] = Tile.Empty;
        w.Score += 10;
        if (t == Tile.Cell) w.Regrow.Add((c, r, w.StageIndex == 3 ? 4 : 7));
        w.Effects.Add(new Effect { Kind = EffectKind.Spark, X = (c + 0.5) * T, Y = Field.Top + (r + 0.5) * T, Life = 0.3, Colour = t == Tile.Cell ? 0xFFE070A0 : 0xFF9AE8FF });
        w.Heard.Add(Sfx.Crack);
    }

    private static void Regrowth(World w)
    {
        for (int i = 0; i < w.Regrow.Count; i++)
        {
            var (c, r, t) = w.Regrow[i];
            t -= Dt;
            if (t <= 0)
            {
                double x0 = c * T, y0 = Field.Top + r * T;
                var s = w.Ship;
                bool inside = !s.Dead && s.X + Ship.HalfWidth > x0 && s.X - Ship.HalfWidth < x0 + T && s.Y + Ship.HalfHeight > y0 && s.Y - Ship.HalfHeight < y0 + T;
                if (inside) t = 0.5;
                else
                {
                    w.Grid[c, r] = Tile.Cell;
                    w.Regrow.RemoveAt(i--);
                    continue;
                }
            }
            w.Regrow[i] = (c, r, t);
        }
    }

    public static void Kill(World w, string cause)
    {
        var s = w.Ship;
        if (s.Dead) return;
        s.Dead = true;
        s.DeadTime = 0;
        w.Cause = cause;
        w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = s.X, Y = s.Y, Life = 1.2, Colour = 0xFFFFC040 });
        w.Heard.Add(Sfx.Die);
    }

    // ───────────────────────────── what comes ─────────────────────────────

    private static void Arrivals(World w)
    {
        var sp = w.Stage.Spawns;
        while (w.NextSpawn < sp.Count && sp[w.NextSpawn].Col * T <= w.CamX + Field.Width + 64)
            Create(w, sp[w.NextSpawn++]);
    }

    private static Enemy New(World w, Foe kind, double x, double y, int hp = 1)
    {
        var e = new Enemy { Kind = kind, X = x, Y = y, HomeY = y, Hp = hp, Id = w.NextId++, Level = w.Round };
        w.Enemies.Add(e);
        return e;
    }

    private static void Create(World w, Spawn sp)
    {
        double y = Field.Top + (sp.Row + 0.5) * T;
        switch (sp.Kind)
        {
            case Foe.Darter:
            {
                int line = w.Lines.Count;
                w.Lines.Add(sp.Count);
                for (int i = 0; i < sp.Count; i++)
                {
                    bool behind = sp.Pattern == 2;
                    var e = New(w, Foe.Darter, behind ? w.CamX - 40 - i * 46 : w.CamX + Field.Width + 30 + i * 46, y);
                    e.Pattern = sp.Pattern;
                    e.Red = sp.Red;
                    e.Wave = line;
                    e.Vx = behind ? 330 : -230;
                }
                break;
            }
            case Foe.Spinner:
            {
                var e = New(w, Foe.Spinner, w.CamX + Field.Width + 30, y);
                e.Red = sp.Red;
                break;
            }
            default:
            {
                double x = (sp.Col + 0.5) * T;
                var (_, hh) = new Enemy { Kind = sp.Kind }.Size;
                double gy = sp.Roof ? w.Stage.RoofY(sp.Col) + hh : w.Stage.FloorY(sp.Col) - hh;
                var e = New(w, sp.Kind, x, gy, sp.Kind switch { Foe.Hatch => 8, Foe.Turret => 2, Foe.Sentinel => 10, Foe.Polyp => 4, _ => 1 });
                e.Roof = sp.Roof;
                e.FireIn = w.Dice.Between(0.6, 1.8);
                break;
            }
        }
    }

    private static void Volcanoes(World w)
    {
        if (w.BossLock) return;
        bool near = false;
        (int Col, int Row) at = default;
        foreach (var v in w.Stage.Volcanoes)
            if (v.Col * T > w.CamX - 60 && v.Col * T < w.CamX + Field.Width + 60) { near = true; at = v; }
        if (!near) return;
        w.VolcanoIn -= Dt;
        if (w.VolcanoIn > 0) return;
        w.VolcanoIn = w.Dice.Between(0.4, 0.75) / (w.Round > 1 ? 1.3 : 1);
        var e = New(w, Foe.Boulder, (at.Col + 0.5) * T, Field.Top + at.Row * T - 6, 2);
        e.Vx = w.Dice.Between(-170, 70);
        e.Vy = w.Dice.Between(-560, -380);
        e.Entered = true;
        w.Heard.Add(Sfx.Erupt);
    }

    /// <summary>How much fiercer things are: a little more each stage, and more again each round.</summary>
    private static double Rf(World w) => 1 + 0.08 * w.StageIndex + 0.3 * (w.Round - 1);

    private static void Behave(World w, Enemy e)
    {
        e.T += Dt;
        e.Hurt -= Dt;
        e.LaserCool -= Dt;
        var s = w.Ship;
        double sx = s.X, sy = s.Y, rf = Rf(w);
        bool onScreen = e.X > w.CamX - 10 && e.X < w.CamX + Field.Width + 10;
        if (onScreen) e.Entered = true;
        switch (e.Kind)
        {
            case Foe.Darter:
                switch (e.Pattern)
                {
                    case 0:
                        // In from ahead, then a turn at the ship.
                        if (e.State == 0 && e.X < sx + 250 && e.X < w.CamX + Field.Width - 80)
                        {
                            e.State = 1;
                            e.Vx = -250 * rf;
                            e.Vy = Math.Clamp((sy - e.Y) * 1.8, -210, 210) * rf;
                        }
                        break;
                    case 1:
                        // A weave.
                        e.Vx = -200 * rf;
                        e.Y = e.HomeY + 64 * Math.Sin(e.T * 3.2);
                        break;
                    case 2:
                        // From behind, over the ship, then back at it.
                        if (e.State == 0 && e.X > sx + 170)
                        {
                            e.State = 1;
                            e.Vx = -270 * rf;
                            e.Vy = Math.Clamp((sy - e.Y) * 1.5, -170, 170) * rf;
                        }
                        break;
                    case 3:
                        // Out of a hatch, then off at the ship.
                        e.Vx = -w.Speed;
                        if (e.T > 0.45 && e.State == 0)
                        {
                            e.State = 1;
                            double dx = sx - e.X, dy = sy - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                            e.Vx = dx / d * 220 * rf;
                            e.Vy = dy / d * 220 * rf;
                        }
                        break;
                }
                e.X += e.Vx * Dt;
                if (e.Pattern != 1) e.Y += e.Vy * Dt;
                break;
            case Foe.Spinner:
                e.X -= 150 * rf * Dt;
                e.Y += Math.Clamp(sy - e.Y, -1, 1) * 75 * Dt;
                break;
            case Foe.Hatch:
                if (!onScreen) break;
                e.FireIn -= Dt;
                if (e.FireIn <= 0)
                {
                    var d = New(w, Foe.Darter, e.X, e.Y + (e.Roof ? 10 : -10));
                    d.Pattern = 3;
                    d.Vy = e.Roof ? 200 : -200;
                    d.Entered = true;
                    e.State++;
                    e.FireIn = e.State % 5 == 0 ? 3.5 / rf : 0.5;
                }
                break;
            case Foe.Walker:
            {
                double dir = Math.Sign(sx - e.X);
                double nx = e.X + dir * 70 * Dt;
                int nc = (int)(nx / T), oc = (int)(e.X / T);
                double ny = e.Roof ? w.Stage.RoofY(nc) + 18 : w.Stage.FloorY(nc) - 18;
                // It steps up or down a square, but not up a cliff.
                if (nc == oc || Math.Abs(ny - e.Y) <= T + 1) { e.X = nx; e.Y = ny; e.Vx = dir; }
                FireAt(w, e, 220, 1.8, 2.8);
                break;
            }
            case Foe.Turret:
                if (s.Dead || (e.Roof ? sy < e.Y : sy > e.Y)) break;
                FireAt(w, e, 240, 2.0, 3.0);
                break;
            case Foe.Boulder:
                e.Vy += 420 * Dt;
                e.X += e.Vx * Dt;
                e.Y += e.Vy * Dt;
                if (e.Vy > 0 && w.AtPoint(e.X, e.Y + 12) != Tile.Empty) { e.Dead = true; Burst(w, e.X, e.Y, 0xFFE07030, 8); }
                if (e.Y > Field.Bottom + 30) e.Dead = true;
                break;
            case Foe.Sentinel:
                if (!onScreen) break;
                e.FireIn -= Dt;
                if (e.FireIn <= 0)
                {
                    var r = New(w, Foe.Ring, e.X, e.Y + (e.Roof ? 40 : -40));
                    r.Entered = true;
                    e.FireIn = 2.8 / rf;
                    w.Heard.Add(Sfx.RingOut);
                }
                break;
            case Foe.Ring:
            case Foe.Spore:
            {
                double speed = (e.Kind == Foe.Ring ? 105 : 140) * rf;
                double dx = sx - e.X, dy = sy - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                e.Vx += (dx / d * speed - e.Vx) * Dt * 0.9;
                e.Vy += (dy / d * speed - e.Vy) * Dt * 0.9;
                e.X += e.Vx * Dt;
                e.Y += e.Vy * Dt;
                if (e.T > (e.Kind == Foe.Ring ? 9 : 7)) e.Dead = true;
                break;
            }
            case Foe.Polyp:
                if (!onScreen || s.Dead) break;
                e.FireIn -= Dt;
                if (e.FireIn <= 0)
                {
                    double a = Math.Atan2(sy - e.Y, sx - e.X);
                    for (int k = -1; k <= 1; k++)
                        w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = Math.Cos(a + k * 0.3) * 190 * rf, Vy = Math.Sin(a + k * 0.3) * 190 * rf });
                    e.FireIn = 3.2 / rf;
                    w.Heard.Add(Sfx.Spit);
                }
                break;
            case Foe.Bastion: Bastion(w, e); break;
            case Foe.Queen: Queen(w, e); break;
            case Foe.Mind: Mind(w, e); break;
        }
        if (!e.Boss && e.Entered && (e.X < w.CamX - 80 || e.X > w.CamX + Field.Width + 160 || e.Y < Field.Top - 80 || e.Y > Field.Bottom + 80))
        {
            e.Dead = true;
            if (e.Wave >= 0) w.Lines[e.Wave] = -1;
        }
    }

    /// <summary>A shot at the ship, now and then, while on the screen.</summary>
    private static void FireAt(World w, Enemy e, double speed, double min, double max)
    {
        if (e.X > w.CamX + Field.Width - 20 || e.X < w.CamX + 20) return;
        e.FireIn -= Dt;
        if (e.FireIn > 0) return;
        double rf = Rf(w);
        e.FireIn = w.Dice.Between(min, max) / rf;
        if (w.Ship.Dead) return;
        double dx = w.Ship.X - e.X, dy = w.Ship.Y - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        if (d < 90) return;
        w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = dx / d * speed * rf, Vy = dy / d * speed * rf });
        w.Heard.Add(Sfx.EnemyFire);
    }

    /// <summary>What the Varkh fire flies across the screen, so it goes along with the stage.</summary>
    private static void MoveMissiles(World w, double scroll)
    {
        foreach (var m in w.Missiles)
        {
            m.X += m.Vx * Dt + scroll;
            m.Y += m.Vy * Dt;
            if (m.X + m.Length < w.CamX - 20 || m.X > w.CamX + Field.Width + 40 || m.Y < Field.Top - 10 || m.Y > Field.Bottom + 10) m.Dead = true;
            else if (m.Length == 0 && w.AtPoint(m.X, m.Y) == Tile.Rock) m.Dead = true;
        }
    }

    // ───────────────────────────── the ends of the stages ─────────────────────────────

    /// <summary>The hits each of the Bastion's plates takes.</summary>
    public const int PlateHp = 8;

    /// <summary>The time a boss is given before it gives up and goes.</summary>
    public const double BossPatience = 50;

    private static void Ending(World w)
    {
        if (!w.BossLock || w.Done) return;
        w.BossTime += Dt;
        if (w.BossDown)
        {
            if (w.BossTime > 3) w.Done = true;
            return;
        }
        if (!w.BossUp && w.BossTime > 1.2)
        {
            w.BossUp = true;
            w.BossTime = 0;
            double mid = (Field.Top + Field.Bottom) / 2;
            var e = w.Stage.Boss switch
            {
                Foe.Bastion => New(w, Foe.Bastion, w.CamX + Field.Width + 140, mid, 3),
                Foe.Queen => New(w, Foe.Queen, w.CamX + Field.Width + 120, mid, 20 + 8 * (w.Round - 1)),
                _ => New(w, Foe.Mind, (w.Stage.BossCol - 5) * T, mid, 40),
            };
            e.Boss = true;
            e.Entered = true;
            e.Level = w.StageIndex == 0 ? w.Round : w.Round + 1;
            e.Plates = e.Kind == Foe.Bastion ? 2 + 2 * e.Level : 0;
            e.PlateHp = PlateHp;
            e.FireIn = 2;
            w.Heard.Add(Sfx.BossCome);
            w.Heard.Add(Sfx.SayCore);
            w.Heard.Add(Sfx.BossMusic);
        }
    }

    private static void Bastion(World w, Enemy e)
    {
        double home = w.CamX + 790, lunge = w.CamX + 560;
        var s = w.Ship;
        int col = (int)(e.X / T);
        double lo = w.Stage.RoofY(col) + 80, hi = w.Stage.FloorY(col) - 80;
        switch (e.State)
        {
            case 0:
                e.X = Math.Max(home, e.X - 220 * Dt);
                if (e.X <= home) { e.State = 1; e.T = 0; }
                break;
            case 1 or 2 or 3:
                if (!s.Dead) e.Y += Math.Clamp(s.Y - e.Y, -1, 1) * Math.Min(Math.Abs(s.Y - e.Y) * 4, 55 + 25 * e.Level) * Dt;
                e.Y = Math.Clamp(e.Y, lo, hi);
                e.FireIn -= Dt;
                if (e.FireIn <= 0 && e.State == 1)
                {
                    e.FireIn = Math.Max(1.0, 2.1 - 0.35 * e.Level);
                    double v = -(560 + 90 * e.Level);
                    foreach (var (ox, oy) in new[] { (-96.0, -46.0), (-96.0, 46.0), (-50.0, -76.0), (-50.0, 76.0) })
                        w.Missiles.Add(new Missile { X = e.X + ox, Y = e.Y + oy, Vx = v, Length = 70 });
                    w.Heard.Add(Sfx.Beam);
                }
                // Now and then it lunges.
                if (e.State == 1 && e.T > 7) { e.State = 2; e.T = 0; }
                if (e.State == 2) { e.X = Math.Max(lunge, e.X - 300 * Dt); if (e.T > 1.6) { e.State = 3; e.T = 0; } }
                if (e.State == 3) { e.X = Math.Min(home, e.X + 200 * Dt); if (e.X >= home) { e.State = 1; e.T = 0; } }
                if (w.BossTime > BossPatience) { e.State = 9; w.Heard.Add(Sfx.BossLeave); }
                break;
            case 9:
                e.X += 240 * Dt;
                if (e.X > w.CamX + Field.Width + 160) Gone(w, e);
                break;
        }
    }

    private static void Queen(World w, Enemy e)
    {
        double home = w.CamX + 850;
        var s = w.Ship;
        switch (e.State)
        {
            case 0:
                e.X = Math.Max(home, e.X - 200 * Dt);
                if (e.X <= home) { e.State = 1; e.T = 0; }
                break;
            case 1:
                e.Y = e.HomeY + 70 * Math.Sin(e.T * 0.7);
                e.FireIn -= Dt;
                if (QueenOpen(e))
                {
                    if (e.FireIn <= 0 && !s.Dead)
                    {
                        double a = Math.Atan2(s.Y - e.Y, s.X - (e.X - 70));
                        for (int k = -2; k <= 2; k++)
                            w.Missiles.Add(new Missile { X = e.X - 70, Y = e.Y, Vx = Math.Cos(a + k * 0.26) * 220 * Rf(w), Vy = Math.Sin(a + k * 0.26) * 220 * Rf(w) });
                        e.FireIn = 1.0;
                        w.Heard.Add(Sfx.Spit);
                    }
                }
                else if (e.FireIn <= 0)
                {
                    foreach (double oy in new[] { -80.0, 80.0 })
                    {
                        var sp = New(w, Foe.Spore, e.X - 50, e.Y + oy);
                        sp.Entered = true;
                        sp.Vx = -60;
                    }
                    e.FireIn = 1.6;
                }
                if (w.BossTime > BossPatience) { e.State = 9; w.Heard.Add(Sfx.BossLeave); }
                break;
            case 9:
                e.X += 200 * Dt;
                if (e.X > w.CamX + Field.Width + 160) Gone(w, e);
                break;
        }
    }

    /// <summary>The Queen's eye is open (and can be hurt) for the last three seconds of every six.</summary>
    public static bool QueenOpen(Enemy e) => e.State == 1 && e.T % 6 > 3;

    private static void Mind(World w, Enemy e)
    {
        e.FireIn -= Dt;
        if (e.FireIn <= 0)
        {
            e.FireIn = 3.2 / Rf(w);
            var r = New(w, Foe.Ring, e.X - 60, e.Y);
            r.Entered = true;
            r.Vx = -80;
            w.Heard.Add(Sfx.RingOut);
        }
    }

    /// <summary>A boss gone without being beaten: the stage ends all the same, without its points.</summary>
    private static void Gone(World w, Enemy e)
    {
        e.Dead = true;
        w.BossDown = true;
        w.BossTime = 0;
    }

    // ───────────────────────────── hits ─────────────────────────────

    private static (double X0, double Y0, double X1, double Y1) Box(Shot x) => x.Kind switch
    {
        ShotKind.Laser => (x.X - x.Length, x.Y - 4, x.X, x.Y + 4),
        ShotKind.Missile => (x.X - 7, x.Y - 6, x.X + 7, x.Y + 6),
        _ => (x.X - 9, x.Y - 4, x.X + 9, x.Y + 4),
    };

    private static void Hits(World w)
    {
        foreach (var x in w.Shots)
        {
            if (x.Dead) continue;
            var (x0, y0, x1, y1) = Box(x);
            foreach (var e in w.Enemies)
            {
                if (e.Dead) continue;
                var (hw, hh) = e.Size;
                if (x1 < e.X - hw || x0 > e.X + hw || y1 < e.Y - hh || y0 > e.Y + hh) continue;
                // A laser goes on through, hurting each thing once; a boss stops it.
                if (x.Kind == ShotKind.Laser && !e.Boss)
                {
                    if (e.LaserCool > 0) continue;
                    e.LaserCool = 0.14;
                }
                if (e.Boss) { HitBoss(w, x, e); break; }
                if (x.Kind != ShotKind.Laser) x.Dead = true;
                e.Hp--;
                e.Hurt = 0.1;
                if (e.Hp <= 0) Destroy(w, e);
                else w.Heard.Add(Sfx.Hit);
                if (x.Dead) break;
            }
        }
        var s = w.Ship;
        if (s.Dead) return;
        bool safe = s.Safe > 0;
        foreach (var e in w.Enemies)
        {
            if (e.Dead || safe && !e.Boss) continue;
            var (hw, hh) = e.Size;
            if (e.Kind == Foe.Bastion)
            {
                // The hull is a wedge: narrower at its nose.
                hh = e.X - s.X > 40 ? hh * 0.75 : hh;
            }
            if (Math.Abs(s.X - e.X) > hw + Ship.HalfWidth * 0.7 || Math.Abs(s.Y - e.Y) > hh + Ship.HalfHeight * 0.8) continue;
            if (e.Boss) { Kill(w, e.Kind switch { Foe.Bastion => "the Bastion", Foe.Queen => "the Queen", _ => "the Mind" }); return; }
            if (s.Guard > 0)
            {
                s.Guard = Math.Max(0, s.Guard - 3);
                Destroy(w, e);
                w.Heard.Add(Sfx.GuardHit);
                continue;
            }
            Kill(w, Name(e.Kind));
            return;
        }
        foreach (var m in w.Missiles)
        {
            if (m.Dead || safe) continue;
            bool hit = m.Length > 0
                ? s.Y + Ship.HalfHeight * 0.7 > m.Y - 3 && s.Y - Ship.HalfHeight * 0.7 < m.Y + 3 && s.X + Ship.HalfWidth * 0.8 > m.X && s.X - Ship.HalfWidth * 0.8 < m.X + m.Length
                : Math.Abs(m.X - s.X) < Ship.HalfWidth * 0.6 + 4 && Math.Abs(m.Y - s.Y) < Ship.HalfHeight * 0.7 + 4;
            if (!hit) continue;
            if (s.Guard > 0)
            {
                s.Guard--;
                m.Dead = true;
                w.Heard.Add(Sfx.GuardHit);
                continue;
            }
            Kill(w, m.Length > 0 ? "a beam" : "a shot");
            return;
        }
        foreach (var cap in w.Capsules)
        {
            if (cap.Dead || Math.Abs(cap.X - s.X) > 30 || Math.Abs(cap.Y - s.Y) > 24) continue;
            cap.Dead = true;
            s.Gauge = (s.Gauge + 1) % 6;
            w.Score += 500;
            w.Heard.Add(Sfx.Capsule);
        }
    }

    private static void HitBoss(World w, Shot x, Enemy e)
    {
        switch (e.Kind)
        {
            case Foe.Bastion:
                x.Dead = true;
                if (Math.Abs(x.Y - e.Y) > 13 || e.State == 0) { w.Heard.Add(Sfx.Clink); return; }
                if (e.Plates > 0)
                {
                    if (--e.PlateHp <= 0)
                    {
                        e.Plates--;
                        e.PlateHp = PlateHp;
                        w.Effects.Add(new Effect { Kind = EffectKind.Spark, X = e.X - 70 - e.Plates * 8, Y = e.Y, Life = 0.4, Colour = 0xFF80C8FF });
                        w.Heard.Add(Sfx.Plate);
                    }
                    else w.Heard.Add(Sfx.Clink);
                    return;
                }
                break;
            case Foe.Queen:
                x.Dead = true;
                if (!QueenOpen(e) || Math.Abs(x.Y - e.Y) > 34) { w.Heard.Add(Sfx.Clink); return; }
                break;
            default:
                x.Dead = true;
                break;
        }
        e.Hp--;
        e.Hurt = 0.12;
        if (e.Hp <= 0) Destroy(w, e);
        else w.Heard.Add(Sfx.BossHit);
    }

    public static string Name(Foe f) => f switch
    {
        Foe.Darter => "a darter",
        Foe.Spinner => "a spinner",
        Foe.Hatch => "a hatch",
        Foe.Walker => "a walker",
        Foe.Turret => "a gun",
        Foe.Boulder => "a boulder",
        Foe.Sentinel => "a spire",
        Foe.Ring => "a ring",
        Foe.Polyp => "a polyp",
        Foe.Spore => "a spore",
        _ => f.ToString(),
    };

    public static int Points(Foe f) => f switch
    {
        Foe.Darter => 100,
        Foe.Spinner => 200,
        Foe.Hatch => 1000,
        Foe.Walker => 300,
        Foe.Turret => 400,
        Foe.Boulder => 100,
        Foe.Sentinel => 1000,
        Foe.Ring => 50,
        Foe.Polyp => 500,
        Foe.Spore => 50,
        Foe.Bastion => 10000,
        Foe.Queen => 15000,
        _ => 50000,
    };

    public static void Destroy(World w, Enemy e)
    {
        e.Dead = true;
        int pts = Points(e.Kind);
        w.Score += pts;
        if (e.Boss)
        {
            w.BossDown = w.BossBeaten = true;
            w.BossTime = 0;
            for (int i = 0; i < 6; i++)
                w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = e.X + w.Dice.Between(-90, 90), Y = e.Y + w.Dice.Between(-70, 70), Life = 0.9 + i * 0.25, Colour = 0xFFFFB040 });
            // Everything else goes with it.
            foreach (var o in w.Enemies) if (!o.Dead && o != e) { o.Dead = true; Burst(w, o.X, o.Y, 0xFFFFD080, 8); }
            foreach (var m in w.Missiles) m.Dead = true;
            w.Heard.Add(Sfx.BossDie);
            w.Heard.Add(Sfx.MusicStop);
        }
        else
        {
            Burst(w, e.X, e.Y, e.Red ? 0xFFFF6040 : 0xFFFFD080, e.Kind is Foe.Hatch or Foe.Sentinel ? 14 : 9);
            w.Heard.Add(e.Kind is Foe.Hatch or Foe.Sentinel or Foe.Polyp ? Sfx.BigBoom : Sfx.Boom);
        }
        if (pts >= 300) w.Effects.Add(new Effect { Kind = EffectKind.Score, X = e.X, Y = e.Y - 20, Text = pts.ToString(), Colour = 0xFFFFE070 });
        // A capsule: from a red flier, or the last of a red line.
        bool capsule = e.Red && e.Wave < 0;
        if (e.Wave >= 0 && w.Lines[e.Wave] > 0 && --w.Lines[e.Wave] == 0 && e.Red) capsule = true;
        if (capsule) w.Capsules.Add(new Capsule { X = e.X, Y = e.Y });
    }

    private static void Burst(World w, double x, double y, uint colour, int n) =>
        w.Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Text = n.ToString(), Life = 0.5, Colour = colour });
}
