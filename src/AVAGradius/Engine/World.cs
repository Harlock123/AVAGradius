namespace AVAGradius.Engine;

/// <summary>A small random number generator whose state can be copied (so a copy of the world plays out the same).</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Between(double a, double b) => a + Next() * (b - a);
}

/// <summary>What the power gauge can light: each capsule moves it on one.</summary>
public enum Power { Speed, Missile, Twin, Laser, Drone, Guard }

/// <summary>The Halcyon: where it is, what it's armed with, and the drones trailing it.</summary>
public sealed class Ship
{
    public const double HalfWidth = 18, HalfHeight = 8;
    public const int MaxSpeed = 5, MaxDrones = 4, GuardHits = 10, TrailLength = 64, DroneGap = 12;
    public double X, Y, FireCool, MissileCool, DeadTime, Safe, Bank;
    public int Speed, Drones, Guard, Gauge = -1;
    public bool Missile, Twin, Laser, Dead, PowerPrev;
    /// <summary>Where the ship has been on the screen, newest last, for the drones to follow.</summary>
    public double[] TrailX = new double[TrailLength], TrailY = new double[TrailLength];
    public int TrailHead;

    public double Speed0 => 170 + Speed * 60;

    public Ship Copy()
    {
        var s = (Ship)MemberwiseClone();
        s.TrailX = (double[])TrailX.Clone();
        s.TrailY = (double[])TrailY.Clone();
        return s;
    }

    /// <summary>Where drone i (0 nearest) is on the screen.</summary>
    public (double X, double Y) Drone(int i)
    {
        int k = ((TrailHead - (i + 1) * DroneGap) % TrailLength + TrailLength) % TrailLength;
        return (TrailX[k], TrailY[k]);
    }

    /// <summary>Whether the gauge's lit box can be taken now.</summary>
    public bool CanTake(Power p) => p switch
    {
        Power.Speed => Speed < MaxSpeed,
        Power.Missile => !Missile,
        Power.Twin => !Twin,
        Power.Laser => !Laser,
        Power.Drone => Drones < MaxDrones,
        _ => Guard <= 0,
    };
}

public enum ShotKind { Bullet, Twin, Laser, Missile }

/// <summary>Something the ship (or a drone) fired.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy, Length;
    public ShotKind Kind;
    /// <summary>Which gun: 0 the ship, then the drones.</summary>
    public int Gun;
    /// <summary>A missile that has come down onto the ground and runs along it.</summary>
    public bool Running, Dead;
    public Shot Copy() => (Shot)MemberwiseClone();
}

public sealed class Enemy
{
    public Foe Kind;
    public int Id, Hp = 1, Pattern, State, Wave = -1, Level = 1;
    public double X, Y, Vx, Vy, T, FireIn, Hurt, LaserCool, HomeY;
    /// <summary>The Bastion's shield plates still standing in front of its core, and how much the outer one can take.</summary>
    public int Plates, PlateHp;
    public bool Red, Roof, Boss, Dead, Entered;
    public Enemy Copy() => (Enemy)MemberwiseClone();

    /// <summary>Half its width and height, for hits.</summary>
    public (double W, double H) Size => Kind switch
    {
        Foe.Darter => (14, 10),
        Foe.Spinner => (14, 14),
        Foe.Hatch => (30, 18),
        Foe.Walker => (16, 18),
        Foe.Turret => (16, 14),
        Foe.Boulder => (14, 14),
        Foe.Sentinel => (16, 40),
        Foe.Ring => (12, 12),
        Foe.Polyp => (20, 16),
        Foe.Spore => (8, 8),
        Foe.Bastion => (110, 70),
        Foe.Queen => (90, 120),
        _ => (50, 50),
    };
}

/// <summary>Something an enemy fired: a pellet, or (with a length) one of the Bastion's beams.</summary>
public sealed class Missile
{
    public double X, Y, Vx, Vy, Length;
    public bool Dead;
    public Missile Copy() => (Missile)MemberwiseClone();
}

/// <summary>A power capsule, left where a red flier (or the last of a red line) was shot down.</summary>
public sealed class Capsule
{
    public double X, Y, T;
    public bool Dead;
    public Capsule Copy() => (Capsule)MemberwiseClone();
}

public enum EffectKind { Score, Burst, Word, Spark, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

/// <summary>
/// Everything in a stage that moves or changes, kept together so it can be copied whole: the computer pilot
/// tries its moves out on a copy before making them.
/// </summary>
public sealed class World
{
    public StageDef Stage = StageBook.All[0];
    public int StageIndex, Round = 1;
    public Ship Ship = new();
    public List<Shot> Shots = [];
    public List<Enemy> Enemies = [];
    public List<Missile> Missiles = [];
    public List<Capsule> Capsules = [];
    public List<Effect> Effects = [];
    /// <summary>The squares as they are now: glass shot away, cells burst (and growing back).</summary>
    public Tile[,] Grid = new Tile[0, 0];
    public List<(int C, int R, double T)> Regrow = [];
    public double CamX, Clock, BossTime, VolcanoIn;
    public int Score, NextSpawn, NextId;
    /// <summary>How many of each line of fliers are still flying (or -1, once one has got away).</summary>
    public List<int> Lines = [];
    /// <summary>The stage has stopped for the fight at the end; the boss is here; it's beaten; it's gone (beaten or not).</summary>
    public bool BossLock, BossUp, BossDown, Done;
    /// <summary>The boss was beaten (rather than leaving).</summary>
    public bool BossBeaten;
    public Dice Dice;
    /// <summary>Sounds asked for this step (the game plays them; a copy's are thrown away).</summary>
    public List<Sfx> Heard = [];
    public string Cause = "";

    public double Speed => Field.Scroll * (Round > 1 ? 1.15 : 1);

    public World Copy() => new()
    {
        Stage = Stage, StageIndex = StageIndex, Round = Round, Ship = Ship.Copy(),
        Shots = Shots.Select(s => s.Copy()).ToList(),
        Enemies = Enemies.Select(e => e.Copy()).ToList(),
        Missiles = Missiles.Select(m => m.Copy()).ToList(),
        Capsules = Capsules.Select(c => c.Copy()).ToList(),
        Grid = (Tile[,])Grid.Clone(), Regrow = [.. Regrow],
        CamX = CamX, Clock = Clock, BossTime = BossTime, VolcanoIn = VolcanoIn,
        Score = Score, NextSpawn = NextSpawn, NextId = NextId, Lines = [.. Lines],
        BossLock = BossLock, BossUp = BossUp, BossDown = BossDown, Done = Done, BossBeaten = BossBeaten, Dice = Dice, Cause = Cause,
    };

    public Tile At(int c, int r) => r < 0 || r >= Field.Rows || c < 0 || c >= Stage.Cols ? Tile.Empty : Grid[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor((y - Field.Top) / Field.Tile));
    public static bool Solid(Tile t) => t != Tile.Empty;
}
