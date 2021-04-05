using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public static class Util
    {
        public static int MAX_THREADS = Environment.ProcessorCount - 1;
        public static PlayerAction_Build ClonePlayerAction_Build(PlayerAction_Build original)
        {
            var nearcdClone = new NearColliderLogic()
            {
                planet = original.nearcdLogic.planet,
                colChunks = original.nearcdLogic.colChunks,
                activeColHashes = new int[600],
                activeColHashCount = 0,
                colliderObjs = original.nearcdLogic.colliderObjs
            };

            var clone = new PlayerAction_Build()
            {
                factory = original.factory,
                player = original.player,
                nearcdLogic = nearcdClone,
                tmpPackage = original.tmpPackage,
                planetAux = original.planetAux,
                cursorTarget = original.cursorTarget,
                buildPreviews = original.buildPreviews,
                planetPhysics = original.planetPhysics,
                previewPose = new Pose(Vector3.zero, Quaternion.identity),
                posePairs = new List<PlayerAction_Build.PosePair>(64),
                startSlots = new List<PlayerAction_Build.SlotPoint>(64),
                endSlots = new List<PlayerAction_Build.SlotPoint>(64)
            };

            return clone;
        }
        public static void Parallelize(Action<int> job)
        {
            Task[] tasks = new Task[MAX_THREADS];

            for (int i = 0; i < MAX_THREADS; i++)
            {
                int taskIndex = i;
                tasks[taskIndex] = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        job(taskIndex);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                });
            }
            try
            {
                Task.WaitAll(tasks, 1000);
            }
            catch (AggregateException ae)
            {
                Console.WriteLine("One or more exceptions occurred: ");
                foreach (var ex in ae.Flatten().InnerExceptions)
                    Console.WriteLine($"   {ex.Message}");
            }
        }

        public static Vector2 ToSpherical(this Vector3 vector)
        {
            float inclination = Mathf.Acos(vector.y / vector.magnitude);
            float azimuth = Mathf.Atan2(vector.z, vector.x);
            return new Vector2(inclination, azimuth);
        }

        public static Vector3 ToCartesian(this Vector2 vector, float realRadius)
        {
            float x = realRadius * Mathf.Sin(vector.x) * Mathf.Cos(vector.y);
            float y = realRadius * Mathf.Cos(vector.x);
            float z = realRadius * Mathf.Sin(vector.x) * Mathf.Sin(vector.y);
            return new Vector3(x, y, z);
        }

        public static Vector3 SnapToGrid(this Vector2 sprPos, float altitude = 0)
        {
            float planetRadius = GameMain.localPlanet.realRadius;

            //Both with +90 deg from physics definition
            float theta = sprPos.x - Mathf.PI / 2;
            float phi = sprPos.y - Mathf.PI / 2;

            int segments = (int)(planetRadius / 4f + 0.1f) * 4;
            float rawLatitudeIndex = theta / 6.2831855f * planetRadius;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            float segmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, segments);

            float newPhi = phi / 6.2831855f * segmentCount;
            rawLatitudeIndex = Mathf.Round(rawLatitudeIndex * 5f) / 5f;
            newPhi = Mathf.Round(newPhi * 5f) / 5f;
            theta = rawLatitudeIndex / planetRadius * 6.2831855f;
            phi = newPhi / segmentCount * 6.2831855f;

            return new Vector2(theta + Mathf.PI / 2, phi + Mathf.PI / 2).ToCartesian(planetRadius + 0.2f + altitude);
        }

        public static int GetSegmentsCount(this Vector2 vector)
        {
            float planetRadius = GameMain.localPlanet.realRadius;

            int segments = (int)(planetRadius / 4f + 0.1f) * 4;
            float rawLatitudeIndex = (vector.x - Mathf.PI / 2) / 6.2831855f * planetRadius;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            return PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, segments);
        }

        public static Vector2 ApplyDelta(this Vector2 vector, Vector2 delta, int deltaCount)
        {
            float sizeDeviation = deltaCount / (float)((vector + delta).GetSegmentsCount());
            var fixedDelta = new Vector2(delta.x, delta.y * sizeDeviation);

            return (vector + fixedDelta).Clamp();
        }

        public static Vector2 Clamp(this Vector2 vector)
        {
            vector.x = Mathf.Repeat(vector.x + Mathf.PI, 2 * Mathf.PI) - Mathf.PI;
            vector.y = Mathf.Repeat(vector.y + Mathf.PI, 2 * Mathf.PI) - Mathf.PI;
            return vector;
        }

        public static Vector2 ToDegrees(this Vector2 vector)
        {
            return vector * Mathf.Rad2Deg;
        }

        public static Vector2 ToRadians(this Vector2 vector)
        {
            return vector * Mathf.Deg2Rad;
        }

        /// <summary>
        /// For spherical coordinates only. Only supports angles %90 degrees
        /// </summary>
        public static Vector2 Rotate(this Vector2 v, float delta, int sectorCount)
        {
            float planetRadius = GameMain.localPlanet.realRadius;
            float value = sectorCount / planetRadius;
            delta *= -1;
            if (value == 0) value = 1f;

            Vector2 correction = new Vector2(value, 1 / value); //Try new Vector2(value, 1) for continuous rotation

            Vector2 rotated = new Vector2(
                v.x * Mathf.Cos(delta) - v.y * Mathf.Sin(delta),
                v.x * Mathf.Sin(delta) + v.y * Mathf.Cos(delta)
            ).Clamp();
            if (Mathf.Abs(Mathf.Sin(delta)) > 0.3f)
            {
                return (rotated * correction).Clamp();
            }

            return rotated;
        }

    }
}
