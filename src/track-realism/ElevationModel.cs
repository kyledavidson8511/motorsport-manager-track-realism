using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrackRealism
{
    /// <summary>
    /// Gate-by-gate point-mass lap, run twice (flat and with the hills) using the same rules as
    /// the live patches. Only the difference is used, added to the game's own estimate.
    /// </summary>
    public static class ElevationModel
    {
        private struct Key
        {
            public PathData Path;
            public float Top, Acc, Brk, L, M, H, Strength, Cap;
            public bool Crests;
        }

        private static readonly Dictionary<Key, float> sCache = new Dictionary<Key, float>();

        public static void ClearCache()
        {
            sCache.Clear();
        }

        public static float LapDelta(TrackGeometry geo, CarStats stats, Settings s)
        {
            Key k = new Key
            {
                Path = geo.Path, Top = stats.topSpeed, Acc = stats.acceleration, Brk = stats.braking,
                L = stats.lowSpeedCorners, M = stats.mediumSpeedCorners, H = stats.highSpeedCorners,
                Strength = s.ElevationStrength, Cap = s.MaxCrestEffect, Crests = s.CrestsAndCompressions
            };
            float d;
            if (sCache.TryGetValue(k, out d))
            {
                return d;
            }
            d = Lap(geo, stats, s, true) - Lap(geo, stats, s, false);
            if (float.IsNaN(d) || float.IsInfinity(d))
            {
                d = 0f;
            }
            if (sCache.Count > 512)
            {
                sCache.Clear();
            }
            sCache[k] = d;
            return d;
        }

        public static float Lap(TrackGeometry geo, CarStats stats, Settings s, bool hills)
        {
            List<PathData.Gate> gates = geo.Path.gates;
            int n = geo.GateCount;
            float strength = hills ? s.ElevationStrength : 0f;
            float[] seg = new float[n];
            float[] vmax = new float[n];
            Dictionary<PathData.Corner, float> cornerSpeed = new Dictionary<PathData.Corner, float>();

            bool bypass = Elevation.Bypass;
            Elevation.Bypass = true; // corner speeds without the crest factor; applied below
            try
            {
                for (int i = 0; i < n; i++)
                {
                    seg[i] = Mathf.Max(1f, Vector3.Distance(gates[i].position, gates[(i + 1) % n].position));
                    float sin = geo.SinGrade[i] * strength;
                    float top = Elevation.AdjustTopSpeed(stats.topSpeed, sin);
                    PathData.Corner c = gates[i].corner;
                    if (c != null)
                    {
                        float cs;
                        if (!cornerSpeed.TryGetValue(c, out cs))
                        {
                            cs = Mathf.Clamp(SessionPerformance.GetSpeedForCorner(stats, c), 1f, stats.topSpeed);
                            if (hills && s.CrestsAndCompressions)
                            {
                                cs *= geo.CornerFactor(c, cs, s);
                            }
                            cornerSpeed[c] = cs;
                        }
                        vmax[i] = Mathf.Min(cs, top);
                    }
                    else
                    {
                        vmax[i] = top;
                    }
                }
            }
            finally
            {
                Elevation.Bypass = bypass;
            }

            float[] v = (float[])vmax.Clone();
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    float a = Elevation.AdjustAcceleration(stats.acceleration, geo.SinGrade[i] * strength);
                    v[j] = Mathf.Min(v[j], Mathf.Sqrt(v[i] * v[i] + 2f * a * seg[i]));
                }
                for (int i = n - 1; i >= 0; i--)
                {
                    int j = (i + 1) % n;
                    float b = Elevation.AdjustBraking(stats.braking, geo.SinGrade[i] * strength);
                    v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[j] * v[j] + 2f * b * seg[i]));
                }
            }
            float t = 0f;
            for (int i = 0; i < n; i++)
            {
                float avg = 0.5f * (v[i] + v[(i + 1) % n]);
                t += seg[i] / Mathf.Max(1f, avg);
            }
            return t;
        }
    }
}
