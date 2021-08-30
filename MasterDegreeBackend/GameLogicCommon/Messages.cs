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
            SpawnPlayer
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
                WallSpawned = yourId.HasAnyBitOn(4);
                yourId = yourId.MarkBit(4, false);
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
    }
}