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
using System.Threading;
using DarkRift;
using DarkRift.Server;

namespace GameServersController
{
    internal static class Program
    {
        #region Variables

        private static DarkRiftServer _Server;

        #endregion Variables

        #region Init Methods

        static void Main()
        {
            ServerSpawnData serverSpawnData = new ServerSpawnData
            {
                Server =
                {
                    MaxStrikes = 3
                },
                PluginSearch =
                {
                    PluginSearchPaths =
                    {
                        new ServerSpawnData.PluginSearchSettings.PluginSearchPath
                        {
                            Source          = "Plugins/",
                            CreateDirectory = false
                        }
                    }
                },
                Logging =
                {
                    LogWriters =
                    {
                        new ServerSpawnData.LoggingSettings.LogWriterSettings
                        {
                            Name      = "ConsoleWriter1",
                            Type      = "ConsoleWriter",
                            LogLevels = new[] { LogType.Info, LogType.Warning, LogType.Error, LogType.Fatal }
                        },
                        new ServerSpawnData.LoggingSettings.LogWriterSettings
                        {
                            Name      = "DebugWriter1",
                            Type      = "DebugWriter",
                            LogLevels = new[] { LogType.Warning, LogType.Error, LogType.Fatal }
                        }
                    }
                },
                Plugins =
                {
                    LoadByDefault = true,
                    Plugins =
                    {
                        new ServerSpawnData.PluginsSettings.PluginSettings
                        {
                            Type = "BadWordFilter",
                            Load = false
                        }
                    }
                },
                Data =
                {
                    Directory = "Data/"
                },
                Listeners =
                {
                    NetworkListeners =
                    {
                        new ServerSpawnData.ListenersSettings.NetworkListenerSettings
                        {
                            Name    = "DefaultNetworkListener",
                            Type    = "BichannelListener",
                            Address = IPAddress.Any,
                            Port    = 39998,
                            Settings = { {"noDelay", "true"} }
                        }
                    }
                },
                DispatcherExecutorThreadID = Thread.CurrentThread.ManagedThreadId
            };

            _Server = new DarkRiftServer(serverSpawnData);
            _Server.StartServer();
            
            new Thread(() => ConsoleLoop(_Server.LogManager.GetLoggerFor("GameServersController"))).Start();

            while (!_Server.Disposed)
            {
                _Server.DispatcherWaitHandle.WaitOne();
                _Server.ExecuteDispatcherTasks();
            }
        }

        #endregion Init Methods

        #region Private Methods

        private static void ConsoleLoop(Logger logger)
        {
            while (!_Server.Disposed)
            {
                string input = Console.ReadLine();

                if (input == null)
                {
                    logger.Log("Input loop turned off.", LogType.Info);
                    return;
                }

                _Server.ExecuteCommand(input);
            }
        }

        #endregion Private Methods
    }
}