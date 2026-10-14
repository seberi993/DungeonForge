namespace DungeonForge.Web.Components.Stage;

// The hall: nested stone frames shrink toward a far door, and the floor tiles converge on one vanishing point.
public static class Corridor
{
    public const int Width = 66;
    public const int Height = 30;
    public const int VanishX = 33;
    public const int VanishY = 10;
    private const int Last = 6;
    private static readonly double[] Depths = [1, 0.7, 0.5, 0.35, 0.24, 0.16, 0.1];
    private static readonly char[] Flames = ['(', ')', '^', '*'];

    // intro 0..1 pulls the camera in: the far frames start closer together and spread out to their final spacing.
    public static void Draw(CharGrid g, double ms, double intro)
    {
        var e = Ease(intro);
        var sc = Depths.Select((s, d) => d == 0 ? 1 : Math.Pow(s, 1 + (1 - e) * 0.9)).ToArray();
        int L(int d) => CharGrid.Round(VanishX - 32 * sc[d]);
        int R(int d) => CharGrid.Round(VanishX + 32 * sc[d]);
        int T(int d) => CharGrid.Round(VanishY - 10 * sc[d]);
        int B(int d) => CharGrid.Round(VanishY + 19 * sc[d]);
        int DepthOf(double s)
        {
            var d = 0;
            while (d < Last && s < sc[d + 1]) d++;
            return d;
        }

        for (var y = B(Last) + 1; y < g.Height; y++)
        {
            var s = (y - VanishY) / 19.0;
            var d = DepthOf(s);
            var half = CharGrid.Round(32 * s);
            for (var x = VanishX - half; x <= VanishX + half; x++)
            {
                var u = (x - VanishX) / (32 * s);
                var lateral = (int)Math.Floor((u + 1) * 3);
                g.Set(x, y, ((lateral + d) & 1) != 0 ? '░' : ' ', "st-z" + Math.Min(6, d + 1));
            }
        }

        for (var y = 0; y < T(Last); y++)
        {
            var s = (VanishY - y) / 10.0;
            var d = DepthOf(s);
            var half = CharGrid.Round(32 * s);
            for (var x = VanishX - half; x <= VanishX + half; x++)
            {
                var u = (x - VanishX) / (32 * s);
                var lateral = (int)Math.Floor((u + 1) * 3);
                if (((lateral + d) & 1) == 0 && s > 0.1) g.Set(x, y, '.', "st-z" + Math.Min(6, d + 1));
            }
        }

        for (var y = T(Last); y <= B(Last); y++)
        {
            for (var x = L(Last); x <= R(Last); x++) g.Set(x, y, '▓', "st-z6");
        }

        for (var d = 1; d <= Last; d++)
        {
            int l = L(d), r = R(d), t = T(d), b = B(d);
            var cls = "st-z" + d;
            for (var x = l + 1; x < r; x++) { g.Set(x, t, '─', cls); g.Set(x, b, '─', cls); }
            for (var y = t + 1; y < b; y++) { g.Set(l, y, '│', cls); g.Set(r, y, '│', cls); }
            g.Set(l, t, '┌', cls); g.Set(r, t, '┐', cls); g.Set(l, b, '└', cls); g.Set(r, b, '┘', cls);
        }

        g.Line(0, 0, L(Last), T(Last), "st-z3");
        g.Line(g.Width - 1, 0, R(Last), T(Last), "st-z3");
        g.Line(0, g.Height - 1, L(Last), B(Last), "st-z3");
        g.Line(g.Width - 1, g.Height - 1, R(Last), B(Last), "st-z3");

        var n = 0;
        foreach (var d in new[] { 2, 4 })
        {
            var side = 0;
            foreach (var x in new[] { L(d) + 1, R(d) - 1 })
            {
                var y = VanishY - CharGrid.Round(2 * sc[d] * 3);
                g.Set(x, y, Flames[(int)Math.Floor(ms / 120 + side * 2 + n) % 4], "st-torch");
                g.Set(x, y + 1, '#', "st-torch");
                g.Set(x + (side == 1 ? -2 : 2), y, '.', "st-glow");
                g.Set(x, y - 1, '.', "st-glow");
                side++;
            }

            n++;
        }

        g.Text(VanishX - 2, VanishY - 2, "~ ~", "st-glow");
    }

    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
