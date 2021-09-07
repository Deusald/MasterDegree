using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DarkRift;
using DarkRift.Server;
using GameLogicCommon;
using k8s;
using Microsoft.Rest;
using Newtonsoft.Json;

namespace GameServersControllerPlugin
{
    public class GameServersControllerPlugin : Plugin
    {
        #region Types

        private class GameServerAllocation
        {
            public string ApiVersion { get; }
            public string Kind       { get; }

            public GameServerAllocation()
            {
                ApiVersion = "allocation.agones.dev/v1";
                Kind       = "GameServerAllocation";
            }
        }

        private class GameServerAllocationResult
        {
            internal class StatusData
            {
                internal class PortData
                {
                    public string Name { get; set; }
                    public int    Port { get; set; }
                }

                public string     State          { get; set; }
                public string     GameServerName { get; set; }
                public PortData[] Ports          { get; set; }
                public string     Address        { get; set; }
                public string     NodeName       { get; set; }
            }

            public StatusData Status { get; set; }
        }

        private class Game
        {
            public string Address { get; set; }
            public int    Port    { get; set; }
        }

        #endregion Types

        #region Properties

        public override Version Version    => new Version(0, 2, 0);
        public override bool    ThreadSafe => true;

        #endregion Properties

        #region Variables

        private readonly Logger                _Logger;
        private readonly Kubernetes            _Kubernetes;
        private readonly Random                _Random;
        private readonly Dictionary<int, Game> _Games;

        #endregion Variables

        #region Special Methods

        public GameServersControllerPlugin(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            _Logger = Logger;
            _Games  = new Dictionary<int, Game>();
            _Random = new Random();
            bool isManualEnv = Convert.ToBoolean(Environment.GetEnvironmentVariable("MANUAL"));

            KubernetesClientConfiguration config;

            if (isManualEnv)
            {
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
                if (config.CurrentContext != "masterdegree") throw new Exception("Current context is not 'masterdegree'!");
            }
            else
            {
                config = KubernetesClientConfiguration.InClusterConfig();
            }

            _Kubernetes                                  =  new Kubernetes(config);
            pluginLoadData.ClientManager.ClientConnected += OnClientConnected;
        }

        #endregion Special Methods

        #region Private Methods

        private void OnClientConnected(object sender, ClientConnectedEventArgs e)
        {
            _Logger.Log("Client connected.", LogType.Info);
            e.Client.MessageReceived += OnMessageReceived;
        }

        private void OnMessageReceived(object o, MessageReceivedEventArgs e)
        {
            _Logger.Log($"Received message with tag {e.Tag}", LogType.Info);

            using (Message netMessage = e.GetMessage())
            {
                using (DarkRiftReader reader = netMessage.GetReader())
                {
                    switch (e.Tag)
                    {
                        case (ushort)Messages.MessageId.AllocateGame:
                        {
                            _Logger.Log("Received allocate game message", LogType.Info);
                            Task.Run(() => AllocateNewGame(e.Client));
                            break;
                        }
                        case (ushort)Messages.MessageId.GetAllocatedGameData:
                        {
                            Messages.GetAllocatedGameData msg = new Messages.GetAllocatedGameData();
                            msg.Read(reader);
                            _Logger.Log($"Received GetAllocatedGameData with code {msg.Code}", LogType.Info);

                            Messages.AllocatedGameData msgResponse;

                            if (!_Games.ContainsKey(msg.Code))
                            {
                                msgResponse = new Messages.AllocatedGameData
                                {
                                    Address = "",
                                    Port    = 0,
                                    Code    = 0
                                };
                                _Logger.Log($"Can't find {msg.Code} game", LogType.Info);
                            }
                            else
                            {
                                msgResponse = new Messages.AllocatedGameData
                                {
                                    Address = _Games[msg.Code].Address,
                                    Port    = _Games[msg.Code].Port,
                                    Code    = msg.Code
                                };
                                _Logger.Log("Found the game, sending details", LogType.Info);
                            }

                            SendMessage(e.Client, msgResponse);

                            break;
                        }
                    }
                }
            }
        }

        private async Task AllocateNewGame(IClient client)
        {
            GameServerAllocation gameServerAllocation = new GameServerAllocation();
            HttpOperationResponse<object> result =
                await _Kubernetes.CreateNamespacedCustomObjectWithHttpMessagesAsync(gameServerAllocation, "allocation.agones.dev", "v1", "default",
                    "gameserverallocations");
            _Logger.Log(result.Response.StatusCode.ToString(), LogType.Info);
            GameServerAllocationResult allocationResult = JsonConvert.DeserializeObject<GameServerAllocationResult>(result.Body.ToString()!);

            if (allocationResult!.Status.State == "Allocated")
            {
                int code = _Random.Next(100000);
                Game game = new Game
                {
                    Address = allocationResult.Status.Address,
                    Port    = allocationResult.Status.Ports[0].Port
                };
                _Games[code] = game;

                Messages.AllocatedGameData msg = new Messages.AllocatedGameData
                {
                    Address = game.Address,
                    Port    = game.Port,
                    Code    = code
                };

                SendMessage(client, msg);
                _Logger.Log($"Allocated server {game.Address} {game.Port}", LogType.Info);
            }
            else
            {
                Messages.AllocatedGameData msg = new Messages.AllocatedGameData
                {
                    Address = "",
                    Port    = 0,
                    Code    = 0
                };

                _Logger.Log("Allocate game failed", LogType.Info);
                SendMessage(client, msg);
            }
        }

        private void SendMessage(IClient client, Messages.INetMessage msg)
        {
            using (DarkRiftWriter messageWriter = DarkRiftWriter.Create())
            {
                msg.Write(messageWriter);

                using (Message netMessage = Message.Create((ushort)msg.MessageId, messageWriter))
                {
                    client.SendMessage(netMessage, msg.IsFrequent ? SendMode.Unreliable : SendMode.Reliable);
                }
            }
        }

        #endregion Private Methods
    }
}