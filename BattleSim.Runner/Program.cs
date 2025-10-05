using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClashOfSL.BattleSim;
using Newtonsoft.Json.Linq;

string? layoutPath = null;
string? commandsPath = null;
string? statsPath = null;
bool interactive = false;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];
        switch (arg)
        {
            case "--layout":
            case "-l":
                layoutPath = GetNextValue(args, ref i, "layout");
                break;
            case "--commands":
            case "-c":
                commandsPath = GetNextValue(args, ref i, "commands");
                break;
            case "--stats":
            case "-s":
                statsPath = GetNextValue(args, ref i, "stats");
                break;
            case "--interactive":
            case "-i":
                interactive = true;
                break;
            case "--help":
            case "-h":
                PrintUsage();
                return 0;
            default:
                Console.Error.WriteLine($"Unknown argument '{arg}'.");
                PrintUsage();
                return 1;
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    PrintUsage();
    return 1;
}

if (string.IsNullOrWhiteSpace(layoutPath) || string.IsNullOrWhiteSpace(statsPath))
{
    Console.Error.WriteLine("Error: --layout and --stats are required arguments.");
    PrintUsage();
    return 1;
}

try
{
    if (!File.Exists(layoutPath))
    {
        throw new FileNotFoundException("Layout file not found.", layoutPath);
    }

    if (!File.Exists(statsPath))
    {
        throw new FileNotFoundException("Stats file not found.", statsPath);
    }

    JObject layoutJson = JObject.Parse(File.ReadAllText(layoutPath));
    IBuildingStatsProvider statsProvider = FileBuildingStatsProvider.Load(statsPath);
    BattleLayout layout = BattleLayout.FromJson(layoutJson, statsProvider);

    IReadOnlyList<BattleCommand> commands = Array.Empty<BattleCommand>();
    if (interactive)
    {
        commands = LoadCommandsInteractively(commandsPath);
    }
    else if (!string.IsNullOrWhiteSpace(commandsPath))
    {
        if (!File.Exists(commandsPath))
        {
            throw new FileNotFoundException("Commands file not found.", commandsPath);
        }

        commands = LoadCommands(commandsPath).ToList();
    }

    var simulator = new BattleSimulator(layout);
    BattleResult result = simulator.Run(commands);

    Console.WriteLine("Battle resolved successfully:\n");
    Console.WriteLine($"Stars:                 {result.Stars}");
    Console.WriteLine($"Destruction:           {result.DestructionPercentage}%");
    Console.WriteLine($"Battle time (seconds): {result.BattleTime}");

    if (!interactive)
    {
        Console.WriteLine($"Town hall destroyed:   {result.TownHallDestroyed}");
        Console.WriteLine($"Prep time left:        {result.PreparationTimeRemaining:F2}s");
        Console.WriteLine($"Attack time left:      {result.AttackTimeRemaining:F2}s");
        Console.WriteLine($"Simulation end tick:   {result.EndTick}");
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Simulation failed: " + ex.Message);
    return 1;
}

static IEnumerable<BattleCommand> LoadCommands(string path)
{
    string json = File.ReadAllText(path);
    JArray array = JArray.Parse(json);
    foreach (JToken token in array)
    {
        if (token is not JObject obj)
        {
            continue;
        }

        int tick = obj.Value<int?>("tick") ?? 0;
        int dataId = obj.Value<int?>("dataId") ?? 0;
        int x = obj.Value<int?>("x") ?? 0;
        int y = obj.Value<int?>("y") ?? 0;

        yield return new BattleCommand(tick, dataId, x, y);
    }
}

static IReadOnlyList<BattleCommand> LoadCommandsInteractively(string? commandsPath)
{
    var commands = new List<BattleCommand>();

    if (!string.IsNullOrWhiteSpace(commandsPath) && File.Exists(commandsPath))
    {
        commands.AddRange(LoadCommands(commandsPath));
        Console.WriteLine($"Loaded {commands.Count} commands from '{commandsPath}'.");
    }

    Console.WriteLine();
    Console.WriteLine("Interactive test interface");
    Console.WriteLine("Enter one command per line using: <tick> <dataId> <x> <y>.");
    Console.WriteLine("Press ENTER on an empty line to finish.");
    Console.WriteLine("Type 'sample' to load the built-in sample sequence.");
    Console.WriteLine("Sample command: 63 1000001 56 38\n");

    while (true)
    {
        Console.Write("> ");
        string? line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            break;
        }

        if (string.Equals(line.Trim(), "sample", StringComparison.OrdinalIgnoreCase))
        {
            commands.Clear();
            commands.AddRange(GetSampleCommands());
            Console.WriteLine($"Loaded {commands.Count} sample commands. Press ENTER again to run or keep typing to add more.");
            continue;
        }

        string[] parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            Console.WriteLine("Invalid command. Expected four numbers separated by spaces.");
            continue;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tick) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dataId) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            Console.WriteLine("Invalid numbers. Please try again.");
            continue;
        }

        commands.Add(new BattleCommand(tick, dataId, x, y));
    }

    if (commands.Count == 0)
    {
        Console.WriteLine("No commands entered. Using sample sequence.");
        return GetSampleCommands();
    }

    return commands;
}

static IReadOnlyList<BattleCommand> GetSampleCommands() => new[]
{
    new BattleCommand(63, 1000001, 56, 38),
    new BattleCommand(126, 1000002, 47, 49),
    new BattleCommand(189, 1000000, 40, 42),
    new BattleCommand(252, 500000001, 50, 40)
};

static string GetNextValue(string[] args, ref int index, string argumentName)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"Missing value for --{argumentName} argument.");
    }

    index++;
    return args[index];
}

static void PrintUsage()
{
    Console.WriteLine("BattleSim Runner\n");
    Console.WriteLine("Usage:");
    Console.WriteLine("  BattleSim.Runner --layout <layout.json> --stats <stats.json> [--commands <commands.json>] [--interactive]\n");
    Console.WriteLine("Arguments:");
    Console.WriteLine("  --layout, -l    Path to the defender layout JSON payload.");
    Console.WriteLine("  --stats,  -s    Building stats JSON file that provides hitpoints for each data id and level.");
    Console.WriteLine("  --commands,-c   Optional command list JSON file to replay during the simulation.");
    Console.WriteLine("  --interactive,-i Run the interactive testing interface (ignore commands file).");
    Console.WriteLine("  --help,   -h    Display this help message.");
}

sealed class FileBuildingStatsProvider : IBuildingStatsProvider
{
    readonly Dictionary<int, StatsEntry> entries;

    FileBuildingStatsProvider(Dictionary<int, StatsEntry> entries)
    {
        this.entries = entries;
    }

    public static IBuildingStatsProvider Load(string path)
    {
        string json = File.ReadAllText(path);
        JObject document = JObject.Parse(json);
        if (document["buildings"] is not JArray array)
        {
            throw new InvalidDataException("Stats file must contain a 'buildings' array.");
        }

        var entries = new Dictionary<int, StatsEntry>();
        foreach (JToken token in array)
        {
            if (token is not JObject obj)
            {
                continue;
            }

            int dataId = obj.Value<int?>("dataId") ?? 0;
            if (dataId <= 0)
            {
                continue;
            }

            bool isTownHall = obj.Value<bool?>("isTownHall") ?? false;
            int? fallbackHitpoints = obj.Value<int?>("defaultHitpoints");
            var hitpointsByLevel = new Dictionary<int, int>();

            JToken? hitpointsToken = obj["hitpoints"];
            if (hitpointsToken is JArray hitpointArray)
            {
                for (int levelIndex = 0; levelIndex < hitpointArray.Count; levelIndex++)
                {
                    int level = levelIndex + 1;
                    int hitpoints = hitpointArray[levelIndex]?.Value<int>() ?? 0;
                    if (hitpoints > 0)
                    {
                        hitpointsByLevel[level] = hitpoints;
                    }
                }
            }
            else if (hitpointsToken is JObject hitpointObject)
            {
                foreach (JProperty property in hitpointObject.Properties())
                {
                    if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
                    {
                        continue;
                    }

                    int hitpoints = property.Value.Value<int?>() ?? 0;
                    if (hitpoints > 0)
                    {
                        hitpointsByLevel[level] = hitpoints;
                    }
                }
            }

            entries[dataId] = new StatsEntry(isTownHall, fallbackHitpoints, hitpointsByLevel);
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("Stats file did not contain any valid building entries.");
        }

        return new FileBuildingStatsProvider(entries);
    }

    public BuildingStats GetStats(int dataId, int level)
    {
        if (!this.entries.TryGetValue(dataId, out StatsEntry? entry))
        {
            throw new KeyNotFoundException($"Stats file is missing data id {dataId}.");
        }

        if (entry.HitpointsByLevel.TryGetValue(level, out int hitpoints))
        {
            return new BuildingStats(hitpoints, entry.IsTownHall);
        }

        if (entry.DefaultHitpoints.HasValue)
        {
            return new BuildingStats(entry.DefaultHitpoints.Value, entry.IsTownHall);
        }

        throw new KeyNotFoundException($"Stats file does not provide hitpoints for data id {dataId} at level {level}.");
    }

    sealed class StatsEntry
    {
        public StatsEntry(bool isTownHall, int? defaultHitpoints, Dictionary<int, int> hitpointsByLevel)
        {
            this.IsTownHall = isTownHall;
            this.DefaultHitpoints = defaultHitpoints;
            this.HitpointsByLevel = hitpointsByLevel;
        }

        public bool IsTownHall { get; }

        public int? DefaultHitpoints { get; }

        public Dictionary<int, int> HitpointsByLevel { get; }
    }
}
