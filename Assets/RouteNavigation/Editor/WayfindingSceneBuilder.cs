using System.Collections.Generic;
using RouteNavigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RouteNavigation.EditorTools
{
    /// <summary>
    /// One-stop setup for the waypoint-based wayfinding test (Tools > BO Route > ...):
    ///   Set Path Start / Goal Here     - drop a glowing beacon at the Scene-view focus (snapped to floor).
    ///   Add Path Waypoint Here          - drop a cyan waypoint along the route (snapped to floor).
    ///   Snap PathStart/Goal/ALL         - re-drop points onto their local floor.
    ///   Clear Path Waypoints            - remove the intermediate waypoints.
    ///   Build Wayfinding Test Objects   - (re)creates GuidancePath (WayfindingPathController) + TrialRunner,
    ///                                     wires the route Start -> waypoints -> Goal, re-enables the camera,
    ///                                     and uses the scene's own FirstPersonAIO player.
    /// </summary>
    public static class WayfindingSceneBuilder
    {
        // --- Markers -----------------------------------------------------------

        [MenuItem("Tools/BO Route/Set Path Start Here (Scene view)")]
        public static void SetStartHere() => PlaceMarker("PathStart", Color.green);

        [MenuItem("Tools/BO Route/Set Path Goal Here (Scene view)")]
        public static void SetGoalHere() => PlaceMarker("PathGoal", Color.red);

        private static void PlaceMarker(string markerName, Color color)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
            {
                Debug.LogError("[Build] Open a Scene view, frame the floor spot (hover it and press F), then run this.");
                return;
            }
            var go = GameObject.Find(markerName);
            if (go == null)
            {
                go = new GameObject(markerName);
                Undo.RegisterCreatedObjectUndo(go, "Create " + markerName);
                BuildBeacon(go, color);
            }
            Undo.RecordObject(go.transform, "Move " + markerName);
            go.transform.position = sv.pivot;
            SnapToGround(go.transform);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] {markerName} placed at {go.transform.position}.");
        }

        private static void BuildBeacon(GameObject root, Color color)
        {
            // Just a small glowing sphere marker - no tall pillar.
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Marker";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localPosition = Vector3.up * 0.3f;
            sphere.transform.localScale = Vector3.one * 0.5f;
            StripCollider(sphere);
            SetMaterial(sphere, MakeUnlit(color));
        }

        // --- Waypoints ---------------------------------------------------------

        [MenuItem("Tools/BO Route/Add Path Waypoint Here (Scene view)")]
        public static void AddWaypointHere()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) { Debug.LogError("[Build] Open a Scene view, frame the floor spot (press F), then run this."); return; }

            var holder = FindOrCreate("PathWaypoints");
            int n = holder.transform.childCount + 1;
            var wp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wp.name = "PathWaypoint " + n;
            Undo.RegisterCreatedObjectUndo(wp, "Add Waypoint");
            StripCollider(wp);
            wp.transform.SetParent(holder.transform, true);
            wp.transform.localScale = Vector3.one * 0.3f;
            SetMaterial(wp, MakeUnlit(Color.cyan));
            wp.transform.position = sv.pivot;
            SnapToGround(wp.transform);
            Selection.activeGameObject = wp;

            // Works whether or not GuidancePath exists yet: if it does, update the route now;
            // otherwise the waypoint is stored under PathWaypoints and picked up when you run Build.
            var ctrl = FindController();
            if (ctrl != null)
            {
                RebuildWaypointArray(ctrl);
                Debug.Log($"[Build] Added {wp.name}. Route now has {ctrl.waypoints.Length} points. Move it with W to fine-tune.");
            }
            else
            {
                Debug.Log($"[Build] Added {wp.name} (#{n}). Set PathStart/PathGoal and run 'Build Wayfinding Test Objects' " +
                          "to assemble the route — your waypoints are included automatically.");
            }
            EditorSceneManager.MarkAllScenesDirty();
        }

        [MenuItem("Tools/BO Route/Clear Path Waypoints")]
        public static void ClearWaypoints()
        {
            var holder = GameObject.Find("PathWaypoints");
            if (holder != null) Undo.DestroyObjectImmediate(holder);
            var ctrl = FindController();
            if (ctrl != null) RebuildWaypointArray(ctrl);
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[Build] Cleared intermediate waypoints. Route is Start -> Goal.");
        }

        /// <summary>Sets the controller's route to Start + PathWaypoints children (in order) + Goal.</summary>
        private static void RebuildWaypointArray(WayfindingPathController ctrl)
        {
            var start = GameObject.Find("PathStart");
            var goal = GameObject.Find("PathGoal");
            if (ctrl == null || start == null || goal == null) return;

            var list = new List<Transform> { start.transform };
            var holder = GameObject.Find("PathWaypoints");
            if (holder != null)
                foreach (Transform c in holder.transform) list.Add(c);
            list.Add(goal.transform);

            Undo.RecordObject(ctrl, "Rebuild route");
            ctrl.waypoints = list.ToArray();
        }

        // --- Ground snapping ---------------------------------------------------

        [MenuItem("Tools/BO Route/Snap PathStart to Ground")]
        public static void SnapStart() => SnapNamed("PathStart");

        [MenuItem("Tools/BO Route/Snap PathGoal to Ground")]
        public static void SnapGoal() => SnapNamed("PathGoal");

        [MenuItem("Tools/BO Route/Snap ALL Path Points to Ground")]
        public static void SnapAllToGround()
        {
            int count = 0;
            foreach (var nm in new[] { "PathStart", "PathGoal" })
            {
                var g = GameObject.Find(nm);
                if (g != null) { Undo.RecordObject(g.transform, "Snap " + nm); SnapToGround(g.transform); count++; }
            }
            var holder = GameObject.Find("PathWaypoints");
            if (holder != null)
                foreach (Transform c in holder.transform) { Undo.RecordObject(c, "Snap waypoint"); SnapToGround(c); count++; }
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] Snapped {count} path point(s) to their nearest floor.");
        }

        private static void SnapNamed(string markerName)
        {
            var go = GameObject.Find(markerName);
            if (go == null) { Debug.LogError($"[Build] No {markerName} in the scene yet."); return; }
            Undo.RecordObject(go.transform, "Snap " + markerName);
            SnapToGround(go.transform);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkAllScenesDirty();
        }

        /// <summary>Drops a point onto the floor directly below it (never an upper storey).</summary>
        private static void SnapToGround(Transform t)
        {
            Vector3 p = t.position;
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 10f))
            {
                p.y = hit.point.y;
                t.position = p;
                Debug.Log($"[Build] {t.name} snapped to floor at y={p.y:F2}.");
                return;
            }
            float bestTop = float.NegativeInfinity;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Bounds b = mr.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                if (b.max.y > p.y + 0.5f) continue;   // above the placement
                if (b.max.y < p.y - 4f) continue;     // far below (other storey)
                if (b.max.y > bestTop) bestTop = b.max.y;
            }
            if (!float.IsNegativeInfinity(bestTop)) { p.y = bestTop; t.position = p; Debug.Log($"[Build] {t.name} snapped to mesh surface at y={p.y:F2}."); }
            else Debug.LogWarning($"[Build] {t.name}: no floor found just below it. Frame the floor (press F on it) and snap again.");
        }

        // --- Build -------------------------------------------------------------

        [MenuItem("Tools/BO Route/Hide BO Manager UI (removes the white/pink canvas)")]
        public static void HideBoUi()
        {
            var go = GameObject.Find("BOControlCanvas");
            if (go == null)
            {
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay) { go = c.gameObject; break; }
            }
            if (go == null) { Debug.LogWarning("[Build] No BO manager UI canvas found in the scene."); return; }
            Undo.RecordObject(go, "Hide BO UI");
            go.SetActive(false);
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Build] Deactivated '{go.name}'. The BO manager's white/pink UI is now gone from the Scene view and the game. " +
                      "The study uses its own on-screen UI (status/START/rating). Re-enable it in the Hierarchy if you ever need it.");
        }

        [MenuItem("Tools/BO Route/Build Wayfinding Test Objects")]
        public static void BuildObjects()
        {
            var start = GameObject.Find("PathStart");
            var goal = GameObject.Find("PathGoal");
            if (start == null || goal == null)
            {
                Debug.LogError("[Build] Set PathStart and PathGoal first (Set Path Start Here / Set Path Goal Here).");
                return;
            }

            // Strip the old tall "Beacon" pillar from Start/Goal if present (markers are just spheres now).
            foreach (var m in new[] { start, goal })
            {
                var beacon = m.transform.Find("Beacon");
                if (beacon != null) Undo.DestroyObjectImmediate(beacon.gameObject);
            }

            // Remove any rival walker from an old build and re-enable cameras it disabled.
            var oldPlayer = GameObject.Find("Player");
            if (oldPlayer != null && oldPlayer.GetComponent<DesktopWalkController>() != null)
                Undo.DestroyObjectImmediate(oldPlayer);
            int reEnabled = 0;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!cam.gameObject.activeSelf) { Undo.RecordObject(cam.gameObject, "Enable camera"); cam.gameObject.SetActive(true); reEnabled++; }

            // Fresh GuidancePath (destroy old so no leftover/missing components remain).
            var oldPath = GameObject.Find("GuidancePath");
            if (oldPath != null) Undo.DestroyObjectImmediate(oldPath);
            var pathGo = new GameObject("GuidancePath");
            Undo.RegisterCreatedObjectUndo(pathGo, "Create GuidancePath");
            var ctrl = pathGo.AddComponent<WayfindingPathController>();
            RebuildWaypointArray(ctrl);

            // TrialRunner (the BO bridge).
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
            Debug.Log($"[Build] Done. Fresh GuidancePath + TrialRunner wired. Re-enabled {reEnabled} camera(s). {playerNote}. " +
                      $"Route has {ctrl.waypoints.Length} point(s). Use 'Add Path Waypoint Here' to shape it. Press Play.");
        }

        // --- Helpers -----------------------------------------------------------

        private static WayfindingPathController FindController()
        {
            var pathGo = GameObject.Find("GuidancePath");
            return pathGo != null ? pathGo.GetComponent<WayfindingPathController>() : null;
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
