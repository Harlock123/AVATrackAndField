# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

An athletics meeting in the style of Konami's 1983 arcade game Track & Field, in C# on .NET 10 and Avalonia 12:
six events, each with a qualifying mark. One self-contained executable per platform (Windows, macOS and Linux,
x64 and ARM). The graphics are drawn in code. The sounds, crowd and music are synthesised by
`tools/make_sounds.py`, and the starter's calls by the formant voice in `tools/speech.py`.

## Commands

```bash
dotnet build AVATrackAndField.slnx                # the SDK version is in global.json (10.0.400 or later)
dotnet test AVATrackAndField.slnx                 # every test runs headless: no window opens
dotnet run --project src/AVATrackAndField         # play it; --no-audio for silence
TRACK_SHOTS=$PWD/Images dotnet test tests/AVATrackAndField.Tests --filter ScreenshotTests   # refresh the README's pictures
TRACK_PROBE=1 dotnet test tests/AVATrackAndField.Tests --filter PilotProbe   # each event at several tapping rates, for tuning
python3 tools/make_sounds.py                      # rebuild src/AVATrackAndField/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings
for every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVATrackAndField/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, initials, high scores), the order of events,
    attempts, qualifying and the fixed-step loop.
  - `Sim.cs` holds the rules of the six events, one step at a time.
  - `World.cs` has `Field` (the screen, pixels to the metre, the lanes), the `Dice` generator, `Controls` and
    the events.
  - `Records.cs` keeps the world records in `records.txt`, beside the high scores.
  - `Pilot.cs` is the computer athlete, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVATrackAndField/Views/`: `GameCanvas` draws the stadium and the athletes (`GameCanvas.Draw.cs`) and runs
  the timer.
- `src/AVATrackAndField/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVATrackAndField/Assets/Fonts/`: the VT323 font, under the OFL.
- `tests/AVATrackAndField.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshots, the Shift
  release case and the `PilotProbe` tuning report.

## Rules that keep it working

- **Fixed steps.** `GameEngine` collects frame time (at most 0.25 s a frame) and advances in fixed 1/60 s steps
  (`Field.Step`), so tests can run whole events deterministically. Draw random numbers from the world's `Dice`
  (`w.Dice`), never from `Random` or `Random.Shared`. `Sim.Start` seeds it from the event, the round and the
  attempt.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** There is no up/down axis. `Controls` counts the presses of the two RUN
  buttons (`A`, `B`) since the last read and whether ACTION is held. Holding a RUN button does nothing, and
  `Read()` takes the taps, so call it once a frame, as `GameCanvas` does. Test new controls through
  `InputHandler`.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. Shift is an ACTION key, and `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up; a player's own `ALSOFT_CONF` is left alone.
- **Tests never touch the player's files.** High scores and records live in the app-data folder
  (`AVATrackAndField`); tests load them from a fresh temp folder and set `Silent = true` on the engine.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- Qualifying marks and speeds are tuned against tapping rates: run the probe after changing them.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
