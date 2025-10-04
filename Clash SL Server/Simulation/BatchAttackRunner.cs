using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSS.Core;
using CSS.Logic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UCS.Helpers;
using UCS.Logic;
using UCS.Logic.JSONProperty;
using UCS.Logic.JSONProperty.Item;

namespace UCS.Simulation
{
    internal static class BatchAttackRunner
    {
        internal static bool TryRun(string[] args)
        {
            bool parseResult = BatchAttackOptions.TryParse(args, out BatchAttackOptions options, out string error);

            if (!parseResult)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine($"Simulation setup error: {error}");
                Environment.ExitCode = 1;
                return true;
            }

            Run(options);
            return true;
        }

        private static void Run(BatchAttackOptions options)
        {
            EnsureGameFilesLoaded();

            string baseLayoutJson = File.ReadAllText(options.BaseLayoutPath);
            string attackerLayoutJson = string.IsNullOrWhiteSpace(options.AttackerHomePath)
                ? null
                : File.ReadAllText(options.AttackerHomePath);

            List<List<Battle_Command>> commandSets = LoadCommandSets(options.CommandsPath);

            if (commandSets.Count == 0)
            {
                commandSets.Add(new List<Battle_Command>());
            }

            TextWriter outputWriter = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(options.OutputPath))
                {
                    outputWriter = new StreamWriter(options.OutputPath, append: false);
                }

                for (int attackIndex = 0; attackIndex < options.AttackCount; attackIndex++)
                {
                    List<Battle_Command> commands = commandSets[attackIndex % commandSets.Count];

                    Battle battle = CreateBattle(baseLayoutJson, attackerLayoutJson, options, attackIndex);

                    foreach (Battle_Command command in commands)
                    {
                        battle.Add_Command(command);
                    }

                    battle.Set_Replay_Info();

                    JObject result = BuildResult(battle, attackIndex, commands.Count, options.IncludeBattleState, options.IncludeReplay);

                    string serialized = result.ToString(Formatting.None);

                    if (!options.Silent)
                    {
                        Console.WriteLine(serialized);
                    }

                    outputWriter?.WriteLine(serialized);
                }
            }
            finally
            {
                outputWriter?.Dispose();
            }
        }

        private static Battle CreateBattle(string baseLayoutJson, string attackerLayoutJson, BatchAttackOptions options, int index)
        {
            Level defender = new Level();
            defender.SetHome(baseLayoutJson);

            Level attacker = new Level();

            if (!string.IsNullOrWhiteSpace(attackerLayoutJson))
            {
                attacker.SetHome(attackerLayoutJson);
            }

            Battle battle = new Battle(options.BattleSeed + index, attacker, defender, false)
            {
                Preparation_Time = options.PreparationTime,
                Attack_Time = options.AttackTime
            };

            return battle;
        }

        private static JObject BuildResult(Battle battle, int attackIndex, int commandCount, bool includeBattleState, bool includeReplay)
        {
            JObject result = new JObject
            {
                ["attackIndex"] = attackIndex,
                ["commandCount"] = commandCount,
                ["preparationTimeRemaining"] = battle.Preparation_Time,
                ["attackTimeRemaining"] = battle.Attack_Time,
                ["battleId"] = battle.Battle_ID
            };

            if (includeReplay)
            {
                string replayJson = BattleSerializers.Serialize(battle.Replay_Info);
                result["replay"] = replayJson.Length > 0 ? JToken.Parse(replayJson) : new JObject();
            }

            if (includeBattleState)
            {
                string battleJson = BattleSerializers.Serialize(battle);
                result["battle"] = battleJson.Length > 0 ? JToken.Parse(battleJson) : new JObject();
            }

            return result;
        }

        private static List<List<Battle_Command>> LoadCommandSets(string commandsPath)
        {
            var commandSets = new List<List<Battle_Command>>();

            if (string.IsNullOrWhiteSpace(commandsPath))
            {
                return commandSets;
            }

            string fileContent = File.ReadAllText(commandsPath).Trim();

            if (string.IsNullOrWhiteSpace(fileContent))
            {
                return commandSets;
            }

            if (fileContent.StartsWith("[", StringComparison.Ordinal))
            {
                // Single command set shared across all simulations.
                JArray array = JArray.Parse(fileContent);
                commandSets.Add(ConvertCommands(array));
                return commandSets;
            }

            foreach (string line in File.ReadLines(commandsPath))
            {
                string trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                JArray array = JArray.Parse(trimmed);
                commandSets.Add(ConvertCommands(array));
            }

            return commandSets;
        }

        private static List<Battle_Command> ConvertCommands(JArray array)
        {
            var commands = new List<Battle_Command>(array.Count);

            foreach (JToken token in array)
            {
                Battle_Command command = token.ToObject<Battle_Command>();

                if (command != null)
                {
                    commands.Add(command);
                }
            }

            return commands;
        }

        private static void EnsureGameFilesLoaded()
        {
            if (CSVManager.DataTables == null)
            {
                _ = new CSVManager();
            }
        }
    }
}
