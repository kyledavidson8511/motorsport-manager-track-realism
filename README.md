# Track Realism — Elevation & Track Width for Motorsport Manager

Track Realism makes elevation matter. Most stock circuits already carry real height data in their racing lines, but the base game's car physics ignores it. This mod applies it: cars accelerate slower uphill and brake later downhill, top speed drops on climbs, and crests and compressions nudge corner speeds (capped at ±5%). The game's lap-time estimator gets the same correction, so expected positions and simulated sessions agree with what you see on track. Strength is adjustable in Unity Mod Manager (Ctrl+F10).

Flat circuits are unaffected. Hilly ones change section by section: climbs and descents largely cancel over a lap, typically within a few tenths.

**Track width:** 1.0 does not change width yet. The base game uses a fixed 7 m everywhere. The mod writes per-layout track dumps (`Mods\TrackRealism\dumps`) that per-track narrow and wide zones will be built from in a future version.

## Install

1. Install Unity Mod Manager for Motorsport Manager.
2. Download `TrackRealism-1.0.1.zip` from the GitHub Release.
3. Extract the `TrackRealism` folder to `Steam/steamapps/common/Motorsport Manager/Mods/`.
4. Start the game and confirm **Track Realism — Elevation & Track Width** is active in UMM.

## Build from source

`src/track-realism/` targets .NET 3.5 and the optional `mod-verifier/` is a separate verifier project. Build against your locally installed Motorsport Manager managed assemblies plus Unity Mod Manager and Harmony; proprietary game files are intentionally excluded.

## Feedback

Please include the circuit, session type, mod settings, active-mod list, and any relevant `output_log` lines.
