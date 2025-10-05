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

            var snapshots = this.layout.Buildings
                .Select(b => new BuildingSnapshot(b))
                .ToList();

            var buildingBuckets = snapshots
                .GroupBy(b => b.DataId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var buildingsByInstance = snapshots
                .Where(b => b.HasInstanceId)
                .ToDictionary(b => b.InstanceId, b => b);

            var troops = new List<TroopInstance>();

            double currentTick = 0;

            foreach (BattleCommand command in commandList)
            {
                if (!HasRemainingBuildings(snapshots))
                {
                    break;
                }

                double targetTick = Math.Max(0, command.Tick);
                double deltaTicks = targetTick - currentTick;

                if (deltaTicks > 0)
                {
                    double advanced = AdvanceTime(
                        snapshots,
                        troops,
                        deltaTicks,
                        ref preparationTime,
                        ref attackTime,
                        this.options);

                    currentTick += advanced;

                    if (advanced + 1e-6 < deltaTicks)
                    {
                        // Battle finished before reaching this command.
                        break;
                    }
                }

                currentTick = targetTick;

                ApplyCommand(
                    command,
                    snapshots,
                    troops,
                    buildingBuckets,
                    buildingsByInstance,
                    this.options);
            }

            if (HasRemainingBuildings(snapshots) && HasAliveTroops(troops) && attackTime > 0)
            {
                double consumed = TickTroops(
                    troops,
                    snapshots,
                    Math.Max(0, attackTime),
                    this.options);

                attackTime -= consumed;
                currentTick += consumed * 63.0;
            }

            if (currentTick < 0)
            {
                currentTick = 0;
            }

            preparationTime = Math.Max(0, preparationTime);
            attackTime = Math.Max(0, attackTime);

            int endTick = (int)Math.Round(currentTick);

            return CreateResult(snapshots, preparationTime, attackTime, endTick);
        }

        static double AdvanceTime(
            List<BuildingSnapshot> buildings,
            List<TroopInstance> troops,
            double deltaTicks,
            ref double preparationTime,
            ref double attackTime,
            BattleSimulationOptions options)
        {
            double ticksRemaining = deltaTicks;
            double processedTicks = 0;

            if (ticksRemaining <= 0)
            {
                return 0;
            }

            while (ticksRemaining > 1e-6)
            {
                if (!HasRemainingBuildings(buildings))
                {
                    break;
                }

                double secondsRemaining = ticksRemaining / 63.0;

                if (preparationTime > 0)
                {
                    double prepSeconds = Math.Min(preparationTime, secondsRemaining);
                    preparationTime -= prepSeconds;
                    ticksRemaining -= prepSeconds * 63.0;
                    processedTicks += prepSeconds * 63.0;

                    if (prepSeconds + 1e-6 < secondsRemaining)
                    {
                        continue;
                    }

                    break;
                }

                if (attackTime <= 1e-6)
                {
                    break;
                }

                double attackSeconds = Math.Min(attackTime, secondsRemaining);

                if (attackSeconds <= 0)
                {
                    break;
                }

                if (HasAliveTroops(troops))
                {
                    double consumed = TickTroops(
                        troops,
                        buildings,
                        attackSeconds,
                        options);

                    attackTime -= consumed;
                    ticksRemaining -= consumed * 63.0;
                    processedTicks += consumed * 63.0;

                    if (consumed + 1e-6 < attackSeconds)
                    {
                        break;
                    }
                }
                else
                {
                    attackTime -= attackSeconds;
                    ticksRemaining -= attackSeconds * 63.0;
                    processedTicks += attackSeconds * 63.0;
                }

                if (attackSeconds + 1e-6 < secondsRemaining)
                {
                    break;
                }
            }

            attackTime = Math.Max(0, attackTime);

            return processedTicks;
        }

        static void ApplyCommand(
            BattleCommand command,
            List<BuildingSnapshot> snapshots,
            List<TroopInstance> troops,
            Dictionary<int, List<BuildingSnapshot>> buckets,
            Dictionary<int, BuildingSnapshot> byInstance,
            BattleSimulationOptions options)
        {
            if (command.DataId <= 0)
            {
                return;
            }

            int classId = GlobalIdHelper.GetClassId(command.DataId);

            switch (classId)
            {
                case 500:
                    if (byInstance.TryGetValue(command.DataId, out BuildingSnapshot? snapshot))
                    {
                        DestroyBuilding(snapshot);
                    }

                    return;
                case 1:
                    if (!buckets.TryGetValue(command.DataId, out List<BuildingSnapshot>? bucket))
                    {
                        return;
                    }

                    BuildingSnapshot? target = MatchBuilding(bucket, command.X, command.Y);
                    if (target != null)
                    {
                        DestroyBuilding(target);
                    }

                    return;
                case 4:
                    TroopStats? stats = options.TroopStatsProvider?.GetTroopStats(command.DataId);
                    if (stats == null)
                    {
                        return;
                    }

                    var troop = new TroopInstance(command.DataId, command.X, command.Y, stats);
                    AcquireTarget(troop, snapshots, options);
                    troops.Add(troop);

                    return;
                default:
                    return;
            }
        }

        static bool HasRemainingBuildings(List<BuildingSnapshot> snapshots)
        {
            return snapshots.Any(b => !b.Destroyed);
        }

        static bool HasAliveTroops(List<TroopInstance> troops)
        {
            return troops.Any(t => t.IsAlive);
        }

        static void AcquireTarget(TroopInstance troop, List<BuildingSnapshot> snapshots, BattleSimulationOptions options)
        {
            if (troop == null)
            {
                return;
            }

            troop.Target = SelectTarget(troop, snapshots);

            if (troop.Target != null)
            {
                double range = troop.Stats.AttackRange;
                double distance = troop.DistanceTo(troop.Target);
                double required = Math.Max(0, distance - range);
                if (troop.Stats.MoveSpeed <= 0)
                {
                    troop.TravelTimeRemaining = 0;
                }
                else
                {
                    troop.TravelTimeRemaining = required / troop.Stats.MoveSpeed;
                }
            }
            else
            {
                troop.TravelTimeRemaining = 0;
            }
        }

        static BuildingSnapshot? SelectTarget(TroopInstance troop, List<BuildingSnapshot> snapshots)
        {
            var candidates = snapshots.Where(b => !b.Destroyed).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            IReadOnlyCollection<int> preferred = troop.Stats.PreferredTargetDataIds;
            if (preferred.Count > 0)
            {
                var preferredCandidates = candidates
                    .Where(b => preferred.Contains(b.DataId))
                    .ToList();

                if (preferredCandidates.Count > 0)
                {
                    candidates = preferredCandidates;
                }
            }

            BuildingSnapshot? match = null;
            double bestDistance = double.MaxValue;

            foreach (BuildingSnapshot snapshot in candidates)
            {
                double distance = troop.DistanceSquaredTo(snapshot);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    match = snapshot;
                }
            }

            return match;
        }

        static double TickTroops(
            List<TroopInstance> troops,
            List<BuildingSnapshot> buildings,
            double availableSeconds,
            BattleSimulationOptions options)
        {
            if (availableSeconds <= 1e-6)
            {
                return 0;
            }

            double elapsed = 0;
            double step = Math.Max(1e-3, options.TickResolutionSeconds);

            while (elapsed + 1e-6 < availableSeconds)
            {
                if (!HasAliveTroops(troops))
                {
                    break;
                }

                if (!HasRemainingBuildings(buildings))
                {
                    break;
                }

                double dt = Math.Min(step, availableSeconds - elapsed);

                foreach (TroopInstance troop in troops)
                {
                    if (!troop.IsAlive)
                    {
                        continue;
                    }

                    if (troop.Target == null || troop.Target.Destroyed)
                    {
                        AcquireTarget(troop, buildings, options);
                    }

                    BuildingSnapshot? target = troop.Target;
                    if (target == null)
                    {
                        continue;
                    }

                    double remainingSlice = dt;

                    if (troop.TravelTimeRemaining > 0)
                    {
                        double travel = Math.Min(troop.TravelTimeRemaining, remainingSlice);
                        troop.TravelTimeRemaining -= travel;
                        remainingSlice -= travel;

                        if (remainingSlice <= 1e-6)
                        {
                            continue;
                        }
                    }

                    double multiplier = troop.GetDamageMultiplier(target.DataId);
                    double damage = troop.Stats.DamagePerSecond * multiplier * remainingSlice;
                    target.RemainingHitpoints -= damage;

                    if (target.RemainingHitpoints <= 0)
                    {
                        DestroyBuilding(target);
                        troop.Target = null;
                    }
                }

                elapsed += dt;
            }

            return elapsed;
        }

        static BuildingSnapshot? MatchBuilding(List<BuildingSnapshot>? bucket, int x, int y)
        {
            if (bucket == null)
            {
                return null;
            }

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

        static void DestroyBuilding(BuildingSnapshot? snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.Destroyed = true;
        }

        static BattleResult CreateResult(List<BuildingSnapshot> snapshots, double prepTime, double attackTime, int endTick)
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
                ClampInt(stars, 0, 3),
                destruction,
                townHallDestroyed,
                battleTime,
                prepTime < 0 ? 0 : prepTime,
                attackTime,
                endTick);
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
                this.RemainingHitpoints = definition.Hitpoints;
            }

            internal int InstanceId { get; }
            internal int DataId { get; }
            internal int Hitpoints { get; }
            internal bool IsTownHall { get; }
            internal int X { get; }
            internal int Y { get; }
            internal double RemainingHitpoints { get; set; }
            internal bool Destroyed
            {
                get => this.destroyed;
                set
                {
                    this.destroyed = value;
                    if (value)
                    {
                        this.RemainingHitpoints = 0;
                    }
                }
            }
            internal bool HasInstanceId => this.InstanceId > 0;

            bool destroyed;
        }

        sealed class TroopInstance
        {
            internal TroopInstance(int dataId, int spawnX, int spawnY, TroopStats stats)
            {
                this.DataId = dataId;
                this.SpawnX = spawnX;
                this.SpawnY = spawnY;
                this.Stats = stats ?? throw new ArgumentNullException(nameof(stats));
                this.RemainingHitpoints = stats.Hitpoints;
            }

            internal int DataId { get; }
            internal int SpawnX { get; }
            internal int SpawnY { get; }
            internal TroopStats Stats { get; }
            internal BuildingSnapshot? Target { get; set; }
            internal double RemainingHitpoints { get; set; }
            internal double TravelTimeRemaining { get; set; }
            internal bool IsAlive => this.RemainingHitpoints > 0;

            internal double DistanceSquaredTo(BuildingSnapshot target)
            {
                int dx = target.X - this.SpawnX;
                int dy = target.Y - this.SpawnY;
                return (double)dx * dx + (double)dy * dy;
            }

            internal double DistanceTo(BuildingSnapshot target)
            {
                return Math.Sqrt(this.DistanceSquaredTo(target));
            }

            internal double GetDamageMultiplier(int buildingDataId)
            {
                return this.Stats.GetDamageMultiplier(buildingDataId);
            }
        }
    }

    public sealed class BattleSimulationOptions
    {
        public static BattleSimulationOptions Default => new BattleSimulationOptions();

        public double PreparationTime { get; set; } = 30;

        public double AttackTime { get; set; } = 180;

        public double TickResolutionSeconds { get; set; } = 0.25;

        public ITroopStatsProvider? TroopStatsProvider { get; set; } = TroopStatsProvider.Null;

        internal BattleSimulationOptions Clone()
        {
            return new BattleSimulationOptions
            {
                PreparationTime = this.PreparationTime,
                AttackTime = this.AttackTime,
                TickResolutionSeconds = this.TickResolutionSeconds,
                TroopStatsProvider = this.TroopStatsProvider
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
