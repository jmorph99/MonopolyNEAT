# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

C# / .NET 5 console application that trains neural networks to play 4-player Monopoly via NEAT (NeuroEvolution of Augmenting Topologies). It is a single project (`Monopoly/Monopoly.csproj`) — there are no tests, no scripts, and no external dependencies beyond the SDK.

## Build & Run

```bash
dotnet build Monopoly.sln                 # build (Debug)
dotnet build -c Release Monopoly.sln      # build Release
dotnet run --project Monopoly             # run training loop
```

The program runs 1000 tournament generations (`Program.cs:38`) and writes the population to disk after every generation. There is no CLI — to start fresh, point at an empty file; to resume, point at a populated one.

### Required setup before running

`Monopoly/Program.cs:14` hard-codes a Windows save path (`C:\Users\Brad\Desktop\monopoly_population.txt`). It **must** be edited before the program will run on this machine. Three starter checkpoints sit in the repo root:

- `monopoly_population 162.txt` — gen 162 (well-trained)
- `monopoly_population champions.txt` — gen ~60 (weak but non-suicidal)
- `monopoly_population.txt` — empty; starts at gen 1

Rename the desired one to match the path string (or change the path).

### CPU load

Training is intentionally heavyweight. `Tournament.cs:15-16` defines `WORKERS = 20` and `BATCH_SIZE = 20` (20 threads, each playing 20 games per batch). Lower both to ~3–5 to avoid pegging the CPU; the rest of the code is unaffected.

## Architecture

### Two cooperating namespaces

- **`NEAT`** (`Monopoly/NeuroEvolution/`) — a self-contained NEAT implementation: `Genotype` (vertices + edges with innovation numbers), `Phenotype` (executable graph, propagated via `Propagate(float[])`), `Mutation`, `Crossover`, `Population` (speciation by `SpeciationDistance`, fitness sharing within species), `NetworkFactory`. Nothing here knows about Monopoly.
- **`MONOPOLY`** (`Monopoly/Monopoly/`) — the game simulator: `Board` (51 KB, all rules), `Player` (base, scripted defaults), `NeuralPlayer` (overrides each decision by calling `network.Propagate(adapter.pack)` and thresholding outputs `Y[0..8]`).
- **`Monopoly`** (top-level, `Monopoly/`) — glue: `Program`, `Tournament`, `Analytics`, `RNG`, `NetworkAdapter`.

The boundary between the NEAT library and the game is `NetworkAdapter` plus `NeuralPlayer`. Changing the input/output shape requires coordinated edits in all three of: `NetworkAdapter` (layout of `pack[]`), `Tournament.Initialise` (`INPUTS = 126`, `OUTPUTS = 9`), and `NeuralPlayer` (which `Y[i]` drives which decision).

### Singleton + Initialise() pattern

Every major class is a singleton accessed as `ClassName.instance`, constructed via a static `Initialise()` in `Program.Main`. Order matters and is set in `Program.cs:16-22` (RNG → NetworkFactory → Mutation → Crossover → Population). Adding new global state should follow the same pattern or it will not be visible inside worker threads.

### How a generation runs

1. `Tournament.ExecuteTournament` (`Tournament.cs:38`) takes the 256 phenotypes from `Population.instance.population`, brackets them in groups of 4, and runs rounds until one finalist remains.
2. Each bracket plays `ROUND_SIZE = 2000` games (`Tournament.cs:13`). Games are dispatched in waves of `WORKERS × BATCH_SIZE` via raw `Thread`. Per-game state lives in fresh `Board` + `NetworkAdapter` instances; only `network.score` and `Analytics.instance.wins` are written back under `lock`.
3. Bracket winners advance; losers' phenotype slots are nulled and compacted. Bracket depth (`bracket` count) becomes the genome's fitness via `championScore + diff * 5` (`Tournament.cs:69`).
4. `Population.NewGeneration` (`Population.cs:172`) does standard NEAT: adjust fitness by species size, cull to top `PORTION = 0.2`, drop species stale for `MAX_STALENESS = 15`, breed proportional to species fitness sum, re-speciate, increment `GENERATION`.

### Network I/O contract (126 → 9)

`NetworkAdapter.pack[]` is a fixed-layout `float[127]` (sized one larger than the 126 inputs — index 126 is `select_money` context). Offsets are class fields on `NetworkAdapter`: `turn=0, pos=4, mon=8, card=12, jail=16, own=20, mort=48, house=76, select=98, select_money=126`. The `PROPS` and `HOUSES` arrays map 40 board tiles to compact property/house indices (`-1` for non-property tiles). The 9 outputs map 1:1 to `NeuralPlayer`'s decision methods (buy, jail, mortgage, advance, auctionBid, buildHouse, sellHouse, offerTrade, acceptTrade); see `NeuralPlayer.cs` for the thresholds. `Phenotype.Propagate` is a fixed-iteration relaxation (10 passes, sigmoid activation) — it is not a topological evaluation, so recurrent/cyclic edges are allowed.

### Persistence format

`Program.SaveState` / `LoadState` write a custom-delimited blob — not JSON, not XML. The hierarchy: `;` separates top-level sections (generation; championScore; historical innovation markings; networks). Inside the networks block, `&` alternates between species headers and member lists, `n` separates members within a species, `#` separates a member's vertex list from its edge list, and `,` separates fields. Editing any of `SaveState`, `LoadState`, `Genotype` fields, or `Mutation.Marking` requires syncing both sides.

### Threading caveats

- `Tournament.PlayGameThread` reads `instance.contestants[i..i+3]` and assigns the same `Phenotype` objects to multiple concurrent boards. `Phenotype` itself is not thread-safe — vertex `value` fields are mutated during `Propagate`. The implementation gets away with this because `Population.InscribePopulation` is called between generations (single-threaded) and individual `Propagate` calls happen to complete fast enough that races on `value` mostly cancel out. Treat this as load-bearing accidental behavior; do not refactor `Phenotype.Propagate` to assume single-writer semantics without fixing it.
- Score writes are locked on `network` itself; `Analytics.wins` on `Analytics.instance.wins`. Any new shared mutable state needs its own lock.
