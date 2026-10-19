namespace AVATrackAndField.Engine;

/// <summary>The screen, and the stadium's scale: how many pixels to the metre, and where the two lanes run.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>Pixels to the metre, along the track and up.</summary>
    public const double Scale = 58;
    /// <summary>Where the runners' feet are: the near lane (the player) and the far lane (the rival).</summary>
    public const double Near = 618, Far = 556;
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>
/// The controls for one 60th of a second: how many times each RUN button (A and B) went down, and whether the
/// ACTION button is held. Taps are presses, counted once each: holding a RUN button does nothing.
/// </summary>
public readonly record struct Controls(int A = 0, int B = 0, bool Action = false)
{
    public int Taps => A + B;
}

/// <summary>The six events, in the order they're held.</summary>
public enum Ev { Dash, LongJump, Javelin, Hurdles, Hammer, HighJump }

/// <summary>What an athlete is doing, for the rules and for the drawing.</summary>
public enum Pose
{
    /// <summary>Crouched in the blocks (track events, before the gun).</summary>
    Set,
    Stand,
    Run,
    /// <summary>Planted on the board, gathering for the jump while the angle rises.</summary>
    Plant,
    /// <summary>In the air: the long jump's sail, or a hurdle.</summary>
    Air,
    /// <summary>Landed: sitting in the sand, or on his back on the high-jump mat.</summary>
    Sit,
    Stumble,
    /// <summary>The javelin drawn back while the angle rises.</summary>
    Draw,
    /// <summary>After a throw: the arm through and the body following.</summary>
    Follow,
    /// <summary>Turning in the hammer circle.</summary>
    Spin,
    /// <summary>The high jump, back arched over the bar.</summary>
    Flop,
    Cheer,
}

/// <summary>An athlete: the player in the near lane, or the rival in the far one.</summary>
public sealed class Athlete
{
    /// <summary>Along the track (metres), and up (metres).</summary>
    public double X, Y, Vx, Vy;
    public double Speed;
    /// <summary>The tapping rate, in taps a second: a running average, so the meter rises and falls smoothly.</summary>
    public double Rate;
    /// <summary>The stride cycle, in radians: one turn is two steps.</summary>
    public double Stride;
    /// <summary>How long in this pose.</summary>
    public double T;
    public Pose Pose = Pose.Stand;
    /// <summary>The RUN button tapped last (1 = A, 2 = B): tapping one button over and over is half as good.</summary>
    public int Last;
    /// <summary>Over a hurdle: how long since take-off.</summary>
    public bool Jumping;
    public double JumpT;
    /// <summary>A track race's finish time, once over the line.</summary>
    public double Finish = double.NaN;

    public Athlete Copy() => (Athlete)MemberwiseClone();

    public void Become(Pose p)
    {
        Pose = p;
        T = 0;
    }
}

/// <summary>A javelin or a hammer, in flight.</summary>
public sealed class Implement
{
    public double X, Y, Vx, Vy;
    public bool Flying, Landed;
    public Implement Copy() => (Implement)MemberwiseClone();
}

/// <summary>
/// One attempt at an event, kept together so it can be copied whole: the athlete and the rival, the javelin or
/// hammer, the hurdles, the bar, and how it came out.
/// </summary>
public sealed class World
{
    public Ev Event;
    public int Cycle = 1;
    public Athlete Me = new(), Rival = new();
    /// <summary>The track events have a rival in the far lane.</summary>
    public bool Racing;
    public double RivalTop;
    /// <summary>Seconds: before the gun this counts up from below zero; the gun fires at zero.</summary>
    public double Clock;
    /// <summary>How long the starter holds them at SET before the gun.</summary>
    public double Hold;
    public bool Gun, SetCalled, FalseStart;
    /// <summary>A field event: the athlete has set off.</summary>
    public bool Started;
    public bool ActionPrev;
    /// <summary>The ACTION button held: the angle rising.</summary>
    public bool Holding, Launched;
    public double Angle;
    public double TakeoffX = double.NaN, LaunchSpeed;
    public Implement Thing = new();
    /// <summary>The hammer: round and round (radians), and how fast (radians a second).</summary>
    public double Spin, Omega;
    /// <summary>The hammer: how far off straight down the field it went (radians).</summary>
    public double Release;
    public bool[] Knocked = new bool[10];
    /// <summary>The high jump: the bar's height, and how it went.</summary>
    public double Bar;
    public bool BarDown, Kicked, KickTried, Crossed, Cleared;
    /// <summary>The high jump: how far the body went over (or under) the bar.</summary>
    public double Margin = double.NaN;
    /// <summary>The attempt is decided (the world still runs on for the athletes to slow down).</summary>
    public bool Over, Foul;
    /// <summary>The mark: a time in seconds, or a distance or height in metres; NaN for none.</summary>
    public double Result = double.NaN;
    public string Note = "";
    public double Since;
    public Dice Dice = new(1);
    public List<Sfx> Heard = [];

    public World Copy()
    {
        var w = (World)MemberwiseClone();
        w.Me = Me.Copy();
        w.Rival = Rival.Copy();
        w.Thing = Thing.Copy();
        w.Knocked = (bool[])Knocked.Clone();
        w.Heard = [];
        return w;
    }
}
