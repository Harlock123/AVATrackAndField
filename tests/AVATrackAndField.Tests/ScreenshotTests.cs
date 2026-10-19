using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVATrackAndField.Engine;
using AVATrackAndField.Views;

namespace AVATrackAndField.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set TRACK_SHOTS to a folder to save
/// them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avatrack-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("TRACK_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVATrackAndField-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>The computer athlete plays on until the moment comes.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            c.Advance(1 / 60.0);
        }
        return false;
    }

    /// <summary>On to where the blinking words are lit.</summary>
    private static void Lit(GameCanvas c, double rate = 3)
    {
        double k = c.DrawClock * rate % 2;
        if (k >= 0.9) c.Advance((2.2 - k) / rate);
    }

    private static void Moment(Ev ev, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(ev);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Lit(c, 2);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 21480, 9);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 120 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_dash() => Moment(Ev.Dash, "dash", g => g.World.Gun && g.World.Clock > 4.2);

    [AvaloniaFact]
    public void The_long_jump() => Moment(Ev.LongJump, "long-jump", g => g.World.Me.Pose == Pose.Air && g.World.Me.Vy < 0.5);

    [AvaloniaFact]
    public void The_javelin() => Moment(Ev.Javelin, "javelin", g => g.World.Me.Pose == Pose.Draw && g.World.Angle >= 40);

    [AvaloniaFact]
    public void The_hurdles() => Moment(Ev.Hurdles, "hurdles", g => g.World.Me.Jumping && g.World.Me.JumpT > Sim.HurdleTime * 0.45 && g.World.Clock > 3);

    [AvaloniaFact]
    public void The_hammer() => Moment(Ev.Hammer, "hammer", g => g.World.Holding && g.World.Angle > 30 && Math.Cos(g.World.Spin) > 0.6);

    [AvaloniaFact]
    public void The_high_jump() => Moment(Ev.HighJump, "high-jump", g => g.World.Me.Pose == Pose.Flop && g.World.Me.X > Sim.BarX - 0.15);

    [AvaloniaFact]
    public void A_world_record()
    {
        var (w, c, g) = Open();
        g.Go(Ev.Dash);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Result && g.PhaseTime > 1.2));
        Assert.True(g.NewRecord);
        Lit(c);
        Shot(w, c, "world-record");
    }
}
