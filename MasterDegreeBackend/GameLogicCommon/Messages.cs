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

using System.Collections.Generic;
using DarkRift;
using DeusaldSharp;

namespace GameLogicCommon
{
    public static class Messages
    {
        public interface INetMessage
        {
            MessageId MessageId  { get; }
            bool      IsFrequent { get; }

            void Write(DarkRiftWriter writer);
            void Read(DarkRiftReader reader);
        }

        public enum MessageId
        {
            // Game messages
            ClientHeartBeat,
            ServerHeartBeat,
            PlayerInit,
            SpawnPlayer,
            StartGame,
            PlayerInput,
            PlayerPosition,
            PutBomb,
            ExplosionResult,
            BonusTaken,

            // Game Server Controllers Messages
            AllocateGame,
            AllocatedGameData,
            GetAllocatedGameData,
            SpawnBots,
            KillGame
        }

        public class PlayerInitMsg : INetMessage
        {
            public MessageId MessageId  => MessageId.PlayerInit;
            public bool      IsFrequent => false;

            public bool WallSpawned           { get; set; }
            public byte YourId                { get; set; }
            public long ServerClockStartTimer { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                // Save sending one byte by encoding WallSpawned state inside YourId byte.
                byte yourId = YourId;
                yourId = yourId.MarkBit(1 << 2, WallSpawned);

                writer.Write(yourId);
                writer.Write(ServerClockStartTimer);
            }

            public void Read(DarkRiftReader reader)
            {
                byte yourId = reader.ReadByte();
                WallSpawned           = yourId.HasAnyBitOn(1 << 2);
                yourId                = yourId.MarkBit(1 << 2, false);
                YourId                = yourId;
                ServerClockStartTimer = reader.ReadInt64();
            }
        }

        public class SpawnPlayer : INetMessage
        {
            public MessageId MessageId  => MessageId.SpawnPlayer;
            public bool      IsFrequent => false;

            public byte PlayerId { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(PlayerId);
            }

            public void Read(DarkRiftReader reader)
            {
                PlayerId = reader.ReadByte();
            }
        }

        public class PlayerInputMsg : INetMessage
        {
            public MessageId MessageId  => MessageId.PlayerInput;
            public bool      IsFrequent => true;

            public Dictionary<uint, Game.PlayerInput> StoredInputs     { get; set; }
            public uint                               LastInputFrame   { get; set; }
            public uint                               OldestInputFrame { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(LastInputFrame);
                writer.Write(OldestInputFrame);

                for (uint i = OldestInputFrame; i <= LastInputFrame; ++i)
                {
                    writer.Write(StoredInputs[i].Direction.x);
                    writer.Write(StoredInputs[i].Direction.y);

                    byte buttonStates = 0;
                    buttonStates = buttonStates.MarkBit(1 << 0, StoredInputs[i].PutBomb);
                    buttonStates = buttonStates.MarkBit(1 << 1, StoredInputs[i].Detonate);
                    writer.Write(buttonStates);
                }
            }

            public void Read(DarkRiftReader reader)
            {
                StoredInputs     = new Dictionary<uint, Game.PlayerInput>();
                LastInputFrame   = reader.ReadUInt32();
                OldestInputFrame = reader.ReadUInt32();

                for (uint i = OldestInputFrame; i <= LastInputFrame; ++i)
                {
                    Game.PlayerInput playerInput = new Game.PlayerInput
                    {
                        Frame     = i,
                        Direction = new Vector2(reader.ReadSingle(), reader.ReadSingle())
                    };

                    byte buttonStates = reader.ReadByte();
                    playerInput.PutBomb  = buttonStates.HasAnyBitOn(1 << 0);
                    playerInput.Detonate = buttonStates.HasAnyBitOn(1 << 1);
                    StoredInputs.Add(i, playerInput);
                }
            }
        }

        public class PlayerPositionMsg : INetMessage
        {
            public MessageId MessageId  => MessageId.PlayerPosition;
            public bool      IsFrequent => true;

            public byte    PlayerId    { get; set; }
            public uint    Frame       { get; set; }
            public Vector2 Position    { get; set; }
            public Vector2 PreviousDir { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(PlayerId);
                writer.Write(Frame);
                writer.Write(Position.x);
                writer.Write(Position.y);
                writer.Write(PreviousDir.x);
                writer.Write(PreviousDir.y);
            }

            public void Read(DarkRiftReader reader)
            {
                PlayerId    = reader.ReadByte();
                Frame       = reader.ReadUInt32();
                Position    = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                PreviousDir = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }
        }

        public class PutBomb : INetMessage
        {
            public MessageId MessageId  => MessageId.PutBomb;
            public bool      IsFrequent => false;

            public uint    FrameToDestroy { get; set; }
            public byte    PlayerId       { get; set; }
            public Vector2 Position       { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(FrameToDestroy);
                writer.Write(PlayerId);
                writer.Write(Position.x);
                writer.Write(Position.y);
            }

            public void Read(DarkRiftReader reader)
            {
                FrameToDestroy = reader.ReadUInt32();
                PlayerId       = reader.ReadByte();
                Position       = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }
        }

        public class ExplosionResult : INetMessage
        {
            public MessageId MessageId  => MessageId.ExplosionResult;
            public bool      IsFrequent => false;

            public Vector2                             BombPosition     { get; set; }
            public List<Vector2>                       WallsDestroyed   { get; set; }
            public List<byte>                          PlayersKilled    { get; set; }
            public List<Vector2>                       BonusesDestroyed { get; set; }
            public Dictionary<Vector2, Game.BonusType> BonusesSpawned   { get; set; }
            public float                               UpDistance       { get; set; }
            public float                               DownDistance     { get; set; }
            public float                               LeftDistance     { get; set; }
            public float                               RightDistance    { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(BombPosition.x);
                writer.Write(BombPosition.y);
                writer.Write(WallsDestroyed.Count);

                for (int i = 0; i < WallsDestroyed.Count; ++i)
                {
                    writer.Write(WallsDestroyed[i].x);
                    writer.Write(WallsDestroyed[i].y);
                }

                writer.Write(PlayersKilled.Count);

                for (int i = 0; i < PlayersKilled.Count; ++i)
                {
                    writer.Write(PlayersKilled[i]);
                }

                writer.Write(BonusesDestroyed.Count);

                for (int i = 0; i < BonusesDestroyed.Count; ++i)
                {
                    writer.Write(BonusesDestroyed[i].x);
                    writer.Write(BonusesDestroyed[i].y);
                }

                writer.Write(BonusesSpawned.Count);

                foreach (var pair in BonusesSpawned)
                {
                    writer.Write(pair.Key.x);
                    writer.Write(pair.Key.y);
                    writer.Write((byte)pair.Value);
                }

                writer.Write(UpDistance);
                writer.Write(DownDistance);
                writer.Write(LeftDistance);
                writer.Write(RightDistance);
            }

            public void Read(DarkRiftReader reader)
            {
                BombPosition = new Vector2(reader.ReadSingle(), reader.ReadSingle());

                WallsDestroyed = new List<Vector2>();
                int numberOfWalls = reader.ReadInt32();

                for (int i = 0; i < numberOfWalls; ++i)
                {
                    WallsDestroyed.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                }

                int playersKilled = reader.ReadInt32();
                PlayersKilled = new List<byte>();

                for (int i = 0; i < playersKilled; ++i)
                {
                    PlayersKilled.Add(reader.ReadByte());
                }

                int bonusesDestroyed = reader.ReadInt32();
                BonusesDestroyed = new List<Vector2>();

                for (int i = 0; i < bonusesDestroyed; ++i)
                {
                    BonusesDestroyed.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                }

                int bonusesSpawned = reader.ReadInt32();
                BonusesSpawned = new Dictionary<Vector2, Game.BonusType>();

                for (int i = 0; i < bonusesSpawned; ++i)
                {
                    Vector2        position  = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    Game.BonusType bonusType = (Game.BonusType)reader.ReadByte();
                    BonusesSpawned.Add(position, bonusType);
                }

                UpDistance    = reader.ReadSingle();
                DownDistance  = reader.ReadSingle();
                LeftDistance  = reader.ReadSingle();
                RightDistance = reader.ReadSingle();
            }
        }

        public class BonusTaken : INetMessage
        {
            public MessageId MessageId  => MessageId.BonusTaken;
            public bool      IsFrequent => false;

            public Vector2        Position  { get; set; }
            public Game.BonusType BonusType { get; set; }
            public byte           PlayerId  { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(Position.x);
                writer.Write(Position.y);
                writer.Write((byte)BonusType);
                writer.Write(PlayerId);
            }

            public void Read(DarkRiftReader reader)
            {
                Position  = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                BonusType = (Game.BonusType)reader.ReadByte();
                PlayerId  = reader.ReadByte();
            }
        }

        public class AllocatedGameData : INetMessage
        {
            public MessageId MessageId  => MessageId.AllocatedGameData;
            public bool      IsFrequent => false;

            public string Address { get; set; }
            public int    Port    { get; set; }
            public int    Code    { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(Address);
                writer.Write(Port);
                writer.Write(Code);
            }

            public void Read(DarkRiftReader reader)
            {
                Address = reader.ReadString();
                Port    = reader.ReadInt32();
                Code    = reader.ReadInt32();
            }
        }

        public class GetAllocatedGameData : INetMessage
        {
            public MessageId MessageId  => MessageId.GetAllocatedGameData;
            public bool      IsFrequent => false;

            public int Code { get; set; }

            public void Write(DarkRiftWriter writer)
            {
                writer.Write(Code);
            }

            public void Read(DarkRiftReader reader)
            {
                Code = reader.ReadInt32();
            }
        }
    }
}