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
using System.Linq;
using System.Net;
using Box2D.NetStandard.Dynamics.Bodies;
using DarkRift;
using DarkRift.Client;
using DeusaldSharp;
using GameLogicCommon;
using GuerrillaNtp;
using SharpBox2D;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using DVector2 = DeusaldSharp.Vector2;
using Object = UnityEngine.Object;
using Physics2D = SharpBox2D.Physics2D;
using Vector3 = UnityEngine.Vector3;

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
            public bool           Confirmed        { get; set; }
            public int            Collisions       { get; set; }

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
            public IPhysicsObject    PhysicsObj                        { get; set; }
            public GameObject        VisualGameObject                  { get; set; }
            public bool              IsDead                            { get; set; }
            public DVector2          LastPosition                      { get; set; }
            public DVector2          LastPositionReceivedFromServer    { get; set; }
            public DVector2          LastPreviousDirReceivedFromServer { get; set; }
            public uint              FrameOfLastReceivedDataFromServer { get; set; }
            public HashSet<DVector2> Bombs                             { get; }
            public int               MaxBombs                          { get; set; }

            public Player()
            {
                IsDead   = false;
                Bombs    = new HashSet<DVector2>();
                MaxBombs = 1;
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
        public static int       Code      { get; set; } = 0;

        #endregion Properties

        #region Variables

        [SerializeField] private InfoSystem      _InfoSystem;
        [SerializeField] private TextMeshProUGUI _PingText;
        [SerializeField] private TextMeshProUGUI _CodeText;
        [SerializeField] private GameObject      _DestroyableWallPrefab;
        [SerializeField] private Transform       _DestroyableWallsParent;
        [SerializeField] private GameObject      _PlayerPrefab;
        [SerializeField] private Transform       _PlayersParent;
        [SerializeField] private GameObject      _BombPrefab;
        [SerializeField] private Transform       _BombsParent;
        [SerializeField] private GameObject      _ExplosionPrefab;
        [SerializeField] private GameObject      _BonusPrefab;
        [SerializeField] private Transform       _BonusesParent;

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
        private Dictionary<DVector2, ServerGameObject> _Bombs;
        private Dictionary<DVector2, ServerGameObject> _Bonuses;

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
            _Bombs            = new Dictionary<DVector2, ServerGameObject>();
            _Bonuses          = new Dictionary<DVector2, ServerGameObject>();

            GetTimeOffset();
        }

        private void Start()
        {
            InitPhysics();
            _Client.Connect(IPAddress, Port);
            _Client.MessageReceived += ClientOnMessageReceived;
            _CodeText.text          =  $"Code: {Code}";
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SceneManager.LoadScene(0);
            }

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

            ProcessFrames();
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
            return (uint)Math.Floor((millisecondsFromServerStart / (_FixedDeltaTime * MathUtils.SecToMilliseconds)));
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
            _Physics.PreCollision     += DisablePlayerToPlayerCollision;
            _Physics.OnCollisionEnter += BombCollisionEnter;
            _Physics.OnCollisionExit  += BombCollisionExit;

            InitOuterWalls(_Physics);
            FillInnerWalls(_Physics);
        }

        private void DisablePlayerToPlayerCollision(ICollisionDataExtend data)
        {
            if (((Game.PhysicsObjectId)data.PhysicsObjectA.UserData).ObjectType == Game.ObjectType.Player &&
                ((Game.PhysicsObjectId)data.PhysicsObjectB.UserData).ObjectType == Game.ObjectType.Player)
            {
                data.DisableContact();
            }
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
                wall.AddBoxCollider(0.5f, 0.5f);

                GameObject visualWall = Instantiate(_DestroyableWallPrefab, new Vector3(vector2.x, 0.5f, vector2.y), Quaternion.identity);
                visualWall.transform.SetParent(_DestroyableWallsParent, true);

                _DestroyableWalls[vector2] = new ServerGameObject
                {
                    PhysicsObj       = wall,
                    VisualGameObject = visualWall,
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
            
            if (_Players.Count(x => x != null) < 2)
            {
                _InfoSystem.AddInfo("Can't start game yet. Minimum number of players is 2.", 1.5f);
                return;
            }

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
                PutBomb((sbyte)_MyPlayerId, true, DVector2.Zero);
            }

            UpdateOtherPlayers();
            RemoveMyNotConfirmedBombs();
            RemoveBombsThatExpired();

            _Physics.Step();

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

        private void RemoveMyNotConfirmedBombs()
        {
            foreach (DVector2 vector2 in _Players[_MyPlayerId].Bombs.ToList())
            {
                if (_Bombs[vector2].Confirmed) continue;
                if (_Bombs[vector2].FrameToDestroy > _LastSimulatedFrame) continue;
                _Bombs[vector2].Destroy();
                _Bombs.Remove(vector2);
                _Players[_MyPlayerId].Bombs.Remove(vector2);
            }
        }

        private void RemoveBombsThatExpired()
        {
            foreach (var bomb in _Bombs.ToArray())
            {
                if (bomb.Value.FrameToDestroy > _LastSimulatedFrame) continue;
                bomb.Value.Destroy();
                _Bombs.Remove(bomb.Key);

                if (_Players[_MyPlayerId].Bombs.Contains(bomb.Key))
                {
                    _Players[_MyPlayerId].Bombs.Remove(bomb.Key);
                }
            }
        }

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
                        case (ushort)Messages.MessageId.PutBomb:
                        {
                            Messages.PutBomb msg = new Messages.PutBomb();
                            msg.Read(reader);
                            PutBombMessage(msg);
                            break;
                        }
                        case (ushort)Messages.MessageId.ExplosionResult:
                        {
                            Messages.ExplosionResult msg = new Messages.ExplosionResult();
                            msg.Read(reader);
                            ExplosionResultMessage(msg);
                            break;
                        }
                        case (ushort)Messages.MessageId.BonusTaken:
                        {
                            Messages.BonusTaken msg = new Messages.BonusTaken();
                            msg.Read(reader);
                            BonusTakenMessage(msg);
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

                Player player = new Player
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

        private void PutBombMessage(Messages.PutBomb msg)
        {
            if (msg.FrameToDestroy != UInt32.MaxValue)
            {
                msg.FrameToDestroy += 1; // We are always one frame ahead of server (this is to have synchronization with explosion result)
                // Adding have of rtt as frames to hide explosion lag
                msg.FrameToDestroy += (uint) MathF.Ceiling(_Client.Client.RoundTripTime.SmoothedRtt / 2f / _FixedDeltaTime);
            }

            if (msg.PlayerId == _MyPlayerId)
            {
                if (_Players[_MyPlayerId].Bombs.Contains(msg.Position))
                {
                    _Bombs[msg.Position].Confirmed      = true;
                    _Bombs[msg.Position].FrameToDestroy = msg.FrameToDestroy;
                    return;
                }
            }

            PutBomb((sbyte)msg.PlayerId, false, msg.Position, msg.FrameToDestroy);
        }

        private void ExplosionResultMessage(Messages.ExplosionResult msg)
        {
            foreach (DVector2 position in msg.WallsDestroyed)
            {
                if (_DestroyableWalls.ContainsKey(position))
                {
                    _DestroyableWalls[position].Destroy();
                    _DestroyableWalls.Remove(position);
                }
            }

            foreach (int playerKilled in msg.PlayersKilled)
            {
                _Players[playerKilled].PhysicsObj.Enabled                                    = false;
                _Players[playerKilled].VisualGameObject.GetComponent<MeshRenderer>().enabled = false;
                _Players[playerKilled].IsDead                                                = true;
            }
            
            foreach (DVector2 position in msg.BonusesDestroyed)
            {
                if (!_Bonuses.ContainsKey(position)) continue;
                _Bonuses[position].Destroy();
                _Bonuses.Remove(position);
            }
            
            foreach (var pair in msg.BonusesSpawned)
            {
                SpawnBonus(pair.Key, pair.Value);
            }

            if (_Bombs.ContainsKey(msg.BombPosition))
            {
                DVector2 pos   = msg.BombPosition;
                _Bombs[pos].Destroy();
                _Bombs.Remove(pos);
                
                if (_Players[_MyPlayerId].Bombs.Contains(pos))
                {
                    _Players[_MyPlayerId].Bombs.Remove(pos);
                }
            }
            
            Explode(msg);
            CheckEndGame();
        }
        
        private void CheckEndGame()
        {
            int numberOfAlivePlayers = 0;

            foreach (Player player in _Players)
            {
                if (player == null) continue;
                if (player.IsDead) continue;
                ++numberOfAlivePlayers;
            }
            
            if (numberOfAlivePlayers > 1) return;
            _GameState = Game.GameState.Ended;
            _InfoSystem.AddInfo("Game Over!", 3f);
        }

        private void BonusTakenMessage(Messages.BonusTaken bonusTaken)
        {
            if (bonusTaken.BonusType == Game.BonusType.Bomb && bonusTaken.PlayerId == _MyPlayerId)
            {
                ++_Players[_MyPlayerId].MaxBombs;
            }
            
            _Bonuses[bonusTaken.Position].Destroy();
            _Bonuses.Remove(bonusTaken.Position);
        }
        
        #endregion Messages

        #region Bombs And Bonuses

        private void BombCollisionEnter(ICollisionData collisionData)
        {
            Game.ObjectType typeA = ((Game.PhysicsObjectId)collisionData.PhysicsObjectA.UserData).ObjectType;
            Game.ObjectType typeB = ((Game.PhysicsObjectId)collisionData.PhysicsObjectB.UserData).ObjectType;

            if (typeA != Game.ObjectType.Bomb && typeB != Game.ObjectType.Bomb) return;

            ICollider bomb = typeA == Game.ObjectType.Bomb ? collisionData.ColliderA : collisionData.ColliderB;
            ++_Bombs[bomb.PhysicsObject.Position].Collisions;
        }

        private void BombCollisionExit(ICollisionData collisionData)
        {
            Game.ObjectType typeA = ((Game.PhysicsObjectId)collisionData.PhysicsObjectA.UserData).ObjectType;
            Game.ObjectType typeB = ((Game.PhysicsObjectId)collisionData.PhysicsObjectB.UserData).ObjectType;

            if (typeA != Game.ObjectType.Bomb && typeB != Game.ObjectType.Bomb) return;

            ICollider bomb    = typeA == Game.ObjectType.Bomb ? collisionData.ColliderA : collisionData.ColliderB;
            DVector2  bombPos = bomb.PhysicsObject.Position;
            --_Bombs[bombPos].Collisions;

            if (_Bombs[bombPos].Collisions != 0) return;
            bomb.IsSensor = false;
        }

        private void PutBomb(sbyte playerId, bool simulatedBomb, DVector2 bombPos, uint frameToDestroy = 0)
        {
            if (simulatedBomb && _Players[playerId].IsDead) return;
            if (simulatedBomb && _Players[playerId].Bombs.Count == _Players[playerId].MaxBombs) return;

            // If it's simulated bomb then we need to take predicted position for bomb 
            if (simulatedBomb)
            {
                float xPos = Mathf.Round(_Players[playerId].PhysicsObj.Position.x);
                float yPos = Mathf.Round(_Players[playerId].PhysicsObj.Position.y);
                bombPos = new DVector2(xPos, yPos);

                if (_Bombs.ContainsKey(bombPos)) return;
            }

            // Test if something other than player or bonus is in this place
            bool              hitBreak = false;
            bool              onPlayer = false;
            OverlapShapeInput input    = new OverlapShapeInput();
            input.SetAsCircle(0.48f);
            input.SetPosition(bombPos);
            _Physics.OverlapShape((colliderHit, index) =>
            {
                Game.ObjectType objectType = ((Game.PhysicsObjectId)colliderHit.PhysicsObject.UserData).ObjectType;
                if (objectType == Game.ObjectType.Player)
                {
                    onPlayer = true;
                    return true;
                }

                if (objectType == Game.ObjectType.Bonus) return true;
                hitBreak = true;
                return false;
            }, input);

            if (hitBreak) return;

            GameObject bomb = Instantiate(_BombPrefab, new Vector3(bombPos.x, 0.5f, bombPos.y), Quaternion.identity);
            bomb.transform.SetParent(_BombsParent, true);
            IPhysicsObject physicsBomb  = _Physics.CreatePhysicsObject(BodyType.Kinematic, bombPos, 0f);
            ICollider      bombCollider = physicsBomb.AddCircleCollider(0.5f);
            bombCollider.IsSensor = simulatedBomb || onPlayer;

            physicsBomb.UserData = new Game.PhysicsObjectId
            {
                ObjectType = Game.ObjectType.Bomb,
                Id         = 0
            };

            if (simulatedBomb)
            {
                // We will wait 0.5 seconds for server confirmation of that bomb
                // If that confirmation won't arrive we will destroy the bomb
                uint halfASecondsFrames = (uint)Mathf.CeilToInt(_FramesPerSecond / 2f);
                frameToDestroy = _LastSimulatedFrame + halfASecondsFrames;
            }

            ServerGameObject serverBomb = new ServerGameObject
            {
                PhysicsObj       = physicsBomb,
                VisualGameObject = bomb,
                FrameToDestroy   = frameToDestroy,
                Confirmed        = !simulatedBomb
            };

            _Bombs.Add(bombPos, serverBomb);

            if (playerId == _MyPlayerId)
            {
                _Players[_MyPlayerId].Bombs.Add(bombPos);
            }
        }

        private void Explode(Messages.ExplosionResult explosionResult)
        {
            // Spawn explosion animations
            for (float x = -explosionResult.LeftDistance; x <= explosionResult.RightDistance; ++x)
            {
                Vector3 explosionPosition = new Vector3(explosionResult.BombPosition.x, 0.5f, explosionResult.BombPosition.y);
                explosionPosition.x += x;
                Instantiate(_ExplosionPrefab, explosionPosition, Quaternion.identity);
            }

            for (float z = -explosionResult.DownDistance; z <= explosionResult.UpDistance; ++z)
            {
                Vector3 explosionPosition = new Vector3(explosionResult.BombPosition.x, 0.5f, explosionResult.BombPosition.y);
                explosionPosition.z += z;
                Instantiate(_ExplosionPrefab, explosionPosition, Quaternion.identity);
            }
        }
        
        private void SpawnBonus(DVector2 position, Game.BonusType bonusType)
        {
            GameObject   bonus        = Instantiate(_BonusPrefab, new Vector3(position.x, 0.5f, position.y), Quaternion.identity);
            bonus.transform.SetParent(_BonusesParent, true);
            MeshRenderer meshRenderer = bonus.GetComponent<MeshRenderer>();
            Material     newMaterial  = new Material(meshRenderer.material);

            switch (bonusType)
            {
                case Game.BonusType.Power:
                {
                    newMaterial.color = Color.yellow;
                    break;
                }
                case Game.BonusType.Bomb:
                {
                    newMaterial.color = Color.red;
                    break;
                }
                case Game.BonusType.Detonator:
                {
                    newMaterial.color = Color.cyan;
                    break;
                }
            }

            meshRenderer.material = newMaterial;
            
            _Bonuses.Add(position, new ServerGameObject
            {
                PhysicsObj       = null,
                VisualGameObject = bonus,
                Confirmed        = true,
                Collisions       = 0,
                FrameToDestroy   = UInt32.MaxValue
            });
        }

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