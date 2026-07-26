# Orchestration: how a human and an AI author the same game

Most tools that let an AI "help" with a game do it by letting the model write files. That
works until the model and the person are working at the same time, and then it stops working:
two writers, two mental models, one file, and whoever saves last wins.

90s Engine takes the opposite approach. There is exactly one way to change a project — a
single command layer — and both authors are clients of it. The human painting a tile with the
mouse and the AI calling `map.paint_rect` are not two systems that need reconciling. They are
the same call.

This document explains that machinery. If you just want to build a game, read
[AUTHORING.md](AUTHORING.md) instead.

---

## 1. One command layer, two front ends

`Core/CommandSession.cs` is the only thing in the engine that can modify a project. Every
write goes through `Mutate`, whether it originated from:

- the **MCP server** (`Mcp/McpServer.cs`) — an AI calling one of 57 atomic tools over stdio,
- the **in-game editor** (F1, `Runtime/Editor/EditorMode.cs`) — a human with a mouse,
- or the **CLI** (`Program.cs`).

```
       AI over MCP  ─┐
   Human in F1 editor ├─→  CommandSession.Mutate  →  validate  →  save  →  project.json
             CLI    ─┘            (snapshot)         (global)    (atomic)
```

### The server introduces itself

An agent that connects gets more than a tool list. The `initialize` response carries the MCP
`instructions` field, so before its first call the agent already knows to orient with
`query.content_graph`, that a project still holding only the starter content has **not been
designed yet** and it should ask the author for premise, tone, core loop and chapters (and
whether there is an existing text to adapt with `story.import`) rather than start building,
that `project.json` is never hand-edited, that `ok` must be checked on every response, and what
the build → see → play loop is. Onboarding is part of the protocol handshake, not something a
human has to paste in.

The MCP server is otherwise *only transport*. It parses JSON-RPC and hands off; it holds no
state and knows no rules. That is why a feature added for the AI is automatically available to the human,
and vice versa — there is no second implementation to keep in sync.

This is the part that older engines got wrong, and it was not a small bug: the team's internal
editor and the shipping game read different structures, and letting them drift was a classic
production failure. Here it cannot happen structurally.

## 2. Every write is a transaction

`Mutate` is not "apply the change". It is:

1. **Snapshot** the project (`TransactionLog.BeforeChange`).
2. **Apply** the edit. If it throws → roll back, return a structured error.
3. **Validate the entire project** — not the edited entity, the whole graph.
4. If validation fails → **roll back** and return the first issue.
5. **Save atomically** (temp file → checksum → commit, with a `.bak` kept). If the write
   fails → roll back.

So a tool call that would leave a dangling reference does not leave a broken project behind for
someone to discover later while playing. It leaves *nothing* behind. This is the single most
important guarantee the engine makes to an AI author: you cannot half-break the game.

Errors come back structured, never as prose to be parsed:

```json
{ "code": "missing_vfx_anchor",
  "message": "event.gate anchors the vfx to event event.lantern.",
  "fix": "Create the event, or use '' / 'player' for the player." }
```

`code` is stable and machine-readable, `fix` tells the caller what to do next. An agent can act
on a failure without guessing.

## 3. Batches: many writes, one validation, one undo

Building a scene is not one write — it is a map, a tileset, three events, two dialogues and a
battle, and several of them reference each other. Validating after each step would reject
legitimate intermediate states where a reference points at something not created *yet*.

`batch.apply` opens a batch: writes apply in memory only, validation and save happen **once**
at commit, and the whole thing is all-or-nothing and lands as a **single** undo entry. Forward
references inside a batch are allowed. Nesting is not.

`batch.preview` is the same machinery pointed at a clone: it reports the semantic diff a batch
*would* produce — no disk write, no undo entry, no history note, no revision bump. Look before
you leap, without polluting anything.

## 4. The revision guard — the part that makes simultaneous authoring safe

Two co-authors in the same project at the same time is a write race, and the naive version of
this system loses data in a way that is very hard to diagnose: a session holds an in-memory
copy, someone else writes to disk, and the next write from the stale session saves its whole
copy over the top. Nothing errors. The other author's work is simply gone.

The fix is a monotonic counter. `ProjectStore.Save` increments `GameProject.Revision` on every
write, and before any mutation `RebaseIfStale()` compares the session's revision against the
one on disk:

```csharp
void RebaseIfStale()
{
    if (store.PeekRevision() <= project.Revision) return;
    tx.BeforeChange(project);
    project = store.LoadOrCreate();          // adopt the newer disk state...
    Note("ext", UiStrings.ExternalChangeRebased);   // "External change adopted before writing"
}                                             // ...then apply this edit on top of it
```

The adopted state is itself recorded as an undo step, so nothing becomes invisible.

Undo and redo need stronger treatment, because they write *complete* snapshots and would
therefore clobber a concurrent change wholesale. When the disk is ahead, they refuse with
`external_change` and tell the caller to adopt first. Refusing is the right answer here —
silently winning is what loses work.

`session-smoke` verifies all of this headless with two live sessions over one directory.

## 5. Hot reload closes the loop

The running game watches `project.json`. When the AI writes something, the game adopts it
in place — preserving position, flags, inventory and party — provided it validates. The editor
adopts it into the same session, so `Ctrl+Z` undoes the AI's change and the human's change in
one shared timeline, in the exact order they happened.

Two honest limits, both deliberate:

- Reload only happens in the **world** state. A write that arrives during a dialogue, battle or
  menu is adopted when the player returns to the map — reloading mid-battle would mean
  reasoning about a mutated actor in a live turn queue.
- The session ignores the echo of its own writes (`Matches` compares snapshots), so saving from
  the editor does not trigger a spurious reload.

## 6. The co-authoring log

The editor's LOG tool shows one timeline of the session: `[you]` for the human's operations,
`[ai]` for the AI's, newest first — the same order `Ctrl+Z` walks backwards. Human entries are
short and targeted ("paints 14 tiles in gallery", "moves npc_2 to (5,7)") and are recorded only on
success; failures surface in the UI, not in the history.

Known limit, stated plainly: across processes the game still shows the generic
`[ai] External change adopted` from hot reload rather than the tool-level note. A shared
sidecar log is a declared pending item, not a solved problem.

## 7. Build → see → play

An AI that cannot perceive its own output is guessing. Three tools close that gap, and they are
the reason an agent can work unattended for long stretches:

| Step | Tool | What it gives back |
|---|---|---|
| **Build** | the 57 MCP tools | validated content, or a structured refusal |
| **See** | `playtest.screenshot` | a PNG of the actual game — any map, event, menu section, battle, editor tool, or a cutscene scrubbed to an exact step |
| **Play** | `playtest.run` | a scripted headless session and a JSON report of every step |

`playtest.run` does not simulate keyboard hardware. It injects synthetic input that the
dialogue, battle and ceremony states read alongside real input, and it walks with the same
`GridMover` used by cutscenes — so a blocked step is skipped and stepping onto a trigger
interrupts the walk and says so. Steps include `move`, `face`, `interact`, `auto` (confirm
until the world is free again — this plays entire battles), `choose N`, `wait`, `goto`, and
assertions: `assert-flag`, `map`, `item`, `pos`, `money`. A game over aborts the script and the
report says why, which is usually exactly what you wanted to learn.

### Evals: the loop leaves something behind

A playtest that runs once and disappears verifies a moment. Saved as `evals/<name>.txt` in the
project, the same script becomes **regression**: `evals.run` replays the whole suite and returns
one verdict, so a change in chapter 3 that quietly breaks chapter 1 announces itself instead of
being found months later.

This is worth separating clearly from the engine's own tests. The **smokes** prove the *engine*
works — combat math, dialogue paging, saves, the revision guard. An **eval** proves *this game*
is still playable. They fail for completely different reasons and both matter: an AI author that
can only run the engine's tests has no idea whether it broke the story it is writing.

The reports are asymmetric on purpose: a passing suite is a few lines, a failing one returns the
first failing step and the final world state — enough to fix it without running anything again.

## 8. Determinism is what makes verification meaningful

There is **no RNG anywhere in the engine**. Battles resolve by turn order and formula. Fleeing
succeeds if your top speed beats theirs. Status effects tick on a fixed schedule. Particle
"randomness" in VFX is a hash of the particle index, so the same effect renders identically on
any machine and can be captured at an exact phase.

The consequence is the point: the same script produces the same result, every time. An agent's
verification is evidence rather than a sample, a failing playtest is a real regression rather
than bad luck, and a screenshot taken today can be compared against one taken next month.

## 9. Reading without writing

An agent should not have to parse `project.json` off disk to know what exists — that is how
formats drift and how a tool ends up reimplementing the model. Three read tools cover it:

- `query.content_graph` — what exists and what references what.
- `query.map` — a region of tiles, plus warps and events with their pages and conditions.
- `query.entity` — a generic GET, symmetric with `content.delete`, covering 17 kinds plus
  `project`, returning exactly the shape `project.json` uses.

Read tools are rejected inside `batch.apply` with `not_batchable`: a batch is a unit of
*writing*, and letting a read observe an uncommitted intermediate state would expose exactly
the half-applied world the transaction exists to hide.

## 10. What this buys you

- An AI can author a real game without touching a single file by hand.
- A human can work in the same project at the same time without a merge step.
- One `Ctrl+Z` walks back through both authors' work in true chronological order.
- A broken reference is impossible to commit, not merely discouraged.
- Every claim an agent makes about the game can be checked by rendering it or playing it.

---

See [ARCHITECTURE.md](ARCHITECTURE.md) for the engine's technical design and the Mirror Book,
and [AUTHORING.md](AUTHORING.md) for the practical guide to building a game.
