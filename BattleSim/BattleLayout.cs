using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ClashOfSL.BattleSim
{
    /// <summary>
    ///     Immutable snapshot of a defender layout. The simulator only requires
    ///     building hitpoints, ids and coordinates – everything else from the server
    ///     code base has been stripped away.
    /// </summary>
    public sealed class BattleLayout
    {
        public BattleLayout(IEnumerable<BuildingDefinition> buildings)
        {
            if (buildings == null)
            {
                throw new ArgumentNullException(nameof(buildings));
            }

            var materialized = buildings
                .Where(b => b != null && b.Hitpoints > 0)
                .Select(b => b.Clone())
                .ToList();

            if (materialized.Count == 0)
            {
                throw new ArgumentException("A layout must contain at least one building.", nameof(buildings));
            }

            this.Buildings = materialized;
        }

        public IReadOnlyList<BuildingDefinition> Buildings { get; }

        /// <summary>
        ///     Creates a layout from the JSON representation stored in Clash save files.
        ///     You need to provide a stats provider that can map the <c>data</c> and
        ///     <c>lvl</c> fields to concrete hitpoints + town hall flags.
        /// </summary>
        public static BattleLayout FromJson(JObject baseLayout, IBuildingStatsProvider statsProvider)
        {
            if (baseLayout == null)
            {
                throw new ArgumentNullException(nameof(baseLayout));
            }

            if (statsProvider == null)
            {
                throw new ArgumentNullException(nameof(statsProvider));
            }

            var buildings = new List<BuildingDefinition>();
            JArray? buildingArray = baseLayout["buildings"] as JArray;

            if (buildingArray == null)
            {
                throw new ArgumentException("The provided layout does not contain a buildings array.", nameof(baseLayout));
            }

            int generatedInstanceId = 500000000;

            foreach (JToken token in buildingArray)
            {
                JObject? buildingObject = token as JObject;
                if (buildingObject == null)
                {
                    continue;
                }

                int dataId = buildingObject.Value<int?>("data") ?? 0;
                if (dataId <= 0)
                {
                    continue;
                }

                int level = buildingObject.Value<int?>("lvl") ?? 0;
                BuildingStats stats = statsProvider.GetStats(dataId, level);
                if (stats.Hitpoints <= 0)
                {
                    continue;
                }

                int instanceId = buildingObject.Value<int?>("id") ?? generatedInstanceId++;
                int x = buildingObject.Value<int?>("x") ?? 0;
                int y = buildingObject.Value<int?>("y") ?? 0;

                buildings.Add(new BuildingDefinition(instanceId, dataId, stats.Hitpoints, stats.IsTownHall, x, y));
            }

            return new BattleLayout(buildings);
        }
    }

    public sealed class BuildingDefinition
    {
        public BuildingDefinition(int instanceId, int dataId, int hitpoints, bool isTownHall, int x, int y)
        {
            if (dataId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dataId));
            }

            if (hitpoints <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hitpoints));
            }

            this.InstanceId = instanceId;
            this.DataId = dataId;
            this.Hitpoints = hitpoints;
            this.IsTownHall = isTownHall;
            this.X = x;
            this.Y = y;
        }

        public int InstanceId { get; }

        public int DataId { get; }

        public int Hitpoints { get; }

        public bool IsTownHall { get; }

        public int X { get; }

        public int Y { get; }

        internal BuildingDefinition Clone()
        {
            return new BuildingDefinition(this.InstanceId, this.DataId, this.Hitpoints, this.IsTownHall, this.X, this.Y);
        }
    }

    public readonly struct BuildingStats
    {
        public BuildingStats(int hitpoints, bool isTownHall)
        {
            this.Hitpoints = hitpoints;
            this.IsTownHall = isTownHall;
        }

        public int Hitpoints { get; }

        public bool IsTownHall { get; }
    }

    public interface IBuildingStatsProvider
    {
        BuildingStats GetStats(int dataId, int level);
    }

    public sealed class DelegateBuildingStatsProvider : IBuildingStatsProvider
    {
        readonly Func<int, int, BuildingStats> resolver;

        public DelegateBuildingStatsProvider(Func<int, int, BuildingStats> resolver)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public BuildingStats GetStats(int dataId, int level)
        {
            return this.resolver(dataId, level);
        }
    }
}
