# Making a game with 90s Engine

The complete authoring flow, from idea to executable, with a human and an AI co-authoring the
same live session. Written so that any AI — or any person — can sit down in front of the engine
and produce a JRPG without reading the source.

> Public name: **90s Engine**. Internal code name: `Seto90` (namespace, CLI commands). Every
> published game opens with the "90s ENGINE" splash and its title screen reads "Made with 90s
> Engine".
>
> **A note on language:** this documentation is in English; the engine's CLI output, code
> comments are in Spanish. The engine UI follows `render.language` (`en` default, `es`
> available). Your game's content is in whatever language you
> write it in — the embedded 8x8 font covers full Spanish, and fonts can be imported.

---

## 1. What 90s Engine is

A 2D engine **specialized exclusively in 90s-style JRPGs**. It is not general purpose, and that
is its strength: it can validate maps, events, dialogues, battles, shops and songs in ways a
generic engine structurally cannot.

Principles:

- **Engine and content are separate.** The engine is C# / .NET 9 + raylib. The content is ONE
  file: `project.json` for development, compiled to `game.pack` for distribution.
- **Everything is data, nothing is arbitrary script.** Maps, events, dialogues, damage formulas,
  songs and sprites are typed, validated JSON tables.
- **The AI is a first-class author.** A local MCP server exposes 57 atomic tools. Every write
  validates the ENTIRE project; if it would break a reference it rolls itself back and returns
  the error with a suggested fix. Undo/redo is transactional.
- **The AI SEES what it builds.** `playtest.screenshot` runs the game hidden and captures a PNG
  of the real 256x224 canvas. The loop is edit → look → correct, with actual eyes.
- **The AI REHEARSES each scene.** `scene.audit` hands over commands, dialogues, flags,
  expressive coverage and evidence; the AI then has to judge coherence, staging, emotes,
  VFX/SFX, pacing and game feel by watching and playing the exact page.
- **The AI REVIEWS the whole curve.** `balance.audit` projects party, level, EXP and money per
  checkpoint, compares a basic attack against affordable equipment and supplies, and checks
  one-shots, fight length, rewards, prices, dominated options and counterplay.
- **The AI CLOSES production.** `quality.audit` crosses validation, assets, every scene, balance
  per route, encounter contracts and a real checkpoint playtest. It can block packaging and
  publishing while a verifiable warning remains.
- **Determinism.** No RNG in combat or in the world. Same input, same result — everything is
  testable headless (the "smokes").
- **Real retro constraints.** A 256x224 canvas, integer scaling, a limited palette, 8/16/32 px
  tiles, an 8x8 font, synthesized chiptune audio. Optional CRT filter (F2).
- **A game and a book are born together.** The Mirror Book links each gameplay scene to its
  literary adaptation, detects drift, and exports a Markdown + DOCX manuscript.

## 2. Creating the project: script first

### MANDATORY QUESTION BEFORE CREATING ANYTHING

There are **two startup modes**. An AI about to create a project **must ask the author which one
they want** before running `new` — never assume, because the mode changes where the canon comes
from:

| Mode | When | Command |
| --- | --- | --- |
| **New game** | The story does not exist yet; it is born with the game. | `new --project MyGame` |
| **Import a story** | A book, script or text already exists and the game adapts from it. | `new --project MyGame --from manuscript.md` |

Both produce **the same kind of project**: in this engine **every game is a mirror game** (game
+ Mirror Book in the same `project.json`, publishable as a game and as a manuscript). The only
difference is where the canon enters.

```powershell
# Mode 1: new game
dotnet run --project Seto90 -- new --project MyGame

# Mode 2: start from an existing text
dotnet run --project Seto90 -- new --project MyGame --from manuscript.md

# A text that arrives later (chapter 2, a loose script) can enter at any time:
dotnet run --project Seto90 -- import-story --project MyGame --from ch2.md --dry-run
dotnet run --project Seto90 -- import-story --project MyGame --from ch2.md
```

This generates a **minimal but playable** project (a bordered map, a hero, a guide NPC), the
first Mirror Book chapter and scene, and these files:

| File | What it is |
|---|---|
| `project.json` | all the content; **never edit it by hand** |
| `design.md` | the design document, to be filled in **before building** |
| `AGENTS.md` | instructions for the AI that opens this project |
| `.mcp.json` | a ready-to-use MCP client config, already pointed at this project |
| `.gitignore` | excludes regenerable artifacts and the local MCP config |

With `--from`, the imported manuscript replaces that first example scene.

### What importing a story does (and does not do)

`story.import` / `import-story` **only deposits prose**: it creates chapters and scenes in
`draft` status, with no `links`, no maps, no dialogues, no battles. It **does not infer
gameplay**, and it only ever adds (it never overwrites existing chapters, scenes or syncs; a
repeated id enters with a suffix and warns).

This is deliberate. Importing is not a second way of writing content. The text enters as
**queryable data** (`story.query`) and the game keeps being built scene by scene through the
same validated path as always. The golden rule still holds: **prose does not replace
`design.md`**. An imported text gives you 40 faithful scenes, but chapter length, the difficulty
curve, the battles and what is taught where are design decisions.

Manuscript format: `# title` opens a chapter, `## title` opens a scene, everything else is
prose. A plain `.txt` with no headings enters as one chapter, split on editorial scene marks
(`***`, `---`). Ids come from the title, normalized to ASCII (`## El andén` → `scene.el_anden`).
`--dry-run` / `dryRun: true` shows the chapter/scene split **without writing anything** — worth
reviewing before accepting a long manuscript.

Golden rule of the flow: **script first**. `design.md` asks for premise, core loop, chapter map,
style bible and flag convention. It is the production bible; the canonical literary scenes also
live in `storyBook` inside the project, where the engine does validate them. The sane order:

1. Fill in `design.md` (human and AI discuss it and close it).
2. Build chapter by chapter through MCP + the editor.
3. Verify each stage (validate + smokes + screenshots) and commit.

## 3. Connecting the AI

`new` writes a `.mcp.json` next to your project, already pointing at this engine build:

```json
{
  "mcpServers": {
    "90s-engine": {
      "command": "<path>/Seto90.exe",
      "args": ["mcp", "--project", "<path>/MyGame"]
    }
  }
}
```

Point your MCP client at that file and the 57 tools appear. To run the server by hand:

```powershell
dotnet run --project Seto90 -- mcp --project MyGame
```

The transport is stdio JSON-RPC: `initialize`, `tools/list`, `tools/call`. The `initialize`
response carries an `instructions` briefing, so a connecting agent already knows how to orient
itself, what to ask the author before building, the non-negotiable rules and the working loop —
without anyone pasting this guide into its context.

### Operational gotchas that cost hours

- **Piping to the server from PowerShell:** set
  `$OutputEncoding = New-Object System.Text.UTF8Encoding($false)`.
  `[System.Text.Encoding]::UTF8` emits a BOM, the first line arrives with `0xEF` in front, and
  the server rejects it with "invalid start of a value". Same care with `.jsonl` request files —
  save them without a BOM.
- **Check `ok` on EVERY response, not just the last.** A step that fails validation is reverted
  in full and silently; a final `validate` that returns ok proves nothing about the calls before
  it. Reading only the last response is how a change appears to land and simply is not there.
- **A running game locks the build.** With a game window open, `dotnet run` fails because the
  executable is in use. Invoke the built executable directly instead
  (`Seto90\bin\Debug\net9.0\Seto90.exe mcp --project MyGame`).
- **Hot reload only adopts in the world state.** A write that lands during a dialogue, battle,
  menu or title screen is picked up when the player returns to the map.

## 4. Live co-authoring (what makes this engine different)

Three pieces run at once over **the same command session** (`CommandSession`):

```text
   Human                          AI
     |                             |
  game window (run)             MCP server (stdio)
     |  F1 = visual editor         |  57 atomic tools
     |                             |
     +---------- CommandSession ---+      <- one single truth
                    |
              project.json  (hot reload: the running game adopts changes live)
```

- **The running game IS the editor.** `run` + F1: paint tiles with the mouse, move/create NPCs,
  toggle flags. Every gesture is the SAME validated atomic operation the AI uses.
- **The AI edits in parallel over MCP** and hot reload pushes the change into the live game
  (only if it validates, only in the world state). The human watches the AI's NPC appear.
- **One undo/redo.** `Ctrl+Z` undoes the human's work and the AI's in the same order. The
  editor's LOG tool shows the session's `[you]`/`[ai]` timeline.
- **Nobody overwrites anybody.** `project.json` carries a `revision` counter that grows with
  every save. If a session is about to write and the disk holds a higher revision (the other
  co-author saved first), it ADOPTS that state and applies its change on top (a rebase, recorded
  in the undo stack). Undoing with an unadopted external change is refused with `external_change`.
- **The AI proposes before touching.** `batch.preview` runs the proposal against a clone,
  validates it and returns a per-entity/field/cell diff. It writes no disk, no revision, no
  history, no undo. Its `baseRevision` is passed as `expectedRevision` to `batch.apply`: if
  someone edited while the human was reviewing, it fails with `stale_preview`.
- **The AI verifies with screenshots.** `playtest.screenshot` (or the `screenshot` CLI command)
  covers the title, splash, dialogues, battle, pause menu by section, the editor, remote maps
  (`map`+`x`+`y`), floating damage (`attack`) and the CRT filter (`crt`).
- **Cutscene scrubber.** In the editor, EVENTS + Enter replays the chosen event's command queue
  step by step WITHOUT replaying up to it: Enter advances one step, Space plays it all, R
  restarts, Esc exits — leaving or restarting returns the world EXACTLY to its previous state
  (a snapshot of flags, inventory, party, positions). The active page is chosen with the current
  flags (the FLAGS tool lets you test each variant). The AI captures it with `scrub: "event.x"`
  + `scrubSteps: N` (CLI `--scrub` / `--scrub-steps`). For a conditioned variant use
  `scrubPageIndex`; the engine moves to the event's map and prepares its conditions without
  contaminating the project or the save.

### The AI's scene rehearsal

A scene is not done because it validates. For each important page the AI calls `scene.audit` and
receives:

- Concrete risks: repeatable rewards or battles, badly closed flags, broken dialogues, impossible
  waits, VFX that never become visible, contradictory states.
- Staging coverage: movement, camera, timing, emotes, VFX, SFX and feedback.
- The linearized script: commands, speakers, text, choices and the Mirror Book prose.
- Questions that demand real judgment: whether the reaction is human, whether the subtext lands,
  whether a line is redundant, whether the moment deserves silence, a bubble, movement or impact.
- Suggested screenshots and scrub points to check the result.

The tool does not invent a "creativity score" — it separates checkable facts from authorial
judgment. The AI has to read the script, answer those questions, watch the scene, play it and
fix it. A quiet conversation may be perfect; a major revelation with no reaction, no pause and
no feedback probably is not.

### The global balance audit

After building or changing battles, actors, growth, skills, items, shops or rewards, the AI
calls `balance.audit`. The engine reconstructs a reachable route and generates a checkpoint per
battle with the projected party and level. At each one it compares:

- **Basic:** progression reached, full HP/MP, and Attack only.
- **Prepared:** equipment-first, supplies-first and tactics-without-purchases; it keeps whichever
  performs best using skills, heals, revives and status answers.

The report shows actions, remaining HP, MP/items spent, one-shot risk, purchases, rewards and
available money. It also detects strictly dominated equipment or heals, skills that cannot be
paid for or that do not beat a basic attack, shops without decisions, and difficulty/reward
jumps. It states its own limits: it merges branches whose canonical route it does not know,
counts each distinct battle once, assumes no grinding, and budgets each preparation independently.

It does not replace playtesting and it does not decide whether something is fun. A long boss can
be correct; an expensive shop can plant a goal. The AI uses the evidence to form the judgment,
confirms the canonical route with `playtest.run`, and only corrects what contradicts the intent.

### The Quality Director and canonical routes

`qualityPlan` turns production intent into validated data. A route orders checkpoints with the
exact event and page, canonical choices and subsequent expectations: map, money, party, levels,
flags and items. Each checkpoint can contribute manual steps; if it needs none, the engine runs
its event and generates the assertions automatically.

Contracts classify each battle as `required`, `optional` or `repeatable`, and by role as
`tutorial`, `common`, `elite` or `boss`. They can also fix a deliberate range of prepared actions
and an HP floor. A long fight therefore only blocks when it contradicts the declared intent, not
because a heuristic decided for the author.

`quality.audit` produces a single dossier with three verdicts:

- `ready_for_pack`: zero warnings; informational notes resolved or accepted.
- `needs_review`: no blocking defect, but a decision is left to authorial judgment.
- `blocked`: validation, assets, a contract, a scene, balance or a playtest contradicts the route.

With `enforceOnPack: true`, `project.build_pack`, `pack` and `publish` reject warnings. If
`runPlaytestsOnPack` is also on, they walk the canonical route inside the runtime before
producing the artifact. Both are opt-in so existing projects keep working.

### The Mirror Book: game <-> novel

This is not an automatic transcription of dialogue. A playable scene and a literary scene pursue
the same canon in different languages: the game expresses through exploration, decisions, combat,
music and game feel; the novel expresses interiority, rhythm, image and voice.

Each `StorySceneDef` stores prose, synopsis, POV, place, time and editorial status, plus `links`
to the game's maps, events, dialogues, battles and characters. Where there are choices,
`canonChoices` says which path the book follows. When a scene is declared reconciled,
`story.scene.sync` stores two fingerprints: one over the linked playable content, one over the
prose. From then on `story.query` and F1 > LIBRO distinguish precisely:

| State | Meaning |
|---|---|
| `IN SYNC` | both expressions are still reconciled |
| `GAME CHANGED` | adapt the novel to a gameplay change |
| `BOOK CHANGED` | build or rewrite gameplay from the new prose |
| `BOTH CHANGED` | review the canon before continuing |

Game → book: the AI queries the scene with `includeSources: true`, reads the real playable
sources, rewrites the prose with `story.scene.set`, reviews and syncs. Book → game: the AI reads
prose and synopsis, proposes events/dialogues/maps with `batch.preview`, the author approves with
`batch.apply`, it gets playtested, and only then is it synced. Free text is never "compiled" into
gameplay by opaque heuristics: the AI does the creative adaptation and the atomic tools keep the
result explainable, validated and reversible.

The manuscript is generated locally into `build/book/`:

```powershell
dotnet run --project Seto90 -- export-book --project MyGame
# Rejects incomplete or out-of-sync drafts:
dotnet run --project Seto90 -- export-book --project MyGame --strict
```

It produces `.md` with front matter and `.docx` OpenXML ready for Word/LibreOffice: Times New
Roman 12, double spaced, one-inch margins, title page, running header, numbered pages, each
chapter starting on a new page.

A typical co-authoring session starts like this:

```powershell
# Terminal 1: the live game (the human plays/edits with F1)
dotnet run --project Seto90 -- run --project MyGame

# The AI connects over MCP (stdio JSON-RPC: initialize, tools/list, tools/call)
dotnet run --project Seto90 -- mcp --project MyGame
```

## 5. The 57 MCP tools, by system

All with a full `inputSchema` (types, required fields, descriptions). Every write validates the
whole project; on error → automatic rollback + suggested fix.

**Read before writing (granular reads)**
- `query.content_graph` — content summary (the project index: ids and counts).
- `query.map` — one map IN DETAIL: the tile matrix of a region (default the whole map), warps,
  and events with position, sprite and pages with their conditions. With this the AI reasons
  about a map WITHOUT opening `project.json`. (The "active page" depends on runtime flags the MCP
  cannot see, so the conditions are returned for you to reason about presence.)
- `query.entity` — the COMPLETE definition of any entity (the GET symmetric to `content.delete`):
  a dialogue with all its nodes, a tileset with cells/collision/tint... the same camelCase shape
  as `project.json`. `kind: "project"` = global info; `kind: "quality"` = plan, routes,
  checkpoints and contracts.

**Batches (one transaction, all or nothing)**
- `batch.preview` — runs the same permitted writes against a clone and returns a compact diff:
  entities added/modified/deleted, changed fields, and how many map cells were affected. Zero
  side effects. Returns `baseRevision`.
- `batch.apply` — a list of WRITE calls `[{name, arguments}]` applied in order in memory, with
  ONE global validation at the end and ONE save. An invalid step reverts the ENTIRE batch (the
  disk is untouched; error `batch_step_failed` with the index). Forward references inside the
  batch are allowed (create tileset + map + events + dialogues together, in any order). Passing
  `expectedRevision` from the preview prevents applying a stale proposal. One batch = one undo.
  Not batchable: `query.*`, `playtest.screenshot`, build/validate/report,
  `transaction.undo/redo`, and nested batches (`not_batchable`).

**Project**
- `project.set_info` — id, title, starting map + exact position (`startX`/`startY`), player
  sprite, UI theme, starting money, starting party (`partyActorIds`), CRT filter, default warp
  transition and title background (`titleImage` static or `titleVfxId` procedural).
- `content.delete` — deletes any entity by kind and id (17 kinds). Deleting an event removes it
  from its maps' eventIds; deleting a map drags its events along. If the deletion leaves dangling
  references, automatic rollback with a suggested fix.
- `project.design` — reads or rewrites the design document (`design.md`). **Script first** is the
  engine's golden rule, so this is normally your first call after `query.content_graph`: read it
  before touching anything, and write back what you agree on with the author. Note what it is
  not: a document, not validated content — no reference checking, no undo, not batchable. Writes
  are atomic and leave a `design.md.bak`.
- `evals.run` — runs the game's **eval suite**: every script in `evals/*.txt` (playtest.run
  format) with one verdict. This is gameplay regression, and it is not the same thing as the
  engine's smokes: the smokes prove the ENGINE works, an eval proves THIS GAME is still
  playable after you changed its content. Leave one per finished chapter. Compact report when
  everything passes; where it fails it returns the first failing step and the final state.
- `project.validate` — global validation on demand.
- `project.audit` — explainable design auditor: unreachable routes, orphan nodes and content,
  variables without consequence, shadowed pages, choices that converge, and comparable
  combat/economy metrics. `includeInfo: false` returns warnings only.
- `scene.audit` — rehearsal of ONE scene by `eventId` + `pageIndex`, or a whole Mirror Book scene
  by `storySceneId`. Reviews flags/repetition, dialogue structure, staging, timing, emotes,
  VFX/SFX and game feel; returns a transcript, evidence, creative-judgment questions and
  suggested visual/playable checks. Modifies nothing.
- `balance.audit` — global auditor of stats, progression, battles and economy. Orders reachable
  checkpoints, accumulates party/level/EXP/money, simulates the Basic and Prepared profiles, and
  reviews one-shots, pacing, rewards, prices, dominated equipment/heals/skills and counterplay.
  `routeId` uses only declared checkpoints; `includeInfo: false` leaves only risks.
- `quality.audit` — read-only master director: combines validation, assets, design, scenes,
  balance per route, contracts and playtests. `runPlaytests: true` executes the checkpoint
  assertions and returns `ready_for_pack`, `needs_review` or `blocked`.
- `quality.plan.set` — enables/configures the gate, the canonical route and encounter contracts
  (`required|optional|repeatable`, `tutorial|common|elite|boss`).
- `quality.route.set` / `quality.route.delete` — creates, replaces or deletes whole routes with
  checkpoints, canonical choices and map/money/party/level/flags/items expectations.
- `project.build_pack` — generates `build/game.pack` (one distributable file).
- `project.asset_report` / `project.export_assets` — asset report and manifest.
- `transaction.undo` / `transaction.redo` — transactional undo/redo (with an unadopted external
  change they refuse with `external_change`: adopt first, then undo).
- `playtest.screenshot` — the AI's eyes (see section 4; `editorZoom: 2|4` captures the editor
  with the map at 1/2 or 1/4).
- `playtest.run` — the AI's HANDS: runs a deterministic play script with the game hidden
  (`event id [page]`, checkpoint, move/face/interact/auto/choose N/wait, assertions on
  flag/map/item/pos/money/party/level, screenshot/dump) and returns each step ok/failed plus the
  final state. Money and level accept `N` or `MIN..MAX`. The full loop: build → see → PLAY →
  correct. CLI: `playtest-run --project X --steps script.txt`.

**World**
- `variable.define` — narrative flags/numbers/text (`Flag`, `Number`, `Text`).
- `tileset.create` — logical tiles with color and collision; optional `image` = a PNG atlas in a
  grid (the tile id is its index in the atlas; the color remains as fallback). ANIMATED tiles:
  `frames` = atlas cells that cycle (e.g. `[8,9,10]` = 3-frame water) on the set's `animMs` clock
  (default 300; global on purpose, so all the water in a map pulses together).
- `map.create` / `map.paint_rect` — create a map and paint tile rectangles. CAREFUL:
  `map.create` on an existing id REGENERATES the tiles (destructive).
- `map.paint_tiles` — FINE painting: an arbitrary list of `{x,y,tile}` cells in ONE transaction
  (the editor's stroke exposed to the AI). Strict: a cell outside the map returns an error naming
  the exact cell and paints nothing. Optional `flags` (0-7) orients the stroke: quarter-turn
  rotation + mirror.
- `map.flood_fill` — 4-connected flood fill from `(x,y)` with `tileId` (the same algorithm as the
  editor's F key); if the origin already holds that tile it returns `painted: 0`.
- `map.set_info` — change a map's name, song or weather without touching what is painted.
- `map.set_warps` — step-on teleports (with `fade`/`iris`/`spiral` transition).

**Events and cutscenes**
- `event.create` — NPC/Trigger/Object/Cutscene on a map, with sprite and routine (`idle`,
  `pace_horizontal`, `pace_vertical`, `look_around`, `guard`).
- `event.set_commands` — commands of the main page.
- `event.set_pages` — pages conditioned by variables (evaluated back to front: the last one whose
  conditions hold becomes active — the classic pattern). PRESENCE: if no page matches, the event
  does not exist in the world (invisible, no collision, cannot be triggered). That is how you
  build "an NPC that appears/disappears with the story": two events taking turns on a flag. A page
  with no conditions always matches.

**Narrative**
- `dialogue.create` — a node graph: speaker, text, choices (which jump to another node) and node
  effects (any event command: give an item, set a flag, open an inn...).
- `story.import` — **the on-ramp**: an already-written manuscript or script (file or inline text)
  enters as chapters and scenes. Prose only: it creates no gameplay and no links, it only adds,
  and `dryRun` shows the split without writing.
- `story.book.set` / `story.chapter.set` — editorial metadata and ordered structure.
- `story.scene.set` — prose + dramatic sheet + gameplay links + canonical route.
- `story.scene.sync` — confirms game and book were reconciled and fixes their fingerprints.
- `story.query` — manuscript, word counts, drift, and optionally the full playable sources.
- `story.delete` — transactionally deletes a scene or a chapter.
- `story.export` — generates Markdown + DOCX; `strict` demands a complete, up-to-date manuscript.

**Combat and economy**
- `actor.create` — hero/companion: base stats, per-level `growth`, skills.
- `skill.create` — `kind`: `damage` (with optional `status`: `poison`/`sleep`), `heal`, `revive`;
  MP cost and power; optional `vfxId` (its own visual effect on impact; empty = the engine's
  `vfx.hit`/`vfx.heal`).
- `enemy.create` — stats, EXP, money, `inflicts` (status its attack applies).
- `battle.create` — enemies, `victoryFlag`, declarative damage formula (e.g.
  `max(1, attack - defense)`), rolling HP, its own music (`songId`), `boss` (pulsing aura + name
  plate + reinforced shake for a climactic enemy) and `backgroundVfxId` (animated battle
  background, see section 7).
- `item.create` — `effect`: `heal:N`, `cure:poison|sleep|all`, `revive:N`; equipment with `slot`
  (`weapon`/`armor`) + `bonus` (stats added while equipped); optional `description` and
  `spriteId` for the `ShowItemGet` ceremony. **A price of 0 marks a key item**: it never appears
  on a shop's sell counter.
- `shop.create` — a shop with a list of items (buy / sell at half price).

**Presentation**
- `sprite.create` — procedural pixel art: a palette (max 15 colors) + poses per direction as rows
  of hex indices; automatic left/right mirroring, up→down fallback.
- `sprite.import_sheet` — a PNG spritesheet (4 rows: down/up/left/right, N frames).
- `font.import` — a bitmap PNG font with a charset (the embedded 8x8 one covers full Spanish).
- `uitheme.set` — window colors, style and typewriter speed.
- `song.create` — a tracker song (see section 7).
- `sfx.create` — a synthesized effect: waveform, frequency sweep, decay, duty.
- `vfx.create` — a code-generated VISUAL effect (see section 7): attack flashes (`impact`),
  animated battle backgrounds (`background`) and weather (`weather`). Zero PNGs, zero shaders.

## 6. Event commands (the vocabulary of cutscenes)

Each command is `{kind, targetId, value}`. They run in a queue; blocking ones wait. A movement
step blocked by collision IS SKIPPED — a cutscene never freezes the game.

| Kind | targetId | value | Effect |
|---|---|---|---|
| `Dialogue` | dialogue | — | Opens the dialogue (typewriter, choices) |
| `Battle` | battle | — | Starts the battle (swirl + its own music) |
| `SetVariable` | variable | value | Sets a flag/number/text |
| `GiveItem` | item | amount | Adds to the inventory; a "+N Name" float appears over the player |
| `ShowItemGet` | item | amount | The key-item ceremony: delivers the item AND stops the world — darkened background, golden rays turning behind the item sprite, name + `description`, and a fanfare (`sfx.item_get`). Enter continues. Sprite: the item's `spriteId`, or the `item.X` → `sprite.X` convention |
| `GiveMoney` | — | amount | Adds money with a gold "+$N" float (chests, cutscene rewards) |
| `TakeMoney` | — | amount | Charges money with a "-$N" float. **If there is not enough it shows "Not enough gold", plays the cancel sound and CUTS the whole queue** — the classic vending-machine/toll pattern: charge before the ritual, no credit |
| `OpenShop` | shop | — | Opens the shop |
| `OpenInn` | — | price | Inn: charges, fades to black, heals and revives everyone |
| `PlaySong` | song | — | Changes the music |
| `PlaySfx` | sfx | — | Fires a sound effect |
| `TransferPlayer` | map | `"x,y"` | Teleports with a transition |
| `Wait` | — | seconds | Blocking pause (`"0.8"`, max 10) |
| `MoveEvent` | event | steps | Walks an NPC: `"up,down,left,right,face:up"` |
| `MovePlayer` | — | steps | Walks the player (same steps) |
| `PanCamera` | event (or `player`) | seconds | Slides the camera with smoothstep and waits |
| `AddPartyMember` | actor | — | Adds the actor to the party (no duplicates; the save persists it) |
| `RemovePartyMember` | actor | — | Removes the actor (never leaves the party empty) |
| `ShowEmote` | event (or `player`) | `"zzz:6"` | An emote bubble over the head (`!`, `?`, `zzz`, `nota`, `puntos`, `corazon`; default 2s, max 10). Does NOT block: it accompanies the scene |
| `ShowFloat` | anchor (`''`/`player`/event) | `"text"` or `"text:#RRGGBB"` | Floating text that rises and blinks (e.g. `'+6 HP:#82F0A0'`). Does NOT block |
| `PlayVfx` | vfx | anchor event (or `player`) | Fires an `impact` vfx in the world, anchored to a character. Does NOT block: compose it with `Wait` |
| `SetWeather` | weather vfx (`''` = clear) | — | Runtime weather override for the current map. Does NOT block; changing map returns to the destination's authored weather |
| `AdvanceTime` | — | `"tarde"` / `"+dia"` | Advances world time (fade + a "DAY 2 - MORNING" plate). Pages condition on the reserved ids `time.dia` and `time.franja`; the UI clock is enabled with `render.showDayClock` |

`PanCamera` has a safety net: if the queue ends with the camera far away, it returns to the
player on its own. Dialogue node effects use this same vocabulary — a classic innkeeper is a
dialogue with a choice whose "Yes" node has an `OpenInn` effect.

## 7. Audio, VFX and weather: the AI composes

No files and no middleware: the engine synthesizes chiptune and draws effects locally.

- **Songs** (`song.create`): tempo + several channels, each with a waveform
  (`square`/`triangle`/`saw`/`noise`), volume, an attack/release envelope in ms, square duty
  (0.25/0.125 for NES colors) and notes with a duration in pulses: `"C4"` (1 pulse), `"C4:2"`,
  `"R:4"` (rest). Percussion = a noise channel with short notes and a small release. Native
  gapless looping. Map music is `map.songId`; battle music is `battle.songId`.
- **SFX** (`sfx.create`): simplified sfxr-style synthesis. Engine-reserved ids (overridable by the
  project): `sfx.cursor`, `sfx.confirm`, `sfx.cancel`, `sfx.text_blip`, `sfx.encounter`,
  `sfx.hit`, `sfx.player_hit`, `sfx.victory`, `sfx.save`, `sfx.door`, `sfx.item_get` (the
  `ShowItemGet` fanfare) and `sfx.boot` (the splash jingle).
- **VFX** (`vfx.create`): the VISUAL tracker, symmetric to `song.create` — layers of primitives
  on a timeline, deterministic (particle "randomness" is a hash of the index: the same effect on
  any machine, capturable at an exact phase).
  - `kind: "impact"` — an attack flash: `flash` (full-screen), `spark` (particles with `motion`:
    `burst`/`rise`/`fall`/`spiral`/`expand`), `ring` (expanding wave), `slash` (with `angle`) and
    `beam` (column of light), each with `color`, a `startMs`..`endMs` window and `blend`
    (`additive` = light, the default). Used from `skill.vfxId`, the `PlayVfx` command, or by
    overriding the reserved **`vfx.hit`** (attacks and damage skills) and **`vfx.heal`**
    (heals/revives) that every battle already uses.
  - `kind: "background"` — an animated battle background: pattern layers
    (`bands`/`checker`/`rings`/`waves`) with `colors` cycled every `cycleMs`, scroll in px/s and
    per-scanline undulation (`distortAmp`/`distortFreq`/`distortSpeed` — the edge between bands
    snakes). Loops for the whole battle; attached with `battle.backgroundVfxId`.
  - `kind: "weather"` — map weather: `rain` (streaks falling, `angle` = wind, `scrollY` = speed,
    `sizePx` = length), `snow` (slow drifting flakes), `fog` (additive banks drifting with
    `scrollX`, `spreadPx` = radius), `flash` (periodic lightning every `cycleMs` >= 1000) and
    `splash` (deterministic splashes where the drops land). Screen coordinates, so the sky follows
    the camera. **Cycles:** with `durationMs` > 1000 that is the cycle length and each layer lives
    in its own `startMs`/`endMs` window with smooth ramps — it starts raining and it clears,
    rather than appearing. Attached with `map.weatherVfxId` or the `SetWeather` command. Reserved
    and overridable: `vfx.lluvia`, `vfx.niebla`, `vfx.nieve`, `vfx.tormenta`.

  Minimal background example:
  `{"id":"vfx.tower_bg","kind":"background","layers":[{"pattern":"bands",
  "colors":["#1A1030","#241448","#301A60"],"sizePx":16,"scrollY":12,
  "distortAmp":10,"distortFreq":0.1,"cycleMs":400}]}`

- Player volume (the pause menu's OPTIONS) lives in a global `settings.json`, NEVER in
  `project.json`.

**Rendering lesson worth knowing:** semi-transparent overlays drawn straight onto the render
texture degrade its alpha channel and exported PNGs come out with grey banding. Use additive
blending for light and multiplicative for shadow. Every VFX layer in the engine follows this.

## 8. Systems the engine already ships (you only add content)

- **Party and progression**: real EXP (curve `10 + 5*level^2`), per-actor growth, HP/MP persisting
  between battles, revives, equipment with bonuses. Defeat leaves you at 1 HP; a real game over
  resets the world and returns to the title.
- **Deterministic turn-based combat**: full party vs multiple enemies, turns by speed,
  Attack/Skill/Item/Defend/Flee, statuses (poison/sleep), heals and revives with a chosen target,
  floating damage, flash, shake, optional rolling HP.
- **Visible encounters**: NO random battles. The enemy is a visible event on the map that starts a
  `Battle` when touched, and disappears via its `victoryFlag` through conditioned pages.
- **Saving**: 3 atomic slots with backup and checksum, F5/F9 quick save/load, "Continue" on the
  title loads the most recent. The save includes party, equipment, money, map and position.
- **Pause menu** (Esc): ITEMS / STATUS / EQUIPMENT / OPTIONS / SAVE / LOAD / QUIT.
- **Transitions**: fade, iris and block spiral per warp; battle swirl into combat.
- **Weather and world time**: authored per map, advanced by cutscene, conditioning event pages.

## 9. Art and licensing

The engine ships original art only: the 8x8 font, the logo glyphs, the default sprites and every
sound are drawn or synthesized from scratch for this engine.

**Clean-licence rule: never import ripped assets** (fonts or sprites from commercial games). The
period style is learned by MEASURING references — proportions, density, contrast — never by
copying pixels. If you bring in third-party art packs, record author, licence and modifications
before moving anything into your project's `assets/`, and keep that record in the repository. A
pack whose licence you cannot produce on demand is a pack you cannot ship.

For bulk imports there is a helper that copies a folder of PNGs in as sprites in one validated
transaction:

```powershell
dotnet run --project Seto90 -- import-sprites --project MyGame --from <folder> --prefix P [--max 128]
```

## 10. The quality cycle (non-negotiable)

Every work stage ends with a green battery and a commit. Every scene also goes through
**audit → read/judge → look → play → fix**:

```powershell
dotnet run --project Seto90 -- validate --project MyGame      # references and rules
dotnet run --project Seto90 -- audit --project MyGame         # structure, narrative, routes
dotnet run --project Seto90 -- scene-audit --project MyGame --event event.x --page-index 0
dotnet run --project Seto90 -- balance-audit --project MyGame # stats/economy/progression
dotnet run --project Seto90 -- quality-audit --project MyGame --run-playtests
dotnet run --project Seto90 -- playtest --project MyGame      # headless walkthrough
```

### Evals: your game's own regression suite

The commands above verify the engine and audit your content. An **eval** verifies that *your
game* is still playable: a `playtest.run` script with assertions, saved as `evals/<name>.txt`
in your project. Leave one per finished chapter and the suite grows into a safety net — a
change in chapter 3 that quietly breaks chapter 1 stops being something you find months later.

```powershell
dotnet run --project Seto90 -- evals --project MyGame                     # the whole suite
dotnet run --project Seto90 -- evals --project MyGame --only cap1_intro   # just one
dotnet run --project Seto90 -- evals --project MyGame --out build\evals.json
```

Exit code 1 if any eval fails, so it works as a gate. An eval is just lines:

```text
# Chapter 1: the opening cutscene fires once and sets its flag.
move up
auto
assert-flag flag.intro_done true
assert-map map.gallery
```

`DemoGame/evals/` ships three working examples. The AI can run the suite itself with `evals.run`.

Per-system smokes, all headless and deterministic:

```powershell
# with --project MyGame:
#   dialogue-smoke  event-smoke  battle-smoke  audio-smoke  asset-smoke
#   ui-smoke  save-smoke  shop-smoke  party-smoke
# standalone:
#   font-smoke  map-smoke  vfx-smoke  session-smoke  audit-smoke
#   scene-smoke  balance-smoke  quality-smoke  story-smoke
```

Then the distribution pipeline:

```powershell
dotnet run --project Seto90 -- export-assets --project MyGame
dotnet run --project Seto90 -- pack --project MyGame
dotnet run --project Seto90 -- validate-pack --pack MyGame\build\game.pack
dotnet run --project Seto90 -- run-pack --pack MyGame\build\game.pack --frames 5
```

And the visual side is verified with screenshots, not faith:

```powershell
dotnet run --project Seto90 -- screenshot --project MyGame --out MyGame\build\shot.png
# --event event.x --title --splash --pause --pause-section N --editor --attack
# --map map.x --x 4 --y 6 --crt --frames N
# --event event.x --event-page-index N
# --scrub event.x --scrub-page-index N --scrub-steps N
# --editor-zoom 2|4 (map at 1/2 or 1/4) --editor-tool N (0=TILES..7=LOG)
# (--editor captures export the whole window: canvas + crisp editor UI)
```

## 11. Publishing

```powershell
dotnet run --project Seto90 -- publish --project MyGame
```

Generates `dist/` with a self-contained win-x64 executable (named after the game), `game.pack`
and a readme. Double-click = play (with no arguments the executable looks for the pack beside
it). A Steamworks adapter would be a separate optional module; the core depends on nothing
proprietary.

## 12. Runtime controls (for playtesting)

- Arrows: walk (hold = continuous). Enter/Space: interact / advance dialogue.
- Esc: pause menu. F5/F9: quick save/load (slot 1).
- F1: visual editor (Tab cycles tools: TILES / OBJECTS / EVENTS / WARPS / DIALOGUE / BOOK /
  FLAGS / LOG). In DIALOGUE the node texts are edited in place: click picks the dialogue and node,
  Enter/T edits the text, S the speaker (Enter saves validated, ESC cancels). Inside: H shows the
  active tool's shortcuts; Z zooms the map out (1x / 1/2 / 1/4), V is a clickable minimap, G the
  grid, ESC cancels (selection/paste/choice, and no longer pauses).
  In TILES: F flood fill, Shift+drag selection, Ctrl+C/X/V copy/cut/paste (stamp).
  Basic logic with the mouse: P conditions an object's or event's presence on a flag (always
  exists / if flag=true / if flag=false), D assigns an existing dialogue to an event, N in FLAGS
  creates a new flag. Anything complex (cutscenes, multiple pages) stays with the AI over MCP,
  and the editor says so rather than overwriting it.
  In BOOK: click/arrows navigate chapters and scenes, the status shows which side changed, S
  confirms a deliberate reconciliation and E exports Markdown + DOCX.
- F2: CRT filter. Enter on the boot splash: skip it.

## The golden rule

**Do not turn 90s Engine into a general-purpose engine.** Every new feature must justify why it
serves 90s JRPGs specifically and how it is exposed to the AI over MCP with validation. If a
chapter of your game asks for something the engine does not have, first ask whether it can be
told with what is already there — the great games of the era did that all the time.
