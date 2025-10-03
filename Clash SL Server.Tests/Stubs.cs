using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UCS.Logic.JSONProperty;

namespace UCS.Core
{
    internal static class Logger
    {
        public static void Write(string text)
        {
            // Logging is not required for test scenarios.
        }
    }
}

namespace UCS.Logic
{
    internal class Units : List<int[]>
    {
    }

    internal class ClientAvatar
    {
        public List<Slot> Resources { get; } = new List<Slot>();
        public int HighID { get; set; }
        public int LowID { get; set; }
        public int Trophies { get; set; }
        public Units Units { get; set; } = new Units();
    }

    internal class Level
    {
        public ClientAvatar Avatar { get; } = new ClientAvatar();
        public GameObjectManager GameObjectManager { get; } = new GameObjectManager();
    }

    internal class GameObjectManager
    {
        public JObject Save() => new JObject();
    }
}

namespace UCS.Logic.JSONProperty
{
    using System.Collections.Generic;

    internal class Calendar
    {
    }

    internal class Replay_Info
    {
        public List<int[]> Loot { get; } = new List<int[]>();
        public List<int[]> Available_Loot { get; } = new List<int[]>();
        public List<int[]> Units { get; } = new List<int[]>();
        public List<int[]> Spells { get; } = new List<int[]>();
        public List<int[]> Levels { get; } = new List<int[]>();
        public Replay_Stats Stats { get; } = new Replay_Stats();

        public void Add_Unit(int data, int count) { }
        public void Add_Spell(int data, int count) { }
        public void Add_Level(int data, int count) { }
        public void Add_Available_Loot(int data, int count) { }
        public void Add_Loot(int data, int count) { }
    }

    internal class Replay_Stats
    {
        public int[] Home_ID { get; } = new int[2];
        public int Original_Attacker_Score { get; set; }
        public int Original_Defender_Score { get; set; }
        public int Battle_Time { get; set; }
    }
}
