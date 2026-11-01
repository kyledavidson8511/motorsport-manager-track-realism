using System;
using HarmonyLib;
using UnityEngine;

namespace TrackRealism
{
    /// <summary>
    /// Makes the height already in each racing line matter.
    /// The base game moves cars with a point-mass model: accelerate at the car's acceleration
    /// stat up to its top speed, brake at its braking stat, hold a corner speed. Nothing reads
    /// height. Here gravity acts along the slope:
    ///   acceleration  a - g*sin(slope)      (climbs slow you, descents help)
    ///   braking       b + g*sin(slope)      (uphill braking zones are shorter, downhill longer)
    ///   top speed     v * (1 - 0.33*sin)    (power/drag balance; ~3% lower on a 10% climb)
    /// Each car feels a smoothed slope (time constant ~0.8 s) so speed never snaps.
    /// </summary>
    public static class Elevation
    {
        public const float TopSpeedSlopeFactor = 0.33f;
        private const float FilterSeconds = 0.8f;
        private const int MaxVehicles = 128;

        private static readonly float[] sFiltered = new float[MaxVehicles];

        /// <summary>When true, every elevation patch steps aside (used to measure the flat baseline).</summary>
        public static bool Bypass;

        public static bool Active
        {
            get { return Main.Enabled && !Bypass && Main.Settings != null && Main.Settings.ElevationPhysics && Main.Settings.ElevationStrength > 0f; }
        }

        public static void ResetVehicles()
        {
            Array.Clear(sFiltered, 0, sFiltered.Length);
        }

        /// <summary>Raw slope under the vehicle (sin), 0 when not on the track path.</summary>
        public static float RawGrade(Vehicle v)
        {
            try
            {
                PathController pc = v.pathController;
                if (pc == null)
                {
                    return 0f;
                }
                PathController.Path path = pc.GetCurrentPath();
                if (path == null || path.pathType != PathController.PathType.Track)
                {
                    return 0f;
                }
                TrackGeometry geo = TrackGeometry.For(path.data);
                if (geo == null)
                {
                    return 0f;
                }
                PathData.Gate prev = pc.GetPreviousGate();
                PathData.Gate next = pc.GetNextGate();
                if (prev == null)
                {
                    return 0f;
                }
                float g = geo.GradeAtGate(prev.id);
                if (next != null)
                {
                    g = 0.5f * (g + geo.GradeAtGate(next.id));
                }
                return g;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        public static void Tick(Vehicle v, float dt)
        {
            if (v == null || v.id < 0 || v.id >= MaxVehicles)
            {
                return;
            }
            float target = RawGrade(v);
            float k = dt <= 0f ? 0f : 1f - Mathf.Exp(-dt / FilterSeconds);
            sFiltered[v.id] += (target - sFiltered[v.id]) * k;
        }

        /// <summary>Smoothed, strength-scaled sin(slope) for this vehicle.</summary>
        public static float Grade(Vehicle v)
        {
            if (v == null || v.id < 0 || v.id >= MaxVehicles)
            {
                return 0f;
            }
            return sFiltered[v.id] * Main.Settings.ElevationStrength;
        }

        public static float AdjustAcceleration(float a, float sin)
        {
            return Mathf.Max(a - TrackGeometry.G * sin, a * 0.3f);
        }

        public static float AdjustBraking(float b, float sin)
        {
            return Mathf.Max(b + TrackGeometry.G * sin, b * 0.5f);
        }

        public static float AdjustTopSpeed(float v, float sin)
        {
            return v * Mathf.Clamp(1f - TopSpeedSlopeFactor * sin, 0.85f, 1.05f);
        }
    }

    // Update each car's felt slope once per simulation step, before it picks its speed.
    [HarmonyPatch(typeof(TrackLayoutSpeedController), "CalculateDesiredSpeed")]
    public static class Patch_CalculateDesiredSpeed
    {
        public static void Prefix(TrackLayoutSpeedController __instance)
        {
            if (!Elevation.Active)
            {
                return;
            }
            try
            {
                Elevation.Tick(__instance.vehicle, GameTimer.simulationDeltaTime);
            }
            catch (Exception e)
            {
                Main.LogOnce("tick", "Elevation tick failed: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(RacingVehicle), "GetAcceleration")]
    public static class Patch_GetAcceleration
    {
        public static void Postfix(RacingVehicle __instance, ref float __result)
        {
            if (Elevation.Active)
            {
                __result = Elevation.AdjustAcceleration(__result, Elevation.Grade(__instance));
            }
        }
    }

    [HarmonyPatch(typeof(RacingVehicle), "GetBraking")]
    public static class Patch_GetBraking
    {
        public static void Postfix(RacingVehicle __instance, ref float __result)
        {
            if (Elevation.Active)
            {
                __result = Elevation.AdjustBraking(__result, Elevation.Grade(__instance));
            }
        }
    }

    [HarmonyPatch(typeof(RacingVehicle), "GetTopSpeed")]
    public static class Patch_GetTopSpeed
    {
        public static void Postfix(RacingVehicle __instance, ref float __result)
        {
            if (Elevation.Active)
            {
                __result = Elevation.AdjustTopSpeed(__result, Elevation.Grade(__instance));
            }
        }
    }

    // Crests and compressions: corner speed responds to the vertical curve through the corner.
    // Static method, used both by the live cars and by the game's lap-time estimator.
    [HarmonyPatch(typeof(SessionPerformance), "GetSpeedForCorner")]
    public static class Patch_GetSpeedForCorner
    {
        public static void Postfix(PathData.Corner inCorner, ref float __result)
        {
            if (!Elevation.Active || !Main.Settings.CrestsAndCompressions || inCorner == null)
            {
                return;
            }
            try
            {
                TrackGeometry geo = Main.CurrentTrackGeometry();
                if (geo != null)
                {
                    __result *= geo.CornerFactor(inCorner, __result, Main.Settings);
                }
            }
            catch (Exception e)
            {
                Main.LogOnce("corner", "Corner adjustment failed: " + e.Message);
            }
        }
    }

    // Keep the game's own lap-time estimator consistent with the live physics. 0.1.0 adjusted
    // the estimator's car stats section by section, which overstated the effect (+4.2 s at Munich
    // against ~0 from the physics). Now the estimator runs untouched (flat), and the elevation
    // delta from a gate-by-gate model of the same physics is added on top.
    [HarmonyPatch(typeof(SimulationLapTimeModel), "EstimateLapTime")]
    public static class Patch_EstimateLapTime
    {
        public static void Prefix(out bool __state)
        {
            __state = Elevation.Active;
            if (__state)
            {
                Elevation.Bypass = true;
            }
        }

        public static void Postfix(CarStats inStats, CircuitScene inCircuit, bool __state, ref TimeSpan __result)
        {
            if (!__state)
            {
                return;
            }
            Elevation.Bypass = false;
            try
            {
                if (inCircuit == null || inStats == null)
                {
                    return;
                }
                TrackGeometry geo = TrackGeometry.For(inCircuit.GetTrackPath().data);
                if (geo == null)
                {
                    return;
                }
                float delta = ElevationModel.LapDelta(geo, inStats, Main.Settings);
                __result = __result + TimeSpan.FromSeconds(delta);
            }
            catch (Exception e)
            {
                Main.LogOnce("estimator", "Estimator adjustment failed: " + e.Message);
            }
        }
    }
}
