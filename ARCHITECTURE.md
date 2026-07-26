# Architecture

90s Engine is a 2D engine for one genre: narrative JRPGs with SNES-era constraints. It is
deliberately not general purpose. Every feature has to justify why it serves that genre
specifically, and how it is exposed to an AI author with validation.

This document covers the technical design. For the human/AI co-authoring model see
[ORCHESTRATION.md](ORCHESTRATION.md); to actually build a game see [AUTHORING.md](AUTHORING.md).

---

## 1. What the era got right, and what it got wrong

JRPGs of the 90s separated engine from content: maps, text, enemies, items and events lived in
tables, banks and compact data, outside the main loop. That separation is why small teams
shipped enormous games, and why writers who could not program could still author content.

What was bad was not the idea, it was the encoding. That data lived in opaque formats —
pointers, offsets, ROM banks, conventions no tool enforced. A broken reference was not a build
error; it was something you found while playing, weeks later, if you were lucky.

The design position of this engine is: keep the separation, throw away the encoding.

**Kept:** a specialized engine, content as data, tiles/tables/events, a light deterministic
loop, text and events as first-class citizens, real creative constraints.

**Discarded:** raw offsets and opaque binaries as the source of truth, unvalidated scripting,
a visual editor as the only interface, proprietary middleware in the core.

**Added, because it did not exist then:** a local MCP server as the primary authoring surface,
atomic and reversible tools, automatic global validation, and a mirror manuscript that grows
with the game.

## 2. Stack

- **C# / .NET 9** — the weight of this engine is data, validation, tooling, serialization and
  rules, not raw frame time. C# buys development speed there and is more than fast enough for a
  256x224 2D runtime.
- **raylib-cs** — a thin, permissively licensed layer for window, render, input and audio. It
  does not impose an architecture, which is the whole reason it was chosen over a full engine.
- **JSON** for development content, a single `game.pack` for distribution.
- **No network, no middleware, no telemetry.** The engine runs entirely offline.

A C++ / SDL3 stack was considered and rejected: more control, but more accidental complexity
and a much slower iteration loop on the data and MCP model, which is where this project's real
risk lives.

## 3. Layout

```
Seto90/
  Program.cs        CLI: 37 commands.
  Core/             Content model, store, pack, validation, formulas, transactions,
                    Mirror Book, audits, and CommandSession — the shared command layer.
  Mcp/              MCP stdio transport (pure JSON-RPC) + ToolRegistry with full schemas.
  Runtime/          raylib runtime, F1 editor, headless smoke tests.
    Render/         VirtualScreen (256x224 + integer scale), camera, sprite bank, CRT filter.
    Text/           Hand-drawn 8x8 font data and the CPU font atlas.
    Ui/             Themes, window boxes, title screen, engine splash.
    Fx/             Transitions, screen shake, declarative VFX renderer.
    Battle/         Turn engine (pure logic, no raylib) and the rolling HP counter.
    Editor/         The in-game editor and project hot reload.
  Save/             Atomic saves with backup and checksum.
  Audio/            Chiptune synth, SFX synth, gapless music streaming.
  Assets/           Asset pipeline, embedded defaults, retro constraints.
```

`Core/` never references raylib. That is what lets the battle engine, map operations, VFX math,
validation and the audits all run headless in deterministic smoke tests.

## 4. Content is data, and the data is checked

Everything lives in one `project.json`: render settings, variables, tilesets, maps, events,
dialogues, actors, items, enemies, battles, skills, shops, songs, SFX, VFX, sprites, fonts, UI
themes, the quality plan and the story book.

Every write validates the **entire** project, not the edited entity. The validator checks
references (a map's tiles exist, a warp's target map exists and its coordinates are in bounds,
an event's dialogue exists), retro constraints (virtual resolution, integer scaling, tile size
consistency, `#RRGGBB` colors, palette ceiling), and per-system rules (song tempo and note
syntax, VFX layer shapes and durations, item slots and bonuses, time conditions, money amounts).
Failure returns a structured `{code, message, fix}` and rolls the write back.

The distribution artifact is a single file, `build/game.pack`, with referenced PNGs embedded as
base64. Development stays diffable and reviewable; distribution stays sealed.

## 5. Retro constraints that are real

The 256x224 canvas is an actual `RenderTexture` with point filtering, scaled by whole integers
with letterboxing into a resizable window. It is not a stylistic filter over a modern canvas —
content genuinely has that much room, and the palette ceiling genuinely constrains art.

The optional CRT filter is a GLSL fragment shader over the upscaled frame: glass curvature,
scanlines, an aperture-grille phosphor mask, vignette and slight RGB misconvergence. If the
driver fails to compile it, the engine degrades to clean pixels without an error. Authoring
screenshots default to the clean 256x224 canvas.

One rendering lesson is baked into the code and worth repeating: **semi-transparent overlays
drawn straight onto the render texture degrade its alpha channel**, and exported PNGs come out
with grey banding. Light effects use additive blending and shadows use multiplicative. Every
VFX layer in the engine follows that rule.

## 6. Audio is synthesized, not shipped

There is not a single audio file in this repository. `SongDef` is a tracker-style structure —
tempo, channels, waveform, notes with pulse durations, per-channel volume, attack/release
envelopes, square duty cycle — and `ChiptuneSynth` renders it to PCM at runtime. Sound effects
are synthesized in memory from a waveform, a frequency sweep and a decay.

Music streams with gapless looping. Twelve reserved SFX ids ship as embedded defaults and any
project can override them by declaring the same id. Both players rebind on hot reload, so a song
created live through MCP is audible immediately without restarting.

## 7. Saves

Slots are JSON under `%LOCALAPPDATA%/Seto90/Saves/<projectId>/`, written atomically through a
temp file with a `.bak` backup and a SHA-256 checksum; a corrupt slot falls back to its backup.
Saves persist flags, position, map, inventory, money, party (level, EXP, HP, MP, equipment) and
world time.

Player preferences (music and SFX volume) live in a separate global `settings.json` next to the
saves — **never** in `project.json`. Player state is not authored content.

## 8. The Mirror Book

The engine's most distinctive system: while the JRPG is built, its novel is built alongside it.
Both are first-class, and neither is generated from the other.

This is not dialogue transcription. A playable scene and a literary scene pursue the same canon
in different languages — the game expresses through exploration, choice, combat, music and game
feel; the novel expresses interiority, rhythm, image and voice. A dialogue tree flattened into
prose is not a chapter.

Each `StorySceneDef` holds prose, synopsis, POV, place, time and editorial status, plus `links`
to the maps, events, dialogues, battles and characters that express it in play. Where the game
branches, `canonChoices` records which path the book follows.

When an author declares a scene reconciled, `story.scene.sync` stores **two fingerprints**: one
over the linked playable content, one over the prose. From then on the engine can tell exactly
which side moved:

| State | Meaning |
|---|---|
| `IN SYNC` | both expressions still agree |
| `GAME CHANGED` | gameplay changed — the prose needs adapting |
| `BOOK CHANGED` | prose changed — gameplay needs building or rewriting |
| `BOTH CHANGED` | both moved — review the canon before continuing |

Adaptation runs in both directions and is always done by the author or the AI, never by
heuristics. Free text is deliberately **not** compiled into gameplay: `story.import` deposits
prose as draft chapters and scenes with no links, no maps and no inferred mechanics. Inferring
gameplay from prose would create a second authoring pipeline with its own truth, which is
exactly what the single-command-layer architecture exists to prevent.

The manuscript exports locally to `build/book/` as Markdown with front matter and as OpenXML
`.docx` formatted for editorial work: Times New Roman 12, double spaced, one-inch margins, title
page, running header, page numbers, chapter breaks.

## 9. The audits

Three layers of automated review, each returning a report an AI can act on:

- **`scene.audit`** — linearizes one scene (script, flags, staging, emotes, VFX/SFX, pacing,
  game feel) so it can be reviewed before playtesting.
- **`balance.audit`** — reconstructs the progression curve and checks stats, difficulty, EXP,
  prices, equipment, consumables, skills and counterplay, stating its assumptions.
- **`quality.audit`** — the director: validation, assets, scenes, encounter contracts, balance
  along a declared canonical route, and an optional scripted runtime pass with assertions. It
  returns one verdict: `ready_for_pack`, `needs_review` or `blocked`.

Encounter contracts classify each battle as `required`, `optional` or `repeatable` and by role
(`tutorial`, `common`, `elite`, `boss`), with a deliberate range of prepared actions and an HP
floor. A long fight therefore only blocks when it contradicts the author's *declared* intent —
not because a heuristic decided on their behalf.

With `enforceOnPack`, packaging and publishing refuse to proceed on warnings. It is opt-in so
existing projects keep working.

## 10. Verification

Every system has a headless, deterministic smoke test: dialogue, events, battle, audio, assets,
font, UI, saves, shops, party, map operations, VFX, sessions, and each audit. They run without a
display or an audio device, which means they run in CI and they run fast.

Because there is no RNG anywhere in the engine (see
[ORCHESTRATION.md §8](ORCHESTRATION.md)), these tests assert exact numbers rather than ranges.

## 11. Publishing

`publish` produces `dist/` with a self-contained win-x64 executable named after the game, the
`game.pack`, and a player-facing readme. Running the executable with no arguments looks for a
`game.pack` beside it, so double-clicking it plays the game.

Steam integration is deliberately **not** in the core. If it is ever added it will be a separate
optional adapter, because nothing proprietary belongs in an engine that is supposed to run
entirely offline and be forkable.

## 12. The rule that keeps this coherent

Do not turn 90s Engine into a general-purpose engine. Every new feature must justify why it
serves 90s JRPGs specifically, and how it is exposed to an AI through MCP with validation. A
specialized engine can validate maps, events, dialogues, battles, shops, economies and songs in
ways a general one structurally cannot — and that validation is the entire reason an AI can
author here safely.
