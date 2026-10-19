using System.Diagnostics;
using Avalonia.Input;

namespace AVATrackAndField.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: taps of the two RUN buttons, and the ACTION button.
/// A tap is a key going down that wasn't down already. The keyboard's auto-repeat sends more key-downs while a key is
/// held (and some systems send each repeat as a key-up then a key-down at the same instant): neither is a tap.
/// </summary>
public sealed class InputHandler : IDisposable
{
    /// <summary>A key down this soon after the same key came up is the keyboard repeating, not a finger.</summary>
    public const double RepeatGuard = 0.012;

    private static readonly Key[] RunA = [Key.Z, Key.Left, Key.A, Key.NumPad4];
    private static readonly Key[] RunB = [Key.X, Key.Right, Key.D, Key.NumPad6];
    private static readonly Key[] ActionKeys = [Key.Space, Key.LeftCtrl, Key.RightCtrl, Key.Up, Key.LeftShift, Key.RightShift, Key.W, Key.NumPad8];

    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly Dictionary<Key, double> _upAt = [];
    private readonly ControllerManager _pad = new(0);
    private readonly Func<double> _now;
    private int _tapsA, _tapsB;
    private bool _actionPressed, _padA, _padB, _padAction;

    /// <summary>The clock (seconds) is for the tests; it's the real one otherwise.</summary>
    public InputHandler(Func<double>? clock = null)
    {
        var watch = Stopwatch.StartNew();
        _now = clock ?? (() => watch.Elapsed.TotalSeconds);
    }

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        bool fresh = !_held.Contains(key) && !(_upAt.TryGetValue(key, out double up) && _now() - up < RepeatGuard);
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
        if (!fresh) return;
        if (RunA.Contains(key)) _tapsA++;
        else if (RunB.Contains(key)) _tapsB++;
        else if (ActionKeys.Contains(key)) _actionPressed = true;
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        double now = _now();
        if (_held.Remove(key)) _upAt[key] = now;
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was) && _held.Remove(was)) _upAt[was] = now;
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    /// <summary>Reads the gamepad: A and B going down are taps, like the keys.</summary>
    public void Update()
    {
        _pad.Update();
        if (_pad.ButtonA && !_padA) _tapsA++;
        if (_pad.ButtonB && !_padB) _tapsB++;
        bool action = PadAction;
        if (action && !_padAction) _actionPressed = true;
        _padA = _pad.ButtonA;
        _padB = _pad.ButtonB;
        _padAction = action;
    }

    private bool PadAction => _pad.ButtonX || _pad.ButtonY || _pad.LeftBumper || _pad.RightBumper || _pad.RightTrigger > 0.5f;

    /// <summary>
    /// The controls since the last read: the taps of Z or Left (A) and X or Right (B), and ACTION (Space, Ctrl, Up or
    /// Shift) held. On a gamepad: A and B to run, X, Y, a bumper or the right trigger for ACTION. The taps are taken
    /// by the read; an ACTION press too quick to be held at the read still counts as held, once.
    /// </summary>
    public Controls Read()
    {
        var c = new Controls(_tapsA, _tapsB, _actionPressed || Any(ActionKeys) || PadAction);
        _tapsA = _tapsB = 0;
        _actionPressed = false;
        return c;
    }

    /// <summary>Enter or 1 for one player.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;
    /// <summary>2 for two players.</summary>
    public bool Two => Any(Key.D2, Key.NumPad2);

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
