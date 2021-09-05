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
using Box2D.NetStandard.Dynamics.Bodies;
using DarkRift;
using DarkRift.Client;
using GameLogicCommon;
using GuerrillaNtp;
using SharpBox2D;
using TMPro;
using UnityEngine;
using DVector2 = DeusaldSharp.Vector2;
using Object = UnityEngine.Object;
using Physics2D = SharpBox2D.Physics2D;

namespace MasterDegree
{
    public class GameController : MonoBehaviour
    {
        #region Types

        private class ServerGameObject
        {
            public IPhysicsObject PhysicsObj       { get; set; }
            public GameObject     VisualGameObject { get; set; }
            public uint           FrameToDestroy   { get; set; }
            public sbyte          Owner            { get; set; }
            public byte           Power            { get; set; }
            public bool           Confirmed        { get; set; }

            public void Destroy()
            {
                PhysicsObj?.Destroy();

                if (VisualGameObject != null)
                {
                    Object.Destroy(VisualGameObject);
                }
            }
        }

        private class Player
        {
            public byte           Id                                { get; }
            public IPhysicsObject PhysicsObj                        { get; set; }
            public GameObject     VisualGameObject                  { get; set; }
            public bool           IsDead                            { get; set; }
            public DVector2       LastPosition                      { get; set; }
            public DVector2       LastPositionReceivedFromServer    { get; set; }
            public DVector2       LastPreviousDirReceivedFromServer { get; set; }
            public uint           FrameOfLastReceivedDataFromServer { get; set; }

            public Player(byte id)
            {
                Id     = id;
                IsDead = false;
            }
        }

        private class AwaitingInputs
        {
            public bool     PutBomb   { get; set; }
            public bool     Detonate  { get; set; }
            public DVector2 Direction { get; set; }
        }

        private class Inputs
        {
            public Dictionary<uint, Game.PlayerInput> StoredInputs     { get; }
            public Dictionary<uint, DVector2>         Positions        { get; }
            public uint                               LastInputFrame   { get; set; }
            public uint                               OldestInputFrame { get; set; }

            public Inputs()
            {
                StoredInputs = new Dictionary<uint, Game.PlayerInput>();
                Positions    = new Dictionary<uint, DVector2>();
            }
        }

        #endregion Types

        #region Properties

        public static IPAddress IPAddress { get; set; } = IPAddress.Loopback;
        public static int       Port      { get; set; } = 40000;

        #endregion Properties

        #region Variables

        [SerializeField] private InfoSystem      _InfoSystem;
        [SerializeField] private TextMeshProUGUI _PingText;
        [SerializeField] private GameObject      _DestroyableWallPrefab;
        [SerializeField] private Transform       _DestroyableWallsParent;
        [SerializeField] private GameObject      _PlayerPrefab;
        [SerializeField] private Transform       _PlayersParent;

        private DrClient                               _Client;
        private Physics2DControl                       _Physics;
        private TimeSpan                               _OffsetFromCorrectTime;
        private DateTime                               _ServerStartTime;
        private Game.GameState                         _GameState;
        private byte                                   _MyPlayerId;
        private uint                                   _MyCurrentFrame;
        private uint                                   _LastSimulatedFrame;
        private float                                  _TimeToNextFrame;
        private Inputs                                 _Inputs;
        private Player[]                               _Players;
        private AwaitingInputs                         _AwaitingInputs;
        private Dictionary<DVector2, ServerGameObject> _DestroyableWalls;

        private const int   _FramesPerSecond = 15;
        private const float _FixedDeltaTime  = 1f / _FramesPerSecond;

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
            _AwaitingInputs   = new AwaitingInputs();
            _Inputs           = new Inputs();
            _DestroyableWalls = new Dictionary<DVector2, ServerGameObject>();

            GetTimeOffset();
        }

        private void Start()
        {
            InitPhysics();
            _Client.Connect(IPAddress, Port);
            _Client.MessageReceived += ClientOnMessageReceived;
        }

        private void Update()
        {
            _Client.Update();
            SendStartGame();
            ShowPing();

            if (_GameState != Game.GameState.Running)
            {
                for (int i = 0; i < 1000; ++i)
                {
                    _Physics.Step();
                }
                
                return;
            }

            try
            {
                ProcessFrames();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private void FixedUpdate()
        {
            SendHeatBeat();
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

        #region Time

        private void GetTimeOffset()
        {
            using (var ntp = new NtpClient(Dns.GetHostAddresses("pool.ntp.org")[0]))
            {
                _OffsetFromCorrectTime = ntp.GetCorrectionOffset();
            }
        }

        private uint GetLastProcessedServerFrame()
        {
            DateTime currentTime                 = DateTime.UtcNow + _OffsetFromCorrectTime;
            double   millisecondsFromServerStart = (currentTime - _ServerStartTime).TotalMilliseconds;
            return (uint)Math.Floor((millisecondsFromServerStart / (_FixedDeltaTime * DeusaldSharp.MathUtils.SecToMilliseconds)));
        }

        private uint GetCurrentClientFrame()
        {
            uint clientFrame = GetLastProcessedServerFrame();
            clientFrame += 1; // Client is always one frame ahead of server
            float playerRtt     = _Client.Client.RoundTripTime.SmoothedRtt;
            uint  framesFromRtt = (uint)Math.Ceiling(playerRtt / _FixedDeltaTime);
            clientFrame += framesFromRtt;
            return clientFrame;
        }

        #endregion Time

        #region Init

        private void InitPhysics()
        {
            _Physics                  =  new Physics2DControl(_FramesPerSecond, DVector2.Zero);
            _Physics.PreCollision     += data => { };
            _Physics.OnCollisionEnter += data => { };
            _Physics.OnCollisionExit  += data => { };

            InitOuterWalls(_Physics);
            FillInnerWalls(_Physics);
        }

        private void SpawnDestroyableWalls()
        {
            Game.PhysicsObjectId objectId = new Game.PhysicsObjectId
            {
                Id         = 0,
                ObjectType = Game.ObjectType.DestroyableWall
            };

            FillDestroyableWalls(vector2 =>
            {
                IPhysicsObject wall = _Physics.CreatePhysicsObject(BodyType.Kinematic, vector2, 0f);
                wall.UserData = objectId;
                wall.AddBoxCollider(1, 1);

                GameObject visualWall = Instantiate(_DestroyableWallPrefab, new Vector3(vector2.x, 0.5f, vector2.y), Quaternion.identity);
                visualWall.transform.SetParent(_DestroyableWallsParent, true);

                _DestroyableWalls[vector2] = new ServerGameObject
                {
                    PhysicsObj       = wall,
                    VisualGameObject = visualWall,
                    Owner            = -1,
                    Power            = 0,
                    FrameToDestroy   = uint.MaxValue,
                    Confirmed        = true
                };
            });
        }

        #endregion Init

        #region Update

        private void SendHeatBeat()
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                using (Message message = Message.Create((ushort)Messages.MessageId.ClientHeartBeat, writer))
                {
                    message.MakePingMessage();
                    _Client.Client.SendMessage(message, SendMode.Unreliable);
                }
            }
        }

        private void SendStartGame()
        {
            if (_GameState != Game.GameState.BeforeStart) return;

            if (!Input.GetKeyDown(KeyCode.Return)) return;

            // TODO: uncomment this
            /*if (_Players.Count(x => x != null) < 2)
            {
                _InfoSystem.AddInfo("Can't start game yet. Minimum number of players is 2.", 1.5f);
                return;
            }*/

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                using (Message message = Message.Create((ushort)Messages.MessageId.StartGame, writer))
                    _Client.Client.SendMessage(message, SendMode.Reliable);
            }
        }

        private void ShowPing()
        {
            if (_Client?.Client == null || _Client.Client.ConnectionState != ConnectionState.Connected) return;
            int ping = Mathf.RoundToInt(_Client.Client.RoundTripTime.SmoothedRtt * 1000f);
            _PingText.text = $"Ping: {ping}ms";
        }

        private void ProcessFrames()
        {
            _MyCurrentFrame = GetCurrentClientFrame();
            GatherInput();
            Reconciliation();

            while (_LastSimulatedFrame < _MyCurrentFrame)
            {
                uint simulatedFrame = _LastSimulatedFrame + 1;
                _TimeToNextFrame = 0f;
                SimulateFrame(simulatedFrame);
                _LastSimulatedFrame = simulatedFrame;
            }

            _TimeToNextFrame += Time.deltaTime;
            MoveVisualRepresentations();
        }

        private void GatherInput()
        {
            _AwaitingInputs.PutBomb   = _AwaitingInputs.PutBomb || Input.GetKeyDown(KeyCode.Space);
            _AwaitingInputs.Detonate  = _AwaitingInputs.Detonate || Input.GetKeyDown(KeyCode.RightControl);
            _AwaitingInputs.Direction = DVector2.Zero;

            if (Input.GetKey(KeyCode.W))
            {
                _AwaitingInputs.Direction = DVector2.Up;
            }
            else if (Input.GetKey(KeyCode.S))
            {
                _AwaitingInputs.Direction = DVector2.Down;
            }

            if (Input.GetKey(KeyCode.A))
            {
                _AwaitingInputs.Direction += DVector2.Left;
            }
            else if (Input.GetKey(KeyCode.D))
            {
                _AwaitingInputs.Direction += DVector2.Right;
            }
        }

        private void Reconciliation()
        {
            uint frameToCheck = _Players[_MyPlayerId].FrameOfLastReceivedDataFromServer;

            if (!_Inputs.Positions.ContainsKey(frameToCheck)) return;

            DVector2 predictedPosition = _Inputs.Positions[frameToCheck];
            DVector2 serverPosition    = _Players[_MyPlayerId].LastPositionReceivedFromServer;

            float missPredictionDistance = DVector2.Distance(predictedPosition, serverPosition);

            if (missPredictionDistance <= 0.01f) return;

            _Inputs.Positions[frameToCheck]           = serverPosition;
            _Players[_MyPlayerId].PhysicsObj.Position = serverPosition;
            _Physics.Step();

            uint frame     = frameToCheck;
            uint lastFrame = _Inputs.LastInputFrame;

            for (; frame <= lastFrame; ++frame)
            {
                if (!_Inputs.StoredInputs.ContainsKey(frame)) continue;
                DVector2 position = _Players[_MyPlayerId].PhysicsObj.Position;
                _Players[_MyPlayerId].PhysicsObj.MovePosition(position + _Inputs.StoredInputs[frame].Direction * (_FixedDeltaTime * Game.PlayerSpeed));
                _Physics.Step();
            }
        }

        private void SimulateFrame(uint frame)
        {
            Game.PlayerInput newInput = new Game.PlayerInput
            {
                Direction = _AwaitingInputs.Direction,
                PutBomb   = _AwaitingInputs.PutBomb,
                Detonate  = _AwaitingInputs.Detonate,
                Frame     = frame
            };

            _Inputs.StoredInputs.Add(frame, newInput);
            _Inputs.LastInputFrame = frame;

            if (_Inputs.OldestInputFrame == 0)
            {
                _Inputs.OldestInputFrame = frame;
            }

            // We only hold one second of frames + 10 in buffer to send to gameServer
            if (_Inputs.LastInputFrame - _Inputs.OldestInputFrame > _FramesPerSecond + 10)
            {
                while (_Inputs.LastInputFrame - _Inputs.OldestInputFrame > _FramesPerSecond + 10)
                {
                    _Inputs.StoredInputs.Remove(_Inputs.OldestInputFrame);
                    _Inputs.Positions.Remove(_Inputs.OldestInputFrame);
                    ++_Inputs.OldestInputFrame;
                }
            }

            _AwaitingInputs.PutBomb  = false;
            _AwaitingInputs.Detonate = false;

            SendInput();

            DVector2 position = _Players[_MyPlayerId].PhysicsObj.Position;
            _Players[_MyPlayerId].PhysicsObj.MovePosition(position + newInput.Direction * (_FixedDeltaTime * Game.PlayerSpeed));
            _Players[_MyPlayerId].LastPosition = _Players[_MyPlayerId].PhysicsObj.Position;

            if (newInput.PutBomb)
            {
                PutBomb(_MyPlayerId, true, Vector2.zero);
            }

            UpdateOtherPlayers();
            RemoveMyNotConfirmedBombs();
            AnimateBombsThatExpired();

            try
            {
                _Physics.Step();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }


            DVector2 nextPosition = _Players[_MyPlayerId].PhysicsObj.Position;
            _Inputs.Positions[frame] = nextPosition;
        }

        private void SendInput()
        {
            if (_Players[_MyPlayerId].IsDead) return;

            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                Messages.PlayerInputMsg playerInput = new Messages.PlayerInputMsg
                {
                    StoredInputs     = _Inputs.StoredInputs,
                    LastInputFrame   = _Inputs.LastInputFrame,
                    OldestInputFrame = _Inputs.OldestInputFrame
                };

                playerInput.Write(writer);

                using (Message message = Message.Create((ushort)Messages.MessageId.PlayerInput, writer))
                    _Client.Client.SendMessage(message, SendMode.Unreliable);
            }
        }

        private void UpdateOtherPlayers()
        {
            for (int i = 0; i < _Players.Length; ++i)
            {
                if (i == _MyPlayerId) continue;
                if (_Players[i] == null) continue;

                float distanceToLastKnownPosition = DVector2.Distance(_Players[i].PhysicsObj.Position, _Players[i].LastPositionReceivedFromServer);

                if (distanceToLastKnownPosition >= 1f)
                {
                    // Something went wrong -> we shouldn't be that far from server position
                    _Players[i].PhysicsObj.Position = _Players[i].LastPositionReceivedFromServer;
                }
                else
                {
                    // Extrapolate player position
                    _Players[i].LastPosition = _Players[i].PhysicsObj.Position;
                    DVector2 newPlayerPosition = _Players[i].LastPositionReceivedFromServer;
                    newPlayerPosition += _Players[i].LastPreviousDirReceivedFromServer *
                                         ((_MyCurrentFrame - _Players[i].FrameOfLastReceivedDataFromServer) * _FixedDeltaTime * Game.PlayerSpeed);
                    _Players[i].PhysicsObj.MovePosition(newPlayerPosition);
                }
            }
        }

        private void RemoveMyNotConfirmedBombs() { }

        private void AnimateBombsThatExpired() { }

        private void MoveVisualRepresentations()
        {
            float percentToNextFrame = _TimeToNextFrame / _FixedDeltaTime;

            for (int i = 0; i < _Players.Length; ++i)
            {
                if (_Players[i] == null) continue;
                // Interpolate players position based on last and current position using time between physics updates
                DVector2 position = DVector2.Lerp(_Players[i].LastPosition, _Players[i].PhysicsObj.Position, percentToNextFrame);
                _Players[i].VisualGameObject.transform.position = new Vector3(position.x, 1f, position.y);
            }
        }

        #endregion Update

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
                        case (ushort)Messages.MessageId.StartGame:
                        {
                            _GameState          = Game.GameState.Running;
                            _LastSimulatedFrame = GetLastProcessedServerFrame();
                            _MyCurrentFrame     = _LastSimulatedFrame;
                            _InfoSystem.AddInfo("Game Started!!!", 0.5f);
                            break;
                        }
                        case (ushort)Messages.MessageId.PlayerPosition:
                        {
                            Messages.PlayerPositionMsg msg = new Messages.PlayerPositionMsg();
                            msg.Read(reader);
                            PlayerPositionMessage(msg);
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
                    PhysicsObj = SpawnPlayer((byte)i, _Physics),
                    IsDead     = false
                };

                GameObject playerVisual = Instantiate(_PlayerPrefab, new Vector3(Game.PlayersSpawnPoints[i].x, 1f, Game.PlayersSpawnPoints[i].y),
                    Quaternion.identity);
                playerVisual.transform.SetParent(_PlayersParent, true);
                player.VisualGameObject = playerVisual;

                MeshRenderer meshRenderer = playerVisual.GetComponent<MeshRenderer>();
                Material     newMaterial  = new Material(meshRenderer.material);
                newMaterial.color     = _PlayersColors[i];
                meshRenderer.material = newMaterial;

                player.LastPosition = player.PhysicsObj.Position;

                _Players[i] = player;
            }
        }

        private void PlayerPositionMessage(Messages.PlayerPositionMsg playerPositionMsg)
        {
            if (_Players[playerPositionMsg.PlayerId] == null) return;

            _Players[playerPositionMsg.PlayerId].LastPositionReceivedFromServer    = playerPositionMsg.Position;
            _Players[playerPositionMsg.PlayerId].LastPreviousDirReceivedFromServer = playerPositionMsg.PreviousDir;
            _Players[playerPositionMsg.PlayerId].FrameOfLastReceivedDataFromServer = playerPositionMsg.Frame;
        }

        #endregion Messages

        #region Bombs And Bonuses

        private void PutBomb(int playerId, bool simulatedBomb, Vector2 bombPos, int frameToDestroy = 0, int power = 0) { }

        #endregion Bombs And Bonuses

        #region Physics

        private void FillDestroyableWalls(Action<DVector2> spawnWallCallback)
        {
            for (int y = 3; y >= -3; --y)
            {
                int maxX = 3;
                int minX = -3;

                // Players needs to have safe corner for the start of the game
                if (y == 3 || y == 2 || y == -3 || y == -2)
                {
                    maxX = 1;
                    minX = -1;
                }

                for (int x = maxX; x >= minX; --x)
                {
                    spawnWallCallback?.Invoke(new DVector2(x, y));
                }
            }
        }

        private void InitOuterWalls(Physics2D physics)
        {
            Game.PhysicsObjectId objectId = new Game.PhysicsObjectId
            {
                Id         = 0,
                ObjectType = Game.ObjectType.StaticWall
            };

            IPhysicsObject leftWall = physics.CreatePhysicsObject(BodyType.Static, DVector2.Zero, 0f);
            leftWall.UserData = objectId;
            leftWall.AddEdgeCollider(new DVector2(-3.5f, -3.5f), new DVector2(-3.5f, 3.5f));

            IPhysicsObject rightWall = physics.CreatePhysicsObject(BodyType.Static, DVector2.Zero, 0f);
            rightWall.UserData = objectId;
            rightWall.AddEdgeCollider(new DVector2(3.5f, -3.5f), new DVector2(3.5f, 3.5f));

            IPhysicsObject upWall = physics.CreatePhysicsObject(BodyType.Static, DVector2.Zero, 0f);
            upWall.UserData = objectId;
            upWall.AddEdgeCollider(new DVector2(-3.5f, 3.5f), new DVector2(3.5f, 3.5f));

            IPhysicsObject downWall = physics.CreatePhysicsObject(BodyType.Static, DVector2.Zero, 0f);
            downWall.UserData = objectId;
            downWall.AddEdgeCollider(new DVector2(-3.5f, -3.5f), new DVector2(3.5f, -3.5f));
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
                    IPhysicsObject wall = physics.CreatePhysicsObject(BodyType.Static, new DVector2(x, y), 0f);
                    wall.UserData = objectId;
                    wall.AddBoxCollider(0.5f, 0.5f);
                }
            }
        }

        private IPhysicsObject SpawnPlayer(byte id, Physics2D physics2D)
        {
            IPhysicsObject player = physics2D.CreatePhysicsObject(BodyType.Dynamic, Game.PlayersSpawnPoints[id], 0);
            player.FixedRotation = true;
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