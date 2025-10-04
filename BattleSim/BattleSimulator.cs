using System;
using System.Collections.Generic;
using System.Linq;

namespace ClashOfSL.BattleSim
{
    public sealed class BattleSimulator
    {
        readonly BattleLayout layout;
        readonly BattleSimulationOptions options;

        public BattleSimulator(BattleLayout layout, BattleSimulationOptions? options = null)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            this.options = (options ?? BattleSimulationOptions.Default).Clone();
        }

        public BattleLayout Layout => this.layout;

        public BattleSimulationOptions Options => this.options.Clone();

        public BattleResult Run(IEnumerable<BattleCommand>? commands)
        {
            var commandList = commands?.Where(c => c != null).OrderBy(c => c.Tick).ToList() ?? new List<BattleCommand>();

            double preparationTime = this.options.PreparationTime;
            double attackTime = this.options.AttackTime;
            double lastTick = 0;
            int processed = 0;

            List<BuildingSnapshot> snapshots = this.layout.Buildings
                .Select(b => new BuildingSnapshot(b))
                .ToList();

            Dictionary<int, List<BuildingSnapshot>> buildingBuckets = snapshots
                .GroupBy(b => b.DataId)
                .ToDictionary(g => g.Key, g => g.ToList());

            Dictionary<int, BuildingSnapshot> buildingsByInstance = snapshots
                .Where(b => b.HasInstanceId)
                .ToDictionary(b => b.InstanceId, b => b);

            foreach (BattleCommand command in commandList)
            {
                AdvanceClock(ref preparationTime, ref attackTime, ref lastTick, command.Tick, processed);
                processed++;
                ApplyCommand(command, buildingBuckets, buildingsByInstance);
            }

            if (commandList.Count > 0)
            {
                AdvanceClock(ref preparationTime, ref attackTime, ref lastTick, commandList[^1].Tick, processed);
            }

            return CreateResult(snapshots, preparationTime, attackTime, lastTick);
        }

        static void AdvanceClock(ref double prep, ref double attack, ref double lastTick, int targetTick, int processedCommands)
        {
            if (targetTick <= lastTick)
            {
                return;
            }

            double delta = targetTick - lastTick;
            lastTick = targetTick;

            if (prep > 0 && processedCommands == 0)
            {
                prep -= delta / 63.0;
                if (prep < 0)
                {
                    attack += prep;
                    prep = 0;
                }
            }
            else
            {
                attack -= delta / 63.0;
            }

            if (attack < 0)
            {
                attack = 0;
            }
        }

        static void ApplyCommand(
            BattleCommand command,
            Dictionary<int, List<BuildingSnapshot>> buckets,
            Dictionary<int, BuildingSnapshot> byInstance)
        {
            if (command.DataId <= 0)
            {
                return;
            }

            int classId = GlobalIdHelper.GetClassId(command.DataId);

            if (classId == 500)
            {
                if (byInstance.TryGetValue(command.DataId, out BuildingSnapshot? snapshot))
                {
                    snapshot.Destroyed = true;
                }

                return;
            }

            if (classId != 1)
            {
                return;
            }

            if (!buckets.TryGetValue(command.DataId, out List<BuildingSnapshot>? bucket))
            {
                return;
            }

            BuildingSnapshot? target = MatchBuilding(bucket, command.X, command.Y);
            if (target != null)
            {
                target.Destroyed = true;
            }
        }

        static BuildingSnapshot? MatchBuilding(List<BuildingSnapshot> bucket, int x, int y)
        {
            BuildingSnapshot? match = null;
            int bestDistance = int.MaxValue;

            foreach (BuildingSnapshot snapshot in bucket)
            {
                if (snapshot.Destroyed)
                {
                    continue;
                }

                int deltaX = snapshot.X - x;
                int deltaY = snapshot.Y - y;
                int distance = deltaX * deltaX + deltaY * deltaY;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    match = snapshot;
                }
            }

            if (match == null)
            {
                match = bucket.FirstOrDefault(b => !b.Destroyed);
            }

            return match;
        }

        static BattleResult CreateResult(List<BuildingSnapshot> snapshots, double prepTime, double attackTime, double lastTick)
        {
            int totalHitpoints = snapshots.Sum(b => b.Hitpoints);
            int destroyedHitpoints = snapshots.Where(b => b.Destroyed).Sum(b => b.Hitpoints);

            int destruction = 0;
            if (totalHitpoints > 0)
            {
                destruction = (int)Math.Round(destroyedHitpoints * 100.0 / totalHitpoints);
            }

            destruction = ClampInt(destruction, 0, 100);
            bool townHallDestroyed = snapshots.Any(b => b.IsTownHall && b.Destroyed);

            int stars = 0;
            if (destruction >= 50)
            {
                stars++;
            }

            if (townHallDestroyed)
            {
                stars++;
            }

            if (destruction >= 100)
            {
                stars++;
            }

            int battleTime = (int)Math.Round(180 - attackTime);
            battleTime = ClampInt(battleTime, 0, 180);

            return new BattleResult(
                stars,
                destruction,
                townHallDestroyed,
                battleTime,
                prepTime < 0 ? 0 : prepTime,
                attackTime,
                (int)Math.Round(lastTick));
        }

        static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        sealed class BuildingSnapshot
        {
            internal BuildingSnapshot(BuildingDefinition definition)
            {
                this.InstanceId = definition.InstanceId;
                this.DataId = definition.DataId;
                this.Hitpoints = definition.Hitpoints;
                this.IsTownHall = definition.IsTownHall;
                this.X = definition.X;
                this.Y = definition.Y;
            }

            internal int InstanceId { get; }
            internal int DataId { get; }
            internal int Hitpoints { get; }
            internal bool IsTownHall { get; }
            internal int X { get; }
            internal int Y { get; }
            internal bool Destroyed { get; set; }
            internal bool HasInstanceId => this.InstanceId > 0;
        }
    }

    public sealed class BattleSimulationOptions
    {
        public static BattleSimulationOptions Default => new BattleSimulationOptions();

        public double PreparationTime { get; set; } = 30;

        public double AttackTime { get; set; } = 180;

        internal BattleSimulationOptions Clone()
        {
            return new BattleSimulationOptions
            {
                PreparationTime = this.PreparationTime,
                AttackTime = this.AttackTime
            };
        }
    }

    public sealed class BattleResult
    {
        internal BattleResult(
            int stars,
            int destructionPercentage,
            bool townHallDestroyed,
            int battleTime,
            double preparationTimeRemaining,
            double attackTimeRemaining,
            int endTick)
        {
            this.Stars = stars;
            this.DestructionPercentage = destructionPercentage;
            this.TownHallDestroyed = townHallDestroyed;
            this.BattleTime = battleTime;
            this.PreparationTimeRemaining = preparationTimeRemaining;
            this.AttackTimeRemaining = attackTimeRemaining;
            this.EndTick = endTick;
        }

        public int Stars { get; }
        public int DestructionPercentage { get; }
        public bool TownHallDestroyed { get; }
        public int BattleTime { get; }
        public double PreparationTimeRemaining { get; }
        public double AttackTimeRemaining { get; }
        public int EndTick { get; }
    }

    static class GlobalIdHelper
    {
        public static int GetClassId(int globalId)
        {
            const long r1 = 1125899907L;
            long value = (r1 * globalId) >> 32;
            return (int)((value >> 18) + (value >> 31));
        }
    }
}
