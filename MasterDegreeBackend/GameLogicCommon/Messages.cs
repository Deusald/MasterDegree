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
            ClientHeartBeat,
            ServerHeartBeat,
            PlayerInit,
            SpawnPlayer,
            StartGame,
            PlayerInput,
            PlayerPosition
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
                yourId = yourId.MarkBit(4, WallSpawned);

                writer.Write(yourId);
                writer.Write(ServerClockStartTimer);
            }

            public void Read(DarkRiftReader reader)
            {
                byte yourId = reader.ReadByte();
                WallSpawned           = yourId.HasAnyBitOn(4);
                yourId                = yourId.MarkBit(4, false);
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
    }
}