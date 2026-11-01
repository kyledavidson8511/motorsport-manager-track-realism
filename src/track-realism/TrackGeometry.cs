using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrackRealism
{
    /// <summary>
    /// Per-path elevation data, computed once from the gates the game already builds.
    /// Gates are ~15 m apart and their positions are world-space 3D (the y axis is height).
    /// </summary>
    public class TrackGeometry
    {
        public const float G = 9.81f;
        public const float MaxSin = 0.25f;

        public PathData Path;
        public int GateCount;
        public float[] SinGrade;          // sin(slope) at each gate, + = uphill in driving direction, ~60 m smoothing
        public float[] Distance;          // distance along the lap at each gate (m)
        public float Length;              // lap length along the gates (m)
        public float MinY, MaxY;
        public float SteepestUp, SteepestDown; // as sin(slope)
        public Dictionary<PathData.Corner, float> CornerCurvature = new Dictionary<PathData.Corner, float>(); // vertical curvature (1/m), + = compression (dip), - = crest

        private static readonly Dictionary<PathData, TrackGeometry> sCache = new Dictionary<PathData, TrackGeometry>();

        public static void ClearCache()
        {
            sCache.Clear();
        }

        public static TrackGeometry For(PathData path)
        {
            if (path == null || path.gates == null || path.gates.Count < 8)
            {
                return null;
            }
            TrackGeometry geo;
            if (sCache.TryGetValue(path, out geo) && geo.GateCount == path.gates.Count)
            {
                return geo;
            }
            geo = Build(path);
            sCache[path] = geo;
            return geo;
        }

        private static int Wrap(int i, int n, bool loop)
        {
            if (loop)
            {
                i %= n;
                return i < 0 ? i + n : i;
            }
            return Mathf.Clamp(i, 0, n - 1);
        }

        private static TrackGeometry Build(PathData path)
        {
            List<PathData.Gate> gates = path.gates;
            int n = gates.Count;
            bool loop = path.type == PathData.Type.Loop;
            TrackGeometry geo = new TrackGeometry();
            geo.Path = path;
            geo.GateCount = n;
            geo.SinGrade = new float[n];
            geo.Distance = new float[n];
            geo.MinY = float.MaxValue;
            geo.MaxY = float.MinValue;

            float d = 0f;
            for (int i = 0; i < n; i++)
            {
                geo.Distance[i] = d;
                Vector3 p = gates[i].position;
                geo.MinY = Mathf.Min(geo.MinY, p.y);
                geo.MaxY = Mathf.Max(geo.MaxY, p.y);
                if (i < n - 1 || loop)
                {
                    d += Vector3.Distance(p, gates[Wrap(i + 1, n, loop)].position);
                }
            }
            geo.Length = d;

            // Raw slope over +/-2 gates (~60 m): rise over path length.
            const int half = 2;
            for (int i = 0; i < n; i++)
            {
                int a = Wrap(i - half, n, loop);
                int b = Wrap(i + half, n, loop);
                float len = 0f;
                for (int k = -half; k < half; k++)
                {
                    len += Vector3.Distance(gates[Wrap(i + k, n, loop)].position, gates[Wrap(i + k + 1, n, loop)].position);
                }
                float s = len > 1f ? (gates[b].position.y - gates[a].position.y) / len : 0f;
                geo.SinGrade[i] = Mathf.Clamp(s, -MaxSin, MaxSin);
                geo.SteepestUp = Mathf.Max(geo.SteepestUp, geo.SinGrade[i]);
                geo.SteepestDown = Mathf.Min(geo.SteepestDown, geo.SinGrade[i]);
            }

            // Vertical curvature through each corner: second difference of height over the corner's span.
            if (path.corners != null)
            {
                foreach (PathData.Corner c in path.corners)
                {
                    int span = Mathf.Max(3, c.gateCount / 2 + 1);
                    int mid = Wrap(c.startGateID + c.gateCount / 2, n, loop);
                    int lo = Wrap(mid - span, n, loop);
                    int hi = Wrap(mid + span, n, loop);
                    float s1 = 0f, s2 = 0f;
                    for (int k = -span; k < 0; k++) s1 += Vector3.Distance(gates[Wrap(mid + k, n, loop)].position, gates[Wrap(mid + k + 1, n, loop)].position);
                    for (int k = 0; k < span; k++) s2 += Vector3.Distance(gates[Wrap(mid + k, n, loop)].position, gates[Wrap(mid + k + 1, n, loop)].position);
                    if (s1 < 1f || s2 < 1f)
                    {
                        continue;
                    }
                    float yl = gates[lo].position.y, ym = gates[mid].position.y, yh = gates[hi].position.y;
                    // Non-uniform second derivative.
                    float kappa = 2f * (s1 * (yh - ym) - s2 * (ym - yl)) / (s1 * s2 * (s1 + s2));
                    geo.CornerCurvature[c] = kappa;
                }
            }
            return geo;
        }

        public float GradeAtGate(int gateId)
        {
            if (gateId < 0 || gateId >= GateCount)
            {
                return 0f;
            }
            return SinGrade[gateId];
        }

        /// <summary>Average sin(slope) walking forward from one gate to another (inclusive).</summary>
        public float AverageGrade(int fromId, int toId)
        {
            if (GateCount == 0)
            {
                return 0f;
            }
            fromId = Wrap(fromId, GateCount, true);
            toId = Wrap(toId, GateCount, true);
            float sum = 0f;
            int count = 0;
            int i = fromId;
            for (int guard = 0; guard <= GateCount; guard++)
            {
                sum += SinGrade[i];
                count++;
                if (i == toId)
                {
                    break;
                }
                i = (i + 1) % GateCount;
            }
            return count > 0 ? sum / count : 0f;
        }

        public float CornerFactor(PathData.Corner corner, float cornerSpeed, Settings s)
        {
            float kappa;
            if (corner == null || !CornerCurvature.TryGetValue(corner, out kappa))
            {
                return 1f;
            }
            return CrestFactor(kappa, cornerSpeed, s.ElevationStrength, s.MaxCrestEffect);
        }

        /// <summary>
        /// Extra vertical load in g = v^2 * kappa / g. Only the mechanical-grip share responds
        /// (downforce does not care about crests), so it's scaled by 0.35, then capped.
        /// </summary>
        public static float CrestFactor(float kappa, float speed, float strength, float maxEffect)
        {
            float extraG = speed * speed * kappa / G;
            float f = 1f + 0.35f * extraG * strength;
            float cap = maxEffect * Mathf.Max(strength, 0f);
            return Mathf.Clamp(f, 1f - cap, 1f + cap);
        }
    }
}
