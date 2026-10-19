using Avalonia.Input;
using AVATrackAndField.Engine;

namespace AVATrackAndField.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avatrack-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine(string? folder = null) => new(HighScoreTable.Load(folder ?? TempFolder())) { Silent = true };

    private static World Attempt(Ev e, int cycle = 1, double bar = 0)
    {
        var w = new World();
        Sim.Start(w, e, cycle, bar);
        w.Heard.Clear();
        return w;
    }

    /// <summary>Tapping A and B in turn, so many times a second: the tap (if any) due at step t.</summary>
    private static Controls Tap(int t, double rate, bool oneButton = false)
    {
        int before = (int)(t * rate / 60), after = (int)((t + 1) * rate / 60);
        if (after == before) return default;
        return oneButton || after % 2 == 0 ? new Controls(A: 1) : new Controls(B: 1);
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    /// <summary>A race from the gun, tapping at a rate, until so many seconds in.</summary>
    private static World Race(Ev e, double rate, double seconds, bool oneButton = false)
    {
        var w = Attempt(e);
        while (!w.Gun) Sim.Step(w, default);
        for (int t = 0; t < seconds * 60; t++) Sim.Step(w, Tap(t, rate, oneButton));
        return w;
    }

    /// <summary>Runs up tapping at a rate until the next step would pass x; then ACTION goes down there.</summary>
    private static int RunTo(World w, double x, double rate)
    {
        int t = 0;
        while (w.Me.X + w.Me.Speed * Field.Step < x && t < 60 * 20) Sim.Step(w, Tap(t++, rate));
        return t;
    }

    /// <summary>ACTION down, held for so many more steps, and let go: the angle is (hold + 1) × 1.25°.</summary>
    private static void PressHold(World w, int hold, Func<World, Controls>? taps = null)
    {
        Sim.Step(w, new Controls(Action: true) with { A = taps?.Invoke(w).A ?? 0 });
        for (int i = 0; i < hold; i++) Sim.Step(w, (taps?.Invoke(w) ?? default) with { Action = true });
        Sim.Step(w, taps?.Invoke(w) ?? default);
    }

    private static void Finish(World w)
    {
        for (int i = 0; i < 60 * 12 && !(w.Over && !w.Thing.Flying); i++) Sim.Step(w, default);
    }

    private static World LongJump(int hold, double rate = 10.6, double at = Sim.JumpLine - 0.05)
    {
        var w = Attempt(Ev.LongJump);
        RunTo(w, at, rate);
        PressHold(w, hold);
        Finish(w);
        return w;
    }

    private static World Javelin(int hold, double rate = 10.6, double room = 0.2)
    {
        var w = Attempt(Ev.Javelin);
        int t = 0;
        while (w.Me.X + w.Me.Speed * w.Me.Speed / (2 * Sim.ThrowBrake) + w.Me.Speed * Field.Step < Sim.JavelinLine - room) Sim.Step(w, Tap(t++, rate));
        PressHold(w, hold);
        Finish(w);
        return w;
    }

    private static int Hold(double degrees) => (int)Math.Round(degrees / (Sim.AngleRate * Field.Step)) - 1;

    // ───────────────────────────── tapping and speed ─────────────────────────────

    [Fact]
    public void Tapping_faster_runs_faster_up_to_flat_out()
    {
        double still = Race(Ev.Dash, 0, 3).Me.Speed, slow = Race(Ev.Dash, 5, 3).Me.Speed, fast = Race(Ev.Dash, 9, 3).Me.Speed;
        double flat = Race(Ev.Dash, 30, 3).Me.Speed;
        Assert.Equal(0, still);
        Assert.True(slow > 3 && slow < fast, $"{slow} {fast}");
        Assert.True(fast < flat);
        Assert.True(flat <= Sim.Top(Ev.Dash) + 1e-9);
        Assert.True(flat > Sim.Top(Ev.Dash) - 0.01);
    }

    [Fact]
    public void Two_buttons_in_turn_beat_one_button_hammered_as_fast()
    {
        var both = Race(Ev.Dash, 8, 3);
        var one = Race(Ev.Dash, 8, 3, oneButton: true);
        Assert.True(both.Me.Rate > one.Me.Rate * 1.6);
        Assert.True(both.Me.Speed > one.Me.Speed + 2);
    }

    [Fact]
    public void The_meter_follows_the_tapping_and_falls_when_it_stops()
    {
        var w = Race(Ev.Dash, 8, 3);
        Assert.InRange(w.Me.Rate, 6.5, 9.5);
        Assert.InRange(Sim.Power(w.Me), 0.65, 0.95);
        double v = w.Me.Speed;
        Run(w, 60);
        Assert.True(Sim.Power(w.Me) < 0.15);
        Assert.True(w.Me.Speed < v - 2);
    }

    // ───────────────────────────── the dash and the hurdles ─────────────────────────────

    [Fact]
    public void On_your_marks_set_and_the_gun_then_the_clock_runs_to_the_line()
    {
        var w = new World();
        Sim.Start(w, Ev.Dash);
        Assert.Contains(Sfx.SayMarks, w.Heard);
        w.Heard.Clear();
        var heard = new List<Sfx>();
        while (!w.Gun) { Sim.Step(w, default); heard.AddRange(w.Heard); w.Heard.Clear(); }
        Assert.Equal([Sfx.SaySet, Sfx.Gun], heard);
        Assert.Equal(Pose.Run, w.Me.Pose);
        for (int t = 0; t < 60 * 20 && !w.Over; t++) Sim.Step(w, Tap(t, 10.6));
        Assert.True(w.Over);
        Assert.InRange(w.Result, 9.6, 10.2);
        Assert.Equal(Sim.TimeMark(w.Me.Finish), w.Result);
        // The rival in the next lane: steady, and on to the line too.
        Run(w, 60 * 6);
        Assert.False(double.IsNaN(w.Rival.Finish));
        Assert.True(w.Rival.Finish > w.Me.Finish);
    }

    [Fact]
    public void Away_before_the_gun_is_a_false_start_and_two_is_out()
    {
        var w = Attempt(Ev.Dash);
        Run(w, 30);
        Sim.Step(w, new Controls(A: 1));
        Assert.True(w.FalseStart && w.Over && w.Foul);
        var g = Engine();
        g.Go(Ev.Dash);
        g.Run(20);
        g.Tick(new Controls(B: 1));
        Assert.Equal(Phase.Result, g.Phase);
        Assert.Equal(1, g.FalseStarts);
        g.Run((int)(GameEngine.ResultSeconds * 60) + 2);
        Assert.Equal(Phase.Go, g.Phase);
        Assert.False(g.World.FalseStart);
        g.Run(20);
        g.Tick(new Controls(A: 1));
        g.Run((int)(GameEngine.ResultSeconds * 60) + 2);
        Assert.Equal(Phase.Tally, g.Phase);
        Assert.False(g.Qualified);
        Assert.True(g.Current.Out);
    }

    [Fact]
    public void A_jump_at_the_right_moment_clears_a_hurdle_and_running_through_knocks_it_down()
    {
        // Through every hurdle: each one down, and slowed each time.
        var w = Race(Ev.Hurdles, 10.6, 3);
        Assert.True(w.Knocked[0]);
        var clean = Attempt(Ev.Hurdles);
        while (!clean.Gun) Sim.Step(clean, default);
        int t = 0;
        // Up when the hurdle is half a jump away, so it's under the top of the jump.
        while (clean.Me.X < Sim.HurdleAt(0) + 1)
        {
            double d = Sim.HurdleAt(0) - clean.Me.X;
            bool jump = !clean.Me.Jumping && d > 0 && d <= clean.Me.Speed * Sim.HurdleTime / 2;
            Sim.Step(clean, Tap(t++, 10.6) with { Action = jump });
        }
        Assert.False(clean.Knocked[0]);
        Assert.True(clean.Me.Speed > 8.5);
        // Too early, and he's coming down as he gets there.
        var early = Attempt(Ev.Hurdles);
        while (!early.Gun) Sim.Step(early, default);
        t = 0;
        while (early.Me.X < Sim.HurdleAt(0) + 1)
        {
            double d = Sim.HurdleAt(0) - early.Me.X;
            Sim.Step(early, Tap(t++, 10.6) with { Action = !early.Me.Jumping && d < 6 && d > 5 });
        }
        Assert.True(early.Knocked[0]);
    }

    [Fact]
    public void The_computer_hurdler_clears_every_hurdle_and_beats_the_hurdler_who_doesnt_jump()
    {
        var w = Attempt(Ev.Hurdles);
        var p = new Pilot();
        p.Reset();
        for (int i = 0; i < 60 * 30 && !w.Over; i++) Sim.Step(w, p.Decide(w));
        Assert.All(w.Knocked, k => Assert.False(k));
        var lazy = Race(Ev.Hurdles, 10.6, 25);
        Assert.All(lazy.Knocked, k => Assert.True(k));
        Assert.True(lazy.Result > w.Result + 2);
    }

    // ───────────────────────────── the long jump ─────────────────────────────

    [Fact]
    public void Holding_action_raises_the_angle_and_letting_go_jumps()
    {
        var w = Attempt(Ev.LongJump);
        RunTo(w, Sim.JumpLine - 0.1, 10);
        Sim.Step(w, new Controls(Action: true));
        Assert.Equal(Pose.Plant, w.Me.Pose);
        Assert.True(w.Holding);
        Run(w, 30, new Controls(Action: true));
        Assert.Equal(30 * Sim.AngleRate * Field.Step, w.Angle, 6);
        Sim.Step(w, default);
        Assert.Equal(Pose.Air, w.Me.Pose);
        Assert.True(w.Me.Vy > 0);
        Finish(w);
        Assert.Equal(Pose.Sit, w.Me.Pose);
        Assert.Equal(Sim.TapeMark(w.Me.X - Sim.JumpLine), w.Result);
    }

    [Fact]
    public void The_best_long_jump_angle_is_between_42_and_45_degrees()
    {
        var marks = Enumerable.Range(20, 50).Select(h => (Angle: (h + 1) * Sim.AngleRate * Field.Step, Mark: LongJump(h).Result)).ToList();
        var best = marks.MaxBy(m => m.Mark);
        Assert.InRange(best.Angle, 42, 45.1);
        Assert.True(LongJump(Hold(30)).Result < best.Mark - 0.5);
        Assert.True(LongJump(Hold(60)).Result < best.Mark - 0.5);
    }

    [Fact]
    public void A_faster_run_up_jumps_further()
    {
        double slow = LongJump(Hold(44), rate: 8).Result, fast = LongJump(Hold(44), rate: 10.6).Result;
        Assert.True(fast > slow + 1, $"{slow} {fast}");
        Assert.InRange(fast, 8.7, 9.3);
    }

    [Fact]
    public void Over_the_board_is_a_foul()
    {
        // Running through without jumping.
        var w = Attempt(Ev.LongJump);
        RunTo(w, Sim.JumpLine + 1, 10);
        Assert.True(w.Over && w.Foul);
        Assert.True(double.IsNaN(w.Result));
        Assert.Equal("FOUL", w.Note);
        Assert.Contains(Sfx.Foul, w.Heard);
        // And an early take-off is no foul, but it's measured from the board.
        var early = LongJump(Hold(44), at: Sim.JumpLine - 1.5);
        var close = LongJump(Hold(44));
        Assert.False(early.Foul);
        Assert.InRange(close.Result - early.Result, 1.2, 1.7);
    }

    // ───────────────────────────── the javelin ─────────────────────────────

    [Fact]
    public void The_best_javelin_angle_is_between_42_and_46_degrees()
    {
        var marks = Enumerable.Range(20, 50).Select(h => (Angle: (h + 1) * Sim.AngleRate * Field.Step, Mark: Javelin(h).Result)).Where(m => !double.IsNaN(m.Mark)).ToList();
        var best = marks.MaxBy(m => m.Mark);
        Assert.InRange(best.Angle, 42, 46.1);
        Assert.InRange(best.Mark, 95, 105);
        var w = Javelin(Hold(44));
        Assert.True(w.Thing.Landed);
        Assert.Equal(Sim.TapeMark(w.Thing.X - Sim.JavelinLine), w.Result);
    }

    [Fact]
    public void Over_the_javelin_line_even_after_the_throw_is_a_foul()
    {
        // Drawn back too late: he can't stop before the line.
        var w = Javelin(Hold(44), room: -3);
        Assert.True(w.Foul);
        Assert.True(double.IsNaN(w.Result));
        Assert.True(w.Me.X > Sim.JavelinLine);
        // Never thrown at all.
        var run = Attempt(Ev.Javelin);
        RunTo(run, Sim.JavelinLine + 1, 10);
        Assert.True(run.Foul);
    }

    // ───────────────────────────── the hammer ─────────────────────────────

    /// <summary>
    /// Turns the hammer up to speed tapping at a rate, then times ACTION so that, held for so many steps, it's let go
    /// as the hammer swings the way given (0 is straight down the field).
    /// </summary>
    private static World Hammer(double rate, double release, int hold)
    {
        var w = Attempt(Ev.Hammer);
        int t = 0;
        for (; t < 60 * 3; t++) Sim.Step(w, Tap(t, rate));
        while (t < 60 * 6 && Math.Abs(Sim.Wrap(w.Spin + w.Omega * Field.Step * (hold + 2) - release)) > w.Omega * Field.Step / 2)
            Sim.Step(w, Tap(t++, rate));
        Sim.Step(w, Tap(t++, rate) with { Action = true });
        for (int i = 0; i < hold; i++) Sim.Step(w, Tap(t++, rate) with { Action = true });
        Sim.Step(w, Tap(t, rate));
        Finish(w);
        return w;
    }

    [Fact]
    public void The_hammer_turns_faster_with_faster_tapping_and_flies_further()
    {
        var slow = Hammer(7, 0, Hold(44));
        var fast = Hammer(10.6, 0, Hold(44));
        Assert.False(slow.Foul || fast.Foul);
        Assert.True(fast.Result > slow.Result + 15, $"{slow.Result} {fast.Result}");
        Assert.InRange(fast.Result, 70, 90);
    }

    [Fact]
    public void Let_go_out_of_the_sector_the_hammer_hits_the_cage()
    {
        var w = Hammer(10.6, Math.PI, Hold(30));
        Assert.True(w.Foul);
        Assert.True(Math.Abs(w.Release) > Sim.Sector);
        Assert.True(double.IsNaN(w.Result));
        // Off straight down the field, but in the sector: legal, and shorter.
        var off = Hammer(10.6, Sim.Sector * 0.7, Hold(44));
        var straight = Hammer(10.6, 0, Hold(44));
        Assert.False(off.Foul);
        Assert.True(off.Result < straight.Result - 5);
    }

    [Fact]
    public void Standing_in_the_circle_too_long_is_a_foul()
    {
        var w = Attempt(Ev.Hammer);
        Run(w, (int)(Sim.TimeLimit * 60) + 5);
        Assert.True(w.Foul);
        Assert.Equal("TIME", w.Note);
    }

    // ───────────────────────────── the high jump ─────────────────────────────

    /// <summary>A high jump at a bar: up from just outside the mark, held to an angle, and the kick at the bar (or not).</summary>
    private static World HighJump(double bar, int hold, bool kick, double rate = 10.6)
    {
        var w = Attempt(Ev.HighJump, bar: bar);
        RunTo(w, Sim.BarX - Sim.TakeoffNear - 0.05, rate);
        PressHold(w, hold);
        for (int i = 0; i < 60 * 4 && !w.Over; i++)
            Sim.Step(w, new Controls(Action: kick && w.Me.X > Sim.BarX - 0.3 && !w.KickTried));
        return w;
    }

    [Fact]
    public void The_kick_over_the_bar_makes_the_difference_at_the_top()
    {
        var probe = HighJump(2.0, Hold(70), kick: true);
        Assert.True(probe.Cleared);
        double top = 2.0 + probe.Margin;
        Assert.InRange(top, 2.3, 2.5);
        double bar = Math.Round(top - 0.06, 2);
        var kicked = HighJump(bar, Hold(70), kick: true);
        var lazy = HighJump(bar, Hold(70), kick: false);
        Assert.True(kicked.Cleared);
        Assert.Equal(bar, kicked.Result);
        Assert.False(lazy.Cleared);
        Assert.True(lazy.BarDown);
        Assert.Equal("MISS", lazy.Note);
        // Well under the bar, the kick doesn't matter.
        Assert.True(HighJump(bar - 0.2, Hold(70), kick: false).Cleared);
    }

    [Fact]
    public void Too_close_to_the_bar_is_a_miss_and_a_slow_run_up_jumps_lower()
    {
        var w = Attempt(Ev.HighJump, bar: 1.8);
        RunTo(w, Sim.BarX - Sim.TakeoffNear + 0.3, 10);
        Sim.Step(w, new Controls(Action: true));
        Assert.True(w.Over && w.BarDown);
        Assert.Equal("TOO CLOSE", w.Note);
        double fast = HighJump(1.5, Hold(70), true).Margin, slow = HighJump(1.5, Hold(70), true, rate: 7).Margin;
        Assert.True(fast > slow + 0.2);
    }

    [Fact]
    public void Clear_the_bar_and_it_goes_up_three_misses_and_its_over()
    {
        var g = Engine();
        g.Go(Ev.HighJump);
        double bar = g.Bar;
        Assert.Equal(Sim.StartBar(1), bar);
        Assert.True(g.PilotUntil(g => g.Phase == Phase.Result));
        Assert.True(g.World.Cleared);
        g.Run((int)(GameEngine.ResultSeconds * 60) + 2);
        Assert.Equal(Math.Round(bar + Sim.BarStep, 2), g.Bar);
        Assert.Equal(0, g.Attempt);
        // Now stand there: three tries run out at this height.
        g.Run((int)((GameEngine.ReadySeconds + Sim.TimeLimit + GameEngine.ResultSeconds) * 60 * 3) + 30);
        Assert.Equal(Phase.Tally, g.Phase);
        Assert.Equal(bar, g.Best);
        Assert.Equal(bar, g.Current.Marks[(int)Ev.HighJump]);
    }

    // ───────────────────────────── qualifying, records, points ─────────────────────────────

    [Fact]
    public void Three_attempts_at_a_field_event_and_the_best_counts()
    {
        var g = Engine();
        g.Go(Ev.LongJump);
        var marks = new List<double>();
        var attempts = new List<int>();
        // A foul first (run through), then the computer's jumps.
        for (int t = 0; t < 60 * 200 && g.Phase != Phase.Tally; t++)
        {
            bool foul = g.Attempt == 0;
            g.Tick(foul ? (g.Phase == Phase.Go ? Tap(t, 10) : default) : g.Pilot.Decide(g));
            if (g.Phase == Phase.Result && g.PhaseTime < Field.Step * 0.5) { marks.Add(g.LastMark); attempts.Add(g.Attempt); }
        }
        Assert.Equal([0, 1, 2], attempts);
        Assert.True(double.IsNaN(marks[0]));
        Assert.Equal(marks.Where(m => !double.IsNaN(m)).Max(), g.Best);
        Assert.True(g.Qualified);
        Assert.Equal(Sim.Points(Ev.LongJump, g.Best), g.EventPoints);
    }

    [Fact]
    public void Failing_to_qualify_ends_the_game_then_a_high_score_is_entered()
    {
        var g = Engine();
        g.Go(Ev.Javelin);
        // Standing there: three attempts out of time.
        for (int i = 0; i < 60 * 120 && g.State == GameState.Playing; i++) g.Tick(default);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.True(g.Current.Out);
        Assert.Equal(Ev.Javelin, g.Current.FailedAt);
        g.Current.Score = 9000;
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('T');
        g.Initials.Type('&');
        g.Initials.Type('F');
        g.Initials.Type('X');
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("TFX", g.HighScores.Entries[g.NewEntryIndex].Initials);
        Assert.Equal(9000, g.HighScores.Entries[g.NewEntryIndex].Score);
    }

    [Fact]
    public void Qualifying_goes_on_to_the_next_event_and_after_the_high_jump_round_again_harder()
    {
        var g = Engine();
        g.Go(Ev.Dash);
        Assert.True(g.PilotUntil(g => g.Phase == Phase.Tally));
        Assert.True(g.Qualified);
        g.Run((int)(GameEngine.TallySeconds * 60) + 2);
        Assert.Equal(Ev.LongJump, g.Event);
        Assert.Equal(Phase.Intro, g.Phase);
        g.Go(Ev.HighJump);
        Assert.True(g.PilotUntil(g => g.Phase == Phase.Tally));
        g.Run((int)(GameEngine.TallySeconds * 60) + 2);
        Assert.Equal(Ev.Dash, g.Event);
        Assert.Equal(2, g.Cycle);
        foreach (var e in Enum.GetValues<Ev>())
            Assert.True(Sim.Better(e, Sim.Qualify(e, 2), Sim.Qualify(e, 1)), $"{e} should be stiffer the second time round");
        Assert.True(Sim.RivalTop(Ev.Dash, 2) > Sim.RivalTop(Ev.Dash, 1));
        Assert.True(Sim.Better(Ev.HighJump, Sim.StartBar(2), Sim.StartBar(1)));
    }

    [Fact]
    public void The_computer_athlete_qualifies_through_two_whole_rounds()
    {
        var g = Engine();
        g.StartGame();
        Assert.True(g.PilotUntil(g => g.Cycle == 3, 60 * 20));
        Assert.Equal(12, g.Current.Qualified);
        Assert.False(g.Current.Out);
    }

    [Fact]
    public void A_mark_beating_the_world_record_flashes_scores_a_bonus_and_is_kept()
    {
        string folder = TempFolder();
        var g = Engine(folder);
        Assert.Equal(9.93, g.Records[Ev.Dash]);
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go(Ev.Dash);
        Assert.True(g.PilotUntil(g => g.Phase == Phase.Result));
        Assert.True(g.NewRecord);
        Assert.True(g.JustQualified);
        Assert.Contains(Sfx.Record, heard);
        Assert.Equal(g.LastMark, g.Records[Ev.Dash]);
        Assert.True(g.LastMark < 9.93);
        Assert.Equal((int)Sim.RecordBonus, g.Score);
        Assert.Equal(g.LastMark, Records.Load(folder)[Ev.Dash]);
        Assert.Equal(Records.Starting[(int)Ev.LongJump], Records.Load(folder)[Ev.LongJump]);
    }

    [Fact]
    public void The_demonstration_sets_no_records()
    {
        string folder = TempFolder();
        var g = Engine(folder);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * 30 && g.Phase != Phase.Result; i++) g.Idle(1);
        Assert.Equal(Phase.Result, g.Phase);
        Assert.True(g.LastMark < 9.93);
        Assert.False(g.NewRecord);
        Assert.Equal(9.93, g.Records[Ev.Dash]);
        Assert.False(File.Exists(Path.Combine(folder, "records.txt")));
    }

    [Fact]
    public void Better_marks_score_more_points_and_none_is_none()
    {
        foreach (var e in Enum.GetValues<Ev>())
        {
            double q = Sim.Qualify(e, 1), wr = Records.Starting[(int)e];
            Assert.InRange(Sim.Points(e, q), 800, 1300);
            Assert.InRange(Sim.Points(e, wr), 1800, 2500);
            Assert.True(Sim.Points(e, wr) > Sim.Points(e, q));
            Assert.Equal(0, Sim.Points(e, double.NaN));
        }
        Assert.True(Sim.Qualifies(Ev.Dash, 11.5, 1));
        Assert.False(Sim.Qualifies(Ev.Dash, 11.51, 1));
        Assert.True(Sim.Qualifies(Ev.LongJump, 6.8, 1));
        Assert.False(Sim.Qualifies(Ev.LongJump, 6.79, 1));
        Assert.Equal(10.01, Sim.TimeMark(10.0001));
        Assert.Equal(7.99, Sim.TapeMark(7.9999));
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Two_players_take_each_event_in_turn_and_one_going_out_leaves_the_other()
    {
        var g = Engine();
        g.Go(Ev.Dash, players: 2);
        Assert.Equal(1, g.Current.Number);
        Assert.True(g.PilotUntil(g => g.Phase == Phase.Tally));
        g.Run((int)(GameEngine.TallySeconds * 60) + 2);
        // Player 2 at the dash: he never sets off.
        Assert.Equal(2, g.Current.Number);
        Assert.Equal(Ev.Dash, g.Event);
        for (int i = 0; i < 60 * 60 && g.Phase != Phase.Tally; i++) g.Tick(default);
        Assert.False(g.Qualified);
        Assert.True(g.Players[1].Out);
        g.Run((int)(GameEngine.TallySeconds * 60) + 2);
        Assert.Equal(1, g.Current.Number);
        Assert.Equal(Ev.LongJump, g.Event);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void One_or_two_players_from_the_title()
    {
        var g = Engine();
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Single(g.Players);
        g = Engine();
        g.Tick(default, two: true);
        Assert.Equal(2, g.Players.Count);
        g = Engine();
        g.Idle(40);
        g.Tick(new Controls(Action: true));
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Phase.Intro, g.Phase);
        Assert.Equal(Ev.Dash, g.Event);
    }

    [Fact]
    public void Taps_are_counted_once_whatever_the_frame_rate()
    {
        var g = Engine();
        g.Go(Ev.LongJump);
        // A frame too short for a step: the tap waits for the next one.
        g.Update(0.001, new Controls(A: 1), false);
        Assert.False(g.World.Started);
        g.Update(Field.Step, default, false);
        Assert.True(g.World.Started);
        // A long frame of three steps: still one tap.
        var w = Attempt(Ev.LongJump);
        Sim.Step(w, new Controls(B: 1));
        Run(w, 2);
        g.Go(Ev.LongJump);
        g.Update(Field.Step * 3.5, new Controls(B: 1), false);
        Assert.Equal(w.Me.Rate, g.World.Me.Rate, 9);
    }

    // ───────────────────────────── the keys ─────────────────────────────

    [Fact]
    public void Z_and_X_are_the_run_buttons_and_a_held_key_repeating_is_not_a_tap()
    {
        double now = 0;
        using var input = new InputHandler(() => now);
        input.OnKeyDown(Key.Z);
        now += 0.05;
        input.OnKeyUp(Key.Z);
        input.OnKeyDown(Key.X);
        var c = input.Read();
        Assert.Equal((1, 1), (c.A, c.B));
        Assert.False(c.Action);
        Assert.Equal(default, input.Read());
        // X held: the keyboard repeats it, as more key-downs, or as key-up-key-down pairs at the same instant.
        for (int i = 0; i < 10; i++)
        {
            now += 0.033;
            input.OnKeyDown(Key.X);
            input.OnKeyUp(Key.X);
            input.OnKeyDown(Key.X);
        }
        Assert.Equal(0, input.Read().Taps);
        // A real release and press again is a tap.
        input.OnKeyUp(Key.X);
        now += 0.06;
        input.OnKeyDown(Key.X);
        Assert.Equal(1, input.Read().B);
        // The arrows too.
        input.OnKeyDown(Key.Left);
        input.OnKeyDown(Key.Right);
        c = input.Read();
        Assert.Equal((1, 1), (c.A, c.B));
    }

    [Fact]
    public void Action_is_held_and_a_press_too_quick_to_see_still_counts_once()
    {
        double now = 0;
        using var input = new InputHandler(() => now);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Read().Action);
        Assert.True(input.Read().Action);
        input.OnKeyUp(Key.Space);
        Assert.False(input.Read().Action);
        // Down and up between two reads.
        now += 0.1;
        input.OnKeyDown(Key.Up);
        now += 0.02;
        input.OnKeyUp(Key.Up);
        Assert.True(input.Read().Action);
        Assert.False(input.Read().Action);
        // Ctrl, and a Shift let go as CapsLock is let go.
        input.OnKeyDown(Key.LeftCtrl);
        Assert.True(input.Read().Action);
        input.OnKeyUp(Key.LeftCtrl);
        input.OnKeyDown(Key.LeftShift, PhysicalKey.ShiftLeft);
        Assert.True(input.Read().Action);
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftLeft);
        Assert.False(input.Read().Action);
    }

    [Fact]
    public void Up_on_the_initials_goes_on_through_the_alphabet_and_down_back()
    {
        var entry = new InitialsEntry("AAA");
        entry.Cycle(1);
        Assert.Equal('B', entry.Initials[0]);
        entry.Cycle(-1);
        entry.Cycle(-1);
        Assert.Equal(' ', entry.Initials[0]);
    }

    [Fact]
    public void The_keys_drive_the_game_through_the_input_handler()
    {
        double now = 0;
        using var input = new InputHandler(() => now);
        var g = Engine();
        g.Go(Ev.Hurdles);
        while (!g.World.Gun) g.Update(Field.Step, input.Read(), false);
        // Z and X in turn, ten a second, with the key held a little each time and repeating.
        for (int t = 0; t < 60 * 3; t++)
        {
            now += Field.Step;
            if (t % 12 == 0) input.OnKeyDown(Key.Z);
            if (t % 12 == 6) input.OnKeyDown(Key.X);
            if (t % 12 == 3) { input.OnKeyDown(Key.Z); input.OnKeyUp(Key.Z); }
            if (t % 12 == 9) { input.OnKeyDown(Key.X); input.OnKeyUp(Key.X); }
            if (t == 120) input.OnKeyDown(Key.Space);
            if (t == 122) input.OnKeyUp(Key.Space);
            g.Update(Field.Step, input.Read(), false);
            if (t == 121) Assert.True(g.World.Me.Jumping);
        }
        Assert.InRange(g.World.Me.Rate, 9, 11);
        Assert.True(g.World.Me.Speed > 8.5);
    }
}
