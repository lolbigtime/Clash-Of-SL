using System.Collections.Generic;
using System.Linq;
using UCS.Helpers;
using UCS.Logic;
using UCS.Logic.JSONProperty;
using UCS.Logic.JSONProperty.Item;

namespace UCS.Simulation
{
    internal class BatchAttackRunner
    {
        internal IEnumerable<BatchAttackResult> Run(
            IEnumerable<BatchAttackWorkItem> workItems,
            BatchAttackRunnerOptions options = null)
        {
            options ??= BatchAttackRunnerOptions.Default;

            var results = new List<BatchAttackResult>();

            if (workItems == null)
            {
                return results;
            }

            foreach (BatchAttackWorkItem workItem in workItems)
            {
                if (workItem == null || workItem.Battle == null)
                {
                    continue;
                }

                if (options.ResetBattleCommands)
                {
                    workItem.Battle.Commands.Clear();
                }

                if (options.ResetReplayInfo)
                {
                    workItem.Battle.Replay_Info = new Replay_Info();
                }

                foreach (Battle_Command command in workItem.Commands)
                {
                    workItem.Battle.Add_Command(command);
                }

                bool originalLoggingState = workItem.Battle.EnableTickLogging;
                bool toggledLogging = false;

                if (options.SuppressTickLogging && originalLoggingState)
                {
                    workItem.Battle.EnableTickLogging = false;
                    toggledLogging = true;
                }

                try
                {
                    workItem.Battle.EvaluateOutcome();

                    if (options.PopulateReplayInfo)
                    {
                        workItem.Battle.Set_Replay_Info();
                    }

                    string battleJson = options.SerializePayloads
                        ? BattleSerializers.Serialize(workItem.Battle)
                        : null;

                    string replayJson = options.SerializePayloads
                        ? BattleSerializers.Serialize(workItem.Battle.Replay_Info)
                        : null;

                    results.Add(new BatchAttackResult(workItem.Battle, battleJson, replayJson));
                }
                finally
                {
                    if (toggledLogging)
                    {
                        workItem.Battle.EnableTickLogging = originalLoggingState;
                    }
                }
            }

            return results;
        }
    }

    internal class BatchAttackWorkItem
    {
        internal BatchAttackWorkItem(Battle battle, IEnumerable<Battle_Command> commands)
        {
            this.Battle = battle;
            this.Commands = commands?.ToList() ?? new List<Battle_Command>();
        }

        internal Battle Battle { get; }

        internal List<Battle_Command> Commands { get; }
    }

    internal class BatchAttackResult
    {
        internal BatchAttackResult(Battle battle, string battleJson, string replayJson)
        {
            this.Battle = battle;
            this.BattleJson = battleJson;
            this.ReplayJson = replayJson;
        }

        internal Battle Battle { get; }

        internal string BattleJson { get; }

        internal string ReplayJson { get; }

        internal Replay_Info Replay => this.Battle.Replay_Info;
    }

    internal class BatchAttackRunnerOptions
    {
        internal static BatchAttackRunnerOptions Default => new BatchAttackRunnerOptions();

        internal bool ResetBattleCommands { get; set; } = true;

        internal bool ResetReplayInfo { get; set; } = true;

        internal bool PopulateReplayInfo { get; set; } = true;

        internal bool SerializePayloads { get; set; } = true;

        internal bool SuppressTickLogging { get; set; } = true;
    }
}

