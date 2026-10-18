# block-drop-unity

![ci](https://github.com/Supp3r-Man/block-drop-unity/actions/workflows/ci.yml/badge.svg)

A small falling-blocks puzzle game for **Unity 6**: stack the seven shapes, clear rows, speed up with every level. Includes next-piece preview, hold, ghost piece, wall kicks, lock delay, a 7-bag randomiser, pause/restart and a saved best score.

> **What is verified, and what is not.** I wrote this on a machine with **no Unity editor installed**, so **the game has never been opened or played in Unity**. What *was* checked:
> - All the rules live in plain C# under `Assets/Scripts/Core` with no Unity types (board, shapes and rotation, collision, gravity, soft/hard drop, lock delay, line clears, scoring, levels, hold, game over, 7-bag). **76 tests pass** on them, including two bot simulations and a random-input run that checks invariants after every step.
> - I broke those rules on purpose **39 ways** (no floor, rows that do not fall after a clear, unlimited lock resets, a missing wall kick, a hold that works twice, wrong scoring tables, ...) and the tests failed every time. Two breaks survived at first (gravity wrongly awarding soft-drop points; a level-zero guard that a zero-level test cannot see), and I added assertions for both.
> - The Unity-facing scripts compile against **hand-written stubs** of the Unity API, under each input-handling configuration. That catches typos and type mistakes in my code. It does **not** check that I used the real Unity API correctly, and it says nothing about how the game looks or feels.
> - Not checked at all: `Assets/Editor/BuildScript.cs`, the `build-unity` workflow (needs a Unity license, never run), the pinned editor version `6000.0.23f1`, the `com.unity.inputsystem` 1.11.2 package version. (The `ci.yml` logic-test job did run green on GitHub after the first push; it covers `Core` only.) If Unity reports a problem on first open, those are the first places to look.

## Play it

1. Install **Unity 6** (any 6000.0.x; Hub will offer to upgrade the pinned version if yours differs).
2. Unity Hub → **Add project from disk** → pick this folder. The first open imports everything and generates the `.meta` files (commit them afterwards; Unity expects them in version control).
3. Open any scene (or just the default empty one) and press **Play**. There is no scene to wire up: `Bootstrap.cs` builds the camera, board, pieces and HUD at runtime.

| Key | Action |
|---|---|
| Left / Right (or A / D) | Move (hold to auto-repeat) |
| Up or X | Rotate clockwise |
| Z | Rotate counter-clockwise |
| Down (or S) | Soft drop |
| Space | Hard drop |
| C or Shift | Hold |
| P / Esc | Pause (also pauses when the window loses focus) |
| R / Enter | Restart (after game over, or while paused) |

**Rules as implemented.** 10 x 20 board. Pieces come from a 7-bag (every piece once per seven). Gravity speeds up every level (1.0 s per row at level 1, 0.36 s at level 5, 0.064 s at level 10, floor 0.02 s). A piece resting on something locks after 0.5 s; sliding or rotating restarts that timer, up to 15 times. Soft drop scores 1 point per row, hard drop 2. Clearing 1/2/3/4 rows scores 100/300/500/800 times the level. A new level every 10 rows (max 20). Hold can be used once per piece. The game ends when a new piece has no room to appear or a piece locks above the visible board. The best score is kept in `PlayerPrefs`.

## Layout

```
Assets/Scripts/Core/      engine-free rules: BlockGame, Board, Shapes, PieceBag, Scoring (+ Speed), HighScore, Rng
Assets/Scripts/           Unity layer: Bootstrap, GameController, InputReader, SpriteFactory, Sfx
Assets/Editor/            BuildScript (menu "Block Drop" + batch-mode entry points)
Tests/LogicTests/         console test runner that compiles Core without Unity (dotnet run)
.github/workflows/        ci.yml (runs the logic tests), build-unity.yml (reference, needs a Unity license)
```

Design choices worth knowing:
- **Rules separate from engine.** The Unity layer reads keys, calls `BlockGame`, and draws what it reports. That is what makes the part that matters testable without an editor.
- **No assets.** Block sprites are generated grey-scale tiles tinted per piece; sounds are synthesised beeps. Nothing to import, nothing binary in git.
- **No hand-written scene.** The game builds itself at start-up. `Block Drop → Create Scene` makes the empty `Main.unity` that builds need.
- **Own RNG** (xorshift) so a seed gives the same piece sequence on every platform, which also keeps the tests stable.
- **Simple wall kicks, not a published rotation system.** When a rotation does not fit it tries: in place, 1 or 2 columns left, 1 or 2 columns right, 1 row up. That is my own scheme and will feel different from other games.
- **Either input system.** `InputReader` compiles for the old Input Manager, the new Input System, or both.

## Run the tests

```
dotnet run --project Tests/LogicTests -c Release     # needs the .NET 8 SDK
dotnet run --project Tests/LogicTests -c Release -- --stats    # also prints the bot measurements below
```

It prints `76 passed, 0 failed` and exits non-zero on any failure. CI runs the same command. (Locally, with no .NET SDK, I compiled the same sources with the old .NET Framework C# 5 compiler, which is why `Core` and the tests avoid newer C# syntax.)

Simulations, from `--stats` (deterministic seeds):
- A bot that only hard-drops where each piece spawns lost after **10 pieces** and never cleared a row.
- A greedy bot (tries every rotation and column, scores height, holes, bumpiness and lines) cleared **116, 117 and 119 rows** in 300 pieces on three seeds and never lost. After every piece the tests check that the number of filled cells equals `4 x pieces locked - 10 x rows cleared` and that no full row is left behind. This shows the rules are internally consistent over long games; it says nothing about how hard the game is for a person.
- A random-input run (6 seeds x 6000 steps of moves, rotations, drops, holds and ticks) checks after every step that the current piece fits, the cell count is conserved, the score never drops and the level matches the rows cleared.

## Build and deploy

- **In the editor:** menu **Block Drop → Build WebGL** (output `Builds/WebGL`, compression disabled so any static host works) or **Build Windows**.
- **Batch mode:** `Unity -batchmode -quit -projectPath . -executeMethod BlockDrop.Editor.BuildScript.BuildWebGL`.
- **GitHub:** this repo is the source. `build-unity.yml` is a manual (`workflow_dispatch`) game-ci pipeline that needs `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` secrets ([how](https://game.ci/docs/github/activation)). I have not run it. Hosting a playable WebGL build on GitHub Pages is a follow-up: build, then publish the `Builds/WebGL` folder.

## Steps taken

1. **Started from the layout of the sibling project `orb-rush-unity`** (engine-free `Core`, thin Unity layer, runtime-built scene, generated art and sound, console test runner), and kept the C# 5-compatible style in `Core` because the only compiler on this machine is the old .NET Framework `csc`.
2. **Wrote the shapes and board first**: the seven shapes as base cells in a square box, rotated clockwise by `(x, y) -> (size - 1 - y, x)`; a 10 x 20 grid with floor/wall blocking, a free space above the top for spawning, drop-distance, line clearing by compacting rows from the bottom, and helpers (column height, holes, clone).
3. **Wrote the game rules class** (`BlockGame`): movement, rotation with kicks, gravity with a soft-drop rate, lock delay with a limited reset budget, hard drop, hold, scoring at the level the rows were cleared on, game over by blocked spawn or lock-out, and `ForceCurrent` so tests (and a future save/restore) can put a specific piece in play.
4. **Wrote the 7-bag** on the repo's own xorshift RNG and tested the bag property (once per seven draws, bounded gap between repeats, really shuffled).
5. **Wrote the tests and ran them** with the C# 5 compiler: 76 tests including a greedy bot, a "never moves" bot and a random-input invariant run. They passed first time, which proved nothing yet.
6. **Mutation-checked them**: 39 deliberate breaks with a script that reports "pattern missed" rather than silently passing. Two survived (gravity awarding soft-drop points; a negative-level score that equals the zero-level case) and I added assertions for both. Before that first run I had already tightened two tests the mutation list was designed to probe: two mid-level gravity values are pinned, and the negative-time test also checks that gravity time is not corrupted. Final: 39 of 39 caught.
7. **Wrote the Unity layer**: runtime bootstrap, camera, pooled tile sprites for the board / piece / ghost / next / hold, key handling with auto-repeat, sounds for move, rotate, hold, lock, clears (a longer jingle for four rows), level up and game over, an IMGUI HUD, pause/restart, and best score.
8. **Compiled the Unity layer against stubs** under four combinations of `ENABLE_LEGACY_INPUT_MANAGER` / `ENABLE_INPUT_SYSTEM`. All compiled. (Caveat above: this proves my code is self-consistent, not that the Unity API usage is right.)
9. **Copied the build script, manifest, version file and workflows from the sibling project** and renamed them for this game.

## Known gaps

- Never run in Unity: look, feel, timing, difficulty, sound volume and the HUD layout are all untested. The HUD panels sit about 8.5 world units either side of the board, so a very narrow (portrait) window will crop them.
- The rotation/kick scheme and the scoring numbers are my own simple versions, not those of any published game. No T-spins, back-to-back bonus, combo bonus or 'perfect clear'.
- No `.meta` files yet, no committed scene, no ProjectSettings beyond the version file; Unity fills these in on first open.
- Keyboard only: no touch or gamepad input.
- The bot results show the rules behave consistently, not that the game is fun or fair.

## License

MIT
