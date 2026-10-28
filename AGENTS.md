# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game and its controls;
this file covers how the code is put together and the rules that keep it working.

## What this is

A side-scrolling shooter in the style of Konami's 1985 arcade game Gradius, in C# on .NET 10 and Avalonia 12,
drawn with Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64
and ARM). The graphics are drawn in code, and the sounds, tunes and the ship's voice are synthesised by
`tools/make_sounds.py` (with `tools/speech.py`).

## Commands

```bash
dotnet build AVAGradius.slnx                      # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAGradius.slnx                       # every test runs headless: no window opens
dotnet run --project src/AVAGradius               # play it; --no-audio for silence
GRADIUS_SHOTS=$PWD/Images dotnet test tests/AVAGradius.Tests --filter ScreenshotTests   # refresh the README's pictures
GRADIUS_PROBE=1 dotnet test tests/AVAGradius.Tests --filter PilotProbe   # how the computer pilot gets on, stage by stage
python3 tools/make_sounds.py                      # rebuild src/AVAGradius/Sounds (standard-library Python only)
```

The probe also reads `GRADIUS_SEEDS` (default `1,2,3`) and `GRADIUS_STAGES` (default `0,1,2,3`);
`GRADIUS_PROBE=lazy` flies a pilot that only holds the middle and fires.

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings for
every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAGradius/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores), the stage phases and the
    fixed-step loop.
  - `Sim.cs` steps a stage: the scrolling, the Halcyon, its guns and drones, the enemies, volcanoes, hits and the
    fight at the end. It works on a `World`, so the pilot can run it on copies.
  - `World.cs` has the copyable `Dice`, the power-ups and everything in a stage (`Ship`, `Enemy`, `Shot`...).
  - `Stage.cs` has `Controls`, the screen measures (`Field`) and the four stages, built in code by
    `StageBuilder` and listed in `StageBook`.
  - `Pilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAGradius/Views/`: `GameCanvas` (split over partial files) draws the frame and runs the 60 Hz timer.
- `src/AVAGradius/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAGradius.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`.
  - `GameplayTests.cs` and `StageTests.cs` cover play and the stage layouts.
  - `ScreenshotTests.cs` draws the README's pictures. `ShiftReleaseTests.cs` covers the Shift key quirk below.
  - `PilotProbe.cs` does nothing unless `GRADIUS_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances the game in fixed 1/60 s steps (`Field.Step`, `Sim.Step`),
  independent of the frame rate, so a seed always plays the same game. Draw random numbers from the world's
  seeded `Dice` (`w.Dice`), never from `Random`. Keep all of a stage's state in `World` and keep it copyable: the
  pilot plans by running copies of it.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** Keys, D-pad and stick must produce the same `Dy` sign the engine expects
  (-1 is up the screen). Test new controls through `InputHandler`, not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData/AVAGradius`). Tests load the table from a fresh temporary folder and set
  `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features and controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site (Harlock123.github.io) to rebuild, if `SITE_REFRESH_TOKEN` is set.
