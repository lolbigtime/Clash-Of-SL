using System;
using System.Globalization;
using System.IO;

namespace CSS.Simulation
{
    internal sealed class BatchAttackOptions
    {
        private BatchAttackOptions(
            string baseLayoutPath,
            string commandsPath,
            string attackerHomePath,
            string outputPath,
            int attackCount,
            double preparationTime,
            double attackTime,
            bool includeBattleState,
            bool includeReplay,
            bool silent,
            long battleSeed)
        {
            BaseLayoutPath = baseLayoutPath;
            CommandsPath = commandsPath;
            AttackerHomePath = attackerHomePath;
            OutputPath = outputPath;
            AttackCount = attackCount;
            PreparationTime = preparationTime;
            AttackTime = attackTime;
            IncludeBattleState = includeBattleState;
            IncludeReplay = includeReplay;
            Silent = silent;
            BattleSeed = battleSeed;
        }

        internal string BaseLayoutPath { get; }

        internal string CommandsPath { get; }

        internal string AttackerHomePath { get; }

        internal string OutputPath { get; }

        internal int AttackCount { get; }

        internal double PreparationTime { get; }

        internal double AttackTime { get; }

        internal bool IncludeBattleState { get; }

        internal bool IncludeReplay { get; }

        internal bool Silent { get; }

        internal long BattleSeed { get; }

        internal static bool TryParse(string[] args, out BatchAttackOptions options, out string error)
        {
            options = null;
            error = null;

            if (args == null || args.Length == 0)
            {
                return false;
            }

            bool requested = false;

            string basePath = null;
            string commandsPath = null;
            string attackerHomePath = null;
            string outputPath = null;
            int attackCount = 1;
            double preparationTime = 0;
            double attackTime = 180;
            bool includeBattleState = false;
            bool includeReplay = true;
            bool silent = false;
            long battleSeed = DateTime.UtcNow.Ticks;

            for (int i = 0; i < args.Length; i++)
            {
                string current = args[i];

                switch (current)
                {
                    case "--simulate-batch":
                        requested = true;
                        break;

                    case "--base":
                        if (!TryReadValue(args, ref i, out basePath, out error))
                        {
                            return requested;
                        }

                        break;

                    case "--commands":
                        if (!TryReadValue(args, ref i, out commandsPath, out error))
                        {
                            return requested;
                        }

                        break;

                    case "--attacker-home":
                        if (!TryReadValue(args, ref i, out attackerHomePath, out error))
                        {
                            return requested;
                        }

                        break;

                    case "--output":
                        if (!TryReadValue(args, ref i, out outputPath, out error))
                        {
                            return requested;
                        }

                        break;

                    case "--attacks":
                    case "--repeat":
                        {
                            if (!TryReadValue(args, ref i, out string value, out error))
                            {
                                return requested;
                            }

                            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out attackCount) ||
                                attackCount <= 0)
                            {
                                error = "--attacks must be a positive integer.";
                                return requested;
                            }

                            break;
                        }

                    case "--prep":
                    case "--preparation":
                        {
                            if (!TryReadValue(args, ref i, out string value, out error))
                            {
                                return requested;
                            }

                            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out preparationTime) ||
                                preparationTime < 0)
                            {
                                error = "--prep must be zero or a positive number.";
                                return requested;
                            }

                            break;
                        }

                    case "--attack-time":
                    case "--attack":
                        {
                            if (!TryReadValue(args, ref i, out string value, out error))
                            {
                                return requested;
                            }

                            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out attackTime) ||
                                attackTime <= 0)
                            {
                                error = "--attack must be a positive number.";
                                return requested;
                            }

                            break;
                        }

                    case "--include-battle":
                        includeBattleState = true;
                        break;

                    case "--no-replay":
                        includeReplay = false;
                        break;

                    case "--silent":
                    case "--quiet":
                        silent = true;
                        break;

                    case "--seed":
                        if (!TryReadValue(args, ref i, out string seedValue, out error))
                        {
                            return requested;
                        }

                        if (!long.TryParse(seedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out battleSeed))
                        {
                            error = "--seed must be a valid integer.";
                            return requested;
                        }

                        break;

                    default:
                        break;
                }
            }

            if (!requested)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(basePath))
            {
                error = "--base is required when using --simulate-batch.";
                return true;
            }

            basePath = Path.GetFullPath(basePath);

            if (!File.Exists(basePath))
            {
                error = $"Base layout file not found: {basePath}";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(commandsPath))
            {
                commandsPath = Path.GetFullPath(commandsPath);

                if (!File.Exists(commandsPath))
                {
                    error = $"Commands file not found: {commandsPath}";
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(attackerHomePath))
            {
                attackerHomePath = Path.GetFullPath(attackerHomePath);

                if (!File.Exists(attackerHomePath))
                {
                    error = $"Attacker home file not found: {attackerHomePath}";
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.GetFullPath(outputPath);
                string directory = Path.GetDirectoryName(outputPath);

                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            options = new BatchAttackOptions(
                basePath,
                commandsPath,
                attackerHomePath,
                outputPath,
                attackCount,
                preparationTime,
                attackTime,
                includeBattleState,
                includeReplay,
                silent,
                battleSeed);

            return true;
        }

        private static bool TryReadValue(string[] args, ref int index, out string value, out string error)
        {
            if (index + 1 >= args.Length)
            {
                value = null;
                error = $"Missing value for {args[index]}";
                return false;
            }

            value = args[++index];
            error = null;
            return true;
        }
    }
}
