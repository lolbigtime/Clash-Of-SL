# Minimal Attack Simulation Guide

This guide shows how to reuse the Clash SL Server battle simulation code without
bringing up the full socket server or any of the database/cache plumbing. You can
load a base layout, apply an attack command sequence, and read the resulting
score entirely in-process.

## Skip the database layer

The battle simulator only needs game balance data (CSV tables) plus attacker and
defender snapshots. You **do not** have to spin up `Resources.Initialize()`, the
`DatabaseManager`, Redis, or any TCP gateways. Instead:

1. Ensure the `Gamefiles` directory from the repository accompanies your
   executable or test harness.
2. Instantiate `new CSVManager()` once at startup. This loads the logic CSVs into
   memory so `Battle` can resolve building/troop hitpoints.
3. Construct `Level` objects and populate them from JSON (see below); the avatar
   data and base layout can come from disk, a fixture, or even hand-authored
   JSON strings.

If you omit these steps you'll see null lookups for hitpoints because the
default database loader never ran. Creating the `CSVManager` up front bypasses
the rest of the server infrastructure while still giving the simulator the game
metadata it needs.

## Core building blocks

* `Battle` encapsulates the attacker/defender state, exposes the replay data,
  and computes destruction/stars when you call `EvaluateOutcome`.
* `BatchAttackRunner` adds helpers for executing one or more battles and
  optionally captures serialized payloads for downstream tooling.
* `RLBattlePipeline` layers a simple policy interface over `BatchAttackRunner`
  so you can plug in scripted or randomized command generators and get scalar
  rewards back.

## Step-by-step workflow

1. **Construct attacker/defender levels.** Call `new Level()` (no IDs or tokens
   required) and load JSON via `level.LoadFromJSON(...)` / `level.SetHome(...)`.
   The JSON blobs can come from files, a database export, or unit tests—the
   `Battle` constructor clones these models so the simulation runs against a
   snapshot.
2. **Create a `Battle` instance.** Pass unique IDs and the `Level` instances to
   `Battle(long battleId, Level attacker, Level defender, bool clone)`.
3. **Queue attack commands.** Build a sequence of `Battle_Command` instances
   (unit placements, hero toggles, etc.) and either call `Battle.Add_Command`
   yourself or hand them to `BatchAttackRunner`.
4. **Run the simulation.** Invoke `Battle.EvaluateOutcome()` directly or wrap
   it in `BatchAttackRunner.Run(...)` / `RLBattlePipeline.RunEpisodes(...)` to
   batch multiple evaluations.
5. **Read the score.** Call `Battle.Set_Replay_Info()` if you ran the battle
   manually, then inspect `Battle.Replay_Info.Stats` for stars, destruction
   percentage, battle time, and trophy delta.

## Example harness

```csharp
using CSS.Core;
using CSS.Logic;
using UCS.Logic;

// Minimal bootstrap – loads Gamefiles/logic/*.csv for hitpoint lookups.
var csv = new CSVManager();

Level attacker = new Level();
attacker.LoadFromJSON(attackerJson);

Level defender = new Level();
defender.SetHome(defenderJson);

Battle battle = new Battle(1, attacker, defender);
IEnumerable<Battle_Command> commands = BuildAttackPlan();

var runner = new BatchAttackRunner();
var workItem = new BatchAttackWorkItem(battle, commands);
BatchAttackResult result = runner.Run(new[] { workItem }).Single();

Replay_Stats stats = result.Replay.Stats;
Console.WriteLine($"Stars: {stats.Attacker_Stars}, Destruction: {stats.Destruction_Percentate}%");
```

Swap in `RLBattlePipeline` with a custom `IRLBattlePolicy` if you want the
framework to generate the commands or compute rewards automatically.

### Reinforcement-learning quickstart

`RLBattlePipeline` already wraps `BatchAttackRunner` and exposes a
policy-centric API so you can plug battle simulations into an RL loop with a
handful of lines:

```csharp
// 1. Prime the logic tables once per process.
var csv = new CSVManager();

// 2. Prepare a factory that creates fresh Battle instances per episode.
Battle CreateEpisode(int id) => new Battle(id, attackerLevel, defenderLevel);

// 3. Pick or implement an IRLBattlePolicy.
IRLBattleCommandGenerator generator = new RandomizedCommandGenerator(seedCommands, seed: 42);
IRLBattlePolicy policy = new WeightedRewardPolicy(generator);

// 4. Hand battles to the pipeline and iterate the results.
var pipeline = new RLBattlePipeline();
IEnumerable<Battle> episodes = Enumerable.Range(0, 32).Select(i => CreateEpisode(i + 1));

foreach (RLEpisodeResult episode in pipeline.RunEpisodes(episodes, policy))
{
    Console.WriteLine($"Reward: {episode.Reward}, Stars: {episode.Simulation.Replay.Stats.Attacker_Stars}");
}
```

> Add `using System.Linq;` plus the relevant `using UCS.Simulation;` and
> `using UCS.Logic.JSONProperty.Item;` directives at the top of your harness so
> the snippet compiles as-is.

Key entry points:

* **`RandomizedCommandGenerator`** – clones a command template, jitters drop
  timing, and keeps `IRLBattlePolicy.GenerateCommands` trivial for stochastic
  baselines. You can swap in your own generator for policy-gradient agents or
  scripted experiments.
* **`WeightedRewardPolicy`** – provides a ready-to-use scoring function that
  heavily weights stars, adds linear destruction credit, and slightly rewards
  faster clears. Implement `IRLBattlePolicy` yourself if you prefer advantage
  signals, curriculum shaping, etc.
* **`RLBattlePipeline.Demo`** – accepts an `IEnumerable<RLEpisodeSeed>` (each seed
  wraps a `Battle` plus optional `IEnumerable<Battle_Command>`), primes the
  policy via `WarmStart`, and runs the episodes. It’s a convenient entry point
  when you want to bootstrap from existing replay commands without writing
  plumbing code.

Because the pipeline resets command/replay state for every work item, you can
reuse attacker/defender snapshots (or even memoized `Level` objects) when
creating new `Battle` instances, then log the returned `RLEpisodeResult` objects
(battle snapshot, command list, replay stats, reward) straight into your
training loop.
