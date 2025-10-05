using System;
using System.Collections.Generic;
using System.Linq;

namespace ClashOfSL.BattleSim
{
    public sealed class TroopStats
    {
        readonly HashSet<int> preferredTargets;

        public TroopStats(
            int dataId,
            double hitpoints,
            double damagePerSecond,
            double moveSpeed,
            double attackRange,
            bool isFlying,
            IEnumerable<int>? preferredTargetDataIds = null,
            double preferredTargetDamageMultiplier = 1.0)
        {
            if (dataId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dataId));
            }

            if (hitpoints <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hitpoints));
            }

            if (damagePerSecond < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damagePerSecond));
            }

            if (moveSpeed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            }

            if (attackRange < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attackRange));
            }

            if (preferredTargetDamageMultiplier <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(preferredTargetDamageMultiplier));
            }

            this.DataId = dataId;
            this.Hitpoints = hitpoints;
            this.DamagePerSecond = damagePerSecond;
            this.MoveSpeed = moveSpeed;
            this.AttackRange = attackRange;
            this.IsFlying = isFlying;
            this.preferredTargets = preferredTargetDataIds != null
                ? new HashSet<int>(preferredTargetDataIds.Where(id => id > 0))
                : new HashSet<int>();
            this.PreferredTargetDamageMultiplier = preferredTargetDamageMultiplier;
        }

        public int DataId { get; }

        public double Hitpoints { get; }

        public double DamagePerSecond { get; }

        public double MoveSpeed { get; }

        public double AttackRange { get; }

        public bool IsFlying { get; }

        public double PreferredTargetDamageMultiplier { get; }

        public IReadOnlyCollection<int> PreferredTargetDataIds => this.preferredTargets;

        internal double GetDamageMultiplier(int buildingDataId)
        {
            if (buildingDataId <= 0 || this.preferredTargets.Count == 0)
            {
                return 1.0;
            }

            return this.preferredTargets.Contains(buildingDataId)
                ? this.PreferredTargetDamageMultiplier
                : 1.0;
        }
    }

    public interface ITroopStatsProvider
    {
        TroopStats? GetTroopStats(int dataId);
    }

    public sealed class DelegateTroopStatsProvider : ITroopStatsProvider
    {
        readonly Func<int, TroopStats?> resolver;

        public DelegateTroopStatsProvider(Func<int, TroopStats?> resolver)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public TroopStats? GetTroopStats(int dataId)
        {
            return this.resolver(dataId);
        }
    }

    sealed class NullTroopStatsProvider : ITroopStatsProvider
    {
        NullTroopStatsProvider()
        {
        }

        public static NullTroopStatsProvider Instance { get; } = new NullTroopStatsProvider();

        public TroopStats? GetTroopStats(int dataId)
        {
            return null;
        }
    }

    public static class TroopStatsProvider
    {
        public static ITroopStatsProvider Null => NullTroopStatsProvider.Instance;
    }
}
