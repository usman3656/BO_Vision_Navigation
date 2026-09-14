using RouteNavigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RouteNavigation.EditorTools
{
    /// <summary>
    /// Beginner-friendly setup for the wayfinding test: pick a fixed Start and Goal by framing them
    /// in the Scene view, then build and wire every object with one click.
    ///
    /// Menus (Tools > BO Route > ...):
    ///   Set Path Start Here (Scene view)  - drops/moves a green PathStart at the Scene view focus point.
    ///   Set Path Goal Here (Scene view)   - drops/moves a red PathGoal at the Scene view focus point.
    ///   Build Wayfinding Test Objects      - creates GuidancePath (path + NavMesh subset baker + Dijkstra),
    ///                                        Player (walker), TrialRunner, all wired, and disables other cameras.
    ///
    /// Both routing modes are wired: switch GuidancePath > Wayfinding Path Controller > Routing between
    /// NavMesh and Dijkstra to compare them on the same fixed Start/Goal.
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

            Vector3 pos = sv.pivot; // the point the Scene view is centred on
            var go = GameObject.Find(markerName);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = markerName;
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                go.transform.localScale = Vector3.one * 0.4f;
                var mr = go.GetComponent<MeshRenderer>();
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                if (sh != null && mr != null) mr.sharedMaterial = new Material(sh) { color = color };
                Undo.RegisterCreatedObjectUndo(go, "Create " + markerName);
            }

            Undo.RecordObject(go.transform, "Move " + markerName);
            go.transform.position = pos;
            SnapToGround(go.transform);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] {markerName} placed near the Scene view focus and snapped to the ground. " +
                      "To fine-tune: move it in X/Z with the Move tool (W), then run 'Snap " + markerName + " to Ground' again.");
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

        /// <summary>Drops the marker straight down onto the floor. Uses a physics raycast if the floor has a
        /// collider, otherwise falls back to the highest mesh surface directly beneath it (no collider needed).</summary>
        private static void SnapToGround(Transform t)
        {
            Vector3 p = t.position;

            // 1) Physics raycast down (works when the floor has a collider).
            if (Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 1000f))
            {
                p.y = hit.point.y;
                t.position = p;
                Debug.Log($"[Build] {t.name} snapped to the floor collider at y={p.y:F2}.");
                return;
            }

            // 2) No collider: pick the highest mesh whose footprint is under this X/Z and whose top is at or below us.
            float bestTop = float.NegativeInfinity;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Bounds b = mr.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue; // must be over it
                if (b.max.y > p.y + 0.5f) continue;                                              // ignore things above us
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

            // GuidancePath: draws the route; carries both routing backends.
            var pathGo = FindOrCreate("GuidancePath");
            var ctrl = GetOrAdd<WayfindingPathController>(pathGo);
            var baker = GetOrAdd<NavMeshSubsetBaker>(pathGo);       // also adds NavMeshSurface (RequireComponent)
            var dijkstra = GetOrAdd<DijkstraGridPathfinder>(pathGo);
            Undo.RecordObject(ctrl, "Wire path");
            ctrl.waypoints = new Transform[] { start.transform, goal.transform };
            ctrl.routing = WayfindingPathController.RoutingMode.NavMesh;
            ctrl.navMeshBaker = baker;
            ctrl.pathfinder = dijkstra;
            Undo.RecordObject(baker, "Wire baker");
            baker.startPoint = start.transform;
            baker.goalPoint = goal.transform;

            // Player: desktop walker (builds its own camera at play time).
            var playerGo = FindOrCreate("Player");
            var walker = GetOrAdd<DesktopWalkController>(playerGo);

            // TrialRunner: the BO bridge.
            var runnerGo = FindOrCreate("TrialRunner");
            var runner = GetOrAdd<WayfindingTrialRunner>(runnerGo);
            Undo.RecordObject(runner, "Wire runner");
            runner.path = ctrl;
            runner.walker = walker;
            runner.startPoint = start.transform;
            runner.goalPoint = goal.transform;

            // Disable other cameras so the player's runtime camera is the one that renders.
            int disabled = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cam.transform.IsChildOf(playerGo.transform)) continue;
                Undo.RecordObject(cam.gameObject, "Disable camera");
                cam.gameObject.SetActive(false);
                disabled++;
            }

            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] Done. Wired GuidancePath (+NavMesh subset baker +Dijkstra), Player, TrialRunner. " +
                      $"Disabled {disabled} other camera(s). Routing = NavMesh. " +
                      "To compare, set GuidancePath > Wayfinding Path Controller > Routing = Dijkstra. Press Play.");
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
