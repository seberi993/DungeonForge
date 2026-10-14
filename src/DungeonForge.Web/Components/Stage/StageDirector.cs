using DungeonForge.Web.Models;

namespace DungeonForge.Web.Components.Stage;

public sealed record StageCaption(string Who, string Say, string Tag, string TagClass);

public sealed record StageFrame(string Html, string Text, StageCaption Caption);

// Pure and clock-driven: Advance() moves time, Frame() draws it. No timers, so the whole show is unit-testable.
public sealed class StageDirector
{
    public const double IntroMs = 3200;
    public const double ResultMs = 3000;
    private const int FeetY = 16;
    private const int TargetX = Corridor.VanishX;

    private List<MemberDto> _heroes = [];
    private double _clock;

    private int _roomKey = int.MinValue;
    private RoomType? _room;
    private string? _scene;
    private double _roomStart;
    private (int Key, RoomType Room, string? Scene)? _pending;

    private StageShow? _show;
    private StageShow? _ended;
    private double _showStart;
    private int _lastShowTurn = int.MinValue;

    public bool NarrationPending { get; set; }
    public bool IsShowing => _show is not null;
    public event Action<int>? ShowFinished;

    public static double BeatMs(int beats) => Math.Clamp(13000.0 / Math.Max(1, beats), 1500, 2400);

    public static double ShowMs(StageShow show) => show.Beats.Count * BeatMs(show.Beats.Count) + ResultMs;

    public void SetHeroes(IEnumerable<MemberDto> members) => _heroes = [.. members.Take(8)];

    // The room the party is in (key = the turn it belongs to). A new key plays the camera push-in, unless a show is
    // still running: then it waits, so the room never changes under the actors.
    public void SetRoom(int key, RoomType? room, string? scene)
    {
        if (room is not { } r) return;
        if (key == _roomKey && r == _room && scene == _scene) return;
        if (_show is not null)
        {
            _pending = (key, r, scene);
            return;
        }

        ApplyRoom(key, r, scene);
    }

    private void ApplyRoom(int key, RoomType room, string? scene)
    {
        _roomKey = key;
        _room = room;
        _scene = scene;
        _ended = null;
        _roomStart = _clock;
        _pending = null;
    }

    public bool StartShow(StageShow show)
    {
        if (show.Turn <= _lastShowTurn || show.Beats.Count == 0) return false;
        _lastShowTurn = show.Turn;
        _show = show;
        _ended = null;
        _showStart = _clock;
        return true;
    }

    public void Advance(double dtMs)
    {
        _clock += Math.Max(0, dtMs);
        if (_show is not { } show || _clock - _showStart < ShowMs(show)) return;
        _show = null;
        _ended = show;
        if (_pending is { } p) ApplyRoom(p.Key, p.Room, p.Scene);
        ShowFinished?.Invoke(show.Turn);
    }

    private enum Phase { Idle, Beat, Result }

    private int HeroIndex(string name) => _heroes.FindIndex(h => h.Name == name);

    private bool Small => _heroes.Count > 4;

    private (int X, int Y, int PlateX, int PlateY) Home(int i)
    {
        var n = Math.Max(1, _heroes.Count);
        if (!Small)
        {
            var tl = CharGrid.Round(33 + (i - (n - 1) / 2.0) * 15) - 5;
            return (tl, 21, tl + 4, 26);
        }

        var cx = CharGrid.Round(33 + (i - (n - 1) / 2.0) * 8);
        return (cx - 2, 23, cx, 26);
    }

    private static string Effective(RoomType room, ActionVerb verb) => room == RoomType.Combat
        ? verb switch { ActionVerb.Strike => "strike", ActionVerb.Cast => "cast", ActionVerb.Stab => "stab", _ => "heal" }
        : verb switch { ActionVerb.Strike => "pry", ActionVerb.Cast => "detect", ActionVerb.Stab => "pick", _ => "bless" };

    private static bool Melee(string eff) => eff is "strike" or "pry" or "stab" or "pick";

    private static bool Connects(string eff, bool success) => eff is "heal" or "bless" or "detect" || success || eff == "stab";

    private static (string Text, string Class) Tag(string eff, bool success) => (eff, success) switch
    {
        ("strike", true) => ("HIT", "st-fxr"),
        ("strike", false) => ("MISS", "st-fxd"),
        ("cast", true) => ("BURN", "st-fxo"),
        ("cast", false) => ("FIZZLE", "st-fxd"),
        ("stab", true) => ("CRIT", "st-fxy"),
        ("stab", false) => ("GRAZE", "st-fxr"),
        ("heal", true) => ("BLESSED", "st-fxg"),
        ("heal", false) => ("WEAK", "st-fxd"),
        ("pry", true) => ("FORCED", "st-fxy"),
        ("pry", false) => ("STUCK", "st-fxd"),
        ("detect", true) => ("TRAP SPOTTED", "st-fxc"),
        ("detect", false) => ("NOTHING", "st-fxd"),
        ("pick", true) => ("CLICK", "st-fxy"),
        ("pick", false) => ("SNAP!", "st-fxr"),
        ("bless", true) => ("WARDED", "st-fxg"),
        _ => ("NO EFFECT", "st-fxd")
    };

    private static string Label(MemberDto h, int max) => h.Name.Length <= max ? h.Name : h.Name[..(max - 1)] + ".";

    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public StageFrame Frame()
    {
        var show = _show ?? _ended;
        var phase = Phase.Idle;
        var beat = -1;
        double p = 0;
        if (_show is { } live)
        {
            var bm = BeatMs(live.Beats.Count);
            var t = _clock - _showStart;
            if (t < live.Beats.Count * bm)
            {
                phase = Phase.Beat;
                beat = (int)(t / bm);
                p = (t - beat * bm) / bm;
            }
            else
            {
                phase = Phase.Result;
                p = Math.Min(1, (t - live.Beats.Count * bm) / ResultMs);
            }
        }
        else if (_ended is not null)
        {
            phase = Phase.Result;
            p = 1;
        }

        var intro = show is null ? Math.Clamp((_clock - _roomStart) / IntroMs, 0, 1) : 1;
        var room = show?.Room ?? _room;
        var scene = show is not null ? show.Scene : _scene;
        var success = show?.Success ?? false;
        var combat = room == RoomType.Combat;

        var g = new CharGrid(Corridor.Width, Corridor.Height);
        Corridor.Draw(g, _clock, intro);

        /* --- the foe / prop at the far end --- */
        var baseRows = StageArt.SceneRows(scene, room, 0);
        var foeH = Math.Max(1, baseRows.Count);
        var target = (X: TargetX, Y: FeetY - foeH + 1 + foeH / 2);
        var killBeat = -1;
        if (show is not null && combat)
        {
            for (var b = 0; b < show.Beats.Count; b++)
            {
                if (Effective(RoomType.Combat, show.Beats[b].Verb) is "strike" or "cast" or "stab") killBeat = b;
            }
        }

        var hitWin = false;
        if (show is not null && phase == Phase.Beat && p >= 0.58 && p < 0.8)
        {
            var eff = Effective(show.Room, show.Beats[beat].Verb);
            hitWin = combat
                ? eff is "strike" or "cast" or "stab" && Connects(eff, show.Success)
                : eff == "pry" || (eff == "pick" && show.Success);
        }

        var foeVisible = room is not null && !(phase == Phase.Idle && intro < 0.55);
        if (foeVisible)
        {
            DrawFoe(g, show, phase, beat, p, intro, scene, room!.Value, success, killBeat, hitWin);
        }

        /* --- heroes --- */
        var states = new List<HeroState>();
        for (var i = 0; i < _heroes.Count; i++) states.Add(HeroAt(i, show, phase, beat, p, intro, baseRows));
        foreach (var i in Enumerable.Range(0, _heroes.Count).OrderBy(i => states[i].Y))
        {
            var s = states[i];
            g.Sprite(s.X, s.Y, s.Rows, s.Flash ? "st-fxr" : "st-" + _heroes[i].Class.ToString().ToLowerInvariant());
            if (s.Active && p < 0.92) g.Set(Home(i).X + (Small ? 2 : 4), Home(i).Y - 1, '▼', "st-mk");
        }

        if (show is not null && room is not null) DrawFx(g, show, phase, beat, p, states, target);
        DrawPlates(g, show, phase, beat, p);

        var caption = Caption(show, phase, beat, p, intro);
        return new StageFrame(g.ToHtml(), g.ToPlainText(), caption);
    }

    private void DrawFoe(CharGrid g, StageShow? show, Phase phase, int beat, double p, double intro, string? scene, RoomType room, bool success, int killBeat, bool hitWin)
    {
        var combat = room == RoomType.Combat;
        var cls = combat ? "st-foe" : "st-chest";
        IReadOnlyList<string> rows;
        double dy = 0;
        var dx = 0;
        if (combat)
        {
            var dead = show is not null && success && (killBeat < 0
                ? phase == Phase.Result && p >= 0.1
                : phase == Phase.Result || beat > killBeat || (beat == killBeat && p >= 0.58));
            var frame = (int)(_clock / 600) % 2;
            rows = StageArt.SceneRows(scene, room, frame);
            if (hitWin)
            {
                rows = StageArt.Struck(rows);
                dx = 1;
                if ((int)(_clock / 110) % 2 == 1) cls = "st-fxr";
            }
            else if (dead)
            {
                rows = StageArt.Rubble;
            }
            else if (show is not null && !success && phase == Phase.Result && p > 0.15)
            {
                var f = p < 0.15 ? 0 : p < 0.4 ? Ease((p - 0.15) / 0.25) : p < 0.55 ? 1 : 1 - Ease((p - 0.55) / 0.3);
                dy = Lerp(0, 2, f);
            }
            else if (_clock % 3300 < 150)
            {
                rows = [.. rows.Select(r => r.Replace("o,o", "-,-").Replace("o o", "- -"))];
            }
        }
        else
        {
            var open = show is not null && success && phase == Phase.Result && (p > 0.15 || _ended is not null);
            rows = StageArt.SceneRows(scene, room, open ? 1 : 0);
            if (hitWin) dx = (int)(_clock / 70) % 2 == 1 ? 1 : -1;
            if (show is not null && success && phase == Phase.Result && p is > 0.05 and < 0.15) dx = (int)(_clock / 70) % 2 == 1 ? 1 : -1;
        }

        var w = StageArt.Width(rows);
        g.Sprite(Corridor.VanishX - w / 2 + dx, FeetY - rows.Count + 1 + dy, rows, cls);
    }

    private sealed record HeroState(double X, double Y, IReadOnlyList<string> Rows, bool Active, bool Flash, double Reach, int Verb);

    private HeroState HeroAt(int i, StageShow? show, Phase phase, int beat, double p, double intro, IReadOnlyList<string> baseRows)
    {
        var hero = _heroes[i];
        var home = Home(i);
        double x = home.X;
        double y = home.Y;
        var act = false;
        var active = false;
        double reach = 0;
        var small = Small;
        var archetype = StageArt.ArchetypeOf(hero.Class);

        if (phase == Phase.Idle && intro < 1)
        {
            var q = Ease((intro - 0.35 - 0.1 * i) / 0.4);
            x = home.X - (1 - q) * (home.X + 16);
        }

        var shownHp = ShownHp(hero, show, phase, p);
        if (shownHp <= 0)
        {
            return new HeroState(x, small ? y : y + 2, StageArt.Fallen, false, false, 0, -1);
        }

        var verb = -1;
        if (show is not null && phase == Phase.Beat && beat >= 0 && HeroIndex(show.Beats[beat].Name) == i)
        {
            active = true;
            var eff = Effective(show.Room, show.Beats[beat].Verb);
            verb = (int)show.Beats[beat].Verb;
            if (Melee(eff))
            {
                reach = p < 0.12 ? 0 : p < 0.5 ? Ease((p - 0.12) / 0.38) : p < 0.68 ? 1 : 1 - Ease((p - 0.68) / 0.32);
                var foeLeft = Corridor.VanishX - Math.Max(StageArt.Width(baseRows), 4) / 2;
                var approachX = Math.Max(0, foeLeft - 6);
                x = Lerp(home.X, approachX, reach);
                y = Lerp(home.Y, 16, reach);
                act = p is > 0.12 and < 0.72;
            }
            else
            {
                act = p is > 0.1 and < 0.85;
            }
        }

        var useSmall = small || y < 19.5;
        var rows = StageArt.Hero(archetype, useSmall, act);
        if (!act && (_clock + i * 700) % 3600 < 160)
        {
            rows = [.. rows.Select(r => r.Replace("o.o", "-.-").Replace("(o)", "(-)"))];
        }

        var flash = show is not null && !show.Success && phase == Phase.Result && p is >= 0.45 and < 0.65
            && show.Damage > 0 && show.TargetName == hero.Name && (int)(_clock / 100) % 2 == 0;
        return new HeroState(x, y, rows, active, flash, reach, verb);
    }

    // The hero hit this turn keeps their old HP on screen until the blow lands.
    private static int ShownHp(MemberDto hero, StageShow? show, Phase phase, double p) =>
        show is not null && show.TargetName == hero.Name && show.Damage > 0 && (phase == Phase.Beat || (phase == Phase.Result && p < 0.45))
            ? Math.Min(hero.MaxHp, hero.Hp + show.Damage)
            : hero.Hp;

    private static void Float(CharGrid g, string text, double cx, double y, string cls) =>
        g.Text(CharGrid.Round(cx - text.Length / 2.0), Math.Max(0, CharGrid.Round(y)), text, cls);

    private static void Slash(CharGrid g, int x, int y, double q, string cls)
    {
        var n = Math.Min(3, (int)Math.Ceiling(q * 3 + 0.01));
        for (var s = 0; s < n; s++)
        {
            for (var j = 0; j < 4; j++) g.Set(x - 4 + s * 3 + j, y - 2 + j, '\\', cls);
        }
    }

    private static void Burst(CharGrid g, int x, int y, double q, string cls)
    {
        var r = 1 + (int)Math.Floor(q * 3);
        g.Set(x, y, '*', cls);
        g.Set(x - r * 2, y - r, '\\', cls); g.Set(x, y - r, '|', cls); g.Set(x + r * 2, y - r, '/', cls);
        g.Set(x - r * 2, y, '-', cls); g.Set(x + r * 2, y, '-', cls);
        g.Set(x - r * 2, y + r, '/', cls); g.Set(x, y + r, '|', cls); g.Set(x + r * 2, y + r, '\\', cls);
    }

    private static void Banner(CharGrid g, string text, string cls)
    {
        var x = Corridor.VanishX - text.Length / 2;
        g.Clear(x - 1, 1, text.Length + 2, 1);
        g.Text(x, 1, text, cls);
    }

    private static char Projectile(double q) => q < 0.3 ? 'O' : q < 0.6 ? 'o' : '*';

    private void DrawFx(CharGrid g, StageShow show, Phase phase, int beat, double p, List<HeroState> states, (int X, int Y) target)
    {
        var room = show.Room;
        var tx = target.X;
        var ty = target.Y;
        if (phase == Phase.Beat)
        {
            var b = show.Beats[beat];
            var i = HeroIndex(b.Name);
            if (i < 0) return;
            var eff = Effective(room, b.Verb);
            var connects = Connects(eff, show.Success);
            var h = states[i];
            var home = Home(i);
            var (tag, tagClass) = Tag(eff, show.Success);
            switch (eff)
            {
                case "strike":
                    if (p is > 0.5 and < 0.75)
                    {
                        if (connects) Slash(g, tx, ty, (p - 0.5) / 0.25, "st-fxy");
                        else g.Text(tx - 9, ty - 1, "~~~~~", "st-fxd");
                    }

                    break;
                case "pry":
                    if (p is > 0.5 and < 0.78)
                    {
                        if (connects) Slash(g, tx, ty, (p - 0.5) / 0.28, "st-fxy");
                        else Float(g, "clang", tx, ty - 1, "st-fxd");
                        for (var j = 0; j < 5; j++) g.Set(tx + (j * 7 + (int)(_clock / 90)) % 9 - 4, ty - 2 + j * 3 % 5, '*', "st-fxy");
                    }

                    break;
                case "cast":
                    {
                        var tipIndex = Math.Max(0, h.Rows[0].IndexOf('*'));
                        var sx = h.X + tipIndex;
                        var sy = h.Y;
                        if (p is > 0.3 and < 0.62)
                        {
                            var q = (p - 0.3) / 0.32;
                            var qq = connects ? q : Math.Min(q, 0.55);
                            double Px(double u) => Lerp(sx, tx, u);
                            double Py(double u) => Lerp(sy, ty, u) - Math.Sin(u * Math.PI) * 2;
                            for (var j = 1; j <= 4; j++)
                            {
                                var u = Math.Max(0, qq - j * 0.06);
                                g.Set(Px(u), Py(u), "*+.."[j - 1], "st-fxo");
                            }

                            g.Set(Px(qq), Py(qq), Projectile(qq), "st-fxr");
                            if (!connects && q > 0.55) Float(g, "pfft", Px(qq), Py(qq) - 1, "st-fxd");
                        }
                        else if (p is >= 0.62 and < 0.8 && connects)
                        {
                            Burst(g, tx, ty, (p - 0.62) / 0.18, "st-fxo");
                        }

                        break;
                    }
                case "detect":
                    if (p is > 0.25 and < 0.85)
                    {
                        var rr = (p - 0.25) / 0.6 * 10;
                        for (var a = 0; a < 28; a++)
                        {
                            var ang = a / 28.0 * 2 * Math.PI;
                            g.Set(tx + Math.Cos(ang) * rr * 1.8, ty + Math.Sin(ang) * rr * 0.55, a % 2 == 1 ? '.' : '*', "st-fxc");
                        }

                        if (p > 0.55) Float(g, show.Success ? "(!)" : "(?)", tx, ty - 4, tagClass);
                    }

                    break;
                case "stab":
                    if (p < 0.5 && h.Reach > 0.05)
                    {
                        for (var xx = home.X + 6; xx < h.X; xx += 2) g.Set(xx, h.Y + 1, '=', "st-fxd");
                    }

                    if (p is > 0.5 and < 0.72 && connects)
                    {
                        g.Text(tx - 1, ty - 1, @"\ /", "st-fxy");
                        g.Text(tx - 1, ty, " X ", "st-fxy");
                        g.Text(tx - 1, ty + 1, @"/ \", "st-fxy");
                    }

                    break;
                case "pick":
                    if (p is > 0.5 and < 0.78) Float(g, (int)(_clock / 120) % 2 == 1 ? ".click." : "*click*", tx, ty - 2, show.Success ? "st-fxy" : "st-fxr");
                    break;
                case "heal":
                    if (p is > 0.28 and < 0.9)
                    {
                        var cx = home.PlateX;
                        for (var j = 0; j < 6; j++) g.Set(cx - 5 + j * 2, home.Y + 4 - (p * 14 + j * 1.7) % 6, '+', "st-fxg");
                        g.Text(cx - 1, home.Y - 1, "(+)", "st-fxg");
                    }

                    break;
                case "bless":
                    if (p is > 0.28 and < 0.9)
                    {
                        g.Text(tx - 3, ty - 5, @"\ | /", "st-fxy");
                        g.Text(tx - 2, ty - 4, @"\|/", "st-fxy");
                        for (var j = 0; j < 4; j++) g.Set(tx - 4 + j * 3, ty - 3 - (p * 12 + j) % 4, '*', "st-fxy");
                    }

                    break;
            }

            if (p >= 0.58)
            {
                var rise = (p - 0.58) * 5;
                if (eff is "heal") Float(g, tag, home.PlateX, home.Y - 2 - rise, tagClass);
                else Float(g, tag, tx, ty - 4 - rise, tagClass);
            }

            return;
        }

        /* --- result --- */
        var rp = phase == Phase.Result ? p : 1;
        if (show.Success)
        {
            var word = room switch { RoomType.Combat => "*  VICTORY  *", RoomType.Chest => "*  UNLOCKED  *", _ => "*  SOLVED  *" };
            if (rp > 0.3 && (int)(_clock / 400) % 3 != 2) Banner(g, word, "st-fxy");
            if (room != RoomType.Combat && rp > 0.15)
            {
                for (var j = 0; j < 9; j++)
                {
                    g.Set(tx - 6 + (j * 5 + (int)(_clock / 130)) % 13, ty - 3 - (j * 3 + (int)(_clock / 160)) % 4, j % 2 == 1 ? '*' : '+', "st-fxy");
                }

                if (show.GoldFound > 0) Float(g, $"+{show.GoldFound} gold", tx, ty - 7 - Math.Min(2, (rp - 0.3) * 4), "st-fxy");
            }

            return;
        }

        var failWord = "!  SETBACK  !";
        if (rp > 0.3 && (int)(_clock / 400) % 3 != 2) Banner(g, failWord, "st-fxr");
        if (room != RoomType.Combat && rp is > 0.2 and < 0.55) Burst(g, tx, ty, (rp - 0.2) / 0.35, "st-fxr");
        if (room != RoomType.Combat && rp > 0.35)
        {
            for (var j = 0; j < 5; j++) g.Set(tx - 6 + j * 3, ty - 3 - (rp * 10 + j) % 5, '~', "st-fxd");
        }

        if (rp > 0.45 && show.Damage > 0 && HeroIndex(show.TargetName ?? "") is var hi and >= 0)
        {
            Float(g, $"-{show.Damage}", Home(hi).PlateX, Home(hi).Y - 2 - Math.Min(3, (rp - 0.45) * 8), "st-fxr");
        }
    }

    private void DrawPlates(CharGrid g, StageShow? show, Phase phase, int beat, double p)
    {
        var small = Small;
        var nameMax = small ? 7 : 10;
        var barWidth = small ? 6 : 8;
        for (var i = 0; i < _heroes.Count; i++)
        {
            var hero = _heroes[i];
            var home = Home(i);
            var hp = ShownHp(hero, show, phase, p);
            var activeBeat = show is not null && phase == Phase.Beat && beat >= 0 && HeroIndex(show.Beats[beat].Name) == i;
            var label = Label(hero, nameMax);
            g.Clear(home.PlateX - (nameMax + 2) / 2, home.PlateY, nameMax + 2, 2);
            g.Text(home.PlateX - label.Length / 2, home.PlateY, label, activeBeat ? "st-name st-act" : "st-name");
            var filled = hp <= 0 ? 0 : Math.Max(1, (int)Math.Round(Math.Clamp((double)hp / Math.Max(1, hero.MaxHp), 0, 1) * barWidth));
            var bar = new string('█', filled) + new string('░', barWidth - filled);
            g.Text(home.PlateX - barWidth / 2, home.PlateY + 1, bar, hp * 10 <= hero.MaxHp * 3 ? "st-hpL" : "st-hpG");
        }
    }

    private StageCaption Caption(StageShow? show, Phase phase, int beat, double p, double intro)
    {
        if (show is not null && phase == Phase.Beat && beat >= 0)
        {
            var b = show.Beats[beat];
            var eff = Effective(show.Room, b.Verb);
            var (tag, tagClass) = Tag(eff, show.Success);
            return new StageCaption($"{b.Name} · {b.Class}", $"“{b.Text}”", p >= 0.58 ? tag : "", p >= 0.58 ? tagClass : "");
        }

        if (show is not null && _show is not null)
        {
            return new StageCaption("Outcome", ResultLine(show), "", "");
        }

        if (NarrationPending)
        {
            var dots = new string('.', 1 + (int)(_clock / 500) % 3);
            return new StageCaption("The dungeon master", "is writing what happens next" + dots, "", "");
        }

        if (show is not null)
        {
            return new StageCaption("Outcome", ResultLine(show), "", "");
        }

        var room = show?.Room ?? _room;
        return room is null
            ? new StageCaption("The hall", "The party waits in the dark.", "", "")
            : new StageCaption(room == RoomType.Combat ? "Combat" : room == RoomType.Chest ? "Treasure" : "Puzzle", RoomLine(room.Value), "", "");
    }

    private static string RoomLine(RoomType room) => room switch
    {
        RoomType.Combat => "Something stirs ahead.",
        RoomType.Chest => "A chest waits in the dust.",
        _ => "Strange mechanisms line the walls."
    };

    private static string ResultLine(StageShow show)
    {
        var verdict = show.Success
            ? show.Room switch { RoomType.Combat => "The foe falls.", RoomType.Chest => "The lock gives.", _ => "The mechanism yields." }
            : show.Room switch { RoomType.Combat => "The foe holds its ground.", RoomType.Chest => "The chest resists.", _ => "The mechanism jams." };
        if (!show.Success && show.Damage > 0 && show.TargetName is { } name) return $"{verdict} {name} takes {show.Damage}.";
        if (show.Success && show.GoldFound > 0) return $"{verdict} +{show.GoldFound} gold.";
        return verdict;
    }
}
