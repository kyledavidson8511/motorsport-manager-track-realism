using System;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace TrackRealism
{
    /// <summary>
    /// Phase 0: write every layout of the loaded venue to CSV, so width zones can be authored
    /// against exact geometry, and log a one-line elevation summary for the active layout.
    /// </summary>
    public static class TrackDump
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly AccessTools.FieldRef<CircuitScene, PathDataComponent[]> sLayoutPaths =
            AccessTools.FieldRefAccess<CircuitScene, PathDataComponent[]>("trackLayoutPath");

        public static string LastSummary = "";

        public static string VenueName(CircuitScene scene)
        {
            try
            {
                RaceEventDetails ed = Game.instance.sessionManager.eventDetails;
                if (ed != null && ed.circuit != null && !string.IsNullOrEmpty(ed.circuit.locationName))
                {
                    return ed.circuit.locationName;
                }
            }
            catch (Exception)
            {
            }
            return scene != null ? scene.gameObject.scene.name : "Unknown";
        }

        public static string Summarise(string venue, Circuit.TrackLayout layout, TrackGeometry geo)
        {
            int crests = 0, compressions = 0;
            foreach (float k in geo.CornerCurvature.Values)
            {
                if (k < -0.0015f) crests++;
                else if (k > 0.0015f) compressions++;
            }
            return string.Format(Inv,
                "{0} {1}: {2:0.00} km, climb {3:0} m, steepest {4:0.0}% up / {5:0.0}% down, {6} corners ({7} on crests, {8} in compressions), {9} straights.",
                venue, LayoutLetter(layout), geo.Length / 1000f, geo.MaxY - geo.MinY,
                geo.SteepestUp * 100f, -geo.SteepestDown * 100f,
                geo.Path.corners.Count, crests, compressions, geo.Path.straights.Count);
        }

        public static string LayoutLetter(Circuit.TrackLayout layout)
        {
            return layout.ToString().Replace("Track", "");
        }

        private static int sLastSceneId;
        private static Circuit.TrackLayout sLastLayout;
        private static float sLastSessionLog = -100f;
        private static SessionDetails.SessionType sLastSessionType;

        public static void OnLayoutSet(CircuitScene scene)
        {
            // The game sets the layout several times per weekend; only handle a new scene/layout.
            if (scene.GetInstanceID() == sLastSceneId && scene.trackLayout == sLastLayout)
            {
                return;
            }
            sLastSceneId = scene.GetInstanceID();
            sLastLayout = scene.trackLayout;
            TrackGeometry.ClearCache();
            ElevationModel.ClearCache();
            Elevation.ResetVehicles();
            string venue = VenueName(scene);
            PathData active = scene.GetTrackPath().data;
            TrackGeometry geo = TrackGeometry.For(active);
            if (geo != null)
            {
                LastSummary = Summarise(venue, scene.trackLayout, geo);
                Main.Log("TRACK: " + LastSummary);
            }
            if (Main.Settings.DumpOnLoad)
            {
                WriteAll(scene);
            }
        }

        public static int WriteAll(CircuitScene scene)
        {
            string venue = VenueName(scene);
            PathDataComponent[] paths = sLayoutPaths(scene);
            if (paths == null)
            {
                return 0;
            }
            string dir = Path.Combine(Main.ModPath, "dumps");
            Directory.CreateDirectory(dir);
            int written = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] == null || paths[i].data == null || paths[i].data.gates == null || paths[i].data.gates.Count == 0)
                {
                    continue;
                }
                Circuit.TrackLayout layout = (Circuit.TrackLayout)i;
                string file = Path.Combine(dir, Safe(venue) + "_" + LayoutLetter(layout) + ".csv");
                File.WriteAllText(file, BuildCsv(paths[i].data), Encoding.UTF8);
                written++;
            }
            Main.Log(string.Format(Inv, "DUMP: wrote {0} layout file(s) for {1} to {2}", written, venue, dir));
            return written;
        }

        private static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s.Replace(' ', '_');
        }

        private static string BuildCsv(PathData data)
        {
            TrackGeometry geo = TrackGeometry.For(data);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("gate,distance_m,x,y,z,grade_pct,gate_type,corner_id,corner_type,corner_dir,corner_radius,corner_angle,corner_vert_curv,straight_id,straight_len_m,overtake_possibility,lockup_gate,speed_trap");
            for (int i = 0; i < data.gates.Count; i++)
            {
                PathData.Gate g = data.gates[i];
                PathData.Corner c = g.corner;
                PathData.Straight s = g.straight;
                float curv = 0f;
                if (c != null && geo != null)
                {
                    geo.CornerCurvature.TryGetValue(c, out curv);
                }
                sb.AppendFormat(Inv, "{0},{1:0.0},{2:0.00},{3:0.00},{4:0.00},{5:0.00},{6},{7},{8},{9},{10:0.0},{11:0.0},{12:0.00000},{13},{14:0.0},{15:0.000},{16},{17}\n",
                    g.id,
                    geo != null ? geo.Distance[i] : 0f,
                    g.position.x, g.position.y, g.position.z,
                    geo != null ? geo.SinGrade[i] * 100f : 0f,
                    g.gateType,
                    c != null ? c.id.ToString(Inv) : "",
                    c != null ? c.type.ToString() : "",
                    c != null ? c.direction.ToString() : "",
                    c != null ? c.radius : 0f,
                    c != null ? c.angle : 0f,
                    curv,
                    s != null ? s.id.ToString(Inv) : "",
                    s != null ? s.length : 0f,
                    s != null ? s.overtakePossibility : 0f,
                    g.isLockUpGate ? 1 : 0,
                    g.isSpeedTrap ? 1 : 0);
            }
            return sb.ToString();
        }

        /// <summary>Lap estimate for the fastest car, with and without the elevation physics.</summary>
        public static void LogLapComparison()
        {
            if (!Main.Enabled || Main.Settings == null || !Main.Settings.ElevationPhysics)
            {
                return;
            }
            SessionManager sm = Game.instance.sessionManager;
            if (sm != null && sm.sessionType == sLastSessionType && Time.realtimeSinceStartup - sLastSessionLog < 30f)
            {
                return; // SessionStarting fires twice at the start of a race
            }
            sLastSessionLog = Time.realtimeSinceStartup;
            if (sm != null) sLastSessionType = sm.sessionType;
            if (sm == null || !sm.isCircuitLoaded || sm.raceDirector == null || sm.raceDirector.highestCarStats == null)
            {
                return;
            }
            CircuitScene scene = sm.circuit;
            float hills = (float)SimulationLapTimeModel.EstimateLapTime(sm.raceDirector.highestCarStats, scene).TotalSeconds;
            float flat;
            Elevation.Bypass = true;
            try
            {
                flat = (float)SimulationLapTimeModel.EstimateLapTime(sm.raceDirector.highestCarStats, scene).TotalSeconds;
            }
            finally
            {
                Elevation.Bypass = false;
            }
            Main.Log(string.Format(Inv,
                "SESSION START: {0} at {1} {2} — fastest-car lap estimate {3:0.00}s flat vs {4:0.00}s with elevation ({5:+0.00;-0.00}s), strength {6:0.00}.",
                sm.sessionType, VenueName(scene), LayoutLetter(scene.trackLayout), flat, hills, hills - flat, Main.Settings.ElevationStrength));
        }
    }

    [HarmonyPatch(typeof(CircuitScene), "SetTrackLayout")]
    public static class Patch_SetTrackLayout
    {
        public static void Postfix(CircuitScene __instance)
        {
            if (!Main.Enabled)
            {
                return;
            }
            try
            {
                TrackDump.OnLayoutSet(__instance);
            }
            catch (Exception e)
            {
                Main.Logger.Error("Track load handling failed: " + e);
            }
            try
            {
                SceneDump.OnLayoutSet(__instance);
            }
            catch (Exception e)
            {
                Main.Logger.Error("Scene dump failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(SessionManager), "SessionStarting")]
    public static class Patch_SessionStarting
    {
        public static void Postfix()
        {
            try
            {
                Elevation.ResetVehicles();
                TrackDump.LogLapComparison();
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session start handling failed: " + e);
            }
        }
    }
}
