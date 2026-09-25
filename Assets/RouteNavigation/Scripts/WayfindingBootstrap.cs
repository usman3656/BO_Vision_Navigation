using System.Collections.Generic;
using BOforUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RouteNavigation
{
    /// <summary>
    /// Rebuilds the ENTIRE wayfinding study in code, automatically, the moment a study scene starts.
    /// Nothing needs to be saved in the scene: this runs on Play (RuntimeInitializeOnLoadMethod) and,
    /// for each known scene, it
    ///   1. spawns the BO manager from Resources if the scene doesn't already contain one,
    ///   2. creates the Start / Goal / waypoint markers at HARDCODED coordinates,
    ///   3. builds GuidancePath (WayfindingPathController) + TrialRunner and wires them together.
    /// The runner's own Awake then configures the optimizer (6 params, 2 objectives, MetaTAF) and finds
    /// the player. So the professor only presses Play, then the on-screen START button.
    ///
    /// The coordinates below ARE the study setup - edit them here to move Start/Goal/waypoints; the
    /// change is permanent and travels with the project (no scene save needed).
    /// </summary>
    public static class WayfindingBootstrap
    {
        private struct SceneRoute
        {
            public string condition;
            public Vector3 start;
            public Vector3 goal;
            public Vector3[] waypoints;
            public bool goalKnown;   // false => Goal coordinate not yet supplied; fall back to a saved PathGoal
        }

        // Active-scene name -> hardcoded route.
        // ALL THREE study scenes (Vol.7, FCG, Vol.6) now keep their route (Start/Goal/waypoints +
        // GuidancePath + TrialRunner) saved IN the scene, and the runner self-heals the BO manager. So the
        // scene is the single source of truth and NONE are code-enforced here: whatever you place and save
        // in the scene is exactly what runs. Add an entry ONLY for a scene that has no saved route at all
        // and must be built purely from code.
        private static readonly Dictionary<string, SceneRoute> Routes = new Dictionary<string, SceneRoute>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!Routes.TryGetValue(scene, out SceneRoute route)) return;      // Vol.7/Vol.6 rely on their saved scene

            EnsureManager();

            // ENFORCE the hardcoded route for this scene even if the scene already has a saved route: existing
            // markers are MOVED to the hardcoded spots and any saved (long) waypoint chain is replaced. This is
            // why FCG's shortened goal always wins over the older, longer route saved in the scene.
            Transform start = EnsureMarker("PathStart", route.start, Color.green);

            Transform goal;
            if (route.goalKnown)
            {
                goal = EnsureMarker("PathGoal", route.goal, Color.red);
            }
            else
            {
                var existing = GameObject.Find("PathGoal");
                if (existing == null)
                {
                    Debug.LogError($"[Bootstrap] Scene '{scene}': no Goal coordinate hardcoded and no PathGoal in the scene.");
                    return;
                }
                goal = existing.transform;
            }

            // Drop any saved intermediate waypoints so only the hardcoded route remains.
            var oldHolder = GameObject.Find("PathWaypoints");
            if (oldHolder != null) Object.Destroy(oldHolder);

            var routePoints = new List<Transform> { start };
            if (route.waypoints != null && route.waypoints.Length > 0)
            {
                var holder = new GameObject("PathWaypoints").transform;
                for (int i = 0; i < route.waypoints.Length; i++)
                {
                    Transform w = EnsureMarker("PathWaypoint " + (i + 1), route.waypoints[i], Color.cyan);
                    w.SetParent(holder, true);
                    routePoints.Add(w);
                }
            }
            routePoints.Add(goal);

            // Reuse the scene's GuidancePath if it has one; otherwise create it. Point it at the hardcoded route.
            var ctrl = Object.FindAnyObjectByType<WayfindingPathController>();
            if (ctrl == null) ctrl = new GameObject("GuidancePath").AddComponent<WayfindingPathController>();
            ctrl.waypoints = routePoints.ToArray();

            // Only build a runner if the scene doesn't already have one (the saved runner self-wires to
            // start/goal/path and derives its condition label from the scene name).
            if (Object.FindAnyObjectByType<WayfindingTrialRunner>() == null)
            {
                var runnerGo = new GameObject("TrialRunner");
                runnerGo.SetActive(false);
                var runner = runnerGo.AddComponent<WayfindingTrialRunner>();
                runner.path = ctrl;
                runner.startPoint = start;
                runner.goalPoint = goal;
                runner.conditionId = route.condition;
                runnerGo.SetActive(true);
            }

            Debug.Log($"[Bootstrap] Enforced route for '{scene}' ({route.condition}): {ctrl.waypoints.Length} point(s), " +
                      $"goal at {goal.position}.");
        }

        /// <summary>Ensures a single Bo manager exists; instantiates the Resources prefab if the scene has none.</summary>
        private static void EnsureManager()
        {
            if (Object.FindAnyObjectByType<BoForUnityManager>() != null) return;
            var prefab = Resources.Load<GameObject>("BOforUnityManager");
            if (prefab == null)
            {
                Debug.LogError("[Bootstrap] BOforUnityManager prefab not found under a Resources folder.");
                return;
            }
            var go = Object.Instantiate(prefab);
            go.name = "BOforUnityManager";
        }

        /// <summary>Finds a marker by name (moving it to the hardcoded spot) or creates a small unlit sphere.</summary>
        private static Transform EnsureMarker(string markerName, Vector3 pos, Color color)
        {
            var go = GameObject.Find(markerName);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = markerName;
                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                go.transform.localScale = Vector3.one * 0.3f;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = MakeUnlit(color);
            }
            go.transform.position = pos;
            return go.transform;
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
    }
}
