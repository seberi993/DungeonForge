using System.Text;

namespace DungeonForge.Web.Features.Dungeon.Stage;

// A fixed grid of characters, each with an optional CSS class, drawn back to front and rendered as one block of spans.
public sealed class CharGrid(int width, int height)
{
    private readonly char[] _chars = Enumerable.Repeat(' ', width * height).ToArray();
    private readonly string?[] _classes = new string?[width * height];

    public int Width { get; } = width;
    public int Height { get; } = height;

    // Same rounding as the design preview: halves round up.
    public static int Round(double v) => (int)Math.Floor(v + 0.5);

    public char CharAt(int x, int y) => _chars[y * Width + x];

    public void Set(double x, double y, char ch, string? cls = null)
    {
        var ix = Round(x);
        var iy = Round(y);
        if (ix < 0 || iy < 0 || ix >= Width || iy >= Height) return;
        _chars[iy * Width + ix] = ch;
        _classes[iy * Width + ix] = cls;
    }

    // Spaces are transparent.
    public void Text(double x, double y, string s, string? cls = null)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != ' ') Set(x + i, y, s[i], cls);
        }
    }

    public void Clear(double x, double y, int w, int h)
    {
        for (var j = 0; j < h; j++)
        {
            for (var i = 0; i < w; i++) Set(x + i, y + j, ' ');
        }
    }

    // Spaces inside a row's drawn span are opaque (a body hides the wall behind it); spaces outside stay transparent.
    public void Sprite(double x, double y, IReadOnlyList<string> rows, string? cls = null)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var first = row.Length - row.TrimStart().Length;
            var last = row.TrimEnd().Length - 1;
            if (last < 0) continue;
            for (var i = first; i <= last; i++) Set(x + i, y + r, row[i], cls);
        }
    }

    public void Line(double x0, double y0, double x1, double y1, string? cls)
    {
        var ax = Round(x0);
        var ay = Round(y0);
        var bx = Round(x1);
        var by = Round(y1);
        var dx = Math.Abs(bx - ax);
        var dy = Math.Abs(by - ay);
        var sx = ax < bx ? 1 : -1;
        var sy = ay < by ? 1 : -1;
        var slope = dy / (dx * 0.5 + 1e-9);
        var glyph = slope > 5 ? '|' : slope > 0.8 ? (sx * sy > 0 ? '\\' : '/') : '-';
        var err = dx - dy;
        for (var n = 0; n < 400; n++)
        {
            Set(ax, ay, glyph, cls);
            if (ax == bx && ay == by) break;
            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; ax += sx; }
            if (e2 < dx) { err += dx; ay += sy; }
        }
    }

    public string ToHtml()
    {
        var sb = new StringBuilder(Width * Height * 2);
        for (var y = 0; y < Height; y++)
        {
            string? current = null;
            for (var x = 0; x < Width; x++)
            {
                var cls = _classes[y * Width + x];
                if (cls != current)
                {
                    if (current is not null) sb.Append("</span>");
                    if (cls is not null) sb.Append("<span class=\"").Append(cls).Append("\">");
                    current = cls;
                }

                switch (_chars[y * Width + x])
                {
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '&': sb.Append("&amp;"); break;
                    case var ch: sb.Append(ch); break;
                }
            }

            if (current is not null) sb.Append("</span>");
            sb.Append('\n');
        }

        return sb.ToString();
    }

    // Plain text of the grid (for tests and the accessible label).
    public string ToPlainText()
    {
        var sb = new StringBuilder(Width * Height + Height);
        for (var y = 0; y < Height; y++)
        {
            sb.Append(_chars, y * Width, Width).Append('\n');
        }

        return sb.ToString();
    }
}
