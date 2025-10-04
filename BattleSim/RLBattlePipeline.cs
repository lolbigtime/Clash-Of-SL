using System;
using System.Collections.Generic;
using System.Linq;

namespace ClashOfSL.BattleSim
{
    /// <summary>
    ///     Lightweight orchestration layer for reinforcement learning workflows.
    ///     Provide a battle simulator factory per episode plus a policy that emits
    ///     commands and evaluates the resulting reward.
    /// </summary>
    public sealed class RLBattlePipeline
    {
        public IReadOnlyList<RLEpisodeResult> RunEpisodes(IEnumerable<RLEpisodeSeed> seeds, IRLBattlePolicy policy)
        {
            if (seeds == null)
            {
                return Array.Empty<RLEpisodeResult>();
            }

            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            var results = new List<RLEpisodeResult>();

            foreach (RLEpisodeSeed seed in seeds)
            {
                if (seed == null)
                {
                    continue;
                }

                BattleSimulator simulator = seed.CreateSimulator();
                var context = new BattleContext(simulator.Layout, simulator.Options, seed.SeedCommands ?? Array.Empty<BattleCommand>());

                policy.WarmStart(context, seed.SeedCommands ?? Array.Empty<BattleCommand>());

                IEnumerable<BattleCommand>? generatedCommands = policy.GenerateCommands(context);
                var materializedCommands = generatedCommands?.Where(c => c != null).Select(Clone).ToList() ?? new List<BattleCommand>();

                BattleResult result = simulator.Run(materializedCommands);
                double reward = policy.EvaluateReward(result, context);

                results.Add(new RLEpisodeResult(context, materializedCommands, result, reward));
            }

            return results;
        }

        static BattleCommand Clone(BattleCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return new BattleCommand(command.Tick, command.DataId, command.X, command.Y);
        }
    }

    public sealed class RLEpisodeSeed
    {
        readonly Func<BattleSimulator> simulatorFactory;

        public RLEpisodeSeed(Func<BattleSimulator> simulatorFactory, IEnumerable<BattleCommand>? seedCommands = null)
        {
            this.simulatorFactory = simulatorFactory ?? throw new ArgumentNullException(nameof(simulatorFactory));
            this.SeedCommands = seedCommands?.ToList();
        }

        public IReadOnlyList<BattleCommand>? SeedCommands { get; }

        internal BattleSimulator CreateSimulator()
        {
            return this.simulatorFactory();
        }
    }

    public sealed class RLEpisodeResult
    {
        internal RLEpisodeResult(BattleContext context, IReadOnlyList<BattleCommand> commands, BattleResult result, double reward)
        {
            this.Context = context;
            this.Commands = commands;
            this.Result = result;
            this.Reward = reward;
        }

        public BattleContext Context { get; }
        public IReadOnlyList<BattleCommand> Commands { get; }
        public BattleResult Result { get; }
        public double Reward { get; }
    }

    public sealed class BattleContext
    {
        internal BattleContext(BattleLayout layout, BattleSimulationOptions options, IEnumerable<BattleCommand> seedCommands)
        {
            this.Layout = layout;
            this.Options = options;
            this.SeedCommands = seedCommands?.ToList() ?? new List<BattleCommand>();
        }

        public BattleLayout Layout { get; }
        public BattleSimulationOptions Options { get; }
        public IReadOnlyList<BattleCommand> SeedCommands { get; }
    }

    public interface IRLBattlePolicy
    {
        IEnumerable<BattleCommand> GenerateCommands(BattleContext context);

        double EvaluateReward(BattleResult result, BattleContext context);

        void WarmStart(BattleContext context, IEnumerable<BattleCommand> seedCommands);
    }

    public interface IRLBattleCommandGenerator
    {
        IEnumerable<BattleCommand> Generate(BattleContext context);
    }

    public interface ISeedableBattleCommandGenerator : IRLBattleCommandGenerator
    {
        void Seed(BattleContext context, IEnumerable<BattleCommand> seedCommands);
    }

    public sealed class WeightedRewardPolicy : IRLBattlePolicy
    {
        readonly double destructionWeight;
        readonly double starWeight;
        readonly double timeBonusWeight;
        readonly IRLBattleCommandGenerator commandGenerator;

        public WeightedRewardPolicy(
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

        public IEnumerable<BattleCommand> GenerateCommands(BattleContext context)
        {
            return this.commandGenerator?.Generate(context) ?? Enumerable.Empty<BattleCommand>();
        }

        public double EvaluateReward(BattleResult result, BattleContext context)
        {
            if (result == null)
            {
                return 0;
            }

            double reward = result.Stars * this.starWeight;
            reward += result.DestructionPercentage * this.destructionWeight;
            reward += (180 - result.BattleTime) * this.timeBonusWeight;
            return reward;
        }

        public void WarmStart(BattleContext context, IEnumerable<BattleCommand> seedCommands)
        {
            if (this.commandGenerator is ISeedableBattleCommandGenerator seedable)
            {
                seedable.Seed(context, seedCommands);
            }
        }
    }

    public sealed class ReplayDrivenCommandGenerator : ISeedableBattleCommandGenerator
    {
        readonly List<BattleCommand> replaySeed = new();

        public IEnumerable<BattleCommand> Generate(BattleContext context)
        {
            return this.replaySeed.Select(command => new BattleCommand(command.Tick, command.DataId, command.X, command.Y));
        }

        public void Seed(BattleContext context, IEnumerable<BattleCommand> seedCommands)
        {
            this.replaySeed.Clear();
            if (seedCommands == null)
            {
                return;
            }

            this.replaySeed.AddRange(seedCommands.Select(command => new BattleCommand(command.Tick, command.DataId, command.X, command.Y)));
        }
    }

    public sealed class RandomizedCommandGenerator : IRLBattleCommandGenerator, ISeedableBattleCommandGenerator
    {
        readonly List<BattleCommand> templateCommands = new();
        readonly Random random;

        public RandomizedCommandGenerator(IEnumerable<BattleCommand>? template, int seed = 0)
        {
            if (template != null)
            {
                this.templateCommands.AddRange(template.Select(c => new BattleCommand(c.Tick, c.DataId, c.X, c.Y)));
            }

            this.random = seed == 0 ? new Random() : new Random(seed);
        }

        public IEnumerable<BattleCommand> Generate(BattleContext context)
        {
            if (this.templateCommands.Count == 0)
            {
                return Array.Empty<BattleCommand>();
            }

            return this.templateCommands
                .OrderBy(_ => this.random.NextDouble())
                .Select(command => new BattleCommand(command.Tick, command.DataId, command.X, command.Y))
                .ToList();
        }

        public void Seed(BattleContext context, IEnumerable<BattleCommand> seedCommands)
        {
            this.templateCommands.Clear();
            if (seedCommands != null)
            {
                this.templateCommands.AddRange(seedCommands.Select(c => new BattleCommand(c.Tick, c.DataId, c.X, c.Y)));
            }
        }
    }
}
