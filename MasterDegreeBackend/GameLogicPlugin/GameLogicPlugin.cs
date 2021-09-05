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
using DarkRift;
using DarkRift.Server;
using DeusaldSharp;
using GuerrillaNtp;

namespace GameLogic
{
    public class GameLogicPlugin : Plugin
    {
        #region Properties

        public override Version Version    => new Version(0, 1, 1);
        public override bool    ThreadSafe => true;

        #endregion Properties

        #region Variables

        private long        _ServerClockStartTime;
        private ServerClock _ServerClock;

        private readonly Logger    _Logger;
        private readonly GameLogic _GameLogic;

        private const ushort _ServerTicksPerSecond = 15;
        private const ushort _ServerTickLogEveryX  = _ServerTicksPerSecond * 30;

        #endregion Variables

        #region Special Methods

        public GameLogicPlugin(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            _Logger    = Logger;
            _GameLogic = new GameLogic(_Logger, _ServerTicksPerSecond);
            StartServerClock();
            _GameLogic.ServerClockStartTime              =  _ServerClockStartTime;
            pluginLoadData.ClientManager.ClientConnected += OnClientConnected;
        }

        #endregion Special Methods

        #region Private Methods

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
        }

        #endregion Private Methods
    }
}