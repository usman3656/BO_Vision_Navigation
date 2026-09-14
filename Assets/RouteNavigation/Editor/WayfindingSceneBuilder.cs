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
            Selection.activeGameObject = go;
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] {markerName} placed at {pos}. Fine-tune with the Move tool (W) if needed.");
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
