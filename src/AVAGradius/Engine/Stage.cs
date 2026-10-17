namespace AVAGradius.Engine;

/// <summary>Up, down, left and right (up the screen is -1), the power-up button, and fire (held for a stream of shots).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Power = false, bool Fire = false);

/// <summary>The screen: the score along the top, the stage 32 squares wide and 20 high, and the power gauge below.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 48;
    public const double Tile = 32;
    public const int Rows = 20, ViewCols = 32;
    public const double Top = Hud, Bottom = Top + Rows * Tile;
    /// <summary>How fast the stage goes by, in pixels a second.</summary>
    public const double Scroll = 80;
}

/// <summary>What a square of a stage is.</summary>
public enum Tile : byte
{
    Empty,
    /// <summary>Rock (or a citadel's steel): touching it is the end of the ship.</summary>
    Rock,
    /// <summary>Glass, or a citadel's shutter: as deadly as rock, but a shot breaks it.</summary>
    Crystal,
    /// <summary>The hive's living wall: a shot bursts it, and it grows back.</summary>
    Cell,
}

public enum Foe
{
    /// <summary>The fliers that come in lines; a whole red line shot down leaves a capsule.</summary>
    Darter,
    /// <summary>A lone spinning craft that edges toward the ship's height.</summary>
    Spinner,
    /// <summary>A hatch on the ground or the roof that lets out darters.</summary>
    Hatch,
    /// <summary>A two-legged gun that walks the ground (or the roof) and fires at the ship.</summary>
    Walker,
    Turret,
    /// <summary>A rock flung up out of a volcano.</summary>
    Boulder,
    /// <summary>A glass spire that sends out drifting rings.</summary>
    Sentinel,
    Ring,
    /// <summary>A growth in the hive's walls that spits spores.</summary>
    Polyp,
    Spore,
    // The ends of the stages.
    Bastion, Queen, Mind,
}

/// <summary>
/// Something placed in a stage, coming to life when the stage reaches it: a line of fliers (with how they fly), or
/// something on the ground or the roof.
/// </summary>
public readonly record struct Spawn(int Col, Foe Kind, double Row = 10, int Count = 1, int Pattern = 0, bool Red = false, bool Roof = false);

/// <summary>
/// A stage: its squares, what comes at the ship and where, its volcanoes, where a lost ship starts again, and what
/// waits at the end.
/// </summary>
public sealed class StageDef
{
    public string Name = "";
    public int Theme, Cols;
    /// <summary>The squares, [column, row].</summary>
    public Tile[,] Grid = new Tile[0, 0];
    /// <summary>How deep the rock of the ground and of the roof is at each column, in squares.</summary>
    public int[] Floor = [], Roof = [];
    public List<Spawn> Spawns = [];
    public List<(int Col, int Row)> Volcanoes = [];
    public int[] Checkpoints = [0];
    /// <summary>The column at the screen's right edge when the stage stops for the fight at the end.</summary>
    public int BossCol;
    public Foe Boss;

    public Tile At(int c, int r) => r < 0 || r >= Field.Rows || c < 0 || c >= Cols ? Tile.Empty : Grid[c, r];

    /// <summary>The top of the ground at a column, in screen pixels.</summary>
    public double FloorY(int c) => Field.Bottom - Floor[Math.Clamp(c, 0, Cols - 1)] * Field.Tile;
    /// <summary>The underside of the roof at a column, in screen pixels.</summary>
    public double RoofY(int c) => Field.Top + Roof[Math.Clamp(c, 0, Cols - 1)] * Field.Tile;
}

/// <summary>
/// Building a stage: the ground and the roof as heights along it (wandering, flat, peaked), walls of glass or
/// living cells across the way, and what comes where.
/// </summary>
public sealed class StageBuilder
{
    private readonly StageDef _s;
    private readonly List<(int C0, int C1, int R0, int R1, Tile Kind)> _walls = [];
    private Dice _dice;

    public StageBuilder(string name, int theme, int cols, int seed)
    {
        _s = new StageDef { Name = name, Theme = theme, Cols = cols, Floor = new int[cols], Roof = new int[cols] };
        _dice = new Dice(seed);
    }

    /// <summary>Ground and roof at fixed depths from column c0 to c1.</summary>
    public StageBuilder Flat(int c0, int c1, int floor, int roof)
    {
        for (int c = c0; c <= c1; c++) { _s.Floor[c] = floor; _s.Roof[c] = roof; }
        return this;
    }

    /// <summary>Ground and roof wandering up and down, a square at a time, never closing to less than <paramref name="gap"/> rows.</summary>
    public StageBuilder Wander(int c0, int c1, int floorMin, int floorMax, int roofMin, int roofMax, int gap = 8)
    {
        int f = Math.Clamp(_s.Floor[Math.Max(0, c0 - 1)], floorMin, floorMax), r = Math.Clamp(_s.Roof[Math.Max(0, c0 - 1)], roofMin, roofMax);
        for (int c = c0; c <= c1; c++)
        {
            if (c % 2 == 0)
            {
                f = Math.Clamp(f + _dice.Next(3) - 1, floorMin, floorMax);
                r = Math.Clamp(r + _dice.Next(3) - 1, roofMin, roofMax);
                while (Field.Rows - f - r < gap) { if (f > floorMin) f--; else r--; }
            }
            _s.Floor[c] = f;
            _s.Roof[c] = r;
        }
        return this;
    }

    /// <summary>A mountain rising from the ground (a volcano, if it erupts), its peak <paramref name="height"/> squares up.</summary>
    public StageBuilder Peak(int col, int height, int half, bool volcano = false)
    {
        for (int c = col - half; c <= col + half; c++)
            if (c >= 0 && c < _s.Cols)
                _s.Floor[c] = Math.Max(_s.Floor[c], height - Math.Abs(c - col) * height / (half + 1));
        if (volcano) _s.Volcanoes.Add((col, Field.Rows - height));
        return this;
    }

    /// <summary>A crag hanging from the roof.</summary>
    public StageBuilder Crag(int col, int depth, int half)
    {
        for (int c = col - half; c <= col + half; c++)
            if (c >= 0 && c < _s.Cols)
                _s.Roof[c] = Math.Max(_s.Roof[c], depth - Math.Abs(c - col) * depth / (half + 1));
        return this;
    }

    /// <summary>A wall of glass (or cells) across the open way, from column c0 to c1, to be shot through.</summary>
    public StageBuilder Wall(int c0, int c1, Tile kind = Tile.Crystal, int r0 = 0, int r1 = Field.Rows - 1)
    {
        _walls.Add((c0, c1, r0, r1, kind));
        return this;
    }

    /// <summary>A line of fliers coming in at a row: pattern 0 swoops at the ship, 1 weaves, 2 comes from behind.</summary>
    public StageBuilder Line(int col, double row, int count = 5, int pattern = 0, bool red = false)
    {
        _s.Spawns.Add(new Spawn(col, Foe.Darter, row, count, pattern, red));
        return this;
    }

    /// <summary>Lines of fliers every few columns, at different heights and ways, two lines in three red.</summary>
    public StageBuilder Lines(int c0, int c1, int every, int seed)
    {
        var d = new Dice(seed);
        int n = 0;
        for (int c = c0; c <= c1; c += every, n++)
        {
            int pattern = d.Next(3);
            double row = pattern == 2 ? (n % 2 == 0 ? 4 : 15) : 5 + d.Next(10);
            Line(c, row, 4 + d.Next(3), pattern, n % 3 != 2);
        }
        return this;
    }

    public StageBuilder Spinner(int col, double row, bool red = false)
    {
        _s.Spawns.Add(new Spawn(col, Foe.Spinner, row, 1, 0, red));
        return this;
    }

    /// <summary>Something on the ground (or, with <paramref name="roof"/>, hanging from the roof) at a column.</summary>
    public StageBuilder On(int col, Foe kind, bool roof = false)
    {
        _s.Spawns.Add(new Spawn(col, kind, 0, 1, 0, false, roof));
        return this;
    }

    public StageDef End(Foe boss, params int[] checkpoints)
    {
        _s.Boss = boss;
        _s.BossCol = _s.Cols;
        _s.Checkpoints = checkpoints;
        _s.Grid = new Tile[_s.Cols, Field.Rows];
        for (int c = 0; c < _s.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
            _s.Grid[c, r] = r < _s.Roof[c] || r >= Field.Rows - _s.Floor[c] ? Tile.Rock : Tile.Empty;
        foreach (var (c0, c1, r0, r1, kind) in _walls)
            for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
                if (_s.Grid[c, r] == Tile.Empty) _s.Grid[c, r] = kind;
        _s.Spawns.Sort((a, b) => a.Col.CompareTo(b.Col));
        return _s;
    }
}

/// <summary>The four strongholds of the Varkh, from the fire ridges to the citadel of the Mind.</summary>
public static class StageBook
{
    public static readonly StageDef[] All = [FireRidges(), GlassReef(), Hive(), Citadel()];

    /// <summary>Open space, then ridges with walkers, guns and hatches, two volcanoes, and the Bastion.</summary>
    private static StageDef FireRidges() =>
        new StageBuilder("THE FIRE RIDGES", 0, 236, 11)
            .Flat(0, 24, 0, 0)
            .Wander(25, 60, 1, 4, 0, 1)
            .Wander(61, 120, 2, 5, 1, 4)
            .Flat(121, 175, 2, 2)
            .Peak(132, 9, 7, volcano: true).Peak(158, 10, 8, volcano: true)
            .Wander(176, 200, 1, 4, 1, 4)
            .Flat(201, 235, 1, 1)
            .Lines(6, 56, 8, 1)
            .Spinner(22, 6, red: true)
            .On(34, Foe.Turret).On(40, Foe.Walker).On(46, Foe.Hatch).On(52, Foe.Turret, roof: true)
            .Lines(64, 116, 10, 2)
            .On(66, Foe.Walker).On(72, Foe.Turret, roof: true).On(78, Foe.Hatch, roof: true).On(84, Foe.Walker)
            .On(92, Foe.Turret).On(98, Foe.Walker, roof: true).On(104, Foe.Hatch).On(112, Foe.Turret, roof: true)
            .Line(124, 4, 5, 1, red: true).Line(146, 4, 5, 1).Line(168, 5, 5, 1, red: true)
            .Spinner(140, 3, red: true)
            .Lines(180, 200, 9, 3)
            .On(186, Foe.Walker).On(192, Foe.Turret, roof: true).On(196, Foe.Hatch)
            .End(Foe.Bastion, 0, 64, 122, 202);

    /// <summary>A reef of glass: walls to shoot through, spires sending rings, and a fiercer Bastion.</summary>
    private static StageDef GlassReef() =>
        new StageBuilder("THE GLASS REEF", 1, 236, 23)
            .Flat(0, 16, 1, 1)
            .Wander(17, 70, 1, 5, 1, 5, 9)
            .Wall(30, 31, r0: 2, r1: 17).Wall(56, 56, r0: 2, r1: 17)
            .Flat(71, 90, 3, 3)
            .Wall(78, 79)
            .Peak(100, 7, 4).Crag(112, 7, 4).Peak(124, 7, 4).Crag(136, 7, 4)
            .Wander(140, 196, 2, 5, 2, 5, 9)
            .Wall(150, 151).Wall(166, 166).Wall(184, 185)
            .Flat(197, 235, 1, 1)
            .Lines(4, 26, 7, 4)
            .On(36, Foe.Sentinel).On(44, Foe.Sentinel, roof: true).On(50, Foe.Walker)
            .Spinner(48, 9, red: true).Spinner(62, 5, red: true)
            .On(66, Foe.Turret, roof: true).Lines(60, 74, 7, 5)
            .On(86, Foe.Sentinel).On(88, Foe.Sentinel, roof: true)
            .Line(96, 3, 5, 1, red: true).Line(118, 16, 5, 1).Spinner(128, 10, red: true)
            .On(104, Foe.Walker, roof: true).On(120, Foe.Walker)
            .On(144, Foe.Hatch).On(158, Foe.Sentinel, roof: true).On(162, Foe.Turret)
            .Lines(170, 196, 8, 6)
            .On(176, Foe.Sentinel).On(190, Foe.Walker, roof: true)
            .End(Foe.Bastion, 0, 72, 140, 200);

    /// <summary>The hive: tunnels of living cells that grow back, polyps spitting spores, and the Queen.</summary>
    private static StageDef Hive() =>
        new StageBuilder("THE HIVE", 2, 236, 37)
            .Flat(0, 14, 2, 2)
            .Wander(15, 66, 2, 6, 2, 6, 8)
            .Wall(28, 29, Tile.Cell).Wall(46, 46, Tile.Cell).Wall(60, 61, Tile.Cell)
            .Flat(67, 84, 4, 4)
            .Wall(74, 76, Tile.Cell, 4, 9)
            .Wander(85, 150, 2, 6, 2, 6, 8)
            .Wall(98, 99, Tile.Cell).Wall(118, 118, Tile.Cell).Wall(136, 137, Tile.Cell)
            .Flat(151, 170, 3, 3)
            .Wander(171, 198, 2, 5, 2, 5, 9)
            .Wall(184, 185, Tile.Cell)
            .Flat(199, 235, 2, 2)
            .Lines(4, 22, 7, 7)
            .On(34, Foe.Polyp).On(40, Foe.Polyp, roof: true).On(54, Foe.Polyp)
            .Spinner(44, 10, red: true)
            .Lines(68, 90, 8, 8)
            .On(92, Foe.Polyp, roof: true).On(104, Foe.Hatch).On(110, Foe.Polyp).On(124, Foe.Polyp, roof: true)
            .Spinner(114, 9, red: true).Line(130, 10, 5, 1, red: true)
            .On(144, Foe.Hatch, roof: true)
            .Lines(152, 170, 7, 9)
            .On(176, Foe.Polyp).On(180, Foe.Polyp, roof: true).On(192, Foe.Walker)
            .End(Foe.Queen, 0, 68, 152, 200);

    /// <summary>The citadel: steel corridors, guns everywhere, shutters to shoot through, and the Mind in its chamber.</summary>
    private static StageDef Citadel() =>
        new StageBuilder("THE CITADEL", 3, 224, 51)
            .Flat(0, 12, 2, 2)
            .Flat(13, 40, 4, 4)
            .Wall(26, 26)
            .Flat(41, 58, 6, 6)
            .Wall(50, 50)
            .Flat(59, 80, 3, 3)
            .Peak(70, 7, 3).Crag(76, 7, 3)
            .Flat(81, 110, 5, 5).Wall(92, 93).Wall(104, 104)
            .Flat(111, 132, 2, 2)
            .Flat(133, 166, 4, 4).Wall(146, 147).Wall(158, 158)
            .Flat(167, 188, 3, 3)
            // The Mind's chamber, behind walls of cells.
            .Flat(189, 223, 2, 2)
            .Wall(206, 207, Tile.Cell)
            .Lines(3, 11, 5, 10)
            .On(16, Foe.Turret).On(20, Foe.Turret, roof: true).On(30, Foe.Turret).On(34, Foe.Turret, roof: true)
            .On(38, Foe.Hatch)
            .On(44, Foe.Turret).On(48, Foe.Turret, roof: true).On(54, Foe.Walker)
            .Lines(60, 66, 6, 11).Spinner(64, 9, red: true)
            .On(84, Foe.Hatch, roof: true).On(88, Foe.Turret).On(98, Foe.Turret, roof: true).On(100, Foe.Walker)
            .Lines(112, 130, 6, 12)
            .On(136, Foe.Turret).On(140, Foe.Turret, roof: true).On(152, Foe.Hatch).On(162, Foe.Walker, roof: true)
            .Lines(168, 184, 8, 13)
            .On(196, Foe.Turret).On(200, Foe.Turret, roof: true).On(212, Foe.Turret).On(214, Foe.Turret, roof: true)
            .End(Foe.Mind, 0, 59, 111, 167);
}
