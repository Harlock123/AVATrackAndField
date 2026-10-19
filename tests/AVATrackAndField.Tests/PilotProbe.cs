using AVATrackAndField.Engine;

namespace AVATrackAndField.Tests;

/// <summary>
/// How the computer athlete gets on, for tuning (set TRACK_PROBE to run it): each event at a few tapping rates, then a
/// whole game round and round until it fails to qualify.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_athlete_does()
    {
        if (Environment.GetEnvironmentVariable("TRACK_PROBE") is null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var ev in Enum.GetValues<Ev>())
        {
            var line = $"{Sim.Names[(int)ev],-18}";
            foreach (double rate in new[] { 6.0, 8.0, 9.0, 10.6, 14.0 })
            {
                var w = new World();
                Sim.Start(w, ev, 1, ev == Ev.HighJump ? 2.0 : 0);
                var p = new Pilot { Rate = rate };
                p.Reset();
                for (int t = 0; t < 60 * 40 && !(w.Over && !w.Thing.Flying); t++) Sim.Step(w, p.Decide(w));
                string mark = ev == Ev.HighJump ? $"{w.Margin:+0.00;-0.00}{(w.Cleared ? "ok" : "x")}" : double.IsNaN(w.Result) ? w.Note : $"{w.Result:0.00}";
                line += $"  {rate,4:0.0}/s {mark,-8} {w.Angle,2:0}°";
                if (ev == Ev.Hammer) line += $" off {w.Release:0.00} turn {w.Omega / Sim.SpinTop:0.00}";
            }
            output.WriteLine(line);
        }
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avatrack-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.StartGame(1);
        int cycle = 1, ev0 = 0;
        for (int t = 0; t < 60 * 60 * 30 && g.State == GameState.Playing; t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Tally && g.PhaseTime < Field.Step * 0.5)
                output.WriteLine($"round {g.Cycle} {Sim.Names[g.EventIndex],-18} {Sim.Format(g.Event, g.Best),-10} qualify {Sim.Format(g.Event, g.QualifyMark),-10} {(g.Qualified ? "QUALIFIED" : "FAILED")}  score {g.Score}  at {t / 60}s");
            (cycle, ev0) = (g.Cycle, g.EventIndex);
        }
        output.WriteLine($"ended in round {cycle} at {Sim.Names[ev0]}; {sw.ElapsedMilliseconds} ms");
    }
}
