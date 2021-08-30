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
using System.Net;
using Box2D;
using DarkRift;
using DarkRift.Client;
using GameLogicCommon;
using GuerrillaNtp;
using SharpBox2D;
using UnityEngine;
using Physics2D = SharpBox2D.Physics2D;
using Physics = GameLogicCommon.Physics;
using DVector2 = DeusaldSharp.Vector2;

namespace MasterDegree
{
    public class GameController : MonoBehaviour
    {
        #region Types

        public class ServerGameObject
        {
            public IPhysicsObject MainPhysicsObj      { get; set; }
            public IPhysicsObject ServerRecPhysicsObj { get; set; }
            public GameObject     VisualGameObject    { get; set; }
            public uint           FrameToDestroy      { get; set; }
            public sbyte          Owner               { get; set; }
            public byte           Power               { get; set; }
            public bool           Confirmed           { get; set; }

            public void Destroy()
            {
                MainPhysicsObj?.Destroy();
                ServerRecPhysicsObj?.Destroy();
            }
        }

        public class Player
        {
            public byte           Id                  { get; }
            public IPhysicsObject MainPhysicsObj      { get; set; }
            public IPhysicsObject ServerRecPhysicsObj { get; set; }
            public GameObject     VisualGameObject    { get; set; }

            public Player(byte id)
            {
                Id = id;
            }
        }

        #endregion Types

        #region Properties

        public static IPAddress IPAddress { get; set; } = IPAddress.Loopback;
        public static int       Port      { get; set; } = 40000;

        #endregion Properties

        #region Variables

        [SerializeField] private GameObject _DestroyableWallPrefab;
        [SerializeField] private Transform  _DestroyableWallsParent;
        [SerializeField] private GameObject _PlayerPrefab;
        [SerializeField] private Transform  _PlayersParent;

        private DrClient                               _Client;
        private Physics2D                              _MainPhysics;
        private Physics2D                              _ServerReconciliationPhysics;
        private TimeSpan                               _OffsetFromCorrectTime;
        private DateTime                               _ServerStartTime;
        private byte                                   _MyPlayerId;
        private Player[]                               _Players;
        private Dictionary<DVector2, ServerGameObject> _DestroyableWalls;

        private readonly Color32[] _PlayersColors =
        {
            new Color32(255, 57, 45, 255), new Color32(85, 189, 255, 255),
            new Color32(255, 218, 74, 255), new Color32(131, 255, 58, 255)
        };

        #endregion Variables

        #region Special Methods

        private void Awake()
        {
            _Client           = new DrClient();
            _Players          = new Player[4];
            _DestroyableWalls = new Dictionary<DVector2, ServerGameObject>();

            InitPhysics();
            GetTimeOffset();
        }

        private void Start()
        {
            _Client.Connect(IPAddress, Port);
            _Client.MessageReceived += ClientOnMessageReceived;
        }

        private void Update()
        {
            _Client.Update();
        }

        private void OnDestroy()
        {
            _Client.Close();
        }

        private void OnApplicationQuit()
        {
            _Client.Close();
        }

        #endregion Special Methods

        #region Private Methods

        #region Init

        private void InitPhysics()
        {
            Box2dNativeLoader.LoadNativeLibrary(Box2dNativeLoader.System.Windows);
            _MainPhysics                 = new Physics2DControl(25, DeusaldSharp.Vector2.Zero);
            _ServerReconciliationPhysics = new Physics2DControl(25, DeusaldSharp.Vector2.Zero);

            Physics.InitOuterWalls(_MainPhysics);
            Physics.InitOuterWalls(_ServerReconciliationPhysics);
            Physics.FillInnerWalls(_MainPhysics);
            Physics.FillInnerWalls(_ServerReconciliationPhysics);
        }

        private void GetTimeOffset()
        {
            using (var ntp = new NtpClient(Dns.GetHostAddresses("pool.ntp.org")[0]))
            {
                _OffsetFromCorrectTime = ntp.GetCorrectionOffset();
            }
        }

        private void SpawnDestroyableWalls()
        {
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

                IPhysicsObject wall2 = _ServerReconciliationPhysics.CreatePhysicsObject(BodyType.Kinematic, vector2, 0f);
                wall2.UserData = objectId;
                wall2.AddBoxCollider(1, 1);

                GameObject visualWall = Instantiate(_DestroyableWallPrefab, new Vector3(vector2.x, 0.5f, vector2.y), Quaternion.identity);
                visualWall.transform.SetParent(_DestroyableWallsParent, true);

                _DestroyableWalls[vector2] = new ServerGameObject
                {
                    MainPhysicsObj      = wall,
                    ServerRecPhysicsObj = wall2,
                    VisualGameObject    = visualWall,
                    Owner               = -1,
                    Power               = 0,
                    FrameToDestroy      = uint.MaxValue,
                    Confirmed           = true
                };
            });
        }

        #endregion Init

        #region Messages

        private void ClientOnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using (Message netMessage = e.GetMessage())
            {
                using (DarkRiftReader reader = netMessage.GetReader())
                {
                    switch (e.Tag)
                    {
                        case (ushort)Messages.MessageId.ServerHeartBeat:
                        {
                            ServerHeartBeatMessage(netMessage);
                            break;
                        }
                        case (ushort)Messages.MessageId.PlayerInit:
                        {
                            Messages.PlayerInitMsg msg = new Messages.PlayerInitMsg();
                            msg.Read(reader);
                            PlayerInitMessage(msg);
                            break;
                        }
                        case (ushort)Messages.MessageId.SpawnPlayer:
                        {
                            Messages.SpawnPlayer msg = new Messages.SpawnPlayer();
                            msg.Read(reader);
                            SpawnPlayerMessage(msg);
                            break;
                        }
                    }
                }
            }
        }

        private void ServerHeartBeatMessage(Message netMessage)
        {
            // Time to run unity so it doesn't send corrupted packages.
            if (Time.time <= 1f) return;

            using (DarkRiftWriter messageWriter = DarkRiftWriter.Create())
            {
                using (Message acknowledgementMessage = Message.Create((ushort)Messages.MessageId.ServerHeartBeat, messageWriter))
                {
                    acknowledgementMessage.MakePingAcknowledgementMessage(netMessage);
                    _Client.Client.SendMessage(acknowledgementMessage, SendMode.Unreliable);
                }
            }
        }

        private void PlayerInitMessage(Messages.PlayerInitMsg msg)
        {
            _MyPlayerId      = msg.YourId;
            _ServerStartTime = new DateTime(msg.ServerClockStartTimer, DateTimeKind.Utc);

            if (msg.WallSpawned)
            {
                SpawnDestroyableWalls();
            }
        }

        private void SpawnPlayerMessage(Messages.SpawnPlayer msg)
        {
            for (int i = 0; i <= msg.PlayerId; ++i)
            {
                if (_Players[i] != null) continue;

                Player player = new Player((byte)i)
                {
                    MainPhysicsObj      = Physics.SpawnPlayer((byte)i, _MainPhysics),
                    ServerRecPhysicsObj = Physics.SpawnPlayer((byte)i, _ServerReconciliationPhysics)
                };

                GameObject playerVisual = Instantiate(_PlayerPrefab, new Vector3(Physics.PlayersSpawnPoints[i].x, 1f, Physics.PlayersSpawnPoints[i].y),
                    Quaternion.identity);
                playerVisual.transform.SetParent(_PlayersParent, true);
                player.VisualGameObject = playerVisual;

                MeshRenderer meshRenderer = playerVisual.GetComponent<MeshRenderer>();
                Material     newMaterial  = new Material(meshRenderer.material);
                newMaterial.color     = _PlayersColors[i];
                meshRenderer.material = newMaterial;

                _Players[i] = player;
            }
        }

        #endregion Messages

        #endregion Private Methods
    }
}