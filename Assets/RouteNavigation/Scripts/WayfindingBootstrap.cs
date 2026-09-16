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

        // Active-scene name -> hardcoded route. Add the third (Vol.6 transfer) scene here when needed.
        private static readonly Dictionary<string, SceneRoute> Routes = new Dictionary<string, SceneRoute>
        {
            // Fantastic City Generator (outdoor). Start + Goal recovered from the saved scene markers.
            ["Scene-Demo"] = new SceneRoute
            {
                condition = "FCG",
                start = new Vector3(-345.41f, -0.23f, 42.84f),
                goal  = new Vector3(-399.05f, 0.151f, 99.72f),
                waypoints = new Vector3[0],
                goalKnown = true,
            },

            // ArchVizPRO Vol.7 (indoor). Start recovered from the saved start pose (Sept 3).
            // GOAL: fill in the real coordinate below and set goalKnown = true. Until then the
            // bootstrap uses a PathGoal already in the scene if one exists.
            ["ArchVizPRO_Interior_Vol.7_URP"] = new SceneRoute
            {
                condition = "Vol7",
                start = new Vector3(-2.5403f, 4.8587f, 0.0220f),
                goal  = new Vector3(0f, 0f, 0f),   // TODO: real Vol.7 goal
                waypoints = new Vector3[0],
                goalKnown = false,
            },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (!Routes.TryGetValue(scene, out SceneRoute route)) return;      // not a study scene

            // If the scene was already set up manually (a runner exists), don't build a second copy.
            if (Object.FindAnyObjectByType<WayfindingTrialRunner>() != null) return;

            EnsureManager();

            Transform start = EnsureMarker("PathStart", route.start, Color.green);

            Transform goal;
            if (route.goalKnown)
            {
                goal = EnsureMarker("PathGoal", route.goal, Color.red);
            }
            else
            {
                // No hardcoded goal yet: use a PathGoal saved in the scene, if any.
                var existing = GameObject.Find("PathGoal");
                if (existing == null)
                {
                    Debug.LogError($"[Bootstrap] Scene '{scene}': no Goal coordinate hardcoded and no PathGoal in the " +
                                   "scene. Set WayfindingBootstrap's goal for this scene (goalKnown = true).");
                    return;
                }
                goal = existing.transform;
            }

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

            // GuidancePath (the arrow trail).
            var pathGo = new GameObject("GuidancePath");
            var ctrl = pathGo.AddComponent<WayfindingPathController>();
            ctrl.waypoints = routePoints.ToArray();

            // TrialRunner: create INACTIVE so we can wire references before its Awake runs, then activate.
            var runnerGo = new GameObject("TrialRunner");
            runnerGo.SetActive(false);
            var runner = runnerGo.AddComponent<WayfindingTrialRunner>();
            runner.path = ctrl;
            runner.startPoint = start;
            runner.goalPoint = goal;
            runner.conditionId = route.condition;
            runnerGo.SetActive(true);   // Awake now runs with everything wired + configures the optimizer

            Debug.Log($"[Bootstrap] Built study for '{scene}' ({route.condition}): manager + markers + path + runner. " +
                      $"Route has {ctrl.waypoints.Length} point(s). Press START.");
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
