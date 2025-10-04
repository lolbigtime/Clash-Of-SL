using System;
using System.Collections.Generic;
using System.Linq;
using UCS.Logic;
using UCS.Logic.JSONProperty.Item;

namespace UCS.Simulation
{
    /// <summary>
    ///     Thin orchestration layer that ties the batch simulation helper into a
    ///     reinforcement-learning friendly loop. The pipeline generates commands for
    ///     each episode via a policy, executes the battle server-side, and reports
    ///     scalar rewards plus optional JSON payloads for logging.
    /// </summary>
    internal class RLBattlePipeline
    {
        readonly BatchAttackRunner runner;
        readonly BatchAttackRunnerOptions runnerOptions;

        internal RLBattlePipeline(BatchAttackRunner runner = null, BatchAttackRunnerOptions options = null)
        {
            this.runner = runner ?? new BatchAttackRunner();
            this.runnerOptions = options ?? new BatchAttackRunnerOptions
            {
                SerializePayloads = false,
                PopulateReplayInfo = true,
                SuppressTickLogging = true,
                ResetBattleCommands = true,
                ResetReplayInfo = true
            };
        }

        internal IEnumerable<RLEpisodeResult> RunEpisodes(IEnumerable<Battle> battles, IRLBattlePolicy policy)
        {
            if (battles == null)
            {
                return Enumerable.Empty<RLEpisodeResult>();
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            List<BatchAttackWorkItem> workItems = new List<BatchAttackWorkItem>();
            List<RLEpisodeContext> contexts = new List<RLEpisodeContext>();

            foreach (Battle battle in battles)
            {
                if (battle == null)
                {
                    continue;
                }

                IEnumerable<Battle_Command> commands = policy.GenerateCommands(battle);
                var commandList = commands?.ToList() ?? new List<Battle_Command>();

                workItems.Add(new BatchAttackWorkItem(battle, commandList));
                contexts.Add(new RLEpisodeContext(battle, commandList));
            }

            List<BatchAttackResult> simulationResults = this.runner.Run(workItems, this.runnerOptions).ToList();

            var episodeResults = new List<RLEpisodeResult>(simulationResults.Count);

            for (int i = 0; i < simulationResults.Count; i++)
            {
                BatchAttackResult simulation = simulationResults[i];
                RLEpisodeContext context = contexts[i];

                double reward = policy.EvaluateReward(simulation);

                episodeResults.Add(new RLEpisodeResult(
                    context.Battle,
                    context.Commands,
                    simulation,
                    reward));
            }

            return episodeResults;
        }

        internal static IEnumerable<RLEpisodeResult> Demo(IRLBattlePolicy policy, IEnumerable<RLEpisodeSeed> episodes)
        {
            if (episodes == null)
            {
                return Enumerable.Empty<RLEpisodeResult>();
            }

            var pipeline = new RLBattlePipeline();
            var battles = new List<Battle>();

            foreach (RLEpisodeSeed episode in episodes)
            {
                if (episode == null || episode.Battle == null)
                {
                    continue;
                }

                IEnumerable<Battle_Command> candidateCommands = episode.SeedCommands ?? Enumerable.Empty<Battle_Command>();
                policy.WarmStart(episode.Battle, candidateCommands);
                battles.Add(episode.Battle);
            }

            return pipeline.RunEpisodes(battles, policy);
        }

        class RLEpisodeContext
        {
            internal RLEpisodeContext(Battle battle, List<Battle_Command> commands)
            {
                this.Battle = battle;
                this.Commands = commands;
            }

            internal Battle Battle { get; }

            internal List<Battle_Command> Commands { get; }
        }
    }

    internal interface IRLBattlePolicy
    {
        IEnumerable<Battle_Command> GenerateCommands(Battle battle);

        double EvaluateReward(BatchAttackResult result);

        void WarmStart(Battle battle, IEnumerable<Battle_Command> seedCommands);
    }

    /// <summary>
    ///     Example reward function that targets high destruction and quick finishes.
    ///     Stars are heavily weighted, destruction percent acts as tie-breaker, and
    ///     faster clears earn a small bonus to encourage efficient policies.
    /// </summary>
    internal class WeightedRewardPolicy : IRLBattlePolicy
    {
        readonly double destructionWeight;
        readonly double starWeight;
        readonly double timeBonusWeight;
        readonly IRLBattleCommandGenerator commandGenerator;

        internal WeightedRewardPolicy(
            IRLBattleCommandGenerator commandGenerator,
            double starWeight = 10,
            double destructionWeight = 1,
            double timeBonusWeight = 0.05)
        {
            this.commandGenerator = commandGenerator;
            this.starWeight = starWeight;
            this.destructionWeight = destructionWeight;
            this.timeBonusWeight = timeBonusWeight;
        }

        public IEnumerable<Battle_Command> GenerateCommands(Battle battle)
        {
            return this.commandGenerator?.Generate(battle) ?? Enumerable.Empty<Battle_Command>();
        }

        public double EvaluateReward(BatchAttackResult result)
        {
            if (result?.Replay?.Stats == null)
            {
                return 0;
            }

            double reward = result.Replay.Stats.Attacker_Stars * this.starWeight;
            reward += result.Replay.Stats.Destruction_Percentate * this.destructionWeight;
            reward += (180 - result.Replay.Stats.Battle_Time) * this.timeBonusWeight;

            return reward;
        }

        public void WarmStart(Battle battle, IEnumerable<Battle_Command> seedCommands)
        {
            ISeedableBattleCommandGenerator seedable = this.commandGenerator as ISeedableBattleCommandGenerator;

            if (seedable != null && battle != null)
            {
                seedable.Seed(battle, seedCommands);
            }
        }
    }

    internal interface IRLBattleCommandGenerator
    {
        IEnumerable<Battle_Command> Generate(Battle battle);
    }

    internal interface ISeedableBattleCommandGenerator : IRLBattleCommandGenerator
    {
        void Seed(Battle battle, IEnumerable<Battle_Command> seedCommands);
    }

    internal class ReplayDrivenCommandGenerator : ISeedableBattleCommandGenerator
    {
        List<Battle_Command> replaySeed = new List<Battle_Command>();

        public IEnumerable<Battle_Command> Generate(Battle battle)
        {
            return this.replaySeed;
        }

        public void Seed(Battle battle, IEnumerable<Battle_Command> seedCommands)
        {
            this.replaySeed = seedCommands?.ToList() ?? new List<Battle_Command>();
        }
    }

    internal class RandomizedCommandGenerator : IRLBattleCommandGenerator
    {
        readonly Random random;
        readonly List<Battle_Command> template;

        internal RandomizedCommandGenerator(IEnumerable<Battle_Command> template, int? seed = null)
        {
            this.template = template?.ToList() ?? new List<Battle_Command>();
            this.random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public IEnumerable<Battle_Command> Generate(Battle battle)
        {
            if (battle == null || this.template.Count == 0)
            {
                return this.template;
            }

            return this.template
                .OrderBy(_ => this.random.Next())
                .Select((command, index) =>
                {
                    Battle_Command clone = CloneCommand(command);
                    clone.Command_Base.Base.Tick += index * 63;
                    return clone;
                })
                .ToList();
        }

        static Battle_Command CloneCommand(Battle_Command command)
        {
            if (command == null)
            {
                return new Battle_Command();
            }

            Command_Base baseCommand = command.Command_Base ?? new Command_Base();
            Base baseData = baseCommand.Base ?? new Base();

            return new Battle_Command
            {
                Command_Type = command.Command_Type,
                Command_Base = new Command_Base
                {
                    Base = new Base
                    {
                        Tick = baseData.Tick
                    },
                    Data = baseCommand.Data,
                    X = baseCommand.X,
                    Y = baseCommand.Y
                }
            };
        }
    }

    internal class RLEpisodeResult
    {
        internal RLEpisodeResult(
            Battle battle,
            IEnumerable<Battle_Command> commands,
            BatchAttackResult simulation,
            double reward)
        {
            this.Battle = battle;
            this.Commands = commands?.ToList() ?? new List<Battle_Command>();
            this.Simulation = simulation;
            this.Reward = reward;
        }

        internal Battle Battle { get; }

        internal List<Battle_Command> Commands { get; }

        internal BatchAttackResult Simulation { get; }

        internal double Reward { get; }
    }

    internal class RLEpisodeSeed
    {
        internal RLEpisodeSeed(Battle battle, IEnumerable<Battle_Command> seedCommands)
        {
            this.Battle = battle;
            this.SeedCommands = seedCommands;
        }

        internal Battle Battle { get; }

        internal IEnumerable<Battle_Command> SeedCommands { get; }
    }
}
