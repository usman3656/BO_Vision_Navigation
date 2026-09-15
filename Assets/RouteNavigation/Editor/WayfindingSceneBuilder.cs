using RouteNavigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RouteNavigation.EditorTools
{
    /// <summary>
    /// Beginner-friendly setup for the wayfinding test.
    ///
    /// Menus (Tools > BO Route > ...):
    ///   Set Path Start Here (Scene view)  - drops a bright GREEN glowing beacon at the Scene view focus.
    ///   Set Path Goal Here (Scene view)   - drops a bright RED glowing beacon at the Scene view focus.
    ///   Snap PathStart/PathGoal to Ground - drops the marker onto the floor beneath it.
    ///   Build Wayfinding Test Objects      - wires GuidancePath (path + NavMesh subset baker + Dijkstra) and
    ///                                        TrialRunner, uses the scene's own first-person player, and makes
    ///                                        sure the scene camera is enabled.
    /// </summary>
    public static class WayfindingSceneBuilder
    {
        [MenuItem("Tools/BO Route/Set Path Start Here (Scene view)")]
        public static void SetStartHere() => PlaceMarker("PathStart", Color.green);

        [MenuItem("Tools/BO Route/Set Path Goal Here (Scene view)")]
        public static void SetGoalHere() => PlaceMarker("PathGoal", Color.red);

        private static void PlaceMarker(string markerName, Color color)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
            {
                Debug.LogError("[Build] Open a Scene view first, frame the spot (hover it and press F), then run this menu.");
                return;
            }

            Vector3 pos = sv.pivot;
            var go = GameObject.Find(markerName);
            if (go == null)
            {
                go = new GameObject(markerName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + markerName);
                BuildBeacon(go, color);
            }

            Undo.RecordObject(go.transform, "Move " + markerName);
            go.transform.position = pos;
            SnapToGround(go.transform);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] {markerName} placed at {go.transform.position} as a glowing beacon. " +
                      "Fine-tune with the Move tool (W), then 'Snap " + markerName + " to Ground' again.");
        }

        /// <summary>Builds a bright, unlit sphere + tall thin pillar so the marker is visible from anywhere.</summary>
        private static void BuildBeacon(GameObject root, Color color)
        {
            Material mat = MakeUnlit(color);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Marker";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localPosition = Vector3.up * 0.3f;
            sphere.transform.localScale = Vector3.one * 0.6f;
            StripCollider(sphere);
            SetMaterial(sphere, mat);

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beacon";
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = Vector3.up * 2f;          // base near the floor, rising up
            beam.transform.localScale = new Vector3(0.12f, 2f, 0.12f); // ~4 m tall, thin
            StripCollider(beam);
            SetMaterial(beam, mat);
        }

        private static Material MakeUnlit(Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        private static void SetMaterial(GameObject go, Material mat)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        [MenuItem("Tools/BO Route/Snap PathStart to Ground")]
        public static void SnapStart() => SnapNamed("PathStart");

        [MenuItem("Tools/BO Route/Snap PathGoal to Ground")]
        public static void SnapGoal() => SnapNamed("PathGoal");

        private static void SnapNamed(string markerName)
        {
            var go = GameObject.Find(markerName);
            if (go == null) { Debug.LogError($"[Build] No {markerName} in the scene yet. Set it first."); return; }
            Undo.RecordObject(go.transform, "Snap " + markerName);
            SnapToGround(go.transform);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkAllScenesDirty();
        }

        /// <summary>Drops the marker onto the floor. Physics raycast first, then a collider-free mesh-bounds fallback.</summary>
        private static void SnapToGround(Transform t)
        {
            Vector3 p = t.position;

            if (Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 1000f))
            {
                p.y = hit.point.y;
                t.position = p;
                Debug.Log($"[Build] {t.name} snapped to the floor collider at y={p.y:F2}.");
                return;
            }

            float bestTop = float.NegativeInfinity;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Bounds b = mr.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                if (b.max.y > p.y + 0.5f) continue;
                if (b.max.y > bestTop) bestTop = b.max.y;
            }

            if (!float.IsNegativeInfinity(bestTop))
            {
                p.y = bestTop;
                t.position = p;
                Debug.Log($"[Build] {t.name} snapped to the nearest mesh surface at y={p.y:F2} (no collider; used mesh bounds).");
            }
            else
            {
                Debug.LogWarning($"[Build] {t.name}: no ground found beneath it. Move it over the floor in X/Z first, then snap again.");
            }
        }

        [MenuItem("Tools/BO Route/Build Wayfinding Test Objects")]
        public static void BuildObjects()
        {
            var start = GameObject.Find("PathStart");
            var goal = GameObject.Find("PathGoal");
            if (start == null || goal == null)
            {
                Debug.LogError("[Build] Set PathStart and PathGoal first " +
                               "(Tools > BO Route > Set Path Start Here / Set Path Goal Here).");
                return;
            }

            // Remove any rival player from an earlier build and re-enable cameras it may have disabled.
            var oldPlayer = GameObject.Find("Player");
            if (oldPlayer != null && oldPlayer.GetComponent<DesktopWalkController>() != null)
                Undo.DestroyObjectImmediate(oldPlayer);

            int reEnabled = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!cam.gameObject.activeSelf)
                {
                    Undo.RecordObject(cam.gameObject, "Enable camera");
                    cam.gameObject.SetActive(true);
                    reEnabled++;
                }
            }

            // GuidancePath: draws the route; carries both routing backends.
            var pathGo = FindOrCreate("GuidancePath");
            var ctrl = GetOrAdd<WayfindingPathController>(pathGo);
            var baker = GetOrAdd<NavMeshSubsetBaker>(pathGo);
            var dijkstra = GetOrAdd<DijkstraGridPathfinder>(pathGo);
            Undo.RecordObject(ctrl, "Wire path");
            ctrl.waypoints = new Transform[] { start.transform, goal.transform };
            ctrl.routing = WayfindingPathController.RoutingMode.NavMesh;
            ctrl.navMeshBaker = baker;
            ctrl.pathfinder = dijkstra;
            Undo.RecordObject(baker, "Wire baker");
            baker.startPoint = start.transform;
            baker.goalPoint = goal.transform;

            // TrialRunner: the BO bridge. It auto-finds the scene's first-person player.
            var runnerGo = FindOrCreate("TrialRunner");
            var runner = GetOrAdd<WayfindingTrialRunner>(runnerGo);
            Undo.RecordObject(runner, "Wire runner");
            runner.path = ctrl;
            runner.startPoint = start.transform;
            runner.goalPoint = goal.transform;

            string playerNote = "no FirstPersonAIO found - runner will look again at play time";
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mb.GetType().Name == "FirstPersonAIO")
                {
                    runner.playerRoot = mb.transform;
                    runner.playerController = mb;
                    playerNote = "using the scene's FirstPersonAIO player: " + mb.name;
                    break;
                }
            }

            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] Done. Wired GuidancePath (+NavMesh subset baker +Dijkstra) and TrialRunner. " +
                      $"Re-enabled {reEnabled} camera(s). {playerNote}. Routing = NavMesh. Press Play.");
        }

        private static GameObject FindOrCreate(string goName)
        {
            var go = GameObject.Find(goName);
            if (go == null)
            {
                go = new GameObject(goName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + goName);
            }
            return go;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c == null) c = Undo.AddComponent<T>(go);
            return c;
        }
    }
}
