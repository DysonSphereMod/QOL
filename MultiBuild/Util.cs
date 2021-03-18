using System;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public static class Util
    {
        public const float planetRadius = 200;
        public static Vector2 ToSpherical (this Vector3 vector)
        {
            float inclination = Mathf.Acos(vector.y / vector.magnitude);
            float azimuth  = Mathf.Atan2(vector.z, vector.x);
            return new Vector2(inclination, azimuth);
        }
        
        public static Vector3 ToCartesian(this Vector2 vector, float radius = planetRadius)
        {
            vector.Clamp();
            float x = radius * Mathf.Sin(vector.x) * Mathf.Cos(vector.y);
            float y = radius * Mathf.Cos(vector.x);
            float z = radius * Mathf.Sin(vector.x) * Mathf.Sin(vector.y);
            return new Vector3(x, y, z);
        }

        public static int GetSegmentsCount(this Vector2 vector, float radius = planetRadius)
        {
            float rawLatitudeIndex = (vector.x - Mathf.PI / 2) / 6.2831855f * radius;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            return PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);
        }

        public static Vector2 ApplyDelta(this Vector2 vector, Vector2 delta, int deltaCount)
        {
            float sizeDeviation = deltaCount / (float)((vector + delta).GetSegmentsCount());
            var fixedDelta = new Vector2(delta.x, delta.y * sizeDeviation);

            return vector + fixedDelta;
        }

        public static Vector2 Clamp(this Vector2 vector)
        {
            vector.x = Mathf.Repeat(vector.x + Mathf.PI, 2 * Mathf.PI) - Mathf.PI;
            vector.y = Mathf.Repeat(vector.y + Mathf.PI, 2 * Mathf.PI) - Mathf.PI;
            return vector;
        }

        public static Vector2 ToDegrees (this Vector2 vector)
        {
            return vector * Mathf.Rad2Deg;
        }
        
        public static Vector2 ToRadians (this Vector2 vector)
        {
            return vector * Mathf.Deg2Rad;
        }
        
        /// <summary>
        /// For spherical coordinates only. Only supports angles %90 degrees
        /// </summary>
        public static Vector2 Rotate(this Vector2 v, float delta,  int sectorCount)
        {
            delta *= -1;
            
            float value = sectorCount / planetRadius;
            if (value == 0) value = 1f;
            
            Vector2 correction = new Vector2(value, 1/value); //Try new Vector2(value, 1) for continuous rotation
            
            Vector2 rotated = new Vector2(
                v.x * Mathf.Cos(delta) - v.y * Mathf.Sin(delta),
                v.x * Mathf.Sin(delta) + v.y * Mathf.Cos(delta)
            );
            if (Mathf.Abs(Mathf.Sin(delta)) > 0.3f)
            {
                return rotated * correction;
            }

            return rotated;
        }

    }
}
