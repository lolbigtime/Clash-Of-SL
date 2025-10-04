using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSS.Core;
using CSS.Helpers;
using UCS.Core;
using UCS.Helpers;
using UCS.Logic;
using UCS.Logic.JSONProperty;
using UCS.Logic.JSONProperty.Item;
using UCS.Simulation;

namespace CSS.WebAPI
{
    internal class API
    {
        private static readonly IPHostEntry ipHostInfo = Dns.Resolve(Dns.GetHostName());
        private static HttpListener Listener;
        private static int Port = Utils.ParseConfigInt("APIPort"); // TODO: Add it to the config File
        private static readonly string IP = ipHostInfo.AddressList[0].ToString();
        private static string URL = "http://" + IP + ":" + Port + "/";

        private const string ApiPath = "api";
        private const string RlPath = "api/rl";

        public static string HTML()
        {
            try
            {
                using (StreamReader sr = new StreamReader("WebAPI/HTML/Statistics.html"))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return "File not Found";
            }
        }

        public API()
        {
            new Thread(() =>
            {
                try
                {
                    if (!HttpListener.IsSupported)
                    {
                        Logger.Say("The current System doesn't support the WebAPI.");
                        return;
                    }

                    if (Port == 80)
                    {
                        Logger.Say("Can't start the API on Port 80 using now default Port(88)");
                        Port = 88;
                        URL = "http://" + IP + ":" + Port + "/";
                    }

                    Listener = new HttpListener();
                    Listener.Prefixes.Add(URL);
                    Listener.Prefixes.Add(URL + "api/");
                    Listener.Prefixes.Add(URL + "api/rl/");
                    Listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
                    Listener.Start();

                    Logger.Say("The WebAPI has been started on '" + Port + "'");

                    ThreadPool.QueueUserWorkItem((o) =>
                    {
                        while (Listener.IsListening)
                        {
                            ThreadPool.QueueUserWorkItem((c) =>
                            {
                                try
                                {
                                    HttpListenerContext ctx = (HttpListenerContext)c;
                                    ProcessContext(ctx);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error("Unhandled API exception");
                                    Logger.WriteError(ex.ToString());
                                }

                            }, Listener.GetContext());
                        }
                    });
                }
                catch (Exception)
                {
                    Logger.Say("Please check if the Port '" + Port + "' is not in use.");
                }
            }).Start();
        }

        public static void Stop()
        {
            Listener.Stop();
        }

        public static string GetStatisticHTML()
        {
            try
            {
                return HTML()
                    .Replace("%ONLINEPLAYERS%", ResourcesManager.m_vOnlinePlayers.Count.ToString())
                    .Replace("%INMEMORYPLAYERS%", ResourcesManager.m_vInMemoryLevels.Count.ToString())
                    .Replace("%INMEMORYALLIANCES%", ResourcesManager.GetInMemoryAlliances().Count.ToString())
                    .Replace("%TOTALCONNECTIONS%", ResourcesManager.GetConnectedClients().Count.ToString());
            }
            catch (Exception)
            {
                return "The server encountered an internal error or misconfiguration and was unable to complete your request. (500)";
            }
        }

        public static string GetjsonAPI()
        {
            JObject _Data = new JObject
            {
                {"online_players", ResourcesManager.m_vOnlinePlayers.Count.ToString()},
                {"in_mem_players", ResourcesManager.m_vInMemoryLevels.Count.ToString()},
                {"in_mem_alliances", ResourcesManager.GetInMemoryAlliances().Count.ToString()},
                {"connected_sockets", ResourcesManager.GetConnectedClients().Count.ToString()},
                {"all_players", ObjectManager.GetMaxPlayerID()},
                {"all_clans", ObjectManager.GetMaxAllianceID()}
            };
            return JsonConvert.SerializeObject(_Data, Formatting.Indented);
        }

        private static void ProcessContext(HttpListenerContext context)
        {
            try
            {
                string relativePath = context.Request?.Url?.AbsolutePath ?? "/";
                relativePath = relativePath.Trim('/');

                if (string.Equals(relativePath, string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    SendHtml(context, GetStatisticHTML());
                    return;
                }

                if (string.Equals(relativePath, ApiPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relativePath, ApiPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    SendJson(context, HttpStatusCode.OK, GetjsonAPI());
                    return;
                }

                if (string.Equals(relativePath, RlPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relativePath, RlPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    HandleRlRequest(context);
                    return;
                }

                SendHtml(context, GetStatisticHTML());
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled API exception");
                Logger.WriteError(ex.ToString());
                SendError(context, HttpStatusCode.InternalServerError, "Internal server error");
            }
        }

        private static void HandleRlRequest(HttpListenerContext context)
        {
            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                SendError(context, HttpStatusCode.MethodNotAllowed, "Only POST requests are supported for the RL endpoint.");
                return;
            }

            string body;

            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                SendError(context, HttpStatusCode.BadRequest, "Empty request body.");
                return;
            }

            JObject payload;

            try
            {
                payload = JObject.Parse(body);
            }
            catch (JsonException)
            {
                SendError(context, HttpStatusCode.BadRequest, "Invalid JSON payload.");
                return;
            }

            try
            {
                JObject response = ExecuteRlSimulation(payload);
                SendJson(context, HttpStatusCode.OK, response.ToString(Formatting.None));
            }
            catch (ArgumentException ex)
            {
                SendError(context, HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                Logger.Error("RL endpoint failed");
                Logger.WriteError(ex.ToString());
                SendError(context, HttpStatusCode.InternalServerError, "Simulation failed. See server logs for details.");
            }
        }

        private static JObject ExecuteRlSimulation(JObject payload)
        {
            EnsureGameFilesLoaded();

            string baseLayout = ExtractLayout(payload["baseLayout"]);

            if (string.IsNullOrWhiteSpace(baseLayout))
            {
                throw new ArgumentException("baseLayout is required.");
            }

            string attackerLayout = ExtractLayout(payload["attackerLayout"]);

            List<List<Battle_Command>> commandSets = ParseCommandSets(payload);

            int seed = payload.Value<int?>("battleSeed") ?? Environment.TickCount;
            double? preparationTime = payload.Value<double?>("preparationTime");
            double? attackTime = payload.Value<double?>("attackTime");
            bool includeReplay = payload.Value<bool?>("includeReplay") ?? true;
            bool includeBattleState = payload.Value<bool?>("includeBattleState") ?? false;
            bool suppressTickLogging = payload.Value<bool?>("suppressTickLogging") ?? true;

            JObject weightToken = payload["rewardWeights"] as JObject ?? new JObject();
            double starWeight = weightToken.Value<double?>("stars") ?? 10d;
            double destructionWeight = weightToken.Value<double?>("destruction") ?? 1d;
            double timeBonusWeight = weightToken.Value<double?>("timeBonus") ?? 0.05d;

            bool serializePayloads = includeBattleState || includeReplay;

            var runner = new BatchAttackRunner();
            var options = new BatchAttackRunnerOptions
            {
                PopulateReplayInfo = true,
                ResetBattleCommands = true,
                ResetReplayInfo = true,
                SerializePayloads = serializePayloads,
                SuppressTickLogging = suppressTickLogging
            };

            var episodes = new JArray();

            for (int i = 0; i < commandSets.Count; i++)
            {
                Level defender = new Level();
                defender.SetHome(baseLayout);

                Level attacker = new Level();

                if (!string.IsNullOrWhiteSpace(attackerLayout))
                {
                    attacker.SetHome(attackerLayout);
                }

                var battle = new Battle(seed + i, attacker, defender, false);

                if (preparationTime.HasValue)
                {
                    battle.Preparation_Time = preparationTime.Value;
                }

                if (attackTime.HasValue)
                {
                    battle.Attack_Time = attackTime.Value;
                }

                var workItem = new BatchAttackWorkItem(battle, commandSets[i]);
                BatchAttackResult result = runner.Run(new[] { workItem }, options).FirstOrDefault();

                if (result == null)
                {
                    continue;
                }

                Replay_Info replay = result.Replay ?? new Replay_Info();
                Replay_Stats stats = replay.Stats ?? new Replay_Stats();

                double attackWindow = attackTime ?? 180d;
                double reward = (stats.Attacker_Stars * starWeight)
                                + (stats.Destruction_Percentate * destructionWeight)
                                + ((attackWindow - stats.Battle_Time) * timeBonusWeight);

                var episode = new JObject
                {
                    ["episodeIndex"] = i,
                    ["reward"] = reward,
                    ["stats"] = new JObject
                    {
                        ["attackerStars"] = stats.Attacker_Stars,
                        ["destructionPercentage"] = stats.Destruction_Percentate,
                        ["battleTime"] = stats.Battle_Time,
                        ["townhallDestroyed"] = stats.TownHall_Destroyed
                    },
                    ["commands"] = JArray.FromObject(commandSets[i])
                };

                if (includeReplay)
                {
                    string replayJson = result.ReplayJson ?? BattleSerializers.Serialize(replay);
                    episode["replay"] = string.IsNullOrWhiteSpace(replayJson)
                        ? new JObject()
                        : JToken.Parse(replayJson);
                }

                if (includeBattleState)
                {
                    string battleJson = result.BattleJson ?? BattleSerializers.Serialize(result.Battle);
                    episode["battle"] = string.IsNullOrWhiteSpace(battleJson)
                        ? new JObject()
                        : JToken.Parse(battleJson);
                }

                episodes.Add(episode);
            }

            var response = new JObject
            {
                ["episodeCount"] = episodes.Count,
                ["episodes"] = episodes,
                ["metadata"] = new JObject
                {
                    ["battleSeed"] = seed,
                    ["includeReplay"] = includeReplay,
                    ["includeBattleState"] = includeBattleState
                }
            };

            return response;
        }

        private static List<List<Battle_Command>> ParseCommandSets(JObject payload)
        {
            var commandSets = new List<List<Battle_Command>>();
            JToken commandsToken = payload["commandSets"] ?? payload["commands"];

            if (commandsToken is JArray array)
            {
                foreach (JToken token in array)
                {
                    if (token is JArray inner)
                    {
                        List<Battle_Command> commands = inner.ToObject<List<Battle_Command>>() ?? new List<Battle_Command>();
                        commandSets.Add(commands);
                    }
                }
            }

            if (commandSets.Count == 0)
            {
                commandSets.Add(new List<Battle_Command>());
            }

            return commandSets;
        }

        private static string ExtractLayout(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            return token.ToString(Formatting.None);
        }

        private static void EnsureGameFilesLoaded()
        {
            if (CSVManager.DataTables == null)
            {
                _ = new CSVManager();
            }
        }

        private static void SendHtml(HttpListenerContext context, string html)
        {
            SendResponse(context, HttpStatusCode.OK, "text/html", html ?? string.Empty);
        }

        private static void SendJson(HttpListenerContext context, HttpStatusCode statusCode, string payload)
        {
            SendResponse(context, statusCode, "application/json", payload ?? string.Empty);
        }

        private static void SendError(HttpListenerContext context, HttpStatusCode statusCode, string message)
        {
            var error = new JObject
            {
                ["error"] = message ?? string.Empty
            };

            SendJson(context, statusCode, error.ToString(Formatting.None));
        }

        private static void SendResponse(HttpListenerContext context, HttpStatusCode statusCode, string contentType, string content)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content ?? string.Empty);
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
    }
}
