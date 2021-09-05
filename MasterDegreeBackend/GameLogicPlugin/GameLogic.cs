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
            public byte                               Id                  { get; }
            public IClient                            Client              { get; set; }
            public IPhysicsObject                     MainPhysicsObj      { get; set; }
            public IPhysicsObject                     LagCompPhysicsObj   { get; set; }
            public bool                               IsDead              { get; set; }
            public bool                               HasDetonator        { get; set; }
            public Dictionary<uint, Game.PlayerInput> Inputs              { get; }
            public Dictionary<uint, Vector2>          Positions           { get; }
            public uint                               OldestPositionFrame { get; set; }

            public Player(byte id)
            {
                Id                  = id;
                IsDead              = false;
                Inputs              = new Dictionary<uint, Game.PlayerInput>();
                Positions           = new Dictionary<uint, Vector2>();
                OldestPositionFrame = uint.MaxValue;
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
                    ProcessPlayersInputs();
                    FixedUpdate();
                    _MainPhysics.Step();
                    StoreAllPlayerCurrentPositions();
                    ExplodeAllExpiredBombs();
                    SendAllPlayersPositions();
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
                    MainPhysicsObj    = SpawnPlayer(playerId, _MainPhysics),
                    LagCompPhysicsObj = SpawnPlayer(playerId, _LagCompensationPhysics)
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

            InitOuterWalls(_MainPhysics);
            InitOuterWalls(_LagCompensationPhysics);
            FillInnerWalls(_MainPhysics);
            FillInnerWalls(_LagCompensationPhysics);
        }

        private void FillDestroyableWalls()
        {
            if (!_SpawnDestroyableWalls) return;

            Game.PhysicsObjectId objectId = new Game.PhysicsObjectId
            {
                Id         = 0,
                ObjectType = Game.ObjectType.DestroyableWall
            };

            Game.FillDestroyableWalls(vector2 =>
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

        #endregion Init

        #region Update

        private void ProcessPlayersInputs()
        {
            foreach (Player player in _Players)
            {
                // If we don't have input from that player for current processed frame we must skip him
                if (!player.Inputs.ContainsKey(_PhysicsTickNumber)) continue;
                if (player.IsDead) continue;

                // Move player in direction
                Vector2 dir = player.Inputs[_PhysicsTickNumber].Direction;
                dir *= _FixedDeltaTime * Game.PlayerSpeed;
                player.MainPhysicsObj.MovePosition(player.MainPhysicsObj.Position + dir);

                if (player.Inputs[_PhysicsTickNumber].PutBomb)
                {
                    // PutBomb
                }

                // Detonate all our bombs if player send detonator signal in this frame
                if (player.HasDetonator && player.Inputs[_PhysicsTickNumber].Detonate)
                {
                    // Detonate all bombs
                }

                player.Inputs.Remove(_PhysicsTickNumber);
            }
        }

        private void StoreAllPlayerCurrentPositions()
        {
            // We are storing those positions for lag compensation
            foreach (Player player in _Players)
            {
                player.Positions[_PhysicsTickNumber] = player.MainPhysicsObj.Position;

                if (_PhysicsTickNumber < player.OldestPositionFrame)
                {
                    player.OldestPositionFrame = _PhysicsTickNumber;
                }

                // We store only two seconds of previous positions
                while (player.Positions.Count > _NumberOfFramesPerSecond * 2)
                {
                    if (player.Positions.ContainsKey(player.OldestPositionFrame))
                    {
                        player.Positions.Remove(player.OldestPositionFrame);
                    }

                    ++player.OldestPositionFrame;
                }
            }
        }

        private void ExplodeAllExpiredBombs() { }

        private void SendAllPlayersPositions()
        {
            foreach (Player player in _Players)
            {
                Messages.PlayerPositionMsg msg = new Messages.PlayerPositionMsg
                {
                    PlayerId    = player.Id,
                    Frame       = _PhysicsTickNumber,
                    Position    = player.MainPhysicsObj.Position,
                    PreviousDir = player.Inputs.ContainsKey(_PhysicsTickNumber) ? player.Inputs[_PhysicsTickNumber].Direction : Vector2.Zero
                };
                
                SendMessageToAllPlayers(msg);
            }
        }

        private void FixedUpdate()
        {
            SendHeartBeat();
        }

        #endregion Update

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

        private void SendStartMessage()
        {
            using (DarkRiftWriter messageWriter = DarkRiftWriter.Create())
            {
                using (Message netMessage = Message.Create((ushort)Messages.MessageId.StartGame, messageWriter))
                {
                    foreach (Player player in _Players)
                        player.Client.SendMessage(netMessage, SendMode.Reliable);
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

            lock (_GameLockObject)
            {
                using (Message netMessage = e.GetMessage())
                {
                    using (DarkRiftReader reader = netMessage.GetReader())
                    {
                        switch (e.Tag)
                        {
                            case (ushort)Messages.MessageId.StartGame:
                            {
                                // TODO: uncomment this
                                //if (_Players.Count < 2) break;
                                _GameState = Game.GameState.Running;
                                SendStartMessage();
                                break;
                            }
                            case (ushort)Messages.MessageId.PlayerInput:
                            {
                                if (!_ClientIdToPlayerId.ContainsKey(e.Client.ID)) break;
                                Messages.PlayerInputMsg msg = new Messages.PlayerInputMsg();
                                msg.Read(reader);
                                PlayerInputReceived(msg, _ClientIdToPlayerId[e.Client.ID]);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void PlayerInputReceived(Messages.PlayerInputMsg msg, byte playerId)
        {
            if (_Players[playerId].IsDead) return;

            for (uint i = msg.OldestInputFrame; i <= msg.LastInputFrame; ++i)
            {
                if (_Players[playerId].Inputs.ContainsKey(i)) continue;
                if (!msg.StoredInputs.ContainsKey(i)) continue;
                if (i <= _PhysicsTickNumber) continue;
                _Players[playerId].Inputs.Add(i, msg.StoredInputs[i]);
            }
        }

        #endregion Messages

        #region Physics

        private void InitOuterWalls(Physics2D physics)
        {
            Game.PhysicsObjectId objectId = new Game.PhysicsObjectId
            {
                Id         = 0,
                ObjectType = Game.ObjectType.StaticWall
            };

            IPhysicsObject leftWall = physics.CreatePhysicsObject(BodyType.Static, new Vector2(-3.5f, 0f), 0f);
            leftWall.UserData = objectId;
            leftWall.AddEdgeCollider(new Vector2(-3.5f, -3.5f), new Vector2(-3.5f, 3.5f));

            IPhysicsObject rightWall = physics.CreatePhysicsObject(BodyType.Static, new Vector2(3.5f, 0f), 0f);
            rightWall.UserData = objectId;
            rightWall.AddEdgeCollider(new Vector2(3.5f, -3.5f), new Vector2(3.5f, 3.5f));

            IPhysicsObject upWall = physics.CreatePhysicsObject(BodyType.Static, new Vector2(0f, 3.5f), 0f);
            upWall.UserData = objectId;
            upWall.AddEdgeCollider(new Vector2(-3.5f, 3.5f), new Vector2(3.5f, 3.5f));

            IPhysicsObject downWall = physics.CreatePhysicsObject(BodyType.Static, new Vector2(0f, 3.5f), 0f);
            downWall.UserData = objectId;
            downWall.AddEdgeCollider(new Vector2(-3.5f, -3.5f), new Vector2(3.5f, -3.5f));
        }

        private void FillInnerWalls(Physics2D physics)
        {
            Game.PhysicsObjectId objectId = new Game.PhysicsObjectId
            {
                Id         = 0,
                ObjectType = Game.ObjectType.StaticWall
            };

            for (int x = -2; x <= 2; x += 2)
            {
                for (int y = -2; y <= 2; y += 2)
                {
                    IPhysicsObject wall = physics.CreatePhysicsObject(BodyType.Static, new Vector2(x, y), 0f);
                    wall.UserData = objectId;
                    wall.AddBoxCollider(1, 1);
                }
            }
        }

        private IPhysicsObject SpawnPlayer(byte id, Physics2D physics2D)
        {
            IPhysicsObject player = physics2D.CreatePhysicsObject(BodyType.Dynamic, Game.PlayersSpawnPoints[id], 0);
            player.UserData = new Game.PhysicsObjectId
            {
                ObjectType = Game.ObjectType.Player,
                Id         = id
            };
            player.AddCircleCollider(0.4f);
            return player;
        }

        #endregion Physics

        #endregion Private Methods
    }
}