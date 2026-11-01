using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace TrackRealism
{
    /// <summary>
    /// COTA phase 0 (read-only): when a venue loads, write its scene structure to
    /// Mods/TrackRealism/dumps/scene-&lt;venue&gt;.txt so a donor venue can be chosen and a
    /// generated circuit can be fitted into it. Changes nothing in the scene.
    /// </summary>
    public static class SceneDump
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int MaxTreeDepth = 4;
        private const int MaxChildrenListed = 40;
        private static int sLastDumpedSceneId;
        private static readonly HarmonyLib.AccessTools.FieldRef<PathData, PathData.SplineData[]> sSplineData =
            HarmonyLib.AccessTools.FieldRefAccess<PathData, PathData.SplineData[]>("mSplineData");

        public static void OnLayoutSet(CircuitScene scene)
        {
            if (!Main.Settings.SceneDumpOnLoad || scene.GetInstanceID() == sLastDumpedSceneId)
            {
                return;
            }
            sLastDumpedSceneId = scene.GetInstanceID();
            Write(scene);
        }

        public static string Write(CircuitScene scene)
        {
            string venue = TrackDump.VenueName(scene);
            string dir = Path.Combine(Main.ModPath, "dumps");
            Directory.CreateDirectory(dir);
            string mode = scene.hasLoaded3DGeometry ? "3D" : "2D";
            string file = Path.Combine(dir, "scene-" + Safe(venue) + (mode == "2D" ? "-2D" : "") + ".txt");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Track Realism scene dump " + Main.Version);
            sb.AppendLine("Venue: " + venue + "   Unity scene: " + scene.gameObject.scene.name + "   Mode: " + mode + "   Active layout: " + TrackDump.LayoutLetter(scene.trackLayout));
            sb.AppendLine("Written: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv));
            sb.AppendLine();

            Section(sb, "CIRCUIT DATA ROW");
            try
            {
                Circuit c = Game.instance.sessionManager.eventDetails.circuit;
                sb.AppendFormat(Inv, "id {0}  location '{1}' ({2})  country {3} ({4})  sprite '{5}'  scene '{6}'  layout {7}\n",
                    c.circuitID, c.locationName, c.locationNameID, c.nationalityKey, c.countryNameID, c.spriteName, c.scene, c.trackLayout);
                sb.AppendFormat(Inv, "length {0:0.000} mi  SC {1:0.00}  VSC {2:0.00}  rubber {3:0.00}  minimap rot {4} axisX {5}  north {6}  travel {7}\n",
                    c.trackLengthMiles, c.safetyCarFlagProbability, c.virtualSafetyCarProbability, c.trackRubberModifier,
                    c.minimapRotation, c.minimapAxisX, c.isInNorthHemisphere, c.travelCost);
                sb.AppendFormat(Inv, "tyres {0}/{1}/{2}  wear {3}  fuel {4}  climate {5}  globe {6}\n",
                    c.firstTyreOption, c.secondTyreOption, c.thirdTyreOption, c.tyreWearRate, c.fuelBurnRate,
                    c.climate != null ? c.climate.ToString() : "-", c.globeLocationID);
                sb.Append("all layouts at this location:");
                foreach (Circuit other in Game.instance.circuitManager.GetCircuitsByLocation(c.locationName))
                {
                    sb.AppendFormat(Inv, "  [{0}] {1} {2:0.000} mi", other.circuitID, TrackDump.LayoutLetter(other.trackLayout), other.trackLengthMiles);
                }
                sb.AppendLine();
            }
            catch (Exception e)
            {
                sb.AppendLine("(circuit row unavailable: " + e.Message + ")");
            }
            sb.AppendLine();

            Section(sb, "CIRCUITSCENE FIELDS");
            DumpSceneFields(sb, scene);
            sb.AppendLine();

            Section(sb, "PATHS (every PathDataComponent in the scene)");
            PathDataComponent[] paths = scene.GetComponentsInChildren<PathDataComponent>(true);
            sb.AppendFormat(Inv, "{0} path components\n", paths.Length);
            foreach (PathDataComponent p in paths)
            {
                PathData d = p.data;
                int gates = d.gates != null ? d.gates.Count : 0;
                int corners = d.corners != null ? d.corners.Count : 0;
                int straights = d.straights != null ? d.straights.Count : 0;
                Vector3 first = gates > 0 ? d.gates[0].position : Vector3.zero;
                Vector3 last = gates > 0 ? d.gates[gates - 1].position : Vector3.zero;
                sb.AppendFormat(Inv, "{0}  active={1}  type={2}  len={3:0}m  width={4:0.0}  layoutPts={5}  racingPts={6}  gates={7}  corners={8}  straights={9}  flags={10}  first=({11})  last=({12})\n",
                    PathOf(p.transform, scene.transform), p.gameObject.activeInHierarchy, d.type, d.length, d.width,
                    SafeCount(d, PathData.SplineMode.Layout), SafeCount(d, PathData.SplineMode.RacingLine),
                    gates, corners, straights, PathFlags(d), V(first), V(last));
            }
            sb.AppendLine();

            Section(sb, "GARAGES, GRID AND BOUNDS");
            TeamPitGarage[] garages = scene.GetComponentsInChildren<TeamPitGarage>(true);
            sb.AppendFormat(Inv, "{0} TeamPitGarage components\n", garages.Length);
            foreach (TeamPitGarage g in garages)
            {
                sb.AppendFormat(Inv, "  {0}  pos=({1})  rotY={2:0}\n", PathOf(g.transform, scene.transform), V(g.transform.position), g.transform.eulerAngles.y);
            }
            if (scene.trackBounds != null)
            {
                Bounds tb = scene.trackBounds.bounds;
                sb.AppendFormat(Inv, "trackBounds centre=({0}) size=({1})\n", V(tb.center), V(tb.size));
            }
            sb.AppendLine();

            Section(sb, "CAMERAS");
            foreach (Camera cam in scene.GetComponentsInChildren<Camera>(true))
            {
                sb.AppendFormat(Inv, "  {0}  pos=({1})  fov={2:0}  far={3:0}  scripts={4}\n",
                    PathOf(cam.transform, scene.transform), V(cam.transform.position), cam.fieldOfView, cam.farClipPlane, Components(cam.gameObject, true));
            }
            sb.AppendLine();

            Section(sb, "TERRAIN");
            Terrain[] terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();
            sb.AppendFormat(Inv, "{0} Unity Terrain object(s)\n", terrains.Length);
            foreach (Terrain t in terrains)
            {
                TerrainData td = t.terrainData;
                sb.AppendFormat(Inv, "  {0}  pos=({1})  size=({2})  heightmap={3}  trees={4}  details={5}\n",
                    t.name, V(t.transform.position), td != null ? V(td.size) : "-", td != null ? td.heightmapResolution : 0,
                    td != null ? td.treeInstanceCount : 0, td != null ? td.detailResolution : 0);
            }
            sb.AppendLine();

            Section(sb, "RENDERERS BY LAYER");
            DumpLayers(sb, scene);
            sb.AppendLine();

            Section(sb, "ROOT OBJECTS (whole Unity scene, with world bounds of their renderers)");
            foreach (GameObject root in scene.gameObject.scene.GetRootGameObjects())
            {
                DumpRootSummary(sb, root);
            }
            sb.AppendLine();

            Section(sb, "OBJECT TREE (depth " + MaxTreeDepth + "; long child lists are summarised)");
            foreach (GameObject root in scene.gameObject.scene.GetRootGameObjects())
            {
                DumpTree(sb, root.transform, 0);
            }

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            Main.Log("SCENE DUMP: wrote " + file);
            return file;
        }

        private static void Section(StringBuilder sb, string title)
        {
            sb.AppendLine("==== " + title + " ====");
        }

        private static void DumpSceneFields(StringBuilder sb, CircuitScene scene)
        {
            FieldInfo[] fields = typeof(CircuitScene).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo f in fields)
            {
                object v;
                try
                {
                    v = f.GetValue(scene);
                }
                catch (Exception)
                {
                    continue;
                }
                sb.Append("  ").Append(f.Name).Append(" = ").AppendLine(Describe(v, scene.transform));
            }
        }

        private static string Describe(object v, Transform root)
        {
            if (v == null)
            {
                return "null";
            }
            UnityEngine.Object uo = v as UnityEngine.Object;
            if (uo != null)
            {
                Component comp = uo as Component;
                if (comp != null) return comp.GetType().Name + " " + PathOf(comp.transform, root);
                GameObject go = uo as GameObject;
                if (go != null) return "GameObject " + PathOf(go.transform, root);
                return uo.GetType().Name + " '" + uo.name + "'";
            }
            Array arr = v as Array;
            if (arr != null)
            {
                StringBuilder s = new StringBuilder();
                s.Append(v.GetType().GetElementType().Name).Append("[").Append(arr.Length).Append("]");
                int n = 0;
                foreach (object e in arr)
                {
                    if (n++ >= 12) { s.Append(" …"); break; }
                    s.Append(n == 1 ? " { " : "; ").Append(Describe(e, root));
                }
                if (arr.Length > 0) s.Append(" }");
                return s.ToString();
            }
            if (v is float) return ((float)v).ToString("0.###", Inv);
            Type t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string) return Convert.ToString(v, Inv);
            if (v is System.Collections.ICollection) return t.Name + " count " + ((System.Collections.ICollection)v).Count;
            return t.Name;
        }

        private static void DumpLayers(StringBuilder sb, CircuitScene scene)
        {
            Dictionary<int, int[]> byLayer = new Dictionary<int, int[]>();
            Dictionary<int, List<string>> trackLayerNames = new Dictionary<int, List<string>>();
            int envTrack = LayerMask.NameToLayer("Env_Track");
            foreach (GameObject root in scene.gameObject.scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    int layer = r.gameObject.layer;
                    int[] counts;
                    if (!byLayer.TryGetValue(layer, out counts))
                    {
                        counts = new int[3];
                        byLayer[layer] = counts;
                    }
                    counts[0]++;
                    if (r.isPartOfStaticBatch) counts[1]++;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) counts[2] += mf.sharedMesh.vertexCount;
                    if (layer == envTrack)
                    {
                        List<string> names;
                        if (!trackLayerNames.TryGetValue(layer, out names))
                        {
                            names = new List<string>();
                            trackLayerNames[layer] = names;
                        }
                        if (names.Count < 60)
                        {
                            names.Add(PathOf(r.transform, null) + " [" + (r.sharedMaterial != null ? r.sharedMaterial.name : "-") + "] bounds " + V(r.bounds.size));
                        }
                    }
                }
            }
            foreach (KeyValuePair<int, int[]> kv in byLayer)
            {
                sb.AppendFormat(Inv, "  layer {0,-2} {1,-22} renderers={2}  staticBatched={3}  verts~{4}\n",
                    kv.Key, LayerMask.LayerToName(kv.Key), kv.Value[0], kv.Value[1], kv.Value[2]);
            }
            List<string> road;
            if (trackLayerNames.TryGetValue(envTrack, out road))
            {
                sb.AppendLine("  Env_Track renderers (the drivable surface / path collision meshes):");
                foreach (string n in road) sb.AppendLine("    " + n);
            }
        }

        private static void DumpRootSummary(StringBuilder sb, GameObject root)
        {
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            int objects = root.GetComponentsInChildren<Transform>(true).Length;
            string bounds = "-";
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                bounds = "min (" + V(b.min) + ") max (" + V(b.max) + ")";
            }
            sb.AppendFormat(Inv, "  {0}  active={1}  objects={2}  renderers={3}  bounds {4}\n", root.name, root.activeSelf, objects, rs.Length, bounds);
        }

        private static void DumpTree(StringBuilder sb, Transform t, int depth)
        {
            sb.Append(' ', depth * 2);
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            sb.AppendFormat(Inv, "{0}{1}  [{2}]  children={3} renderers(sub)={4}\n",
                t.name, t.gameObject.activeSelf ? "" : " (inactive)", Components(t.gameObject, false), t.childCount, rs.Length);
            if (depth >= MaxTreeDepth || t.childCount == 0)
            {
                return;
            }
            if (t.childCount > MaxChildrenListed)
            {
                // Summarise long lists (trees, crowd, barrier segments) by name stem.
                Dictionary<string, int> stems = new Dictionary<string, int>();
                for (int i = 0; i < t.childCount; i++)
                {
                    string stem = Stem(t.GetChild(i).name);
                    int n;
                    stems.TryGetValue(stem, out n);
                    stems[stem] = n + 1;
                }
                foreach (KeyValuePair<string, int> kv in stems)
                {
                    sb.Append(' ', (depth + 1) * 2).AppendFormat(Inv, "{0} ×{1}\n", kv.Key, kv.Value);
                }
                return;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                DumpTree(sb, t.GetChild(i), depth + 1);
            }
        }

        private static string Stem(string name)
        {
            int cut = name.Length;
            while (cut > 0 && (char.IsDigit(name[cut - 1]) || name[cut - 1] == ' ' || name[cut - 1] == '_' || name[cut - 1] == '(' || name[cut - 1] == ')' || name[cut - 1] == '.'))
            {
                cut--;
            }
            return cut > 0 ? name.Substring(0, cut) : name;
        }

        private static string Components(GameObject go, bool scriptsOnly)
        {
            StringBuilder s = new StringBuilder();
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) { s.Append("missing,"); continue; }
                if (c is Transform) continue;
                if (scriptsOnly && !(c is MonoBehaviour)) continue;
                s.Append(c.GetType().Name).Append(',');
            }
            if (s.Length > 0) s.Length--;
            return s.ToString();
        }

        private static string PathFlags(PathData d)
        {
            List<string> f = new List<string>();
            if (d.isCrashLane) f.Add("crash" + (d.crashLaneForAnyLayout ? "(any)" : "(" + d.crashSplineForLayout + ")"));
            if (d.isRunWideLane) f.Add("runWide");
            if (d.isCutCornerLane) f.Add("cut");
            return f.Count == 0 ? "-" : string.Join("|", f.ToArray());
        }

        private static int SafeCount(PathData d, PathData.SplineMode mode)
        {
            try
            {
                PathData.SplineData[] sd = sSplineData(d);
                PathData.SplineData one = sd[(int)mode];
                return one != null && one.points != null ? one.points.Count : 0;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string PathOf(Transform t, Transform stopAt)
        {
            string p = t.name;
            Transform cur = t.parent;
            while (cur != null && cur != stopAt)
            {
                p = cur.name + "/" + p;
                cur = cur.parent;
            }
            return p;
        }

        private static string V(Vector3 v)
        {
            return string.Format(Inv, "{0:0.0}, {1:0.0}, {2:0.0}", v.x, v.y, v.z);
        }

        private static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s.Replace(' ', '_');
        }
    }
}
