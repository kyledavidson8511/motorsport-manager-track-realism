using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace TrackRealism
{
    public static class Main
    {
        public const string Version = "1.0.1";
        public static bool Enabled;
        public static Settings Settings;
        public static UnityModManager.ModEntry.ModLogger Logger;
        public static string ModPath;
        private static Harmony sHarmony;
        private static readonly HashSet<string> sLoggedOnce = new HashSet<string>();

        // Entry point named in Info.json.
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            ModPath = modEntry.Path;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            sHarmony = new Harmony(modEntry.Info.Id);
            sHarmony.PatchAll(Assembly.GetExecutingAssembly());
            Enabled = true;
            Logger.Log("Track Realism " + Version + " loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            // Patches stay applied; every patch checks Enabled and does nothing when off.
            Enabled = value;
            return true;
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }

        public static void Log(string message)
        {
            if (Settings != null && Settings.LogEvents && Logger != null)
            {
                Logger.Log(message);
            }
        }

        public static void LogOnce(string key, string message)
        {
            if (sLoggedOnce.Add(key) && Logger != null)
            {
                Logger.Error(message);
            }
        }

        public static TrackGeometry CurrentTrackGeometry()
        {
            if (Game.instance == null || Game.instance.sessionManager == null || !Game.instance.sessionManager.isCircuitLoaded)
            {
                return null;
            }
            PathDataComponent track = Game.instance.sessionManager.circuit.GetTrackPath();
            return track != null ? TrackGeometry.For(track.data) : null;
        }

        // ------------------------------------------------------------------
        // Settings panel (UMM: Ctrl+F10 in game)
        // ------------------------------------------------------------------

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Settings s = Settings;

            GUILayout.Label("<b>Elevation physics</b>: the hills are already in the tracks; this makes the cars feel them.");
            s.ElevationPhysics = GUILayout.Toggle(s.ElevationPhysics, " Gravity on slopes (climbs cost acceleration and top speed; downhill braking zones get longer)");
            s.ElevationStrength = Slider("Strength (1.00 = real-world physics)", s.ElevationStrength, 0f, 2f, "0.00");
            s.CrestsAndCompressions = GUILayout.Toggle(s.CrestsAndCompressions, " Crests and compressions change corner speed");
            s.MaxCrestEffect = Slider("Largest crest/compression effect on corner speed", s.MaxCrestEffect, 0f, 0.15f, "0%");

            GUILayout.Space(8);
            GUILayout.Label("<b>Track dump</b>: writes every layout's geometry to Mods\\TrackRealism\\dumps\\ (for authoring narrow/wide zones).");
            s.DumpOnLoad = GUILayout.Toggle(s.DumpOnLoad, " Write a dump every time a circuit loads");
            bool loaded = Game.instance != null && Game.instance.sessionManager != null && Game.instance.sessionManager.isCircuitLoaded;
            if (loaded && GUILayout.Button("Write track dump now", GUILayout.Width(200)))
            {
                try
                {
                    TrackDump.WriteAll(Game.instance.sessionManager.circuit);
                }
                catch (Exception e)
                {
                    Logger.Error("Dump failed: " + e);
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Scene dump</b> (COTA scoping, read-only): writes the loaded venue's 3D scene structure to Mods\\TrackRealism\\dumps\\scene-<venue>.txt.");
            s.SceneDumpOnLoad = GUILayout.Toggle(s.SceneDumpOnLoad, " Write a scene dump once each time a venue loads");
            if (loaded && GUILayout.Button("Write scene dump now", GUILayout.Width(200)))
            {
                try
                {
                    SceneDump.Write(Game.instance.sessionManager.circuit);
                }
                catch (Exception e)
                {
                    Logger.Error("Scene dump failed: " + e);
                }
            }

            GUILayout.Space(8);
            s.LogEvents = GUILayout.Toggle(s.LogEvents, " Log track summaries and lap-time comparisons to the UMM log");

            if (!string.IsNullOrEmpty(TrackDump.LastSummary))
            {
                GUILayout.Space(8);
                GUILayout.Label("Current track: " + TrackDump.LastSummary);
            }

            GUILayout.Space(8);
            if (GUILayout.Button("Reset to defaults", GUILayout.Width(160)))
            {
                Settings fresh = new Settings();
                s.ElevationPhysics = fresh.ElevationPhysics;
                s.ElevationStrength = fresh.ElevationStrength;
                s.CrestsAndCompressions = fresh.CrestsAndCompressions;
                s.MaxCrestEffect = fresh.MaxCrestEffect;
                s.DumpOnLoad = fresh.DumpOnLoad;
                s.SceneDumpOnLoad = fresh.SceneDumpOnLoad;
                s.LogEvents = fresh.LogEvents;
            }
        }

        private static float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(330));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220));
            GUILayout.Label(value.ToString(format), GUILayout.Width(70));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
