using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CSS.Core;
using CSS.Files.Logic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UCS.Logic.JSONProperty;
using UCS.Logic.JSONProperty.Item;

namespace UCS.Logic
{
    internal class Battle
    {
        internal double Last_Tick;

        internal double Preparation_Time = 30;
        internal double Attack_Time = 180;

        /// <summary>
        ///     Gets or sets the battle tick.
        /// </summary>
        /// <value>The battle tick.</value>
        internal bool EnableTickLogging { get; set; } = true;

        internal double BattleTick
        {
            get
            {
                if (this.Preparation_Time > 0) return this.Preparation_Time;
                return this.Attack_Time;
            }
            set
            {
                if (this.Preparation_Time >= 1 && this.Commands.Count < 1)
                {
                    this.Preparation_Time -= (value - this.Last_Tick) / 63;
                    if (this.EnableTickLogging)
                    {
                        Logger.Write("Preparation Time : " + this.Preparation_Time);
                    }
                }
                else
                {
                    this.Attack_Time -= (value - this.Last_Tick) / 63;
                    if (this.EnableTickLogging)
                    {
                        Logger.Write("Attack Time      : " + this.Attack_Time);
                    }
                }
                this.Last_Tick = value;
                this.End_Tick = (int) value;

            }
        }

        JObject prototypeSource;
        List<BuildingPrototype> buildingPrototypes;

        [JsonProperty("level")] internal JObject Base;

        [JsonProperty("attacker")] internal ClientAvatar Attacker = new ClientAvatar();

        [JsonProperty("defender")] internal ClientAvatar Defender = new ClientAvatar();

        [JsonIgnore] internal Replay_Info Replay_Info = new Replay_Info();

        [JsonProperty("end_tick")] internal int End_Tick;

        [JsonProperty("timestamp")] internal int TimeStamp =
            (int) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

        [JsonProperty("cmd")] internal Commands Commands = new Commands();

        [JsonProperty("prep_skip")] internal int Preparation_Skip = 0;

        [JsonProperty("calendar")] internal Calendar Calendar = new Calendar();
        [JsonProperty("battle_id")] internal long Battle_ID;

        [JsonConstructor]
        internal Battle()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Battle" /> class.
        /// </summary>
        /// <param name="Battle">The battle.</param>
        /// <param name="_Attacker">The attacker.</param>
        /// <param name="_Enemy">The enemy.</param>
        internal Battle(long Battle, Level _Attacker, Level _Enemy, bool clone = true)
        {
            this.Battle_ID = Battle;

            this.Attacker = _Attacker.Avatar;
            this.Defender = _Enemy.Avatar;
            this.Base = _Enemy.GameObjectManager.Save();
            this.Attacker.Units = new Units();
        }

        /// <summary>
        ///     Adds the command.
        /// </summary>
        /// <param name="Command">The command.</param>
        internal void Add_Command(Battle_Command Command)
        {
            this.Commands.Add(this, Command);
        }

        /// <summary>
        ///     Sets the replay informations.
        /// </summary>
        internal void Set_Replay_Info()
        {
            foreach (Slot _Slot in this.Defender.Resources)
            {
                this.Replay_Info.Loot.Add(new[] {_Slot.Data, _Slot.Count}); // For Debug
                this.Replay_Info.Available_Loot.Add(new[] {_Slot.Data, _Slot.Count});
            }

            this.Replay_Info.Stats.Home_ID[0] = (int) this.Defender.HighID;
            this.Replay_Info.Stats.Home_ID[1] = (int) this.Defender.LowID;
            this.Replay_Info.Stats.Original_Attacker_Score = this.Attacker.Trophies;
            this.Replay_Info.Stats.Original_Defender_Score = this.Defender.Trophies;
        }

        internal void EvaluateOutcome()
        {
            if (this.Commands == null)
            {
                return;
            }

            List<Battle_Command> orderedCommands = this.Commands
                .OrderBy(c => c.Command_Base.Base.Tick)
                .ToList();

            if (orderedCommands.Count > 0)
            {
                this.BattleTick = orderedCommands[orderedCommands.Count - 1].Command_Base.Base.Tick;
            }

            List<BuildingSnapshot> buildings = this.CreateBuildingSnapshots();

            if (buildings.Count > 0)
            {
                Dictionary<int, BuildingSnapshot> buildingsByInstance = buildings
                    .Where(b => b.HasInstanceId)
                    .ToDictionary(b => b.InstanceId, b => b);

                Dictionary<int, List<BuildingSnapshot>> buildingBuckets = this.BuildBucketsByDataId(buildings);

                foreach (Battle_Command command in orderedCommands)
                {
                    int data = command.Command_Base.Data;
                    if (data <= 0)
                    {
                        continue;
                    }

                    int classId = GlobalID.GetClassID(data);

                    if (classId == 500)
                    {
                        if (buildingsByInstance.TryGetValue(data, out BuildingSnapshot byInstance))
                        {
                            byInstance.Destroyed = true;
                        }

                        continue;
                    }

                    if (classId == 1 && buildingBuckets.TryGetValue(data, out List<BuildingSnapshot> bucket))
                    {
                        BuildingSnapshot snapshot = this.MatchBuilding(bucket, command.Command_Base.X, command.Command_Base.Y);
                        if (snapshot != null)
                        {
                            snapshot.Destroyed = true;
                        }
                    }
                }

                int totalHitpoints = buildings.Sum(b => b.Hitpoints);
                int destroyedHitpoints = buildings.Where(b => b.Destroyed).Sum(b => b.Hitpoints);

                int destruction = 0;
                if (totalHitpoints > 0)
                {
                    destruction = (int)Math.Round(destroyedHitpoints * 100.0 / totalHitpoints);
                }

                destruction = Math.Max(0, Math.Min(100, destruction));

                bool townHallDestroyed = buildings.Any(b => b.IsTownHall && b.Destroyed);

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

                this.Replay_Info.Stats.TownHall_Destroyed = townHallDestroyed;
                this.Replay_Info.Stats.Destruction_Percentate = destruction;
                this.Replay_Info.Stats.Attacker_Stars = Math.Max(0, Math.Min(3, stars));
            }

            this.Preparation_Time = Math.Max(0, this.Preparation_Time);
            this.Attack_Time = Math.Max(0, this.Attack_Time);
            this.Replay_Info.Stats.Battle_Ended = true;

            int battleTime = (int)Math.Round(180 - this.Attack_Time);
            if (battleTime < 0)
            {
                battleTime = 0;
            }
            else if (battleTime > 180)
            {
                battleTime = 180;
            }

            this.Replay_Info.Stats.Battle_Time = battleTime;
        }

        Dictionary<int, List<BuildingSnapshot>> BuildBucketsByDataId(IEnumerable<BuildingSnapshot> buildings)
        {
            var buckets = new Dictionary<int, List<BuildingSnapshot>>();

            foreach (BuildingSnapshot building in buildings)
            {
                if (!buckets.TryGetValue(building.DataId, out List<BuildingSnapshot> list))
                {
                    list = new List<BuildingSnapshot>();
                    buckets.Add(building.DataId, list);
                }

                list.Add(building);
            }

            return buckets;
        }

        BuildingSnapshot MatchBuilding(List<BuildingSnapshot> bucket, int x, int y)
        {
            BuildingSnapshot match = null;
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

        List<BuildingSnapshot> CreateBuildingSnapshots()
        {
            if (this.Base == null)
            {
                this.buildingPrototypes = null;
                this.prototypeSource = null;
                return new List<BuildingSnapshot>();
            }

            if (!ReferenceEquals(this.Base, this.prototypeSource) || this.buildingPrototypes == null)
            {
                this.buildingPrototypes = this.BuildBuildingPrototypes();
                this.prototypeSource = this.Base;
            }

            if (this.buildingPrototypes == null)
            {
                return new List<BuildingSnapshot>();
            }

            var snapshots = new List<BuildingSnapshot>(this.buildingPrototypes.Count);

            foreach (BuildingPrototype prototype in this.buildingPrototypes)
            {
                snapshots.Add(prototype.CreateSnapshot());
            }

            return snapshots;
        }

        List<BuildingPrototype> BuildBuildingPrototypes()
        {
            var prototypes = new List<BuildingPrototype>();

            JArray buildingsArray = this.Base["buildings"] as JArray;
            if (buildingsArray == null)
            {
                return prototypes;
            }

            int generatedInstanceId = 500000000;

            foreach (JToken token in buildingsArray)
            {
                JObject buildingObject = token as JObject;
                if (buildingObject == null)
                {
                    continue;
                }

                int dataId = buildingObject.Value<int?>("data") ?? 0;
                if (dataId == 0)
                {
                    continue;
                }

                BuildingData buildingData = CSVManager.DataTables.GetDataById(dataId) as BuildingData;
                if (buildingData == null || buildingData.Hitpoints == null || buildingData.Hitpoints.Count == 0)
                {
                    continue;
                }

                int level = buildingObject.Value<int?>("lvl") ?? 0;
                if (level >= buildingData.Hitpoints.Count)
                {
                    level = buildingData.Hitpoints.Count - 1;
                }

                int hitpoints = buildingData.Hitpoints[level];
                if (hitpoints <= 0)
                {
                    continue;
                }

                int instanceId = buildingObject.Value<int?>("id") ?? generatedInstanceId++;

                prototypes.Add(new BuildingPrototype
                {
                    InstanceId = instanceId,
                    DataId = dataId,
                    Hitpoints = hitpoints,
                    X = buildingObject.Value<int?>("x") ?? 0,
                    Y = buildingObject.Value<int?>("y") ?? 0,
                    IsTownHall = buildingData.IsTownHall()
                });
            }

            return prototypes;
        }

        class BuildingPrototype
        {
            internal int InstanceId;
            internal int DataId;
            internal int Hitpoints;
            internal bool IsTownHall;
            internal int X;
            internal int Y;

            internal BuildingSnapshot CreateSnapshot()
            {
                return new BuildingSnapshot
                {
                    InstanceId = this.InstanceId,
                    DataId = this.DataId,
                    Hitpoints = this.Hitpoints,
                    IsTownHall = this.IsTownHall,
                    X = this.X,
                    Y = this.Y
                };
            }
        }

        class BuildingSnapshot
        {
            internal int InstanceId;
            internal int DataId;
            internal int Hitpoints;
            internal bool IsTownHall;
            internal bool Destroyed;
            internal int X;
            internal int Y;

            internal bool HasInstanceId => InstanceId > 0;
        }

    }
}
