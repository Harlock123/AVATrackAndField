namespace AVATrackAndField.Engine;

/// <summary>The rules of the six events, one 60th of a second at a time.</summary>
public static class Sim
{
    public const double G = 9.81;
    // Running: taps a second (a running average over Tau) make the speed; FullRate taps a second is flat out.
    public const double Tau = 0.5, FullRate = 10, SameButton = 0.5, Accel = 6.5, Slow = 3, Coast = 4;
    // The angle: it rises while ACTION is held, as far as MaxAngle.
    public const double AngleRate = 75, MaxAngle = 85;
    public const double StartCall = 1.4, RaceLimit = 30, TimeLimit = 15;
    // The track.
    public const double DashLength = 100, HurdlesLength = 110, FirstHurdle = 13.72, HurdleGap = 9.14;
    public const double HurdleTime = 0.52, HurdlePeak = 1.3, HurdleClear = 0.9, HurdleHeight = 1.067, KnockSlow = 0.55;
    // The long jump: the board, and the take-off speed from the run-up.
    public const double JumpLine = 40, JumpFactor = 0.815, JumpHeight = 0.5;
    // The javelin: the line, the release speed from the run-up, and how hard the thrower brakes once he throws.
    public const double JavelinLine = 30, JavelinFactor = 2.75, JavelinHeight = 1.9, ThrowBrake = 16;
    // The hammer: the circle, its turns, the speed of release at the fastest turn, the sector.
    public const double HammerX = 6, Circle = 1.07, SpinTop = 1.6 * 2 * Math.PI, SpinUp = 5, SpinDown = 3;
    public const double HammerTop = 29, HammerHeight = 1.5, Sector = 0.7;
    // The high jump: the bar, the take-off mark, the jump from the run-up, and the kick over the bar.
    public const double BarX = 17, TakeoffNear = 1.0, HighBase = 2.2, HighFactor = 0.43, Com = 1.1, KickMargin = 0.12, KickBefore = 0.7, KickAfter = 0.35, BarStep = 0.05;
    public const double RecordBonus = 1000;
    public const int Tries = 3, Events = 6;

    public static readonly string[] Names = ["100 METRE DASH", "LONG JUMP", "JAVELIN THROW", "110 METRE HURDLES", "HAMMER THROW", "HIGH JUMP"];

    public static bool Track(Ev e) => e is Ev.Dash or Ev.Hurdles;
    public static bool Timed(Ev e) => Track(e);
    public static double Length(Ev e) => e == Ev.Hurdles ? HurdlesLength : DashLength;
    public static double HurdleAt(int i) => FirstHurdle + i * HurdleGap;

    /// <summary>The top speed, flat out: hurdlers are a little slower, and a high jumper runs in under control.</summary>
    public static double Top(Ev e) => e switch { Ev.Hurdles => 9.4, Ev.HighJump => 7.5, _ => 11.4 };

    /// <summary>The mark to make to go on to the next event: stiffer each time round.</summary>
    public static double Qualify(Ev e, int cycle)
    {
        int k = Math.Max(0, cycle - 1);
        return e switch
        {
            Ev.Dash => Math.Max(10.0, 11.5 - 0.4 * k),
            Ev.LongJump => Math.Min(8.6, 6.8 + 0.35 * k),
            Ev.Javelin => Math.Min(95, 64 + 6 * k),
            Ev.Hurdles => Math.Max(13.2, 16.0 - 0.6 * k),
            Ev.Hammer => Math.Min(82, 56 + 6 * k),
            _ => Math.Round(Math.Min(2.35, 1.9 + 0.05 * k), 2),
        };
    }

    /// <summary>The bar's first height: a little under the qualifying height.</summary>
    public static double StartBar(int cycle) => Math.Round(Qualify(Ev.HighJump, cycle) - 0.05, 2);

    /// <summary>The rival's top speed: quicker each time round.</summary>
    public static double RivalTop(Ev e, int cycle) => Math.Min(Top(e) * 0.97, Top(e) * 0.78 + 0.45 * (cycle - 1));

    /// <summary>Is mark a better than mark b? A lower time, a longer distance, a greater height.</summary>
    public static bool Better(Ev e, double a, double b) =>
        !double.IsNaN(a) && (double.IsNaN(b) || (Timed(e) ? a < b : a > b));

    public static bool Qualifies(Ev e, double mark, int cycle) =>
        !double.IsNaN(mark) && (Timed(e) ? mark <= Qualify(e, cycle) + 1e-9 : mark >= Qualify(e, cycle) - 1e-9);

    /// <summary>The points for a mark, on a table like the decathlon's: about 1,000 for qualifying the first time round, 2,000 for a world-class mark.</summary>
    public static int Points(Ev e, double mark)
    {
        if (double.IsNaN(mark)) return 0;
        double p = e switch
        {
            Ev.Dash => (14 - mark) * 500,
            Ev.LongJump => (mark - 4) * 400,
            Ev.Javelin => (mark - 30) * 30,
            Ev.Hurdles => (18 - mark) * 400,
            Ev.Hammer => (mark - 30) * 35,
            _ => (mark - 1.4) * 2000,
        };
        return (int)Math.Max(0, Math.Round(p));
    }

    /// <summary>A time, as the clocks give it: up to the next hundredth.</summary>
    public static double TimeMark(double t) => Math.Ceiling(t * 100 - 1e-7) / 100;

    /// <summary>A distance, as the tape gives it: down to the last whole centimetre.</summary>
    public static double TapeMark(double d) => Math.Floor(d * 100 + 1e-7) / 100;

    public static string Format(Ev e, double mark) =>
        double.IsNaN(mark) ? "--" : Timed(e) ? $"{mark:0.00} SEC" : $"{mark:0.00} M";

    // ───────────────────────────── an attempt ─────────────────────────────

    public static void Start(World w, Ev e, int cycle = 1, double bar = 0, int seed = 1983)
    {
        w.Event = e;
        w.Cycle = cycle;
        w.Dice = new Dice(seed + cycle * 7 + (int)e * 101);
        w.Me = new Athlete();
        w.Rival = new Athlete();
        w.Racing = Track(e);
        w.Bar = bar > 0 ? bar : StartBar(cycle);
        // Holding ACTION from before counts for nothing: it has to come up and go down again.
        w.ActionPrev = true;
        switch (e)
        {
            case Ev.Dash or Ev.Hurdles:
                w.Me.Become(Pose.Set);
                w.Rival.Become(Pose.Set);
                w.RivalTop = RivalTop(e, cycle) * (0.98 + 0.04 * w.Dice.Next());
                w.Hold = 0.9 + 0.8 * w.Dice.Next();
                w.Clock = -(StartCall + w.Hold);
                w.Heard.Add(Sfx.SayMarks);
                break;
            case Ev.Hammer:
                w.Me.X = HammerX;
                break;
        }
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        if (w.Over)
        {
            w.Since += dt;
            c = default;
        }
        bool press = c.Action && !w.ActionPrev, release = !c.Action && w.ActionPrev;
        w.ActionPrev = c.Action;
        w.Me.T += dt;
        w.Rival.T += dt;
        switch (w.Event)
        {
            case Ev.Dash or Ev.Hurdles: Race(w, c, press); break;
            case Ev.LongJump: LongJump(w, c, press, release); break;
            case Ev.Javelin: Javelin(w, c, press, release); break;
            case Ev.Hammer: Hammer(w, c, press, release); break;
            case Ev.HighJump: HighJump(w, c, press, release); break;
        }
        if (w.Thing.Flying) Fly(w);
    }

    /// <summary>The power: how near flat-out the tapping is, 0 to 1.</summary>
    public static double Power(Athlete a) => Math.Clamp(a.Rate / FullRate, 0, 1);

    /// <summary>The tapping rate dies away, and each tap lifts it; the same button twice running is half a tap.</summary>
    private static void Taps(Athlete a, Controls c)
    {
        a.Rate *= Math.Exp(-Field.Step / Tau);
        for (int i = 0; i < c.A; i++) Tap(a, 1);
        for (int i = 0; i < c.B; i++) Tap(a, 2);
    }

    private static void Tap(Athlete a, int button)
    {
        a.Rate += (a.Last == button ? SameButton : 1) / Tau;
        a.Last = button;
    }

    /// <summary>Speed towards what the tapping is worth, quick to rise and slower to fall.</summary>
    private static void Drive(Athlete a, double top)
    {
        double target = top * Power(a);
        a.Speed = a.Speed < target ? Math.Min(target, a.Speed + Accel * Field.Step) : Math.Max(target, a.Speed - Slow * Field.Step);
    }

    private static void Brake(Athlete a, double rate) => a.Speed = Math.Max(0, a.Speed - rate * Field.Step);

    /// <summary>Along the ground, the legs keeping time: one stride for each two steps, longer the faster he goes.</summary>
    private static void Move(World w, Athlete a, bool me)
    {
        a.X += a.Speed * Field.Step;
        if (a.Speed <= 0) return;
        double before = a.Stride;
        a.Stride += a.Speed * Field.Step / (2 * (0.9 + 0.12 * a.Speed)) * 2 * Math.PI;
        if (me && !a.Jumping && Math.Floor(a.Stride / Math.PI) != Math.Floor(before / Math.PI)) w.Heard.Add(Sfx.Step);
    }

    private static void Decide(World w, double result, string note = "")
    {
        if (w.Over) return;
        w.Over = true;
        w.Result = result;
        w.Note = note;
    }

    private static void Foul(World w, string note = "FOUL")
    {
        if (w.Over) return;
        w.Foul = true;
        Decide(w, double.NaN, note);
        w.Heard.Add(Sfx.Foul);
    }

    // ───────────────────────────── the dash and the hurdles ─────────────────────────────

    private static void Race(World w, Controls c, bool press)
    {
        var me = w.Me;
        var rival = w.Rival;
        if (!w.Gun)
        {
            if (c.Taps > 0 && !w.Over)
            {
                // Away before the gun.
                w.FalseStart = true;
                Foul(w, "FALSE START");
                me.Become(Pose.Stand);
                return;
            }
            if (w.Over) return;
            if (!w.SetCalled && w.Clock >= -w.Hold)
            {
                w.SetCalled = true;
                w.Heard.Add(Sfx.SaySet);
            }
            if (w.Clock >= 0)
            {
                w.Gun = true;
                w.Clock = 0;
                w.Heard.Add(Sfx.Gun);
                me.Become(Pose.Run);
                rival.Become(Pose.Run);
            }
            return;
        }
        double length = Length(w.Event);
        bool hurdles = w.Event == Ev.Hurdles;
        // The player.
        Taps(me, c);
        bool done = !double.IsNaN(me.Finish);
        if (done || me.Pose == Pose.Stumble && me.T < 0.25) Brake(me, done ? Coast : 1);
        else if (!me.Jumping) Drive(me, Top(w.Event));
        if (me.Pose == Pose.Stumble && me.T >= 0.25) me.Become(Pose.Run);
        if (hurdles && press && !me.Jumping && !done)
        {
            me.Jumping = true;
            me.JumpT = 0;
            w.Heard.Add(Sfx.Jump);
        }
        double x0 = me.X;
        Hop(me);
        Move(w, me, true);
        if (hurdles)
            for (int i = 0; i < w.Knocked.Length; i++)
            {
                double hx = HurdleAt(i);
                if (w.Knocked[i] || x0 >= hx || me.X < hx || me.Y >= HurdleClear) continue;
                // Through the hurdle, not over it: it goes down, and so does the speed.
                w.Knocked[i] = true;
                me.Speed *= KnockSlow;
                me.Become(Pose.Stumble);
                w.Heard.Add(Sfx.Knock);
            }
        if (!done && me.X >= length)
        {
            me.Finish = w.Clock - (me.X - length) / Math.Max(0.1, me.Speed);
            Decide(w, TimeMark(me.Finish));
            w.Heard.Add(Sfx.Cheer);
        }
        if (!done && w.Clock > RaceLimit) Decide(w, double.NaN, "DID NOT FINISH");
        // The rival: steady, and clean over every hurdle.
        if (double.IsNaN(rival.Finish))
        {
            double target = w.RivalTop;
            rival.Speed = Math.Min(target, rival.Speed + Accel * 0.9 * Field.Step);
        }
        else Brake(rival, Coast);
        if (hurdles && !rival.Jumping && double.IsNaN(rival.Finish))
            for (int i = 0; i < w.Knocked.Length; i++)
            {
                double d = HurdleAt(i) - rival.X;
                if (d > 0 && d <= rival.Speed * HurdleTime / 2) { rival.Jumping = true; rival.JumpT = 0; }
            }
        Hop(rival);
        Move(w, rival, false);
        if (double.IsNaN(rival.Finish) && rival.X >= length) rival.Finish = w.Clock - (rival.X - length) / Math.Max(0.1, rival.Speed);
    }

    /// <summary>Over a hurdle: up and down in a fixed time, at the speed he took off at.</summary>
    private static void Hop(Athlete a)
    {
        if (!a.Jumping) return;
        a.JumpT += Field.Step;
        double k = a.JumpT / HurdleTime;
        if (k >= 1)
        {
            a.Jumping = false;
            a.Y = 0;
            return;
        }
        a.Y = HurdlePeak * 4 * k * (1 - k);
    }

    // ───────────────────────────── the long jump ─────────────────────────────

    private static void RunUp(World w, Controls c, double top)
    {
        var me = w.Me;
        Taps(me, c);
        if (c.Taps > 0 && !w.Started)
        {
            w.Started = true;
            me.Become(Pose.Run);
        }
        Drive(me, top);
        Move(w, me, true);
        if (!w.Started && w.Clock > TimeLimit) Foul(w, "TIME");
    }

    /// <summary>The angle rising while ACTION is held; true when it's let go (or at its highest).</summary>
    private static bool Gather(World w, bool release)
    {
        w.Angle = Math.Min(MaxAngle, w.Angle + AngleRate * Field.Step);
        return release || w.Angle >= MaxAngle;
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180;

    private static void LongJump(World w, Controls c, bool press, bool release)
    {
        var me = w.Me;
        switch (me.Pose)
        {
            case Pose.Stand or Pose.Run:
                if (press && w.Started)
                {
                    // Off the board: he plants, and gathers for the jump while the angle rises.
                    w.TakeoffX = me.X;
                    w.LaunchSpeed = me.Speed * JumpFactor;
                    w.Holding = true;
                    w.Angle = 0;
                    me.Become(Pose.Plant);
                    return;
                }
                RunUp(w, c, Top(w.Event));
                if (me.X > JumpLine)
                {
                    // Over the board without jumping.
                    Foul(w);
                    me.Become(Pose.Stumble);
                }
                break;
            case Pose.Plant:
                if (!Gather(w, release)) return;
                w.Holding = false;
                w.Launched = true;
                me.Vx = w.LaunchSpeed * Math.Cos(Rad(w.Angle));
                me.Vy = w.LaunchSpeed * Math.Sin(Rad(w.Angle));
                me.Y = JumpHeight;
                me.Become(Pose.Air);
                w.Heard.Add(Sfx.Jump);
                break;
            case Pose.Air:
            {
                double x0 = me.X, y0 = me.Y;
                Ballistic(ref me.X, ref me.Y, ref me.Vx, ref me.Vy);
                if (me.Y > 0) return;
                double land = x0 + (me.X - x0) * y0 / (y0 - me.Y);
                me.X = land;
                me.Y = 0;
                me.Speed = 0;
                me.Become(Pose.Sit);
                w.Heard.Add(Sfx.Land);
                if (land < JumpLine) Decide(w, double.NaN, "SHORT");
                else Decide(w, TapeMark(land - JumpLine));
                break;
            }
            default:
                Brake(me, 9);
                Move(w, me, false);
                break;
        }
    }

    private static void Ballistic(ref double x, ref double y, ref double vx, ref double vy)
    {
        double dt = Field.Step;
        x += vx * dt;
        y += vy * dt - G * dt * dt / 2;
        vy -= G * dt;
    }

    // ───────────────────────────── the javelin ─────────────────────────────

    private static void Javelin(World w, Controls c, bool press, bool release)
    {
        var me = w.Me;
        switch (me.Pose)
        {
            case Pose.Stand or Pose.Run:
                if (press && w.Started)
                {
                    // The javelin drawn back: he brakes hard, the angle rising, and throws as ACTION comes up.
                    w.LaunchSpeed = me.Speed * JavelinFactor;
                    w.Holding = true;
                    w.Angle = 0;
                    me.Become(Pose.Draw);
                    return;
                }
                RunUp(w, c, Top(w.Event));
                break;
            case Pose.Draw:
                Brake(me, ThrowBrake);
                Move(w, me, false);
                if (Gather(w, release))
                {
                    w.Holding = false;
                    w.Launched = true;
                    var t = w.Thing;
                    t.X = me.X + 0.3;
                    t.Y = JavelinHeight;
                    t.Vx = w.LaunchSpeed * Math.Cos(Rad(w.Angle));
                    t.Vy = w.LaunchSpeed * Math.Sin(Rad(w.Angle));
                    t.Flying = true;
                    me.Become(Pose.Follow);
                    w.Heard.Add(Sfx.Throw);
                }
                break;
            default:
                Brake(me, ThrowBrake);
                Move(w, me, false);
                break;
        }
        // Over the line at any point, before the throw or after it, is a foul.
        if (me.X > JavelinLine && !w.Foul)
        {
            Foul(w);
            if (!w.Launched) me.Become(Pose.Stumble);
        }
    }

    /// <summary>A javelin or hammer through the air, and where it comes down.</summary>
    private static void Fly(World w)
    {
        var t = w.Thing;
        double x0 = t.X, y0 = t.Y;
        Ballistic(ref t.X, ref t.Y, ref t.Vx, ref t.Vy);
        if (t.Y > 0) return;
        t.X = x0 + (t.X - x0) * y0 / (y0 - t.Y);
        t.Y = 0;
        t.Flying = false;
        t.Landed = true;
        w.Heard.Add(Sfx.Land);
        if (w.Foul) return;
        double from = w.Event == Ev.Hammer ? HammerX + Circle : JavelinLine;
        Decide(w, TapeMark(t.X - from));
    }

    // ───────────────────────────── the hammer ─────────────────────────────

    /// <summary>An angle brought into -π to π.</summary>
    public static double Wrap(double a) => a - 2 * Math.PI * Math.Floor((a + Math.PI) / (2 * Math.PI));

    private static void Hammer(World w, Controls c, bool press, bool release)
    {
        var me = w.Me;
        if (me.Pose is Pose.Stand && !w.Launched)
        {
            if (c.Taps == 0)
            {
                if (w.Clock > TimeLimit) Foul(w, "TIME");
                return;
            }
            w.Started = true;
            me.Become(Pose.Spin);
        }
        if (me.Pose == Pose.Spin)
        {
            Taps(me, c);
            double target = SpinTop * Power(me);
            w.Omega = w.Omega < target ? Math.Min(target, w.Omega + SpinUp * Field.Step) : Math.Max(target, w.Omega - SpinDown * Field.Step);
            double before = w.Spin;
            w.Spin += w.Omega * Field.Step;
            if (Math.Floor(w.Spin / (2 * Math.PI)) != Math.Floor(before / (2 * Math.PI))) w.Heard.Add(Sfx.Swish);
            if (press && !w.Holding)
            {
                w.Holding = true;
                w.Angle = 0;
            }
            else if (w.Holding && Gather(w, release))
            {
                // Let go: it flies off on the line it was turning along.
                w.Holding = false;
                w.Launched = true;
                w.Release = Wrap(w.Spin);
                double off = w.Release / Sector;
                var t = w.Thing;
                t.X = HammerX + 0.6 * Math.Sin(w.Spin);
                t.Y = HammerHeight;
                t.Flying = true;
                me.Become(Pose.Follow);
                w.Heard.Add(Sfx.Throw);
                if (Math.Abs(off) > 1)
                {
                    // Out of the sector: into the cage.
                    t.Vx = 4 * Math.Cos(w.Release);
                    t.Vy = 2;
                    Foul(w);
                    return;
                }
                double s = HammerTop * w.Omega / SpinTop * (1 - 0.3 * off * off);
                t.Vx = s * Math.Cos(Rad(w.Angle));
                t.Vy = s * Math.Sin(Rad(w.Angle));
                return;
            }
            if (w.Clock > TimeLimit) Foul(w, "TIME");
        }
    }

    // ───────────────────────────── the high jump ─────────────────────────────

    private static void HighJump(World w, Controls c, bool press, bool release)
    {
        var me = w.Me;
        switch (me.Pose)
        {
            case Pose.Stand or Pose.Run:
                if (press && w.Started)
                {
                    if (BarX - me.X < TakeoffNear)
                    {
                        // Too close to get up: into the bar.
                        Miss(w, "TOO CLOSE");
                        return;
                    }
                    w.TakeoffX = me.X;
                    w.LaunchSpeed = HighBase + me.Speed * HighFactor;
                    w.Holding = true;
                    w.Angle = 0;
                    me.Become(Pose.Plant);
                    return;
                }
                RunUp(w, c, Top(w.Event));
                if (BarX - me.X < TakeoffNear - 0.4) Miss(w, "NO JUMP");
                break;
            case Pose.Plant:
                if (!Gather(w, release)) return;
                w.Holding = false;
                w.Launched = true;
                me.Vx = w.LaunchSpeed * Math.Cos(Rad(w.Angle));
                me.Vy = w.LaunchSpeed * Math.Sin(Rad(w.Angle));
                me.Y = Com;
                me.Become(Pose.Flop);
                w.Heard.Add(Sfx.Jump);
                break;
            case Pose.Flop:
            {
                if (press && !w.KickTried)
                {
                    // The kick: the legs flicked up over the bar. Only the first press counts.
                    w.KickTried = true;
                    w.Kicked = me.X >= BarX - KickBefore && me.X <= BarX + KickAfter;
                    if (w.Kicked) w.Heard.Add(Sfx.Swish);
                }
                double x0 = me.X, y0 = me.Y;
                Ballistic(ref me.X, ref me.Y, ref me.Vx, ref me.Vy);
                if (!w.Crossed && x0 < BarX && me.X >= BarX)
                {
                    w.Crossed = true;
                    w.Margin = y0 + (me.Y - y0) * (BarX - x0) / (me.X - x0) - w.Bar;
                }
                bool landed = me.Y <= 0.5 && me.Vy < 0;
                if (w.Crossed && !w.Cleared && !w.BarDown && (me.X >= BarX + KickAfter || landed))
                {
                    if (w.Margin >= (w.Kicked ? 0 : KickMargin)) w.Cleared = true;
                    else { w.BarDown = true; w.Heard.Add(Sfx.Knock); }
                }
                if (!landed) return;
                me.Y = 0.5;
                me.Become(Pose.Sit);
                w.Heard.Add(Sfx.Land);
                if (w.Cleared)
                {
                    Decide(w, w.Bar);
                    w.Heard.Add(Sfx.Cheer);
                }
                else Decide(w, double.NaN, w.Crossed ? "MISS" : "SHORT");
                break;
            }
            case Pose.Sit:
                me.Y = Math.Max(0.35, me.Y - Field.Step);
                break;
            default:
                Brake(me, 9);
                Move(w, me, false);
                break;
        }
    }

    private static void Miss(World w, string note)
    {
        w.BarDown = true;
        w.Me.Become(Pose.Stumble);
        w.Heard.Add(Sfx.Knock);
        Decide(w, double.NaN, note);
    }
}
