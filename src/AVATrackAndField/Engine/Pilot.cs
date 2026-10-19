namespace AVATrackAndField.Engine;

/// <summary>
/// The computer athlete, for the demonstration and the tests. It hammers the two RUN buttons in turn at a steady
/// rate, waits for the gun, and times the ACTION button the way a good player learns to: over each hurdle with the
/// hurdle in the middle of the jump, off the board at the last stride before the line, the javelin away with room
/// to stop, the hammer let go as it swings down the field, the high jump from just outside the take-off mark, and
/// the kick as the body goes over. While ACTION is held it lets go at the best angle, found by trying the moment on
/// a copy of the world: let go now, or a sixtieth of a second later.
/// </summary>
public sealed class Pilot
{
    /// <summary>Taps a second, alternating A and B: a little over flat out.</summary>
    public double Rate { get; set; } = 10.6;
    /// <summary>How long after the gun before the first tap.</summary>
    public double Reaction { get; set; } = 0.14;

    private double _beat;
    private int _release = -1;
    private bool _b, _kick;
    private Controls _taps;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _beat = 0.99;
        _b = false;
        _release = -1;
        _kick = false;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Go ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Over) return default;
        var me = w.Me;
        bool tapping = w.Event switch
        {
            Ev.Dash or Ev.Hurdles => w.Gun && w.Clock >= Reaction,
            Ev.Hammer => !w.Launched,
            _ => me.Pose is Pose.Stand or Pose.Run,
        };
        var taps = _taps = tapping ? Tap(ref _beat, ref _b) : default;
        bool action = w.Event switch
        {
            Ev.Hurdles => Hurdle(w),
            Ev.LongJump => Jump(w, Sim.JumpLine - 0.03),
            Ev.Javelin => Throw(w),
            Ev.Hammer => Hammer(w),
            Ev.HighJump => High(w),
            _ => false,
        };
        if (w.Event == Ev.Dash) Doing = w.Gun ? "running" : "in the blocks";
        return taps with { Action = action };
    }

    /// <summary>The next tap, if one's due: A and B in turn, Rate a second.</summary>
    private Controls Tap(ref double beat, ref bool b)
    {
        beat += Rate * Field.Step;
        if (beat < 1) return default;
        beat -= 1;
        b = !b;
        return b ? new Controls(A: 1) : new Controls(B: 1);
    }

    /// <summary>Up when the next hurdle is half a jump away, so it's under the top of the jump.</summary>
    private bool Hurdle(World w)
    {
        var me = w.Me;
        Doing = "hurdling";
        if (me.Jumping || !w.Gun) return false;
        for (int i = 0; i < w.Knocked.Length; i++)
        {
            double d = Sim.HurdleAt(i) - me.X;
            if (d > 0 && d <= me.Speed * Sim.HurdleTime / 2 + me.Speed * Field.Step / 2) return true;
        }
        return false;
    }

    /// <summary>Off the board at the last stride before the line; then held until the angle is right.</summary>
    private bool Jump(World w, double line)
    {
        var me = w.Me;
        if (w.Holding) return Hold(w);
        if (me.Pose is not (Pose.Stand or Pose.Run) || !w.Started) return false;
        Doing = "running up";
        return me.X + me.Speed * Field.Step > line;
    }

    /// <summary>Drawn back with just room to stop before the line.</summary>
    private bool Throw(World w)
    {
        var me = w.Me;
        if (w.Holding) return Hold(w);
        if (me.Pose is not (Pose.Stand or Pose.Run) || !w.Started) return false;
        Doing = "running up";
        double stop = me.Speed * me.Speed / (2 * Sim.ThrowBrake) + me.Speed * Field.Step;
        return me.X + stop > Sim.JavelinLine - 0.1;
    }

    /// <summary>Flat out, then ACTION down so that the angle is right just as the hammer swings down the field.</summary>
    private bool Hammer(World w)
    {
        if (w.Launched) return false;
        if (w.Holding) return Hold(w);
        Doing = "turning";
        // Flat out, or as fast as it's going to get.
        if (w.Omega < Sim.SpinTop * 0.97 && w.Clock < 4) return false;
        // The angle wanted, and how long holding takes to reach it.
        double hold = 44.5 / Sim.AngleRate;
        double toLine = ((2 * Math.PI - w.Spin % (2 * Math.PI)) % (2 * Math.PI)) / w.Omega;
        double period = 2 * Math.PI / w.Omega;
        double wait = ((toLine - hold) % period + period) % period;
        return wait < Field.Step;
    }

    /// <summary>The high jump: up from just outside the take-off mark, held for the angle, then the kick over the bar.</summary>
    private bool High(World w)
    {
        var me = w.Me;
        if (w.Holding) return Hold(w);
        if (me.Pose == Pose.Flop)
        {
            Doing = "over the bar";
            if (_kick || me.X < Sim.BarX - Sim.KickBefore * 0.5) return false;
            _kick = true;
            return true;
        }
        if (me.Pose is not (Pose.Stand or Pose.Run) || !w.Started) return false;
        Doing = "running up";
        return Sim.BarX - (me.X + me.Speed * Field.Step) < Sim.TakeoffNear + 0.05;
    }

    /// <summary>
    /// Holding ACTION while the angle rises. At the first step it tries letting go at every step to come, each on a
    /// copy of the world, and keeps to the best.
    /// </summary>
    private bool Hold(World w)
    {
        Doing = "setting the angle";
        if (_release < 0)
        {
            double best = double.NegativeInfinity;
            int steps = (int)Math.Ceiling((Sim.MaxAngle - w.Angle) / (Sim.AngleRate * Field.Step));
            for (int k = 0; k <= steps; k++)
            {
                double v = Outcome(w, k);
                if (v > best) { best = v; _release = k; }
            }
        }
        return _release-- > 0;
    }

    /// <summary>How an attempt comes out if ACTION is let go after so many more steps (on a copy of the world).</summary>
    private double Outcome(World w, int more)
    {
        var c = w.Copy();
        // Tapping on as the real one will, so the hammer turns as fast in the copy.
        double beat = _beat;
        bool b = _b;
        for (int i = 0; i < 60 * 8 && !(c.Over && !c.Thing.Flying); i++)
        {
            var taps = i == 0 ? _taps : c.Launched ? default : Tap(ref beat, ref b);
            Sim.Step(c, taps with { Action = i < more });
            // The high jump's copy is judged by how far over the bar the body goes, so the kick doesn't matter.
            if (c.Event == Ev.HighJump && c.Crossed) return c.Margin;
        }
        if (c.Event == Ev.HighJump) return c.Crossed ? c.Margin : -10;
        return double.IsNaN(c.Result) ? -1000 : c.Result;
    }
}
