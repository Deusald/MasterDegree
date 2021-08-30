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
using System.Collections.Generic;
using Box2D;
using DarkRift;
using DarkRift.Server;
using DeusaldSharp;
using GameLogicCommon;
using SharpBox2D;

namespace GameLogic
{
    internal class GameLogic
    {
        #region Types

        public class Player
        {
            public byte           Id                { get; }
            public IClient        Client            { get; set; }
            public IPhysicsObject MainPhysicsObj    { get; set; }
            public IPhysicsObject LagCompPhysicsObj { get; set; }

            public Player(byte id)
            {
                Id = id;
            }
        }

        public class GameObject
        {
            public IPhysicsObject MainPhysicsObj    { get; set; }
            public IPhysicsObject LagCompPhysicsObj { get; set; }
            public uint           FrameToDestroy    { get; set; }
            public sbyte          Owner             { get; set; }
            public byte           Power             { get; set; }

            public void Destroy()
            {
                MainPhysicsObj?.Destroy();
                LagCompPhysicsObj?.Destroy();
            }
        }

        #endregion Types

        #region Properties

        public long ServerClockStartTime { get; set; }

        #endregion Properties

        #region Variables

        private Game.GameState   _GameState;
        private Physics2DControl _MainPhysics;
        private Physics2DControl _LagCompensationPhysics;
        private float            _SimulationAccumulatedTime;
        private uint             _PhysicsTickNumber;

        private readonly object                          _GameLockObject;
        private readonly Logger                          _Logger;
        private readonly bool                            _SpawnDestroyableWalls;
        private readonly ushort                          _NumberOfFramesPerSecond;
        private readonly float                           _FixedDeltaTime;
        private readonly List<Player>                    _Players;
        private readonly Dictionary<ushort, byte>        _ClientIdToPlayerId;
        private readonly Dictionary<Vector2, GameObject> _DestroyableWalls;

        #endregion Variables

        #region Special Methods

        public GameLogic(Logger logger, ushort framesPerSecond)
        {
            _GameLockObject          = new object();
            _Logger                  = logger;
            _SpawnDestroyableWalls   = bool.Parse(Environment.GetEnvironmentVariable("WITH_WALLS")!);
            _NumberOfFramesPerSecond = framesPerSecond;
            _FixedDeltaTime          = 1f / _NumberOfFramesPerSecond;
            _GameState               = Game.GameState.BeforeStart;
            _Players                 = new List<Player>();
            _ClientIdToPlayerId      = new Dictionary<ushort, byte>();
            _DestroyableWalls        = new Dictionary<Vector2, GameObject>();

            InitPhysics();
            FillDestroyableWalls();
        }

        #endregion Special Methods

        #region Public Methods

        public void Update(float deltaTime)
        {
            lock (_GameLockObject)
            {
                _SimulationAccumulatedTime += deltaTime;

                while (_SimulationAccumulatedTime >= _FixedDeltaTime)
                {
                    _SimulationAccumulatedTime -= _FixedDeltaTime;
                    ++_PhysicsTickNumber;
                    // Process player input
                    FixedUpdate();
                    _MainPhysics.Step();
                }
            }
        }

        public void OnClientConnected(IClient client)
        {
            lock (_GameLockObject)
            {
                // Max players is 4 => for each of 4 map corners
                if (_Players.Count == 4) return;

                // Game already started or ended - can't join
                if (_GameState != Game.GameState.BeforeStart) return;

                byte playerId = (byte)_Players.Count;

                // Create player
                Player player = new Player(playerId)
                {
                    Client            = client,
                    MainPhysicsObj    = Physics.SpawnPlayer(playerId, _MainPhysics),
                    LagCompPhysicsObj = Physics.SpawnPlayer(playerId, _LagCompensationPhysics)
                };

                _Players.Add(player);
                _ClientIdToPlayerId.Add(client.ID, playerId);
                client.MessageReceived += OnMessageReceivedFromPlayer;

                Messages.PlayerInitMsg playerInitMsg = new Messages.PlayerInitMsg
                {
                    WallSpawned           = _SpawnDestroyableWalls,
                    YourId                = playerId,
                    ServerClockStartTimer = ServerClockStartTime
                };

                SendMessageToPlayer(playerId, playerInitMsg);

                Messages.SpawnPlayer spawnPlayerMsg = new Messages.SpawnPlayer
                {
                    PlayerId = playerId
                };

                SendMessageToAllPlayers(spawnPlayerMsg);
            }
        }

        #endregion Public Methods

        #region Private Methods

        #region Init

        private void InitPhysics()
        {
            Box2dNativeLoader.LoadNativeLibrary(Box2dNativeLoader.System.Windows);
            _MainPhysics            = new Physics2DControl(_NumberOfFramesPerSecond, Vector2.Zero);
            _LagCompensationPhysics = new Physics2DControl(_NumberOfFramesPerSecond, Vector2.Zero);

            Physics.InitOuterWalls(_MainPhysics);
            Physics.InitOuterWalls(_LagCompensationPhysics);
            Physics.FillInnerWalls(_MainPhysics);
            Physics.FillInnerWalls(_LagCompensationPhysics);
        }

        private void FillDestroyableWalls()
        {
            if (!_SpawnDestroyableWalls) return;

            Game.PhysicsObjectId objectId = new Game.PhysicsObjectId
            {
                Id         = 0,
                ObjectType = Game.ObjectType.DestroyableWall
            };

            Physics.FillDestroyableWalls(vector2 =>
            {
                IPhysicsObject wall = _MainPhysics.CreatePhysicsObject(BodyType.Kinematic, vector2, 0f);
                wall.UserData = objectId;
                wall.AddBoxCollider(1, 1);

                IPhysicsObject wall2 = _LagCompensationPhysics.CreatePhysicsObject(BodyType.Kinematic, vector2, 0f);
                wall2.UserData = objectId;
                wall2.AddBoxCollider(1, 1);

                _DestroyableWalls[vector2] = new GameObject
                {
                    MainPhysicsObj    = wall,
                    LagCompPhysicsObj = wall2,
                    Owner             = -1,
                    Power             = 0,
                    FrameToDestroy    = uint.MaxValue
                };
            });
        }

        private void FixedUpdate()
        {
            SendHeartBeat();
        }

        #endregion Init

        #region Messages

        private void SendMessageToAllPlayers(Messages.INetMessage message)
        {
            using (DarkRiftWriter messageWriter = DarkRiftWriter.Create())
            {
                message.Write(messageWriter);

                using (Message netMessage = Message.Create((ushort)message.MessageId, messageWriter))
                {
                    foreach (Player player in _Players)
                    {
                        player.Client.SendMessage(netMessage, message.IsFrequent ? SendMode.Unreliable : SendMode.Reliable);
                    }
                }
            }
        }

        private void SendMessageToPlayer(int playerId, Messages.INetMessage message)
        {
            using (DarkRiftWriter messageWriter = DarkRiftWriter.Create())
            {
                message.Write(messageWriter);

                using (Message netMessage = Message.Create((ushort)message.MessageId, messageWriter))
                {
                    _Players[playerId].Client.SendMessage(netMessage, message.IsFrequent ? SendMode.Unreliable : SendMode.Reliable);
                }
            }
        }

        private void SendHeartBeat()
        {
            // Heartbeat messages are sent and received every fixed frame to calculate rtt between server and client
            foreach (Player player in _Players)
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    using (Message message = Message.Create((ushort)Messages.MessageId.ServerHeartBeat, writer))
                    {
                        message.MakePingMessage();
                        player.Client.SendMessage(message, SendMode.Unreliable);
                    }
                }
            }
        }

        private void OnMessageReceivedFromPlayer(object sender, MessageReceivedEventArgs e)
        {
            if (e.Tag == (ushort)Messages.MessageId.ClientHeartBeat)
            {
                // Heartbeat messages are sent and received every fixed frame to calculate rtt between server and client
                using (Message netMessage = e.GetMessage())
                {
                    using (DarkRiftWriter messageWriter = DarkRiftWriter.Create())
                    {
                        using (Message acknowledgementMessage = Message.Create((ushort)Messages.MessageId.ClientHeartBeat, messageWriter))
                        {
                            acknowledgementMessage.MakePingAcknowledgementMessage(netMessage);
                            e.Client.SendMessage(acknowledgementMessage, SendMode.Unreliable);
                        }
                    }
                }

                return;
            }
        }

        #endregion Messages

        #endregion Private Methods
    }
}