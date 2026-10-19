namespace AVATrackAndField.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The event's card: its name, the player, and the mark to make.</summary>
    Intro,
    /// <summary>A moment before a field event's attempt.</summary>
    Ready,
    /// <summary>The attempt: the player's to make.</summary>
    Go,
    /// <summary>The mark on the scoreboard (or the foul), the athletes slowing down.</summary>
    Result,
    /// <summary>The event over: the best mark, the points, qualified or not.</summary>
    Tally,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Gun, Step, Jump, Throw, Land, Knock, Foul, Swish, Cheer,
    SayMarks, SaySet, SayFoul,
    Fanfare, Qualify, Record, Fail,
    Crowd, CrowdStop, TitleTune, GameOver,
}

/// <summary>A player: his score, his marks this time round, and whether he's still in.</summary>
public sealed class Player(int number)
{
    public int Number { get; } = number;
    public int Score { get; set; }
    public bool Out { get; set; }
    /// <summary>Events qualified in, over the whole game (shown with the high score).</summary>
    public int Qualified { get; set; }
    public double[] Marks { get; } = Enumerable.Repeat(double.NaN, Sim.Events).ToArray();
    public int[] Points { get; } = new int[Sim.Events];
    public Ev? FailedAt { get; set; }
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 75;
    public const double HighScoreSeconds = 9;
    public const double IntroSeconds = 2.6, ReadySeconds = 1.0, ResultSeconds = 2.8, TallySeconds = 4.2, OverSeconds = 5;

    private double _accumulator, _idle;
    private int _pendingA, _pendingB;
    private bool _startPrev, _twoPrev, _tunePending = true, _crowd, _actionPrev;
    private readonly Queue<Player> _entering = [];

    public GameEngine(HighScoreTable? table = null, Records? records = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        Records = records ?? Records.Load(HighScores.Folder);
        NewGame(1);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Intro;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public Records Records { get; }
    public InitialsEntry? Initials { get; private set; }
    /// <summary>Whose initials are being entered.</summary>
    public Player? Entering { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public List<Player> Players { get; private set; } = [];
    public int Turn { get; private set; }
    public Player Current => Players[Turn];
    public int Score => Current.Score;
    public int EventIndex { get; private set; }
    public Ev Event => (Ev)EventIndex;
    /// <summary>How many times round the six events: the marks get stiffer each time.</summary>
    public int Cycle { get; private set; } = 1;
    /// <summary>The attempt at a field event (0 to 2), or at the high jump, the try at this height.</summary>
    public int Attempt { get; private set; }
    public int FalseStarts { get; private set; }
    /// <summary>The best mark at this event so far.</summary>
    public double Best { get; private set; } = double.NaN;
    /// <summary>The mark of the attempt just made.</summary>
    public double LastMark { get; private set; } = double.NaN;
    /// <summary>The high jump: the bar's height now.</summary>
    public double Bar { get; private set; }
    public double QualifyMark => Sim.Qualify(Event, Cycle);
    public bool NewRecord { get; private set; }
    public bool JustQualified { get; private set; }
    /// <summary>At the tally: the points for the event, and whether that was a qualifying mark.</summary>
    public int EventPoints { get; private set; }
    public bool Qualified { get; private set; }
    public double CheerTime { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    /// <summary>Taps are counted once each: they're kept until a step takes them, even if no step runs this frame.</summary>
    public void Update(double dt, Controls c, bool start, bool two = false)
    {
        _pendingA += c.A;
        _pendingB += c.B;
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(new Controls(_pendingA, _pendingB, c.Action), start, two);
            _pendingA = _pendingB = 0;
        }
    }

    public void Tick(Controls c, bool start = false, bool two = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev, twoPressed = two && !_twoPrev, actionPressed = c.Action && !_actionPrev;
        _startPrev = start;
        _twoPrev = two;
        _actionPrev = c.Action;
        if (MessageTime > 0 && (MessageTime -= dt) <= 0) Message = null;
        if (CheerTime > 0) CheerTime -= dt;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (twoPressed) StartGame(2);
                else if (startPressed || actionPressed && StateTime > 0.5) StartGame(1);
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (twoPressed) { StartGame(2); break; }
                if (startPressed) { StartGame(1); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Crowd(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    _entering.Clear();
                    foreach (var p in Players.Where(p => HighScores.Qualifies(p.Score)).OrderByDescending(p => p.Score)) _entering.Enqueue(p);
                    NextEntry(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done && Entering is { } who)
                {
                    int place = HighScores.Insert(done.Initials, who.Score, who.Qualified);
                    HighScores.Save();
                    NextEntry(place);
                }
                break;
            case GameState.HighScores:
                if (twoPressed) StartGame(2);
                else if (startPressed) StartGame(1);
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>The next player's initials, or the table when everyone who made it has entered theirs.</summary>
    private void NextEntry(int place)
    {
        if (_entering.Count == 0)
        {
            Entering = null;
            Initials = null;
            ShowHighScores(place);
            return;
        }
        Entering = _entering.Dequeue();
        State = GameState.EnterInitials;
        StateTime = 0;
        Initials = new InitialsEntry(HighScores.LastInitials);
    }

    private void NewGame(int players)
    {
        Players = Enumerable.Range(1, players).Select(n => new Player(n)).ToList();
        Turn = 0;
        EventIndex = 0;
        Cycle = 1;
        Message = null;
        Pilot.Reset();
        BeginEvent();
    }

    public void StartGame(int players = 1)
    {
        NewGame(players);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Crowd(true);
    }

    private void StartAttract()
    {
        NewGame(1);
        State = GameState.Attract;
        StateTime = 0;
        Crowd(true);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Crowd(false);
        NewGame(1);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Crowd(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        Crowd(true);
    }

    private void Crowd(bool on)
    {
        if (on == _crowd) return;
        _crowd = on;
        Play(on ? Sfx.Crowd : Sfx.CrowdStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Say(string text, double seconds)
    {
        Message = text;
        MessageTime = seconds;
    }

    private bool Live => State == GameState.Playing;

    // ───────────────────────────── the events ─────────────────────────────

    private void BeginEvent()
    {
        Attempt = 0;
        FalseStarts = 0;
        Best = LastMark = double.NaN;
        NewRecord = JustQualified = false;
        Bar = Sim.StartBar(Cycle);
        World = new World();
        Sim.Start(World, Event, Cycle, Bar);
        World.Heard.Clear();
        SetPhase(Phase.Intro);
        Play(Sfx.Fanfare);
    }

    private void BeginAttempt()
    {
        World = new World();
        Sim.Start(World, Event, Cycle, Bar, seed: 1983 + Attempt * 13 + FalseStarts * 31 + Turn * 57);
        Pilot.Reset();
        NewRecord = false;
        LastMark = double.NaN;
        if (Sim.Track(Event)) SetPhase(Phase.Go);
        else
        {
            SetPhase(Phase.Ready);
            World.Heard.Clear();
        }
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Intro:
                if (PhaseTime > IntroSeconds) BeginAttempt();
                break;
            case Phase.Ready:
                if (PhaseTime > ReadySeconds) SetPhase(Phase.Go);
                break;
            case Phase.Go:
                Sim.Step(w, c);
                if (w.Over) Judge();
                break;
            case Phase.Result:
                Sim.Step(w, default);
                if (PhaseTime > ResultSeconds) Next();
                break;
            case Phase.Tally:
                if (PhaseTime > TallySeconds) NextTurn();
                break;
        }
        if (World != w) return;
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    /// <summary>The attempt decided: the mark on the board, a new best, a world record, the qualifying mark made.</summary>
    private void Judge()
    {
        var w = World;
        double mark = w.Result;
        LastMark = mark;
        NewRecord = JustQualified = false;
        SetPhase(Phase.Result);
        if (w.FalseStart)
        {
            FalseStarts++;
            Say(FalseStarts >= 2 ? "FALSE START - OUT" : "FALSE START", ResultSeconds);
            return;
        }
        if (double.IsNaN(mark))
        {
            if (w.Foul) Play(Sfx.SayFoul);
            Say(w.Note == "" ? "NO MARK" : w.Note, ResultSeconds);
            return;
        }
        bool wasQualified = Sim.Qualifies(Event, Best, Cycle);
        if (Sim.Better(Event, mark, Best)) Best = mark;
        if (Live && Records.Set(Event, mark))
        {
            Records.Save();
            NewRecord = true;
            Current.Score += (int)Sim.RecordBonus;
            Play(Sfx.Record);
            CheerTime = ResultSeconds;
        }
        if (!wasQualified && Sim.Qualifies(Event, Best, Cycle))
        {
            JustQualified = true;
            CheerTime = Math.Max(CheerTime, 2);
            if (!NewRecord) Play(Sfx.Qualify);
        }
        Say(Sim.Format(Event, mark), ResultSeconds);
    }

    /// <summary>After the mark: another attempt, or the event's over.</summary>
    private void Next()
    {
        if (World.FalseStart)
        {
            if (FalseStarts < 2) BeginAttempt();
            else EndEvent();
            return;
        }
        switch (Event)
        {
            case Ev.Dash or Ev.Hurdles:
                EndEvent();
                break;
            case Ev.HighJump:
                // Clear it and the bar goes up; three misses at a height and it's over.
                if (World.Cleared)
                {
                    Bar = Math.Round(Bar + Sim.BarStep, 2);
                    Attempt = 0;
                }
                else Attempt++;
                if (Attempt >= Sim.Tries) EndEvent();
                else BeginAttempt();
                break;
            default:
                Attempt++;
                if (Attempt >= Sim.Tries) EndEvent();
                else BeginAttempt();
                break;
        }
    }

    private void EndEvent()
    {
        var p = Current;
        int e = EventIndex;
        EventPoints = Sim.Points(Event, Best);
        p.Score += EventPoints;
        p.Marks[e] = Best;
        p.Points[e] = EventPoints;
        Qualified = Sim.Qualifies(Event, Best, Cycle);
        if (Qualified) p.Qualified++;
        else
        {
            p.Out = true;
            p.FailedAt = Event;
        }
        Play(Qualified ? Sfx.Cheer : Sfx.Fail);
        if (Qualified) CheerTime = 1.5;
        SetPhase(Phase.Tally);
    }

    /// <summary>The next player at this event, or everyone still in on to the next; round again after the high jump.</summary>
    private void NextTurn()
    {
        int next = Turn + 1 < Players.Count ? Players.FindIndex(Turn + 1, p => !p.Out) : -1;
        if (next >= 0)
        {
            Turn = next;
            BeginEvent();
            return;
        }
        int first = Players.FindIndex(p => !p.Out);
        if (first < 0)
        {
            End();
            return;
        }
        Turn = first;
        if (++EventIndex >= Sim.Events)
        {
            EventIndex = 0;
            Cycle++;
            foreach (var p in Players)
            {
                Array.Fill(p.Marks, double.NaN);
                Array.Clear(p.Points);
            }
        }
        BeginEvent();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Crowd(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into an event's first attempt, past its card.</summary>
    public void Go(Ev e = Ev.Dash, int cycle = 1, int players = 1)
    {
        NewGame(players);
        State = GameState.Playing;
        EventIndex = (int)e;
        Cycle = cycle;
        BeginEvent();
        BeginAttempt();
        SetPhase(Phase.Go);
        Message = null;
    }

    /// <summary>Plays on with the computer athlete until the condition holds (or the time runs out).</summary>
    public bool PilotUntil(Func<GameEngine, bool> done, int maxSeconds = 600)
    {
        for (int t = 0; t < 60 * maxSeconds && State == GameState.Playing; t++)
        {
            if (done(this)) return true;
            Tick(Pilot.Decide(this));
        }
        return done(this);
    }
}
