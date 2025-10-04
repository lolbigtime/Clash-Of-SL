using System;

namespace ClashOfSL.BattleSim
{
    /// <summary>
    ///     Represents a single battle action that can be replayed inside the
    ///     deterministic simulator. A command typically corresponds to a troop
    ///     placement or spell drop at a given world coordinate.
    /// </summary>
    public sealed class BattleCommand
    {
        public BattleCommand(int tick, int dataId, int x, int y)
        {
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            this.Tick = tick;
            this.DataId = dataId;
            this.X = x;
            this.Y = y;
        }

        /// <summary>
        ///     Gets the simulation tick when the command was issued. Tick 0 maps to
        ///     the start of preparation time; each 63 ticks represent one second in
        ///     game time.
        /// </summary>
        public int Tick { get; }

        /// <summary>
        ///     Gets the encoded identifier of the command payload. This is normally a
        ///     Clash of Clans global id representing a unit drop. If the value encodes
        ///     a specific building instance (class id 500) the simulator will mark
        ///     that structure as destroyed immediately.
        /// </summary>
        public int DataId { get; }

        /// <summary>
        ///     Gets the tile coordinate along the X axis.
        /// </summary>
        public int X { get; }

        /// <summary>
        ///     Gets the tile coordinate along the Y axis.
        /// </summary>
        public int Y { get; }

        public BattleCommand WithTick(int tick)
        {
            return new BattleCommand(tick, this.DataId, this.X, this.Y);
        }

        public BattleCommand WithPosition(int x, int y)
        {
            return new BattleCommand(this.Tick, this.DataId, x, y);
        }
    }
}
