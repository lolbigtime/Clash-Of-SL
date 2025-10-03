using CSS.Files.Logic;
using Newtonsoft.Json.Linq;
using UCS.Logic;
using UCS.Logic.JSONProperty.Item;
using Xunit;

namespace Clash_SL_Server.Tests
{
    public class BattleOutcomeTests
    {
        [Fact]
        public void EvaluateOutcomeMarksClass500StructureDestroyed()
        {
            var battle = new Battle
            {
                Base = new JObject
                {
                    ["buildings"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = 500000123,
                            ["data"] = 0
                        }
                    }
                }
            };

            var command = new Battle_Command
            {
                Command_Base = new Command_Base
                {
                    Data = 500000123
                }
            };

            battle.Commands.Add(battle, command);

            battle.EvaluateOutcome();

            var building = (JObject)((JArray)battle.Base["buildings"])[0];
            Assert.True(building.Value<bool>("destroyed"));
        }
    }
}
