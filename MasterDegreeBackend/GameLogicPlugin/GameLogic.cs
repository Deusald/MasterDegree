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
            public IPhysicsObject                     PhysicsObj          { get; set; }
            public ICollider                          Collider            { get; set; }
            public bool                               IsDead              { get; set; }
            public bool                               HasDetonator        { get; set; }
            public Dictionary<uint, Game.PlayerInput> Inputs              { get; }
            public Dictionary<uint, Vector2>          Positions           { get; }
            public uint                               OldestPositionFrame { get; set; }
            public HashSet<Vector2>                   Bombs               { get; }
            public byte                               Power               { get; set; }
            public byte                               MaxBombs            { get; set; }

            public Player(byte id)
            {
                Id                  = id;
                IsDead              = false;
                HasDetonator        = false;
                Inputs              = new Dictionary<uint, Game.PlayerInput>();
                Positions           = new Dictionary<uint, Vector2>();
                Bombs               = new HashSet<Vector2>();
                Power               = 1;
                MaxBombs            = 1;
                OldestPositionFrame = uint.MaxValue;
            }
        }

        public class GameObject
        {
            public IPhysicsObject PhysicsObj     { get; set; }
            public uint           FrameToDestroy { get; set; }
            public sbyte          Owner          { get; set; }
            public byte           Power          { get; set; }
            public int            Collisions     { get; set; }

            public void Destroy()
            {
                PhysicsObj?.Destroy();
            }
        }

        #endregion Types

        #region Properties

        public long ServerClockStartTime { get; set; }

        #endregion Properties

        #region Variables

        private Game.GameState   _GameState;
        private Physics2DControl _Physics;
        private float            _SimulationAccumulatedTime;
        private uint             _PhysicsTickNumber;

        private readonly object                          _GameLockObject;
        private readonly Random                          _Random;
        private readonly Logger                          _Logger;
        private readonly bool                            _SpawnDestroyableWalls;
        private readonly ushort                          _NumberOfFramesPerSecond;
        private readonly float                           _FixedDeltaTime;
        private readonly List<Player>                    _Players;
        private readonly Dictionary<ushort, byte>        _ClientIdToPlayerId;
        private readonly Dictionary<Vector2, GameObject> _DestroyableWalls;
        private readonly Dictionary<Vector2, GameObject> _Bombs;
        private readonly Dictionary<Vector2, GameObject> _Bonuses;

        #endregion Variables

        #region Special Methods

        public GameLogic(Logger logger, ushort framesPerSecond)
        {
            _GameLockObject          = new object();
            _Random                  = new Random();
            _Logger                  = logger;
            _SpawnDestroyableWalls   = bool.Parse(Environment.GetEnvironmentVariable("WITH_WALLS")!);
            _NumberOfFramesPerSecond = framesPerSecond;
            _FixedDeltaTime          = 1f / _NumberOfFramesPerSecond;
            _GameState               = Game.GameState.BeforeStart;
            _Players                 = new List<Player>();
            _ClientIdToPlayerId      = new Dictionary<ushort, byte>();
            _DestroyableWalls        = new Dictionary<Vector2, GameObject>();
            _Bombs                   = new Dictionary<Vector2, GameObject>();
            _Bonuses                 = new Dictionary<Vector2, GameObject>();

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
                    _Physics.Step();
                    StoreAllPlayerCurrentPositions();
                    ExplodeAllExpiredBombs();
                    ClearExpiredBonuses();
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
                var physicsPlayer = SpawnPlayer(playerId, _Physics);

                Player player = new Player(playerId)
                {
                    Client     = client,
                    PhysicsObj = physicsPlayer.Item1,
                    Collider   = physicsPlayer.Item2
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
            _Physics                  =  new Physics2DControl(_NumberOfFramesPerSecond, Vector2.Zero);
            _Physics.PreCollision     += DisablePlayerToPlayerCollision;
            _Physics.OnCollisionEnter += BombCollisionEnter;
            _Physics.OnCollisionEnter += BonusCollisionEnter;
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

        private void FillDestroyableWalls()
        {
            if (!_SpawnDestroyableWalls) return;

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

                _DestroyableWalls[vector2] = new GameObject
                {
                    PhysicsObj     = wall,
                    Owner          = -1,
                    Power          = 0,
                    FrameToDestroy = uint.MaxValue
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
                player.PhysicsObj.MovePosition(player.PhysicsObj.Position + dir);

                if (player.Inputs[_PhysicsTickNumber].PutBomb)
                {
                    PutBomb(player.Id, player.Power);
                }

                // Detonate all our bombs if player send detonator signal in this frame
                if (player.HasDetonator && player.Inputs[_PhysicsTickNumber].Detonate)
                {
                    foreach (Vector3 pos in player.Bombs)
                    {
                        // We set playerId as frame to destroy to later know that this bomb should be checked with lag compensation
                        _Bombs[pos].FrameToDestroy = player.Id;
                    }
                }

                player.Inputs.Remove(_PhysicsTickNumber);
            }
        }

        private void StoreAllPlayerCurrentPositions()
        {
            // We are storing those positions for lag compensation
            foreach (Player player in _Players)
            {
                player.Positions[_PhysicsTickNumber] = player.PhysicsObj.Position;

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

        private void ExplodeAllExpiredBombs()
        {
            bool explosion;

            do
            {
                // We are making it in do while loop because bombs on the end can trigger first bombs from the list
                explosion = false;

                foreach (var bomb in _Bombs.ToArray())
                {
                    // We explode bombs only if frame to destroy is older than current frame
                    if (bomb.Value.FrameToDestroy > _PhysicsTickNumber) continue;

                    uint frameToDestroy = bomb.Value.FrameToDestroy;
                    _Players[bomb.Value.Owner].Bombs.Remove(bomb.Key);
                    bomb.Value.Destroy();
                    _Bombs.Remove(bomb.Key);
                    Explosion(bomb.Key, bomb.Value.Power, frameToDestroy);
                    explosion = true;
                }
            } while (explosion);
        }

        private void ClearExpiredBonuses()
        {
            foreach (Vector2 position in _Bonuses.Keys.ToArray())
            {
                if (_Bonuses[position].FrameToDestroy != 0) return;
                _Bonuses[position].Destroy();
                _Bonuses.Remove(position);
            }
        }

        private void SendAllPlayersPositions()
        {
            foreach (Player player in _Players)
            {
                Messages.PlayerPositionMsg msg = new Messages.PlayerPositionMsg
                {
                    PlayerId    = player.Id,
                    Frame       = _PhysicsTickNumber,
                    Position    = player.PhysicsObj.Position,
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

        #region Bomb & Bonuses

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
            Vector2   bombPos = bomb.PhysicsObject.Position;
            --_Bombs[bombPos].Collisions;

            if (_Bombs[bombPos].Collisions != 0) return;
            bomb.IsSensor = false;
        }

        private void PutBomb(byte playerId, byte power)
        {
            Player player = _Players[playerId];

            if (player.Bombs.Count == player.MaxBombs) return;

            // We put bombs aligned to grid
            float xPos = MathF.Round(_Players[playerId].PhysicsObj.Position.x);
            float yPos = MathF.Round(_Players[playerId].PhysicsObj.Position.y);

            Vector2 bombPos = new Vector2(xPos, yPos);

            // Can't put bomb on a bomb
            if (_Bombs.ContainsKey(bombPos)) return;

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

            IPhysicsObject physicsBomb  = _Physics.CreatePhysicsObject(BodyType.Kinematic, bombPos, 0f);
            ICollider      bombCollider = physicsBomb.AddCircleCollider(0.5f);
            bombCollider.IsSensor = onPlayer;

            physicsBomb.UserData = new Game.PhysicsObjectId
            {
                ObjectType = Game.ObjectType.Bomb,
                Id         = 0
            };

            // With detonator we wait for signal from player to detonate
            uint frameToDestroy = UInt32.MaxValue;

            if (!player.HasDetonator)
            {
                // If player doesn't have detonator then bomb will explode after 2 seconds
                frameToDestroy = (uint)(_PhysicsTickNumber + _NumberOfFramesPerSecond * 2);
            }

            _Logger.Log($"Bobmb created at position {bombPos}", LogType.Info);

            GameObject gameObject = new GameObject
            {
                PhysicsObj     = physicsBomb,
                Owner          = (sbyte)playerId,
                Power          = power,
                FrameToDestroy = frameToDestroy
            };

            _Bombs.Add(bombPos, gameObject);
            player.Bombs.Add(bombPos);

            Messages.PutBomb msg = new Messages.PutBomb
            {
                Position       = bombPos,
                PlayerId       = playerId,
                FrameToDestroy = frameToDestroy
            };

            SendMessageToAllPlayers(msg);
        }

        private void Explosion(Vector2 position, byte power, uint frameToDestroy)
        {
            Messages.ExplosionResult explosionResult = new Messages.ExplosionResult
            {
                BombPosition   = position,
                BonusesSpawned = new Dictionary<Vector2, Game.BonusType>()
            };

            float upExplosionDistance    = power;
            float downExplosionDistance  = power;
            float leftExplosionDistance  = power;
            float rightExplosionDistance = power;

            HashSet<Vector2> wallsDestroyed = new HashSet<Vector2>();
            HashSet<byte>    playersKilled  = new HashSet<byte>();
            HashSet<Vector2> bonusDestroyed = new HashSet<Vector2>();

            // Lag compensation
            // If this was bomb explosion via detonator apply lag compensation
            // Frame to destroy points to who detonates the bomb:
            // 0 -> player 0
            // 1 -> player 1
            // 2 -> player 2
            // 3 -> player 3
            if (frameToDestroy <= 3)
            {
                byte detonatorPlayer = (byte)frameToDestroy;

                // We calculate how many frames of delay player had when he detonated bomb while seeing enemy
                uint playerTriggeringDelayInFrames = (uint)MathF.Ceiling(_Players[detonatorPlayer].Client.RoundTripTime.SmoothedRtt / _FixedDeltaTime);

                // This position is from the past but it was the present frame of enemy position when player detonated bomb
                uint frameInPast = _PhysicsTickNumber - playerTriggeringDelayInFrames - 1;

                // Move all players back to the position from past (time where player detonates bomb on his timeline)
                for (int i = 0; i < _Players.Count; ++i)
                {
                    if (_Players[i].IsDead) continue;
                    _Players[i].Collider.IsSensor   = true;
                    _Players[i].PhysicsObj.Position = _Players[i].Positions[frameInPast];
                }

                _Physics.Step();
            }

            CheckDestruction(power, position, Vector2.Up, playersKilled, wallsDestroyed, bonusDestroyed, frameToDestroy, ref upExplosionDistance);
            CheckDestruction(power, position, Vector2.Down, playersKilled, wallsDestroyed, bonusDestroyed, frameToDestroy, ref downExplosionDistance);
            CheckDestruction(power, position, Vector2.Left, playersKilled, wallsDestroyed, bonusDestroyed, frameToDestroy, ref leftExplosionDistance);
            CheckDestruction(power, position, Vector2.Right, playersKilled, wallsDestroyed, bonusDestroyed, frameToDestroy, ref rightExplosionDistance);

            explosionResult.WallsDestroyed   = wallsDestroyed.ToList();
            explosionResult.PlayersKilled    = playersKilled.ToList();
            explosionResult.BonusesDestroyed = bonusDestroyed.ToList();
            explosionResult.UpDistance       = upExplosionDistance;
            explosionResult.DownDistance     = downExplosionDistance;
            explosionResult.LeftDistance     = leftExplosionDistance;
            explosionResult.RightDistance    = rightExplosionDistance;

            // Destroy all walls that bomb reached
            foreach (Vector2 wallPos in wallsDestroyed)
            {
                if (!_DestroyableWalls.ContainsKey(wallPos)) continue;

                _DestroyableWalls[wallPos].Destroy();
                _DestroyableWalls.Remove(wallPos);
                TrySpawnBonus(wallPos, explosionResult);
            }

            // Destroy all bonuses that bomb reached
            foreach (Vector2 bonusPos in explosionResult.BonusesDestroyed)
            {
                if (!_Bonuses.ContainsKey(bonusPos)) continue;

                _Bonuses[bonusPos].Destroy();
                _Bonuses.Remove(bonusPos);
            }

            // Kill all players that bomb reached
            foreach (int playerKilled in playersKilled)
            {
                if (_Players[playerKilled].IsDead) continue;

                _Players[playerKilled].IsDead             = true;
                _Players[playerKilled].PhysicsObj.Enabled = false;
            }

            SendMessageToAllPlayers(explosionResult);

            // Return lag compensation movements
            if (frameToDestroy <= 3)
            {
                for (int i = 0; i < _Players.Count; ++i)
                {
                    if (_Players[i].IsDead) continue;
                    _Players[i].Collider.IsSensor   = false;
                    _Players[i].PhysicsObj.Position = _Players[i].Positions[_PhysicsTickNumber];
                }

                _Physics.Step();
            }
        }

        private void CheckDestruction(byte power, Vector2 origin, Vector2 dir, HashSet<byte> playersKilled,
            HashSet<Vector2> wallsDestroyed, HashSet<Vector2> bonusesDestroyed, uint frameToDestroy, ref float distanceToColliderCenter)
        {
            ShapeCastInput shapeCastInput = new ShapeCastInput();
            shapeCastInput.SetAsCircle(0.35f);
            shapeCastInput.SetTranslation(origin, dir, power);

            List<Game.CastHit> castHits = new List<Game.CastHit>();

            _Physics.ShapeCast((colliderHit, point, normal, f, index) =>
            {
                castHits.Add(new Game.CastHit
                {
                    ColliderHit = colliderHit.PhysicsObject,
                    HitPoint    = point,
                    Fraction    = f
                });
                return true;
            }, shapeCastInput);

            castHits.Sort((x, y) => x.Fraction.CompareTo(y.Fraction));

            for (int i = 0; i < castHits.Count; ++i)
            {
                IPhysicsObject       physicsObject = (IPhysicsObject)castHits[i].ColliderHit;
                Game.PhysicsObjectId objectId      = (Game.PhysicsObjectId)physicsObject.UserData;

                if (objectId.ObjectType == Game.ObjectType.Player)
                {
                    playersKilled.Add((byte)objectId.Id);
                }
                else if (objectId.ObjectType == Game.ObjectType.DestroyableWall)
                {
                    Vector2 pos = physicsObject.Position;
                    wallsDestroyed.Add(pos);
                    distanceToColliderCenter = MathF.Ceiling(Vector2.Distance(origin, castHits[i].HitPoint));
                    break;
                }
                else if (objectId.ObjectType == Game.ObjectType.Bomb)
                {
                    Vector2 pos = physicsObject.Position;

                    if (_Bombs.ContainsKey(pos))
                    {
                        _Bombs[pos].FrameToDestroy = frameToDestroy;
                    }
                }
                else if (objectId.ObjectType == Game.ObjectType.Bonus)
                {
                    Vector3 pos = physicsObject.Position;
                    bonusesDestroyed.Add(pos);
                }
                else
                {
                    distanceToColliderCenter = MathF.Floor(Vector2.Distance(origin, castHits[i].HitPoint));
                    break;
                }
            }
        }

        private void TrySpawnBonus(Vector2 position, Messages.ExplosionResult explosionResult)
        {
            double bonusRandom = _Random.NextDouble();
            if (bonusRandom > 0.6d) return;

            // Power bonus
            if (_Random.NextDouble() <= 0.5d)
            {
                SpawnBonus(position, Game.BonusType.Power);
                explosionResult.BonusesSpawned.Add(position, Game.BonusType.Power);
            }
            // Additional bomb bonus
            else if (_Random.NextDouble() <= 0.5d)
            {
                SpawnBonus(position, Game.BonusType.Bomb);
                explosionResult.BonusesSpawned.Add(position, Game.BonusType.Bomb);
            }
            // Detonator bonus
            else
            {
                SpawnBonus(position, Game.BonusType.Detonator);
                explosionResult.BonusesSpawned.Add(position, Game.BonusType.Detonator);
            }
        }

        private void SpawnBonus(Vector2 position, Game.BonusType bonusType)
        {
            IPhysicsObject bonus = _Physics.CreatePhysicsObject(BodyType.Static, position, 0);
            bonus.UserData = new Game.PhysicsObjectId
            {
                ObjectType = Game.ObjectType.Bonus,
                Id         = (uint)bonusType
            };
            ICollider collider = bonus.AddCircleCollider(0.4f);
            collider.IsSensor = true;

            _Bonuses.Add(position, new GameObject
            {
                PhysicsObj     = bonus,
                Owner          = -1,
                Collisions     = 0,
                Power          = 0,
                FrameToDestroy = UInt32.MaxValue
            });
        }

        private void BonusCollisionEnter(ICollisionData collisionData)
        {
            Game.PhysicsObjectId idA = (Game.PhysicsObjectId)collisionData.PhysicsObjectA.UserData;
            Game.PhysicsObjectId idB = (Game.PhysicsObjectId)collisionData.PhysicsObjectB.UserData;

            if (idA.ObjectType == Game.ObjectType.Player && idB.ObjectType == Game.ObjectType.Bonus)
            {
                byte           playerId      = (byte)idA.Id;
                Game.BonusType bonusType     = (Game.BonusType)idB.Id;
                Vector2        bonusPosition = collisionData.PhysicsObjectB.Position;
                ApplyBonus(playerId, bonusType, bonusPosition);
                return;
            }

            if (idA.ObjectType == Game.ObjectType.Bonus && idB.ObjectType == Game.ObjectType.Player)
            {
                byte           playerId      = (byte)idB.Id;
                Game.BonusType bonusType     = (Game.BonusType)idA.Id;
                Vector2        bonusPosition = collisionData.PhysicsObjectA.Position;
                ApplyBonus(playerId, bonusType, bonusPosition);
            }

            void ApplyBonus(byte playerId, Game.BonusType bonusType, Vector2 bonusPosition)
            {
                if (_Bonuses[bonusPosition].FrameToDestroy == 0) return;

                switch (bonusType)
                {
                    case Game.BonusType.Power:
                    {
                        ++_Players[playerId].Power;
                        break;
                    }
                    case Game.BonusType.Bomb:
                    {
                        ++_Players[playerId].MaxBombs;
                        break;
                    }
                    case Game.BonusType.Detonator:
                    {
                        _Players[playerId].HasDetonator = true;
                        break;
                    }
                }

                _Bonuses[bonusPosition].FrameToDestroy = 0;

                Messages.BonusTaken bonusTaken = new Messages.BonusTaken
                {
                    Position  = bonusPosition,
                    PlayerId  = playerId,
                    BonusType = bonusType
                };

                SendMessageToAllPlayers(bonusTaken);
            }
        }

        #endregion Bomb & Bonuses

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

        private void FillDestroyableWalls(Action<Vector2> spawnWallCallback)
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
                    spawnWallCallback?.Invoke(new Vector2(x, y));
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

            IPhysicsObject leftWall = physics.CreatePhysicsObject(BodyType.Static, Vector2.Zero, 0f);
            leftWall.UserData = objectId;
            leftWall.AddEdgeCollider(new Vector2(-3.5f, -3.5f), new Vector2(-3.5f, 3.5f));

            IPhysicsObject rightWall = physics.CreatePhysicsObject(BodyType.Static, Vector2.Zero, 0f);
            rightWall.UserData = objectId;
            rightWall.AddEdgeCollider(new Vector2(3.5f, -3.5f), new Vector2(3.5f, 3.5f));

            IPhysicsObject upWall = physics.CreatePhysicsObject(BodyType.Static, Vector2.Zero, 0f);
            upWall.UserData = objectId;
            upWall.AddEdgeCollider(new Vector2(-3.5f, 3.5f), new Vector2(3.5f, 3.5f));

            IPhysicsObject downWall = physics.CreatePhysicsObject(BodyType.Static, Vector2.Zero, 0f);
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
                    wall.AddBoxCollider(0.5f, 0.5f);
                }
            }
        }

        private (IPhysicsObject, ICollider) SpawnPlayer(byte id, Physics2D physics2D)
        {
            IPhysicsObject player = physics2D.CreatePhysicsObject(BodyType.Dynamic, Game.PlayersSpawnPoints[id], 0);
            player.FixedRotation = true;
            player.UserData = new Game.PhysicsObjectId
            {
                ObjectType = Game.ObjectType.Player,
                Id         = id
            };
            ICollider collider = player.AddCircleCollider(0.4f);
            return (player, collider);
        }

        #endregion Physics

        #endregion Private Methods
    }
}