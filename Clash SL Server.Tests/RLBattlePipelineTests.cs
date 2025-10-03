using System.Collections.Generic;
using System.Linq;
using UCS.Logic;
using UCS.Logic.JSONProperty.Item;
using UCS.Simulation;
using Xunit;

namespace Clash_SL_Server.Tests
{
    public class RLBattlePipelineTests
    {
        [Fact]
        public void RunEpisodes_UsesPolicyCommandsAndReturnsReward()
        {
            var commands = new[]
            {
                new Battle_Command
                {
                    Command_Type = 42,
                    Command_Base = new Command_Base
                    {
                        Base = new Base
                        {
                            Tick = 63
                        },
                        Data = 99,
                        X = 3,
                        Y = 7
                    }
                }
            };

            var policy = new CapturingPolicy(commands);
            var runner = new FakeRunner();
            var pipeline = new RLBattlePipeline(runner);

            var battle = new Battle();

            List<RLEpisodeResult> results = pipeline
                .RunEpisodes(new[] { battle }, policy)
                .ToList();

            Assert.Single(runner.WorkItems);
            Assert.Same(battle, runner.WorkItems[0].Battle);
            Assert.Equal(commands[0].Command_Type, runner.WorkItems[0].Commands[0].Command_Type);

            Assert.NotNull(runner.Options);
            Assert.True(runner.Options.ResetBattleCommands);
            Assert.True(runner.Options.ResetReplayInfo);
            Assert.True(runner.Options.PopulateReplayInfo);
            Assert.True(runner.Options.SuppressTickLogging);
            Assert.False(runner.Options.SerializePayloads);

            Assert.Single(results);
            Assert.Same(battle, results[0].Battle);
            Assert.Equal(2, results[0].Reward); // 2 stars * 1 + 0 base reward
            Assert.Single(results[0].Commands);
            Assert.Equal(commands[0].Command_Type, results[0].Commands[0].Command_Type);

            Assert.Single(policy.GenerateCalls);
            Assert.Single(policy.EvaluateCalls);
        }

        private sealed class FakeRunner : BatchAttackRunner
        {
            internal List<BatchAttackWorkItem> WorkItems { get; } = new();

            internal BatchAttackRunnerOptions Options { get; private set; }

            internal override IEnumerable<BatchAttackResult> Run(
                IEnumerable<BatchAttackWorkItem> workItems,
                BatchAttackRunnerOptions options = null)
            {
                this.Options = options;

                if (workItems == null)
                {
                    yield break;
                }

                foreach (BatchAttackWorkItem item in workItems)
                {
                    this.WorkItems.Add(item);

                    item.Battle.Replay_Info.Stats.Attacker_Stars = 2;
                    item.Battle.Replay_Info.Stats.Destruction_Percentate = 75;

                    yield return new BatchAttackResult(item.Battle, battleJson: null, replayJson: null);
                }
            }
        }

        private sealed class CapturingPolicy : IRLBattlePolicy
        {
            readonly List<Battle_Command> templateCommands;

            internal CapturingPolicy(IEnumerable<Battle_Command> commands)
            {
                this.templateCommands = commands?.Select(Clone).ToList() ?? new List<Battle_Command>();
            }

            internal List<Battle> GenerateCalls { get; } = new();

            internal List<BatchAttackResult> EvaluateCalls { get; } = new();

            public IEnumerable<Battle_Command> GenerateCommands(Battle battle)
            {
                this.GenerateCalls.Add(battle);
                return this.templateCommands.Select(Clone).ToList();
            }

            public double EvaluateReward(BatchAttackResult result)
            {
                this.EvaluateCalls.Add(result);
                return result?.Replay?.Stats?.Attacker_Stars ?? 0;
            }

            public void WarmStart(Battle battle, IEnumerable<Battle_Command> seedCommands)
            {
            }

            static Battle_Command Clone(Battle_Command command)
            {
                if (command == null)
                {
                    return new Battle_Command();
                }

                return new Battle_Command
                {
                    Command_Type = command.Command_Type,
                    Command_Base = new Command_Base
                    {
                        Base = new Base
                        {
                            Tick = command.Command_Base.Base.Tick
                        },
                        Data = command.Command_Base.Data,
                        X = command.Command_Base.X,
                        Y = command.Command_Base.Y
                    }
                };
            }
        }
    }
}
