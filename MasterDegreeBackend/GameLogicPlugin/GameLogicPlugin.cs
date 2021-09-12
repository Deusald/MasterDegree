// MIT License

// MasterDegree:
// Copyright (c) 2020 Adam "Deusald" Orliński

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Net;
using System.Threading.Tasks;
using Agones;
using Agones.Dev.Sdk;
using DarkRift;
using DarkRift.Server;
using DeusaldSharp;
using GameLogicCommon;
using GuerrillaNtp;

namespace GameLogic
{
    public class GameLogicPlugin : Plugin
    {
        #region Properties

        public override Version Version    => new Version(1, 0, 0);
        public override bool    ThreadSafe => true;

        #endregion Properties

        #region Variables

        private long        _ServerClockStartTime;
        private ServerClock _ServerClock;
        private AgonesSDK   _Agones;

        private readonly Logger      _Logger;
        private readonly GameLogic   _GameLogic;
        private readonly ServerClock _HeathClock;
        private readonly bool        _IsManualEnv;

        private const ushort _ServerTicksPerSecond = 15;
        private const ushort _ServerTickLogEveryX  = _ServerTicksPerSecond * 30;

        #endregion Variables

        #region Special Methods

        public GameLogicPlugin(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            _Logger                                      =  Logger;
            _GameLogic                                   =  new GameLogic(_Logger, _ServerTicksPerSecond, true);
            _GameLogic.GameOver                          += GameLogicOnGameOver;
            pluginLoadData.ClientManager.ClientConnected += OnClientConnected;
            _IsManualEnv                                 =  Convert.ToBoolean(Environment.GetEnvironmentVariable("MANUAL"));

            if (_IsManualEnv)
            {
                StartGameLoop();

                int bots = Convert.ToInt32(Environment.GetEnvironmentVariable("BOTS"));

                for (int i = 0; i < bots; ++i)
                {
                    _GameLogic.SpawnBotPlayer();
                }
            }
            else
            {
                ConnectToAgones().Wait();
                _HeathClock      =  new ServerClock(1, 10000);
                _HeathClock.Tick += (number, time) => Task.Run(HealthSignal);
            }
        }

        #endregion Special Methods

        #region Private Methods

        private void StartGameLoop()
        {
            StartServerClock();
            _GameLogic.ServerClockStartTime = _ServerClockStartTime;
            _Logger.Log("Game loop started.", LogType.Info);
        }

        private async Task ConnectToAgones()
        {
            await Task.Delay(10000);
            _Agones = new AgonesSDK();
            bool ok = await _Agones.ConnectAsync();

            if (!ok)
            {
                _Logger.Log("Couldn't connect to Agones!", LogType.Error);
                throw new Exception("Couldn't connect to Agones!");
            }

            _Logger.Log("Connected to Agones!", LogType.Info);
            await _Agones.ReadyAsync();
            _Logger.Log("Set Agones status to Ready.", LogType.Info);
            _Agones.WatchGameServer(GameServerUpdate);
            GameServer gs = await _Agones.GetGameServerAsync();

            if (gs.ObjectMeta.Labels.ContainsKey("bot"))
            {
                await _Agones.AllocateAsync();
                _GameLogic.SpawnBotPlayer();
                _GameLogic.SpawnBotPlayer();
                StartGameLoop();
                _GameLogic.StartGame();
            }
        }

        private async Task HealthSignal()
        {
            await _Agones.HealthAsync();
        }

        private void GameServerUpdate(GameServer gameServer)
        {
            if (gameServer.Status.State != "Allocated") return;
            if (_ServerClock != null) return;

            StartGameLoop();
        }

        private void StartServerClock()
        {
            TimeSpan offset;

            using (var ntp = new NtpClient(Dns.GetHostAddresses("pool.ntp.org")[0]))
            {
                offset = ntp.GetCorrectionOffset();
            }

            DateTime accurateTime = DateTime.UtcNow + offset;
            _ServerClockStartTime =  accurateTime.Ticks;
            _ServerClock          =  new ServerClock(_ServerTicksPerSecond, _ServerTickLogEveryX);
            _ServerClock.Log      += s => _Logger.Log(s, LogType.Info);
            _ServerClock.Tick     += ServerClockTick;
        }

        private void ServerClockTick(ulong frameNumber, double deltaTime)
        {
            _GameLogic.Update((float)deltaTime);
        }

        private void OnClientConnected(object sender, ClientConnectedEventArgs e)
        {
            _GameLogic.OnClientConnected(e.Client);
            e.Client.MessageReceived += OnMessageReceived;
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (e.Tag == (ushort)Messages.MessageId.KillGame)
            {
                GameLogicOnGameOver();
            }

            if (e.Tag == (ushort)Messages.MessageId.SpawnBots)
            {
                _Logger.Log("Spawning 2 bots.", LogType.Info);

                for (int i = 0; i < 2; ++i)
                {
                    _GameLogic.SpawnBotPlayer();
                }

                _GameLogic.StartGame();
            }
        }

        private void GameLogicOnGameOver()
        {
            if (_IsManualEnv)
            {
                Environment.Exit(0);
            }
            else
            {
                Task.Run(ShutDownServer);
            }
        }

        private async Task ShutDownServer()
        {
            await _Agones.ShutDownAsync();
        }

        #endregion Private Methods
    }
}