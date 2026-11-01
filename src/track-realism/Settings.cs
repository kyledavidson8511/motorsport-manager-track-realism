using UnityModManagerNet;

namespace TrackRealism
{
    public class Settings : UnityModManager.ModSettings
    {
        // ---- Elevation physics ----
        // Uses the height that is already in every circuit's racing line (the base game ignores it).
        public bool ElevationPhysics = true;

        // 1.0 = real-world physics (gravity along the slope). 0 = off. Above 1 exaggerates.
        public float ElevationStrength = 1.0f;

        // Crests unload the car (slower corner), compressions load it (faster corner).
        public bool CrestsAndCompressions = true;

        // Biggest corner-speed change a crest or compression can cause (realistic: ~5%).
        public float MaxCrestEffect = 0.05f;

        // ---- Track dump (phase 0) ----
        // Writes every layout's gates to Mods/TrackRealism/dumps/ when a circuit loads.
        public bool DumpOnLoad = true;

        // ---- Scene dump (COTA phase 0, read-only) ----
        // Writes the loaded venue's scene structure to Mods/TrackRealism/dumps/scene-<venue>.txt, once per load.
        public bool SceneDumpOnLoad = true;

        // ---- Debug ----
        public bool LogEvents = true;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
