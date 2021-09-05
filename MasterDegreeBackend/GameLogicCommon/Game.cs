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

using DeusaldSharp;

namespace GameLogicCommon
{
    public static class Game
    {
        public enum GameState
        {
            BeforeStart = 0,
            Running     = 1,
            Ended       = 2
        }

        public enum ObjectType
        {
            StaticWall      = 1,
            Player          = 2,
            DestroyableWall = 3,
            Bomb            = 4,
            Bonus           = 5
        }

        public struct PhysicsObjectId
        {
            public ObjectType ObjectType { get; set; }
            public uint       Id         { get; set; }
        }

        public class PlayerInput
        {
            public Vector2 Direction { get; set; }
            public bool    PutBomb   { get; set; }
            public bool    Detonate  { get; set; }
            public uint    Frame     { get; set; }
        }

        public const float PlayerSpeed = 5f;

        public static readonly Vector2[] PlayersSpawnPoints =
        {
            new Vector2(-3, 3), new Vector2(3, -3),
            new Vector2(3, 3), new Vector2(-3, -3)
        };
    }
}