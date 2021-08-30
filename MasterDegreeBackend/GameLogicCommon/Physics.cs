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
using DeusaldSharp;
using SharpBox2D;

namespace GameLogicCommon
{
    public static class Physics
    {
        public static readonly Vector2[] PlayersSpawnPoints =
        {
            new Vector2(-3, 3), new Vector2(3, -3),
            new Vector2(3, 3), new Vector2(-3, -3)
        };

        public static void InitOuterWalls(Physics2D physics)
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

        public static void FillInnerWalls(Physics2D physics)
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

        public static void FillDestroyableWalls(Action<Vector2> spawnWallCallback)
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

        public static IPhysicsObject SpawnPlayer(byte id, Physics2D physics2D)
        {
            IPhysicsObject player = physics2D.CreatePhysicsObject(BodyType.Dynamic, PlayersSpawnPoints[id], 0);
            player.UserData = new Game.PhysicsObjectId
            {
                ObjectType = Game.ObjectType.Player,
                Id         = id
            };
            player.AddCircleCollider(0.4f);
            return player;
        }
    }
}