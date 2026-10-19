using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVATrackAndField.Engine;

namespace AVATrackAndField.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVATrackAndField/Assets/Fonts#VT323"));

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

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Green = 0xFF6AF06A;
    private const uint Amber = 0xFFFFB030, AmberDim = 0xFF7A4A10, Lamp = 0xFFFFE8A0;
    private const uint Tartan = 0xFFB8483A, TartanDark = 0xFF9A3A30, Grass = 0xFF3E8A3A, GrassDark = 0xFF347A32, Sand = 0xFFE8CC88;

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

    private static uint Blend(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool Blink(double rate = 4) => (int)(_clock * rate) % 2 == 0;

    private const double S = Field.Scale;
    /// <summary>The track: its back edge (by the wall), the lane lines, its front edge.</summary>
    private const double TrackTop = 408, LaneLine1 = 464, LaneLine2 = 526, LaneLine3 = 588, TrackBottom = 650, Hud = 672;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawStadium(dc, 30 + _clock * 3, null); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawStadium(dc, 60, null); DrawInitials(dc); break;
            case GameState.GameOver: DrawStadium(dc, g.World.Me.X - 6, g.World); DrawHud(dc); DrawGameOver(dc); break;
            default:
                DrawStadium(dc, Camera(g.World), g.World);
                DrawHud(dc);
                DrawCalls(dc);
                if (g.Phase == Phase.Intro) DrawIntro(dc);
                if (g.Phase == Phase.Tally) DrawTally(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS 1 OR 2 TO PLAY", new Point(512, 395), 22, Yellow, center: true);
    }

    /// <summary>Where the view's left edge is, in metres: on the athlete as he runs, then on whatever's flying.</summary>
    private static double Camera(World w)
    {
        if (w.Thing.Flying || w.Thing.Landed) return Math.Max(w.Me.X - 6, w.Thing.X - 12);
        return w.Event switch
        {
            Ev.Hammer => 0,
            Ev.HighJump => Math.Min(w.Me.X - 6, Sim.BarX - 11),
            _ => w.Me.X - 6,
        };
    }

    // ───────────────────────────── the stadium ─────────────────────────────

    /// <summary>The sky and the floodlights, the scoreboard, the crowd in the stand, the wall, and the track or field.</summary>
    private void DrawStadium(DrawingContext dc, double cam, World? w)
    {
        for (int i = 0; i < 10; i++)
            dc.FillRectangle(Brush(Blend(0xFF3A78C8, 0xFFA8D0F0, i / 9.0)), new Rect(0, i * 16, Field.Width, 17));
        for (int i = 0; i < 4; i++)
        {
            double cx = (i * 330 + 80 - cam * S * 0.04) % 1320 - 100, cy = 40 + Hash(i, 3, 9) % 50;
            for (int j = 0; j < 4; j++) dc.FillEllipse(cx + j * 26, cy - (j % 2) * 8, 22 - j * 2, 0xC0FFFFFF);
        }
        // The floodlight towers, far off.
        foreach (double fx in new[] { 90.0, 934.0 })
        {
            double x = fx - cam * S * 0.06 % 40;
            dc.DrawLine(Pen(0xFF5A6A7A, 6), new Point(x, 160), new Point(x, 30));
            dc.FillRectangle(Brush(0xFF3A4A5A), new Rect(x - 34, 12, 68, 30));
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 5; c++)
                    dc.FillEllipse(x - 26 + c * 13, 21 + r * 13, 4.5, Lamp);
        }
        DrawCrowd(dc, cam);
        DrawScoreboard(dc, w);
        // The wall in front of the stand, with its boards.
        dc.FillRectangle(Brush(0xFF2A3A6A), new Rect(0, 378, Field.Width, 30));
        double wall = cam * S * 0.7;
        string[] boards = ["AVA", "TRACK & FIELD", "1983", "RUN", "JUMP", "THROW"];
        uint[] boardColours = [0xFFE8E8E8, 0xFFF0C030, 0xFF30A0E0, 0xFFE05030];
        for (int k = (int)Math.Floor(wall / 180) - 1; k <= (int)Math.Floor((wall + Field.Width) / 180) + 1; k++)
        {
            double x = k * 180 - wall;
            uint bg = boardColours[(int)(Hash(k, 1, 4) % boardColours.Length)];
            dc.FillRectangle(Brush(bg), new Rect(x + 3, 381, 174, 24));
            Text(dc, boards[(int)(Hash(k, 2, 4) % boards.Length)], new Point(x + 90, 392), 22, bg == 0xFFE8E8E8 ? 0xFF203070 : 0xFF101010, center: true, shadow: false);
        }
        dc.FillRectangle(Brush(0xFF1A1A2A), new Rect(0, 405, Field.Width, 3));
        DrawTrack(dc, cam, w);
        // The infield in front.
        dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(0, TrackBottom, Field.Width, 3));
        dc.FillRectangle(Brush(Grass), new Rect(0, TrackBottom + 3, Field.Width, Hud - TrackBottom - 3));
        for (double x = -(cam * S % 64); x < Field.Width; x += 64) dc.FillRectangle(Brush(GrassDark), new Rect(x, TrackBottom + 3, 32, Hud - TrackBottom - 3));
        if (w != null) DrawAthletes(dc, cam, w);
    }

    /// <summary>The crowd: row on row of them, moving with the athletes a little, on their feet when there's something to cheer.</summary>
    private void DrawCrowd(DrawingContext dc, double cam)
    {
        dc.FillRectangle(Brush(0xFF2A2440), new Rect(0, 150, Field.Width, 230));
        double cheer = Engine.CheerTime > 0 && InGame ? 1 : 0;
        const double seat = 19;
        uint[] shirts = [0xFFE04040, 0xFFF0F0F0, 0xFF3070E0, 0xFFF0C030, 0xFF40B060, 0xFFE070B0, 0xFF303030, 0xFFF08030, 0xFF8040C0, 0xFF60C0E0];
        uint[] skins = [0xFFF0C8A0, 0xFFD8A070, 0xFFA06840, 0xFF6A4028, 0xFFE8B888];
        for (int row = 0; row < 9; row++)
        {
            double y = 168 + row * 25.5;
            double shift = cam * S * (0.3 + row * 0.02) % seat;
            dc.FillRectangle(Brush(row % 2 == 0 ? 0xFF46406A : 0xFF3E3860), new Rect(0, y + 8, Field.Width, 14));
            int first = (int)Math.Floor(cam * S * (0.3 + row * 0.02) / seat);
            for (int i = -1; i < Field.Width / seat + 1; i++)
            {
                int n = first + i;
                uint h = Hash(n, row, 77);
                if (h % 11 == 0) continue;
                double x = i * seat - shift + (row % 2) * seat / 2;
                double bob = Math.Sin(_clock * (2 + h % 3) + h % 17) * (cheer > 0 ? 3.5 : 0.6);
                bool up = cheer > 0 && h % 3 != 0;
                double top = y - (up ? 6 : 0) + bob;
                uint shirt = shirts[h % shirts.Length], skin = skins[(h >> 5) % skins.Length];
                dc.FillRectangle(Brush(shirt), new Rect(x - 6, top + 6, 12, 14), 3);
                dc.FillEllipse(x, top + 2, 5, skin);
                if ((h >> 9) % 4 == 0) dc.FillRectangle(Brush(0xFF2A1A10), new Rect(x - 5, top - 3.5, 10, 3.5));
                if (up)
                {
                    double wave = Math.Sin(_clock * 9 + h % 7) * 3;
                    dc.DrawLine(Pen(skin, 2.5), new Point(x - 5, top + 9), new Point(x - 8 + wave, top - 6));
                    dc.DrawLine(Pen(skin, 2.5), new Point(x + 5, top + 9), new Point(x + 8 - wave, top - 6));
                }
            }
        }
    }

    private static double Sx(double x, double cam) => (x - cam) * S;

    /// <summary>The track (or the field) for the event: lanes, markings, and the board, pit, field, cage or bar.</summary>
    private void DrawTrack(DrawingContext dc, double cam, World? w)
    {
        dc.FillRectangle(Brush(Tartan), new Rect(0, TrackTop, Field.Width, TrackBottom - TrackTop));
        // A grain to the tartan, so it moves when the view does.
        for (double x = -(cam * S % 24); x < Field.Width; x += 24)
            dc.FillRectangle(Brush(TartanDark), new Rect(x, TrackTop, 2, TrackBottom - TrackTop));
        foreach (double y in new[] { TrackTop, LaneLine1, LaneLine2, LaneLine3 })
            dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(0, y, Field.Width, 3));
        var ev = w?.Event ?? Ev.Dash;
        switch (ev)
        {
            case Ev.Dash or Ev.Hurdles: DrawRaceMarks(dc, cam, w, ev); break;
            case Ev.LongJump: DrawPit(dc, cam, w!); break;
            case Ev.Javelin: DrawThrowingField(dc, cam, w!, Sim.JavelinLine); break;
            case Ev.Hammer: DrawThrowingField(dc, cam, w!, -50); DrawCircle(dc, cam, w!); break;
            case Ev.HighJump: DrawHighJump(dc, cam, w!, bar: false); break;
        }
    }

    /// <summary>A line across the track, leaning back with the lanes.</summary>
    private static void Across(DrawingContext dc, double x, uint colour, double width, double top = TrackTop, double bottom = TrackBottom)
    {
        double lean = (bottom - top) * 0.12;
        Poly(dc, colour, null, 0, new Point(x, bottom), new Point(x + width, bottom), new Point(x + width + lean, top), new Point(x + lean, top));
    }

    private void DrawRaceMarks(DrawingContext dc, double cam, World? w, Ev ev)
    {
        double length = Sim.Length(ev);
        for (int m = 0; m <= (int)length; m += 10)
        {
            double x = Sx(m, cam);
            if (x < -60 || x > Field.Width + 60) continue;
            Across(dc, x, m == 0 ? 0xFFF0F0F0 : 0x60F0F0F0, m == 0 ? 4 : 2);
            if (m > 0 && m < length) Text(dc, $"{m}", new Point(x, TrackBottom + 11), 18, 0xFFF0F0F0, center: true, shadow: false);
        }
        double f = Sx(length, cam);
        if (f > -80 && f < Field.Width + 80)
        {
            for (int i = 0; i < 12; i++)
            {
                double y0 = TrackTop + i * (TrackBottom - TrackTop) / 12, x0 = f + (TrackBottom - y0) * 0.12;
                dc.FillRectangle(Brush(i % 2 == 0 ? 0xFF101010 : 0xFFF0F0F0), new Rect(x0, y0, 6, (TrackBottom - TrackTop) / 12 + 1));
                dc.FillRectangle(Brush(i % 2 == 1 ? 0xFF101010 : 0xFFF0F0F0), new Rect(x0 + 6, y0, 6, (TrackBottom - TrackTop) / 12 + 1));
            }
            dc.FillRectangle(Brush(0xFFE0E0E0), new Rect(f + 2, TrackBottom - 120, 5, 120));
            Text(dc, "FINISH", new Point(f + 60, TrackBottom + 11), 18, Yellow, center: true, shadow: false);
        }
        // The starting blocks.
        foreach (double lane in new[] { Field.Near, Field.Far })
        {
            double x = Sx(-0.5, cam);
            if (x > -40) Poly(dc, 0xFF5A5A6A, null, 0, new Point(x - 10, lane), new Point(x + 6, lane), new Point(x - 4, lane - 10));
        }
        if (ev != Ev.Hurdles || w == null) return;
        for (int i = 0; i < w.Knocked.Length; i++)
        {
            double x = Sx(Sim.HurdleAt(i), cam);
            if (x < -60 || x > Field.Width + 60) continue;
            DrawHurdle(dc, x, Field.Far, false, 0.9);
            DrawHurdle(dc, x, Field.Near, w.Knocked[i], 1);
        }
    }

    /// <summary>A hurdle across a lane: the near and far feet, the posts, and the striped board along the top.</summary>
    private static void DrawHurdle(DrawingContext dc, double x, double feet, bool down, double size)
    {
        double h = Sim.HurdleHeight * S * size, depth = 36 * size, lean = depth * 0.7;
        feet += 16 * size;
        var post = Pen(0xFFD0D0D8, 3 * size);
        if (down)
        {
            // Knocked flat, its board on the track ahead.
            dc.DrawLine(post, new Point(x, feet), new Point(x + h * 0.9, feet - 4));
            dc.DrawLine(post, new Point(x + lean, feet - depth), new Point(x + lean + h * 0.9, feet - depth - 4));
            Poly(dc, 0xFFF0F0F0, 0xFF202020, 1, new Point(x + h * 0.9, feet - 8), new Point(x + h * 0.9 + 6, feet - 4), new Point(x + lean + h * 0.9 + 6, feet - depth - 4), new Point(x + lean + h * 0.9, feet - depth - 8));
            return;
        }
        dc.DrawLine(post, new Point(x + lean, feet - depth), new Point(x + lean, feet - depth - h));
        dc.DrawLine(Pen(0xFFD0D0D8, 3 * size), new Point(x + lean - 10, feet - depth), new Point(x + lean + 6, feet - depth));
        Poly(dc, 0xFFF0F0F0, 0xFF202020, 1, new Point(x - 2, feet - h - 9 * size), new Point(x + 3, feet - h), new Point(x + lean + 3, feet - depth - h), new Point(x + lean - 2, feet - depth - h - 9 * size));
        for (int i = 1; i < 4; i += 2)
        {
            double k0 = i / 4.0, k1 = (i + 1) / 4.0;
            Poly(dc, 0xFF202020, null, 0, new Point(x - 2 + lean * k0, feet - h - 9 * size - depth * k0), new Point(x + 3 + lean * k0, feet - h - depth * k0),
                new Point(x + 3 + lean * k1, feet - h - depth * k1), new Point(x - 2 + lean * k1, feet - h - 9 * size - depth * k1));
        }
        dc.DrawLine(post, new Point(x, feet), new Point(x, feet - h));
        dc.DrawLine(Pen(0xFFD0D0D8, 3 * size), new Point(x - 10, feet), new Point(x + 6, feet));
    }

    /// <summary>A small flag in the ground at a mark: the qualifying mark, and the world record.</summary>
    private static void MarkFlag(DrawingContext dc, double x, double y, string label, uint colour)
    {
        dc.DrawLine(Pen(0xFFE0E0E0, 2), new Point(x, y), new Point(x, y - 34));
        Poly(dc, colour, null, 0, new Point(x, y - 34), new Point(x + 26, y - 28), new Point(x, y - 22));
        Text(dc, label, new Point(x + 2, y - 44), 16, colour, center: true);
    }

    private void DrawPit(DrawingContext dc, double cam, World w)
    {
        double line = Sx(Sim.JumpLine, cam);
        // The runway's distance boards.
        for (int m = 10; m < Sim.JumpLine; m += 10)
        {
            double x = Sx(m, cam);
            if (x > -40 && x < Field.Width + 40) Text(dc, $"{Sim.JumpLine - m:0}", new Point(x, TrackBottom + 11), 18, 0xFFF0F0F0, center: true, shadow: false);
        }
        // The sand.
        double p0 = Sx(Sim.JumpLine + 1, cam), p1 = Sx(Sim.JumpLine + 11, cam);
        double top = LaneLine3 + 6, bottom = TrackBottom - 2;
        Poly(dc, Sand, 0xFF8A7A50, 2, new Point(p0, bottom), new Point(p1, bottom), new Point(p1 + 8, top), new Point(p0 + 8, top));
        for (int i = 0; i < 40; i++)
        {
            uint h = Hash(i, 2, 5);
            double sx = Sx(Sim.JumpLine + 1 + h % 1000 / 100.0, cam), sy = top + 4 + (h >> 10) % 50;
            dc.FillRectangle(Brush(0xFFD0B470), new Rect(sx, sy, 3, 2));
        }
        // The board, and the foul line.
        Across(dc, line - 10, 0xFFF0F0F0, 10, LaneLine3 + 3, TrackBottom);
        Across(dc, line, 0xFF404040, 3, LaneLine3 + 3, TrackBottom);
        for (int m = 2; m <= 10; m++)
        {
            double x = Sx(Sim.JumpLine + m, cam);
            Text(dc, $"{m}", new Point(x, TrackBottom + 11), 18, 0xFFF0F0F0, center: true, shadow: false);
        }
        MarkFlag(dc, Sx(Sim.JumpLine + Sim.Qualify(Ev.LongJump, w.Cycle), cam), LaneLine3 + 2, "Q", Yellow);
        MarkFlag(dc, Sx(Sim.JumpLine + Engine.Records[Ev.LongJump], cam), LaneLine3 + 2, "WR", Red);
        if (w.Over && !double.IsNaN(w.Result))
        {
            double x = Sx(w.Me.X, cam);
            dc.DrawEllipse(Brush(0xFFB09050), null, new Point(x, Field.Near + 4), 22, 6);
        }
    }

    private void DrawThrowingField(DrawingContext dc, double cam, World w, double from)
    {
        double x0 = Math.Max(0, Sx(from, cam));
        bool hammer = w.Event == Ev.Hammer;
        if (hammer) from = Sim.HammerX + Sim.Circle;
        dc.FillRectangle(Brush(Grass), new Rect(x0, TrackTop, Field.Width - x0, TrackBottom - TrackTop));
        for (double m = from + (hammer ? -45 : 5); m < from + 130; m += 10)
        {
            double x = Sx(m, cam);
            if (x > x0 && x < Field.Width + 40) dc.FillRectangle(Brush(GrassDark), new Rect(x, TrackTop, Math.Min(5 * S, Field.Width - x), TrackBottom - TrackTop));
        }
        for (int m = 10; m <= 120; m += 10)
        {
            double x = Sx(from + m, cam);
            if (x < -60 || x > Field.Width + 60) continue;
            Across(dc, x, 0xC0F0F0F0, 2);
            Text(dc, $"{m}", new Point(x, TrackBottom + 11), 18, 0xFFF0F0F0, center: true, shadow: false);
        }
        if (w.Event == Ev.Javelin)
        {
            double line = Sx(from, cam);
            Across(dc, line - 4, 0xFFF0F0F0, 8);
            for (int m = 10; m < Sim.JavelinLine; m += 10)
            {
                double x = Sx(m, cam);
                if (x > -40 && x < Field.Width + 40) Text(dc, $"{Sim.JavelinLine - m:0}", new Point(x, TrackBottom + 11), 18, 0xFFF0F0F0, center: true, shadow: false);
            }
        }
        MarkFlag(dc, Sx(from + Sim.Qualify(w.Event, w.Cycle), cam), LaneLine2, "Q", Yellow);
        MarkFlag(dc, Sx(from + Engine.Records[w.Event], cam), LaneLine2, "WR", Red);
        if (w.Thing.Landed && !w.Foul)
        {
            double x = Sx(w.Thing.X, cam);
            dc.DrawLine(Pen(0xFFF0F0F0, 2), new Point(x, Field.Near + 6), new Point(x, Field.Near - 26));
            dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x, Field.Near - 26, 14, 9));
        }
    }

    /// <summary>The hammer circle, and the back of its cage (the front's drawn over the thrower).</summary>
    private static void DrawCircle(DrawingContext dc, double cam, World w)
    {
        double cx = Sx(Sim.HammerX, cam), cy = Field.Near - 6;
        dc.DrawEllipse(Brush(0xFFA8A8A0), Pen(0xFFF0F0F0, 3), new Point(cx, cy), Sim.Circle * S * 1.2, 16);
        DrawCage(dc, cx, cy, back: true);
    }

    /// <summary>The cage: a net behind the circle, open down the field, and its posts in front.</summary>
    private static void DrawCage(DrawingContext dc, double cx, double cy, bool back)
    {
        var pole = Pen(0xFF707880, 5);
        const double h = 250;
        if (back)
        {
            double y0 = cy - 46, left = cx - 150, right = cx + 40;
            dc.FillRectangle(Brush(0x18FFFFFF), new Rect(left, y0 - h, right - left, h));
            var net = Pen(0x50E8E8E8, 1);
            for (double x = left; x <= right; x += 12) dc.DrawLine(net, new Point(x, y0), new Point(x, y0 - h));
            for (double y = y0; y >= y0 - h; y -= 12) dc.DrawLine(net, new Point(left, y), new Point(right, y));
            dc.DrawLine(pole, new Point(left, y0), new Point(left, y0 - h - 8));
            dc.DrawLine(pole, new Point(right, y0), new Point(right, y0 - h - 8));
            return;
        }
        double fy = cy + 22;
        dc.DrawLine(pole, new Point(cx - 170, fy), new Point(cx - 170, fy - h - 30));
        dc.DrawLine(Pen(0x50E8E8E8, 1), new Point(cx - 170, fy - h - 30), new Point(cx - 150, cy - 46 - h - 8));
        for (int i = 0; i < 6; i++) dc.DrawLine(Pen(0x40E8E8E8, 1), new Point(cx - 170, fy - i * 50), new Point(cx - 150, cy - 46 - i * 48));
    }

    /// <summary>How far back the bar stands from the near lane (on the screen, up), and the mat behind it.</summary>
    private const double BarDepth = 70, MatFront = 80, MatBack = 160, MatHeight = 0.55, BarHalf = 2.2;

    /// <summary>A point on the high-jump apron: so far along, so far back, so high.</summary>
    private static Point At(double x, double depth, double height, double cam) => new(Sx(x, cam) + depth * 0.12, Field.Near - depth - height * S);

    /// <summary>How far back the jumper has come: the run-up is in the near lane, and the jump carries him back over the bar onto the mat.</summary>
    private static double JumperDepth(World w)
    {
        if (double.IsNaN(w.TakeoffX) || w.Me.Pose is Pose.Plant or Pose.Stumble) return 0;
        double k = (w.Me.X - w.TakeoffX) / Math.Max(0.3, Sim.BarX - w.TakeoffX);
        return BarDepth * Math.Clamp(k, 0, 1.7);
    }

    /// <summary>The high jump: the mat, the take-off mark, the standards and the bar.</summary>
    private void DrawHighJump(DrawingContext dc, double cam, World w, bool bar)
    {
        if (bar)
        {
            var post = Pen(0xFFD8D8E0, 5);
            foreach (double side in new[] { -BarHalf, BarHalf })
            {
                double x = Sim.BarX + side;
                dc.DrawLine(post, At(x, BarDepth, 0, cam), At(x, BarDepth, 2.7, cam));
                dc.DrawLine(Pen(0xFF8A8A98, 7), At(x, BarDepth, 0, cam), At(x, BarDepth, 0.08, cam));
                dc.FillRectangle(Brush(0xFF303030), new Rect(At(x, BarDepth, w.Bar, cam).X - 5, At(x, BarDepth, w.Bar, cam).Y - 2, 10, 5));
            }
            if (w.BarDown && w.Me.T > 0.15)
            {
                var a = At(Sim.BarX - BarHalf + 0.4, MatFront + 26, MatHeight, cam);
                var b = At(Sim.BarX + BarHalf - 0.2, MatFront + 46, MatHeight, cam);
                dc.DrawLine(Pen(0xFFF0D020, 5), a, b);
                return;
            }
            var l = At(Sim.BarX - BarHalf, BarDepth, w.Bar, cam);
            var r = At(Sim.BarX + BarHalf, BarDepth, w.Bar, cam);
            dc.DrawLine(Pen(0xFFF0D020, 5), l, r);
            for (int i = 1; i < 8; i += 2)
                dc.DrawLine(Pen(0xFF202020, 5), new Point(l.X + (r.X - l.X) * i / 8, l.Y), new Point(l.X + (r.X - l.X) * (i + 1) / 8, r.Y));
            return;
        }
        // The mat: its top, then its front.
        double x0 = Sim.BarX - BarHalf - 0.3, x1 = Sim.BarX + BarHalf + 0.6;
        Poly(dc, 0xFF3A70E0, 0xFF183A80, 2, At(x0, MatFront, MatHeight, cam), At(x1, MatFront, MatHeight, cam), At(x1, MatBack, MatHeight, cam), At(x0, MatBack, MatHeight, cam));
        Poly(dc, 0xFF2A58B8, 0xFF183A80, 2, At(x0, MatFront, 0, cam), At(x1, MatFront, 0, cam), At(x1, MatFront, MatHeight, cam), At(x0, MatFront, MatHeight, cam));
        var mid = At((x0 + x1) / 2, MatFront, MatHeight / 2, cam);
        Text(dc, "AVA", mid, 28, 0xFFE0E8FF, center: true, shadow: false);
        // The take-off mark.
        Across(dc, Sx(Sim.BarX - Sim.TakeoffNear, cam), 0xFFF0F0F0, 3, LaneLine3 + 3, TrackBottom);
    }

    // ───────────────────────────── the athletes ─────────────────────────────

    private readonly record struct Kit(uint Shirt, uint Shorts, uint Skin, uint Hair, uint Shoes);

    private static readonly Kit[] Kits =
    [
        new(0xFFE03030, 0xFFF4F4F4, 0xFFC68A5A, 0xFF2A1A10, 0xFFF4F4F4),
        new(0xFF30B050, 0xFF1A2A6A, 0xFFF0C8A0, 0xFFD8B040, 0xFF202020),
    ];
    private static readonly Kit RivalKit = new(0xFF3060E0, 0xFF101830, 0xFF7A4A2A, 0xFF101010, 0xFFE0E040);

    /// <summary>
    /// A body's joints, all angles in radians. The torso's is from straight up; each thigh's and upper arm's from
    /// straight down; positive is forward. A knee bends the shin back; an elbow bends the forearm forward. The hip is
    /// in metres, forward and up from where the body stands.
    /// </summary>
    private struct Rig
    {
        public double HipX, HipY, Torso, ThighN, KneeN, ThighF, KneeF, ArmN, ElbowN, ArmF, ElbowF, Head;
    }

    private static Rig Standing() => new() { HipY = 0.95, ThighN = 0.05, ThighF = -0.05, KneeN = 0.05, KneeF = 0.05, ArmN = 0.1, ElbowN = 0.15, ArmF = -0.1, ElbowF = 0.15 };

    /// <summary>The running stride: knees driving up in front, heels kicking up behind, the arms against the legs.</summary>
    private static Rig Running(double phase, double power)
    {
        double a = 0.35 + 0.4 * power, b = 1.1 + 0.8 * power;
        double Thigh(double p) => 0.12 + a * Math.Sin(p);
        double Knee(double p) => 0.25 + b * Math.Max(0, Math.Cos(p + 0.35)) + 0.2 * Math.Max(0, Math.Sin(p - 2.2));
        return new Rig
        {
            HipY = 0.93 + 0.035 * Math.Abs(Math.Cos(phase)) * (0.5 + power),
            Torso = 0.12 + 0.16 * power,
            ThighN = Thigh(phase), KneeN = Knee(phase), ThighF = Thigh(phase + Math.PI), KneeF = Knee(phase + Math.PI),
            ArmN = -(0.25 + 0.6 * power) * Math.Sin(phase) + 0.1, ElbowN = 1.4 + 0.3 * Math.Sin(phase),
            ArmF = (0.25 + 0.6 * power) * Math.Sin(phase) + 0.1, ElbowF = 1.4 - 0.3 * Math.Sin(phase),
        };
    }

    private static Point Limb(Point from, double angle, double length) => new(from.X + Math.Sin(angle) * length, from.Y + Math.Cos(angle) * length);

    /// <summary>Draws a body from its rig at (x, y) on the ground, s pixels to the metre: the far limbs darker, behind.</summary>
    private static void DrawBody(DrawingContext dc, Rig r, double x, double y, double s, Kit kit)
    {
        var hip = new Point(x + r.HipX * s, y - r.HipY * s);
        var up = new Point(Math.Sin(r.Torso), -Math.Cos(r.Torso));
        var shoulder = new Point(hip.X + up.X * 0.55 * s, hip.Y + up.Y * 0.55 * s);
        double ha = r.Torso + r.Head;
        var head = new Point(shoulder.X + Math.Sin(ha) * 0.2 * s, shoulder.Y - Math.Cos(ha) * 0.2 * s);
        double limb = 0.095 * s, arm = 0.075 * s;
        uint Dark(uint c) => Blend(c, 0xFF000000, 0.32);

        void Leg(double thigh, double knee, bool far)
        {
            var k = Limb(hip, thigh, 0.47 * s);
            double shin = thigh - knee;
            var ankle = Limb(k, shin, 0.46 * s);
            var toe = new Point(ankle.X + Math.Cos(shin) * 0.15 * s, ankle.Y - Math.Sin(shin) * 0.15 * s);
            uint skin = far ? Dark(kit.Skin) : kit.Skin;
            dc.DrawLine(Pen(skin, limb), hip, k);
            dc.DrawLine(Pen(skin, limb * 0.85), k, ankle);
            dc.DrawLine(Pen(far ? Dark(kit.Shoes) : kit.Shoes, limb * 0.8), ankle, toe);
            // The shorts over the top of the thigh.
            dc.DrawLine(Pen(far ? Dark(kit.Shorts) : kit.Shorts, limb * 1.45), hip, new Point(hip.X + (k.X - hip.X) * 0.42, hip.Y + (k.Y - hip.Y) * 0.42));
        }

        void Arm(double upper, double elbow, bool far)
        {
            var sh = new Point(shoulder.X - up.X * 0.06 * s, shoulder.Y - up.Y * 0.06 * s);
            var e = Limb(sh, upper, 0.3 * s);
            var hand = Limb(e, upper + elbow, 0.28 * s);
            uint skin = far ? Dark(kit.Skin) : kit.Skin;
            dc.DrawLine(Pen(skin, arm), sh, e);
            dc.DrawLine(Pen(skin, arm * 0.9), e, hand);
            if (!far) dc.DrawLine(Pen(kit.Shirt, arm * 1.25), sh, new Point(sh.X + (e.X - sh.X) * 0.3, sh.Y + (e.Y - sh.Y) * 0.3));
        }

        Arm(r.ArmF, r.ElbowF, true);
        Leg(r.ThighF, r.KneeF, true);
        // The torso: the singlet, a number on its front.
        dc.DrawLine(Pen(kit.Shirt, 0.21 * s), new Point(hip.X + up.X * 0.06 * s, hip.Y + up.Y * 0.06 * s), shoulder);
        var mid = new Point(hip.X + up.X * 0.3 * s, hip.Y + up.Y * 0.3 * s);
        dc.DrawLine(Pen(0xFFF8F8F8, 0.09 * s), new Point(mid.X - up.X * 0.06 * s, mid.Y - up.Y * 0.06 * s), new Point(mid.X + up.X * 0.08 * s, mid.Y + up.Y * 0.08 * s));
        dc.DrawLine(Pen(kit.Shorts, 0.2 * s), new Point(hip.X - up.X * 0.04 * s, hip.Y - up.Y * 0.04 * s), new Point(hip.X + up.X * 0.08 * s, hip.Y + up.Y * 0.08 * s));
        Leg(r.ThighN, r.KneeN, false);
        // The neck and head, the hair, and the eye looking where the body faces.
        dc.DrawLine(Pen(kit.Skin, 0.08 * s), shoulder, head);
        dc.FillEllipse(head.X, head.Y, 0.115 * s, kit.Skin);
        var back = new Point(head.X - Math.Cos(ha) * 0.03 * s - Math.Sin(ha) * 0.02 * s, head.Y - Math.Sin(ha) * 0.03 * s - Math.Cos(ha) * 0.04 * s);
        dc.DrawEllipse(Brush(kit.Hair), null, back, 0.1 * s, 0.085 * s);
        dc.FillEllipse(head.X + Math.Cos(ha) * 0.06 * s + Math.Sin(ha) * 0.01 * s, head.Y + Math.Sin(ha) * 0.06 * s - Math.Cos(ha) * 0.01 * s, Math.Max(1, 0.018 * s), 0xFF101010);
        Arm(r.ArmN, r.ElbowN, false);
    }

    /// <summary>Where the near hand is, for the javelin and the hammer.</summary>
    private static Point Hand(Rig r, double x, double y, double s, bool far = false)
    {
        var hip = new Point(x + r.HipX * s, y - r.HipY * s);
        var up = new Point(Math.Sin(r.Torso), -Math.Cos(r.Torso));
        var sh = new Point(hip.X + up.X * 0.49 * s, hip.Y + up.Y * 0.49 * s);
        double upper = far ? r.ArmF : r.ArmN, elbow = far ? r.ElbowF : r.ElbowN;
        return Limb(Limb(sh, upper, 0.3 * s), upper + elbow, 0.28 * s);
    }

    /// <summary>The rig for an athlete's pose at this moment.</summary>
    private Rig PoseOf(Athlete a, World w, bool rival)
    {
        double power = rival ? Math.Clamp(a.Speed / Sim.Top(w.Event), 0, 1) : w.Event == Ev.Hammer ? 0 : Math.Clamp(a.Speed / Sim.Top(w.Event), 0, 1);
        if (a.Jumping)
        {
            // Over a hurdle: the lead leg straight out, the trail leg tucked, a dip of the body and the arm reaching.
            double k = a.JumpT / Sim.HurdleTime;
            return new Rig
            {
                HipY = 0.88, Torso = 0.5 + 0.25 * Math.Sin(Math.PI * k), ThighN = 1.45, KneeN = 0.15 + 0.6 * Math.Max(0, k - 0.75) * 4,
                ThighF = -0.4 + 0.9 * k, KneeF = 1.7 - 0.6 * k, ArmN = -0.5, ElbowN = 0.9, ArmF = 1.4, ElbowF = 0.2,
            };
        }
        switch (a.Pose)
        {
            case Pose.Set:
            {
                // On your marks: a knee down; at SET the hips up.
                bool set = w.SetCalled;
                return new Rig
                {
                    HipX = -0.15, HipY = set ? 0.72 : 0.48, Torso = set ? 1.85 : 1.95, Head = -0.5,
                    ThighN = set ? 1.25 : 1.55, KneeN = set ? 1.9 : 2.4, ThighF = set ? 0.5 : 0.1, KneeF = set ? 1.1 : 1.9,
                    ArmN = 0.05 - (set ? 0.1 : 0), ElbowN = 0, ArmF = 0.1 - (set ? 0.1 : 0), ElbowF = 0,
                };
            }
            case Pose.Run when !rival && w.Racing && !double.IsNaN(a.Finish) && w.Since > 0.5 && (Engine.JustQualified || Engine.NewRecord):
            {
                var c = Running(a.Stride, 0.2);
                c.ArmN = 2.9 + 0.2 * Math.Sin(_clock * 8);
                c.ArmF = 3.3 - 0.2 * Math.Sin(_clock * 8);
                c.ElbowN = c.ElbowF = 0.25;
                c.Torso = -0.05;
                return c;
            }
            case Pose.Run or Pose.Stumble:
            {
                var r = Running(a.Stride, power);
                if (a.Pose == Pose.Stumble) { r.Torso = 0.8; r.ArmN = 1.6; r.ArmF = 1.2; r.ElbowN = r.ElbowF = 0.3; }
                if (a.Speed < 0.3 && double.IsNaN(a.Finish) && a.Pose == Pose.Run && !w.Racing) return Standing();
                if (!rival && w.Event == Ev.Javelin)
                {
                    // Carrying the javelin over the shoulder.
                    r.ArmN = 2.6;
                    r.ElbowN = 1.9;
                }
                return r;
            }
            case Pose.Plant:
            {
                // Gathering on the board: the take-off leg planted, the free knee and the arms driving up.
                double k = Math.Clamp(w.Angle / 60, 0, 1);
                return new Rig
                {
                    HipY = 0.95 + 0.05 * k, Torso = 0.1 - 0.1 * k, ThighN = 1.0 + 0.5 * k, KneeN = 1.6 - 0.4 * k, ThighF = -0.35, KneeF = 0.2,
                    ArmN = 1.2 + 1.2 * k, ElbowN = 0.6, ArmF = -0.6 + 1.6 * k, ElbowF = 0.5,
                };
            }
            case Pose.Air when w.Event == Ev.LongJump:
            {
                if (a.Vy > 0)
                {
                    // Rising: a hitch-kick, cycling the legs.
                    double p = a.T * 9;
                    return new Rig
                    {
                        HipY = 0.95, Torso = 0.05, ThighN = 0.5 + 0.6 * Math.Sin(p), KneeN = 0.8 + 0.5 * Math.Cos(p), ThighF = 0.5 - 0.6 * Math.Sin(p),
                        KneeF = 0.8 - 0.5 * Math.Cos(p), ArmN = 2.6 + 0.4 * Math.Sin(p), ElbowN = 0.3, ArmF = 2.6 - 0.4 * Math.Sin(p), ElbowF = 0.3,
                    };
                }
                // Coming down: legs out ahead, arms swept forward.
                double k = Math.Clamp(-a.Vy / 5, 0, 1);
                return new Rig
                {
                    HipY = 0.9, Torso = 0.25 + 0.35 * k, ThighN = 1.0 + 0.55 * k, KneeN = 0.6 - 0.45 * k, ThighF = 0.95 + 0.55 * k, KneeF = 0.65 - 0.45 * k,
                    ArmN = 2.4 - 1.0 * k, ElbowN = 0.2, ArmF = 2.3 - 1.0 * k, ElbowF = 0.2,
                };
            }
            case Pose.Sit when w.Event == Ev.HighJump:
                // On his back on the mat, legs in the air.
                return new Rig
                {
                    HipX = 0.1, HipY = 0.25, Torso = 1.75, Head = 0.2, ThighN = -2.3, KneeN = 0.6, ThighF = -2.1, KneeF = 0.8,
                    ArmN = 3.6, ElbowN = 0.3, ArmF = 3.8, ElbowF = 0.3,
                };
            case Pose.Sit:
                // Sitting in the sand.
                return new Rig
                {
                    HipY = 0.2, Torso = 0.55, ThighN = 1.45, KneeN = 0.35, ThighF = 1.35, KneeF = 0.5, ArmN = 1.2, ElbowN = 0.3, ArmF = 1.0, ElbowF = 0.3,
                };
            case Pose.Draw:
            {
                // The javelin drawn back: leaning away, the throwing arm straight back.
                double k = Math.Min(1, a.T / 0.25);
                return new Rig
                {
                    HipY = 0.9, Torso = -0.3 * k, ThighN = 0.55, KneeN = 0.1, ThighF = -0.45, KneeF = 0.7,
                    ArmN = -1.6 - 0.2 * k, ElbowN = 0.15, ArmF = 1.7, ElbowF = 0.4,
                };
            }
            case Pose.Follow when w.Event == Ev.Hammer:
            {
                double k = Math.Min(1, a.T / 0.3);
                return new Rig
                {
                    HipY = 0.92, Torso = -0.1 + 0.2 * k, ThighN = 0.3, KneeN = 0.15, ThighF = -0.25, KneeF = 0.3,
                    ArmN = 2.4 - 0.4 * k, ElbowN = 0.05, ArmF = 2.3 - 0.4 * k, ElbowF = 0.05,
                };
            }
            case Pose.Follow:
            {
                // Through: the arm over and down, the body pitching forward onto the front leg.
                double k = Math.Min(1, a.T / 0.3);
                return new Rig
                {
                    HipY = 0.9, Torso = -0.2 + 0.85 * k, ThighN = 0.45, KneeN = 0.4, ThighF = -0.6 + 0.3 * k, KneeF = 0.6 + 0.6 * k,
                    ArmN = 3.2 - 2.0 * k, ElbowN = 0.1, ArmF = -0.5, ElbowF = 0.5,
                };
            }
            case Pose.Spin:
            {
                // Turning: both arms out straight to the hammer, the body leaning back against it, the feet stepping round.
                double p = w.Spin, lean = -0.15 - 0.1 * Math.Cos(p);
                double toward = 1.4 * Math.Sin(p) + 0.2;
                return new Rig
                {
                    HipY = 0.88, Torso = lean, ThighN = 0.25 * Math.Sin(p) + 0.1, KneeN = 0.35, ThighF = -0.25 * Math.Sin(p) - 0.1, KneeF = 0.35,
                    ArmN = toward + 0.1, ElbowN = 0.05, ArmF = toward - 0.1, ElbowF = 0.05,
                };
            }
            case Pose.Flop:
            {
                // The flop: turning over onto the back, head first; the hips high, the legs hanging until the kick.
                double span = Math.Max(0.5, Sim.BarX + 0.8 - w.TakeoffX);
                double k = Math.Clamp((a.X - w.TakeoffX) / span, 0, 1.3);
                bool kick = w.Kicked && a.X > Sim.BarX - Sim.KickBefore;
                return new Rig
                {
                    HipY = 0, Torso = 0.1 + 1.55 * k, Head = 0.35 * k,
                    ThighN = kick ? -1.9 * Math.Min(1, k) : -0.9 * k, KneeN = kick ? 0.15 : 0.3 + 1.3 * k,
                    ThighF = kick ? -1.75 * Math.Min(1, k) : -0.8 * k, KneeF = kick ? 0.2 : 0.4 + 1.3 * k,
                    ArmN = 0.4 + 2.4 * k, ElbowN = 0.2, ArmF = 0.2 + 2.2 * k, ElbowF = 0.2,
                };
            }
            case Pose.Cheer:
                return new Rig { HipY = 0.95, ThighN = 0.1, ThighF = -0.1, KneeN = 0.05, KneeF = 0.05, ArmN = 2.8, ElbowN = 0.3, ArmF = 3.4, ElbowF = 0.3 };
            default:
                return Standing();
        }
    }

    private static double Lift(double y) => y < 2 ? y : 2 + (y - 2) * 0.3;

    private void DrawAthletes(DrawingContext dc, double cam, World w)
    {
        var kit = Engine.Players.Count > 0 ? Kits[(Engine.Current.Number - 1) % Kits.Length] : Kits[0];
        if (w.Racing)
        {
            var r = w.Rival;
            DrawAthlete(dc, r, PoseOf(r, w, true), Sx(r.X, cam), Field.Far - r.Y * S * 0.9, Field.Far, S * 0.9, RivalKit);
        }
        var me = w.Me;
        var rig = PoseOf(me, w, false);
        double x = Sx(me.X, cam), y = Field.Near, ground = Field.Near;
        switch (me.Pose)
        {
            case Pose.Air when w.Event == Ev.LongJump: y -= Math.Max(0, me.Y - Sim.JumpHeight) * 0.45 * S; break;
            case Pose.Flop or Pose.Sit when w.Event == Ev.HighJump:
            {
                double depth = JumperDepth(w);
                x += depth * 0.12;
                y = ground = Field.Near - depth;
                if (me.Pose == Pose.Flop) rig.HipY = me.Y - 0.05;
                else y -= MatHeight * S;
                break;
            }
            default: y -= me.Y * S; break;
        }
        bool behindBar = w.Event == Ev.HighJump && JumperDepth(w) > BarDepth;
        if (w.Event == Ev.HighJump && !behindBar) DrawHighJump(dc, cam, w, bar: true);
        if (w.Event == Ev.Hammer) DrawHammer(dc, cam, w, rig, x, y, kit, front: false);
        DrawAthlete(dc, me, rig, x, y, ground, S, kit);
        switch (w.Event)
        {
            case Ev.Javelin: DrawJavelin(dc, cam, w, rig, x, y); break;
            case Ev.Hammer:
                DrawHammer(dc, cam, w, rig, x, y, kit, front: true);
                DrawCage(dc, Sx(Sim.HammerX, cam), Field.Near - 6, back: false);
                break;
            case Ev.HighJump when behindBar: DrawHighJump(dc, cam, w, bar: true); break;
        }
    }

    private static void DrawAthlete(DrawingContext dc, Athlete a, Rig rig, double x, double y, double ground, double s, Kit kit)
    {
        if (x < -80 || x > Field.Width + 80) return;
        // A shadow on the ground, smaller the higher he is.
        double k = Math.Clamp(1 - (ground - y) / (3 * s), 0.4, 1);
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x + 4, ground + 2), 0.35 * s * k, 0.07 * s * k);
        DrawBody(dc, rig, x, y, s, kit);
    }

    private void DrawJavelin(DrawingContext dc, double cam, World w, Rig rig, double x, double y)
    {
        var t = w.Thing;
        var pen = Pen(0xFFE8E8F0, 3);
        if (t.Flying || t.Landed)
        {
            double tx = Sx(t.X, cam), ty = Field.Near - Lift(t.Y) * S;
            double vy = t.Y >= 2 ? t.Vy * 0.3 : t.Vy, ang = t.Landed ? -0.6 : Math.Atan2(vy, t.Vx);
            double cx = Math.Cos(ang) * 1.3 * S, cy = -Math.Sin(ang) * 1.3 * S;
            var tip = new Point(tx + cx * (t.Landed ? 0.2 : 1), ty + cy * (t.Landed ? 0.2 : 1));
            dc.DrawLine(pen, new Point(tx - cx * (t.Landed ? 1.8 : 1), ty - cy * (t.Landed ? 1.8 : 1)), tip);
            dc.DrawLine(Pen(0xFF303030, 4), new Point(tx - cx * 0.08, ty - cy * 0.08), new Point(tx + cx * 0.08, ty + cy * 0.08));
            return;
        }
        if (w.Me.Pose is Pose.Stumble) return;
        var hand = Hand(rig, x, y, S);
        double a = w.Me.Pose == Pose.Draw ? w.Angle * Math.PI / 180 : 0.12;
        double dx = Math.Cos(a) * S, dy = -Math.Sin(a) * S;
        dc.DrawLine(pen, new Point(hand.X - dx * 1.5, hand.Y - dy * 1.5), new Point(hand.X + dx * 1.1, hand.Y + dy * 1.1));
        dc.DrawLine(Pen(0xFF303030, 4), new Point(hand.X - dx * 0.08, hand.Y - dy * 0.08), new Point(hand.X + dx * 0.08, hand.Y + dy * 0.08));
    }

    /// <summary>The hammer: swinging round on its wire (behind the thrower or in front), or in flight, or down.</summary>
    private void DrawHammer(DrawingContext dc, double cam, World w, Rig rig, double x, double y, Kit kit, bool front)
    {
        var t = w.Thing;
        if (t.Flying || t.Landed)
        {
            if (!front) return;
            double tx = Sx(t.X, cam), ty = Field.Near - Lift(t.Y) * S - 4;
            dc.FillEllipse(tx, ty, 6, 0xFF404048);
            if (t.Flying)
            {
                double ang = Math.Atan2(t.Y >= 2 ? t.Vy * 0.3 : t.Vy, t.Vx);
                dc.DrawLine(Pen(0xFFB0B0B8, 1.5), new Point(tx, ty), new Point(tx - Math.Cos(ang) * 50 + Math.Sin(w.Clock * 20) * 6, ty + Math.Sin(ang) * 50 + Math.Cos(w.Clock * 20) * 6));
            }
            return;
        }
        var hand = Hand(rig, x, y, S);
        Point ball;
        bool behind;
        if (w.Me.Pose == Pose.Spin)
        {
            double p = w.Spin;
            behind = Math.Cos(p) < 0;
            ball = new Point(x + Math.Sin(p) * 1.6 * S, y - (1.15 - 0.55 * Math.Cos(p)) * S);
        }
        else
        {
            behind = false;
            ball = new Point(x + 0.5 * S, y - 0.08 * S);
        }
        if (behind == front) return;
        dc.DrawLine(Pen(0xFFB0B0B8, 1.5), hand, ball);
        dc.FillEllipse(ball.X, ball.Y, behind ? 5 : 6.5, behind ? 0xFF303038 : 0xFF484850);
        if (w.Me.Pose == Pose.Spin && w.Omega > 4)
        {
            // A blur of the swing behind the ball.
            double p = w.Spin - 0.5;
            var trail = new Point(x + Math.Sin(p) * 1.6 * S, y - (1.15 - 0.55 * Math.Cos(p)) * S);
            dc.DrawLine(Pen(0x60FFFFFF, 6), trail, ball);
        }
    }

    // ───────────────────────────── the scoreboard and the meters ─────────────────────────────

    /// <summary>The stadium scoreboard: amber lamps on black, the event, the clock or the mark, the qualifying mark and the world record.</summary>
    private void DrawScoreboard(DrawingContext dc, World? w)
    {
        var g = Engine;
        const double x = 232, y = 8, wd = 560, ht = 134;
        dc.FillRectangle(Brush(0xFF505868), new Rect(x + 120, y + ht, 12, 30));
        dc.FillRectangle(Brush(0xFF505868), new Rect(x + wd - 132, y + ht, 12, 30));
        dc.FillRectangle(Brush(0xFF3A4250), new Rect(x - 8, y - 4, wd + 16, ht + 8), 6);
        dc.FillRectangle(Brush(0xFF080808), new Rect(x, y, wd, ht), 3);
        if (w == null || !InGame && g.State != GameState.GameOver)
        {
            Text(dc, "AVA STADIUM", new Point(x + wd / 2, y + 40), 54, Amber, center: true, shadow: false);
            Text(dc, "TRACK AND FIELD - SIX EVENTS", new Point(x + wd / 2, y + 96), 30, Amber, center: true, shadow: false);
            Lamps(dc, x, y, wd, ht);
            return;
        }
        var ev = w.Event;
        Text(dc, Sim.Names[(int)ev], new Point(x + 14, y + 18), 30, Amber, shadow: false);
        Text(dc, g.Players.Count > 1 ? $"PLAYER {g.Current.Number}" : g.Cycle > 1 ? $"ROUND {g.Cycle}" : "PLAYER 1", new Point(x + wd - 14, y + 18), 30, Amber, right: true, shadow: false);
        string big;
        uint colour = Amber;
        bool flash = g.Phase == Phase.Result && (g.NewRecord || g.JustQualified);
        if (flash && Blink(3))
        {
            big = g.NewRecord ? "WORLD RECORD" : "QUALIFIED";
            colour = g.NewRecord ? 0xFFFF5040 : 0xFF60FF60;
        }
        else if (Sim.Track(ev))
        {
            double t = !double.IsNaN(w.Me.Finish) ? w.Me.Finish : w.Gun ? w.Clock : 0;
            big = w.FalseStart ? "FALSE START" : $"TIME {t:00.00}";
        }
        else if (g.Phase is Phase.Result && w.Foul) big = "FOUL";
        else if (g.Phase is Phase.Result or Phase.Tally && !double.IsNaN(g.LastMark)) big = $"{g.LastMark:0.00} M";
        else if (ev == Ev.HighJump) big = $"BAR {w.Bar:0.00} M";
        else big = $"ATTEMPT {g.Attempt + 1}";
        Text(dc, big, new Point(x + wd / 2, y + 66), 58, colour, center: true, shadow: false);
        string q = Sim.Timed(ev) ? $"{g.QualifyMark:0.00}" : $"{g.QualifyMark:0.00}M";
        string wr = Sim.Timed(ev) ? $"{g.Records[ev]:0.00}" : $"{g.Records[ev]:0.00}M";
        Text(dc, $"QUALIFY {q}", new Point(x + 14, y + 112), 28, Amber, shadow: false);
        Text(dc, $"W.R. {wr}", new Point(x + wd - 14, y + 112), 28, Amber, right: true, shadow: false);
        if (!Sim.Track(ev) && !double.IsNaN(g.Best))
            Text(dc, $"BEST {g.Best:0.00}", new Point(x + wd / 2, y + 112), 28, Amber, center: true, shadow: false);
        Lamps(dc, x, y, wd, ht);
    }

    /// <summary>The grid of the lamps over the board, so its words are made of dots.</summary>
    private static void Lamps(DrawingContext dc, double x, double y, double wd, double ht)
    {
        var grid = Pen(0xA0080808, 1.4);
        for (double gx = x + 2; gx < x + wd; gx += 3.5) dc.DrawLine(grid, new Point(gx, y), new Point(gx, y + ht));
        for (double gy = y + 2; gy < y + ht; gy += 3.5) dc.DrawLine(grid, new Point(x, gy), new Point(x + wd, gy));
    }

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    /// <summary>The panel below: the players' scores, the speed meter, the angle, the attempts.</summary>
    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(0xFF101420), new Rect(0, Hud, Field.Width, Field.Height - Hud));
        dc.FillRectangle(Brush(0xFF3A4A7A), new Rect(0, Hud, Field.Width, 3));
        for (int i = 0; i < g.Players.Count; i++)
        {
            var p = g.Players[i];
            bool now = i == g.Turn;
            uint c = p.Out ? Dim : now ? Kits[i % Kits.Length].Shirt | 0xFF404040 : White;
            Text(dc, $"{i + 1}UP", new Point(20, Hud + 26 + i * 38), 26, now && Blink(2) ? Yellow : c);
            Text(dc, $"{p.Score:000000}", new Point(80, Hud + 26 + i * 38), 30, c);
        }
        Text(dc, $"HI {Math.Max(g.HighScores.TopScore, g.Players.Max(p => p.Score)):000000}", new Point(20, Hud + 26 + g.Players.Count * 38), 24, Dim);
        // The speed meter: the tapping rate, or the hammer's turning.
        double power = w.Event == Ev.Hammer ? w.Omega / Sim.SpinTop : Sim.Power(w.Me);
        const double mx = 300, my = Hud + 22;
        Text(dc, w.Event == Ev.Hammer ? "TURN" : "SPEED", new Point(mx, my), 24, White);
        const int segments = 20;
        for (int i = 0; i < segments; i++)
        {
            double k = (i + 0.5) / segments;
            uint on = k < 0.6 ? 0xFF40D040 : k < 0.85 ? 0xFFF0D030 : 0xFFF04030;
            dc.FillRectangle(Brush(power >= k ? on : Blend(on, 0xFF101420, 0.8)), new Rect(mx + 70 + i * 13, my - 10, 10, 22), 2);
        }
        Text(dc, Sim.Track(w.Event) || w.Event == Ev.Hammer ? $"{(w.Event == Ev.Hammer ? w.Omega / (2 * Math.PI) : w.Me.Speed):0.0}{(w.Event == Ev.Hammer ? " TURNS/S" : " M/S")}" : $"{w.Me.Speed:0.0} M/S",
            new Point(mx + 70, my + 34), 22, Dim);
        // The tries.
        string tries = w.Event switch
        {
            Ev.Dash or Ev.Hurdles => g.FalseStarts > 0 ? "ONE FALSE START" : $"ROUND {g.Cycle}",
            Ev.HighJump => $"BAR {g.Bar:0.00}  TRY {Math.Min(g.Attempt + 1, Sim.Tries)}",
            _ => $"ATTEMPT {Math.Min(g.Attempt + 1, Sim.Tries)} OF {Sim.Tries}",
        };
        Text(dc, tries, new Point(mx + 330, my + 34), 22, Dim, right: true);
        if (!Sim.Track(w.Event)) DrawAngle(dc, 760, Hud + 84, w);
        else
        {
            Text(dc, "RUN: TAP Z AND X", new Point(860, Hud + 26), 22, Dim, center: true);
            Text(dc, w.Event == Ev.Hurdles ? "SPACE: JUMP" : "IN TURN, FAST", new Point(860, Hud + 56), 22, Dim, center: true);
        }
    }

    /// <summary>The angle gauge: a quarter circle, the angle held, and a mark at the best angle.</summary>
    private void DrawAngle(DrawingContext dc, double cx, double cy, World w)
    {
        const double r = 70;
        var arc = new StreamGeometry();
        using (var c = arc.Open())
        {
            c.BeginFigure(new Point(cx + r, cy), true);
            c.ArcTo(new Point(cx, cy - r), new Size(r, r), 0, false, SweepDirection.CounterClockwise);
            c.LineTo(new Point(cx, cy));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(0xFF1E2A44), Pen(0xFF5A6A9A, 2), arc);
        double best = w.Event == Ev.HighJump ? 70 : 44;
        for (int d = 0; d <= 90; d += 15)
        {
            double a = d * Math.PI / 180;
            dc.DrawLine(Pen(0xFF5A6A9A, 1.5), new Point(cx + Math.Cos(a) * r * 0.85, cy - Math.Sin(a) * r * 0.85), new Point(cx + Math.Cos(a) * r, cy - Math.Sin(a) * r));
        }
        double b = best * Math.PI / 180;
        dc.DrawLine(Pen(Green, 3), new Point(cx + Math.Cos(b) * r * 0.7, cy - Math.Sin(b) * r * 0.7), new Point(cx + Math.Cos(b) * r, cy - Math.Sin(b) * r));
        bool show = w.Holding || w.Launched;
        double ang = show ? w.Angle * Math.PI / 180 : 0;
        dc.DrawLine(Pen(w.Holding ? Yellow : White, 3), new Point(cx, cy), new Point(cx + Math.Cos(ang) * r * 0.95, cy - Math.Sin(ang) * r * 0.95));
        dc.FillEllipse(cx, cy, 4, White);
        Text(dc, show ? $"{w.Angle:0}°" : "ANGLE", new Point(cx + 150, cy - 50), 34, w.Holding ? Yellow : White, center: true);
        Text(dc, "HOLD SPACE", new Point(cx + 150, cy - 18), 20, Dim, center: true);
    }

    /// <summary>The starter's calls, and the marks and fouls, over the track.</summary>
    private void DrawCalls(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        string? call = null;
        uint colour = White;
        if (g.Phase == Phase.Go && Sim.Track(w.Event))
        {
            if (!w.Gun) call = w.SetCalled ? "SET" : "ON YOUR MARKS";
            else if (w.Clock < 0.8) { call = "GO!"; colour = Yellow; }
        }
        else if (g.Phase == Phase.Ready) call = w.Event == Ev.HighJump ? $"{w.Bar:0.00} M - TRY {g.Attempt + 1}" : $"ATTEMPT {g.Attempt + 1}";
        else if (g.Phase == Phase.Result && g.Message is { } msg)
        {
            call = msg;
            colour = w.Foul || double.IsNaN(w.Result) ? Red : Yellow;
        }
        if (call != null)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(262, 424, 500, 58), 8);
            Text(dc, call, new Point(512, 452), 50, colour, center: true);
        }
        if (g.Phase == Phase.Result && (g.NewRecord || g.JustQualified))
        {
            string flash = g.NewRecord ? "WORLD RECORD!" : "QUALIFIED!";
            if (Blink(3))
            {
                dc.FillRectangle(Brush(0x90000000), new Rect(332, 484, 360, 46), 8);
                Text(dc, flash, new Point(512, 506), 44, g.NewRecord ? Red : Green, center: true);
            }
        }
    }

    /// <summary>The event's card: its name and the marks.</summary>
    private void DrawIntro(DrawingContext dc)
    {
        var g = Engine;
        var ev = g.Event;
        double cx = 512;
        dc.FillRectangle(Brush(0xD8000000), new Rect(cx - 340, 410, 680, 236), 12);
        dc.DrawRectangle(null, Pen(Kits[(g.Current.Number - 1) % Kits.Length].Shirt, 3), new Rect(cx - 340, 410, 680, 236), 12, 12);
        Text(dc, $"EVENT {g.EventIndex + 1}" + (g.Cycle > 1 ? $" - ROUND {g.Cycle}" : ""), new Point(cx, 440), 28, Dim, center: true);
        Text(dc, Sim.Names[(int)ev], new Point(cx, 484), 62, Yellow, center: true);
        Text(dc, $"PLAYER {g.Current.Number}", new Point(cx, 532), 32, White, center: true);
        Text(dc, $"QUALIFY {Sim.Format(ev, g.QualifyMark)}", new Point(cx, 572), 34, Green, center: true);
        Text(dc, $"WORLD RECORD {Sim.Format(ev, g.Records[ev])}", new Point(cx, 610), 28, Red, center: true);
    }

    /// <summary>The event over: this round's events, their marks and points, and whether this one qualified.</summary>
    private void DrawTally(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Current;
        double cx = 512;
        dc.FillRectangle(Brush(0xE8080808), new Rect(cx - 380, 160, 760, 480), 10);
        dc.DrawRectangle(null, Pen(0xFF3A4250, 4), new Rect(cx - 380, 160, 760, 480), 10, 10);
        Text(dc, $"PLAYER {p.Number} - ROUND {g.Cycle} RESULTS", new Point(cx, 196), 36, Amber, center: true, shadow: false);
        for (int i = 0; i < Sim.Events; i++)
        {
            var ev = (Ev)i;
            double y = 250 + i * 46;
            bool now = i == g.EventIndex;
            uint c = now ? Yellow : i < g.EventIndex ? Amber : AmberDim;
            Text(dc, Sim.Names[i], new Point(cx - 350, y), 30, c, shadow: false);
            string mark = i <= g.EventIndex ? Sim.Format(ev, p.Marks[i]) : "";
            Text(dc, mark, new Point(cx + 150, y), 30, c, right: true, shadow: false);
            Text(dc, i <= g.EventIndex ? $"{p.Points[i],5}" : "", new Point(cx + 350, y), 30, c, right: true, shadow: false);
        }
        bool ok = g.Qualified;
        Text(dc, ok ? "QUALIFIED" : "FAILED TO QUALIFY", new Point(cx, 548), 54, ok ? 0xFF60FF60 : (Blink(3) ? 0xFFFF5040 : AmberDim), center: true, shadow: false);
        Text(dc, $"SCORE {p.Score}", new Point(cx, 600), 34, Amber, center: true, shadow: false);
        Lamps(dc, cx - 376, 164, 752, 472);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        double cam = _clock * 9.5 - 6;
        var w = new World { Event = Ev.Dash, Racing = true, Gun = true, Clock = 5 };
        w.Me = new Athlete { X = cam + 6, Speed = 11.6, Stride = _clock * 15, Pose = Pose.Run };
        w.Rival = new Athlete { X = cam + 8.2 + Math.Sin(_clock * 0.4) * 0.6, Speed = 11.6, Stride = _clock * 15 + 1.7, Pose = Pose.Run };
        DrawStadium(dc, cam, w);
        dc.FillRectangle(Brush(0xC8000000), new Rect(cx - 430, 154, 860, 222), 12);
        string title = "AVA TRACK & FIELD";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 24 + 24 + i * 48, ty = 200 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 92, i < 3 ? 0xFFF0F0F0 : i < 9 ? 0xFFFF5A40 : 0xFFFFD040, center: true);
        }
        Text(dc, "AFTER KONAMI'S 1983 ARCADE GAME", new Point(cx, 252), 26, White, center: true);
        Text(dc, "100 METRES - LONG JUMP - JAVELIN - 110 METRE HURDLES - HAMMER - HIGH JUMP", new Point(cx, 288), 22, Cyan, center: true);
        Text(dc, "HAMMER THE TWO RUN BUTTONS IN TURN FOR SPEED; HOLD ACTION TO SET THE ANGLE.", new Point(cx, 318), 22, White, center: true);
        Text(dc, "MAKE THE QUALIFYING MARK IN EVERY EVENT, OR IT'S ALL OVER.", new Point(cx, 346), 22, White, center: true);
        if (Blink(2)) Text(dc, "PRESS 1 OR 2 PLAYERS", new Point(cx, 452), 46, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, Hud, Field.Width, Field.Height - Hud));
        Text(dc, "RUN: Z AND X (OR LEFT AND RIGHT) IN TURN   ACTION: SPACE, CTRL OR UP   P: PAUSE   M: SOUND", new Point(cx, Hud + 30), 21, 0xFFE0D0B0, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore}", new Point(cx, Hud + 58), 22, Yellow, center: true);
        Text(dc, "MUSIC: ODE TO JOY (BEETHOVEN, 1824) AND TUNES OF ITS OWN", new Point(cx, Hud + 84), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 380, 170, 760, 260), 12);
        Text(dc, "GAME OVER", new Point(cx, 230), 86, Blink() ? Yellow : White, center: true);
        for (int i = 0; i < g.Players.Count; i++)
        {
            var p = g.Players[i];
            string why = p.FailedAt is { } ev ? $"OUT AT THE {Sim.Names[(int)ev]}" : "";
            Text(dc, $"PLAYER {p.Number}  {p.Score}", new Point(cx, 310 + i * 64), 40, White, center: true);
            Text(dc, why, new Point(cx, 342 + i * 64), 24, Cyan, center: true);
        }
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8000000), new Rect(cx - 330, 154, 660, 600), 12);
        Text(dc, "THE GREATEST ATHLETES", new Point(cx, 196), 54, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  EVENTS", new Point(cx, 252), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && Blink();
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 296 + i * 40), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS 1 OR 2 TO PLAY", new Point(cx, 718), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8000000), new Rect(cx - 330, 160, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 220), 64, Yellow, center: true);
        Text(dc, $"PLAYER {g.Entering?.Number ?? 1}  {g.Entering?.Score ?? 0}", new Point(cx, 290), 46, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 370), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 460), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 500, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 580), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
