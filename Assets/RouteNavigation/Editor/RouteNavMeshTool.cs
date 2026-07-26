using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace RouteNavigation.EditorTools
{
    /// <summary>
    /// One-click tool to bake the walkable area and report the result in the Console,
    /// so we never have to hunt for the blue overlay by eye.
    ///
    /// Menu: Tools > BO Route > Bake and Test NavMesh
    ///
    /// It bakes a NavMesh over the whole scene, prints whether any walkable floor was
    /// found and how big it is, and if RouteStart and RouteGoal exist, prints whether
    /// the bed connects to the kitchen.
    /// </summary>
    public static class RouteNavMeshTool
    {
        [MenuItem("Tools/BO Route/Bake and Test NavMesh")]
        public static void BakeAndTest()
        {
            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                var go = new GameObject("NavMesh");
                surface = go.AddComponent<NavMeshSurface>();
                Debug.Log("[Route] Created a NavMesh object with a NavMeshSurface.");
            }

            surface.collectObjects = CollectObjects.All;
            surface.agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID; // built-in Humanoid

            int doors = MarkDoorsIgnored();
            Debug.Log($"[Route] Treated {doors} door object(s) as openings (ignored in the bake) so routes pass through doorways instead of hitting them as walls.");

            int verts = Bake(surface, NavMeshCollectGeometry.RenderMeshes);
            string mode = "Render Meshes";
            if (verts == 0)
            {
                Debug.LogWarning("[Route] Render Meshes produced no walkable area. Retrying with Physics Colliders...");
                verts = Bake(surface, NavMeshCollectGeometry.PhysicsColliders);
                mode = "Physics Colliders";
            }

            if (verts == 0)
            {
                Debug.LogError("[Route] NavMesh is EMPTY with both geometry modes. No walkable floor was found. " +
                               "The floor may be on an excluded layer or lack usable geometry. Tell your assistant.");
                return;
            }

            var tri = NavMesh.CalculateTriangulation();
            Bounds b = ComputeBounds(tri.vertices);
            Debug.Log($"[Route] NavMesh baked OK using {mode}. Walkable vertices: {verts}. " +
                      $"Walkable region: X[{b.min.x:F1} .. {b.max.x:F1}]  Z[{b.min.z:F1} .. {b.max.z:F1}]. " +
                      "Put RouteStart and RouteGoal inside this region.");

            var start = GameObject.Find("RouteStart");
            var goal = GameObject.Find("RouteGoal");
            if (start != null && goal != null)
                TestPath(start.transform.position, goal.transform.position);
            else
                Debug.Log("[Route] No RouteStart / RouteGoal yet. Create two empty objects named exactly " +
                          "RouteStart and RouteGoal, place them on the floor at the bed and the kitchen, then run this again.");

            EditorUtility.SetDirty(surface);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        [MenuItem("Tools/BO Route/Create Start-Goal and Test Path")]
        public static void CreateMarkersAndTest()
        {
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices.Length == 0)
            {
                Debug.LogError("[Route] No NavMesh found. Run 'Bake and Test NavMesh' first.");
                return;
            }

            if (!FindConnectedPair(tri.vertices, out Vector3 a, out Vector3 b))
            {
                Debug.LogWarning("[Route] Could not find two connected walkable points. The walkable area is likely split into disconnected islands. Placing markers at the extremes anyway; you may need to move them.");
                a = tri.vertices[0];
                b = tri.vertices[tri.vertices.Length - 1];
            }

            EnsureMarker("RouteStart", Color.green).transform.position = a;
            EnsureMarker("RouteGoal", Color.red).transform.position = b;
            Debug.Log($"[Route] Placed RouteStart at ({a.x:F1}, {a.z:F1}) and RouteGoal at ({b.x:F1}, {b.z:F1}). " +
                      "These are placeholder spots to test the machinery. Drag them in the Scene view onto the real bed and kitchen when ready, then run 'Bake and Test NavMesh' again.");
            TestPath(a, b);

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        private static GameObject GetOrCreate(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            return go;
        }

        /// <summary>Finds the two most distant walkable points that have a complete path between them.</summary>
        private static bool FindConnectedPair(Vector3[] v, out Vector3 a, out Vector3 b)
        {
            a = Vector3.zero;
            b = Vector3.zero;
            float best = -1f;
            var path = new NavMeshPath();
            int step = Mathf.Max(1, v.Length / 200); // cap the work on large meshes
            for (int i = 0; i < v.Length; i += step)
            {
                if (!NavMesh.SamplePosition(v[i], out NavMeshHit hi, 2f, NavMesh.AllAreas)) continue;
                for (int j = i + step; j < v.Length; j += step)
                {
                    if (!NavMesh.SamplePosition(v[j], out NavMeshHit hj, 2f, NavMesh.AllAreas)) continue;
                    NavMesh.CalculatePath(hi.position, hj.position, NavMesh.AllAreas, path);
                    if (path.status != NavMeshPathStatus.PathComplete) continue;
                    float d = Vector3.Distance(hi.position, hj.position);
                    if (d > best) { best = d; a = hi.position; b = hj.position; }
                }
            }
            return best > 0.5f;
        }

        /// <summary>
        /// Stamps a NavMeshModifier (Ignore From Build) on every door object so the bake
        /// treats doorways as open passages instead of solid walls. Returns how many were marked.
        /// </summary>
        private static int MarkDoorsIgnored()
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int count = 0;
            foreach (var t in all)
            {
                if (!t.name.ToLowerInvariant().Contains("door")) continue;
                var mod = t.GetComponent<NavMeshModifier>();
                if (mod == null) mod = t.gameObject.AddComponent<NavMeshModifier>();
                mod.ignoreFromBuild = true;
                mod.applyToChildren = true; // also remove the door panel if it is a child
                count++;
            }
            return count;
        }

        private static int Bake(NavMeshSurface surface, NavMeshCollectGeometry geometry)
        {
            surface.useGeometry = geometry;
            surface.RemoveData();
            surface.BuildNavMesh();
            return NavMesh.CalculateTriangulation().vertices.Length;
        }

        private static void TestPath(Vector3 a, Vector3 b)
        {
            if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 3f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(b, out NavMeshHit hb, 3f, NavMesh.AllAreas))
            {
                Debug.LogWarning("[Route] RouteStart or RouteGoal is not near the walkable area. " +
                                 "Move them onto the floor inside the walkable region printed above.");
                return;
            }

            var path = new NavMeshPath();
            NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path);
            switch (path.status)
            {
                case NavMeshPathStatus.PathComplete:
                    float len = 0f;
                    for (int i = 1; i < path.corners.Length; i++)
                        len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    Debug.Log($"[Route] PATH OK. Bed to kitchen is connected: {len:F1} m over {path.corners.Length} corners. " +
                              "The route task can run here. Drawing it as a green line named 'RoutePathVisual'.");
                    DrawPath(path.corners);
                    break;
                case NavMeshPathStatus.PathPartial:
                    Debug.LogWarning("[Route] PATH BLOCKED. Bed and kitchen are not fully connected on the walkable area " +
                                     "(a wall or closed door splits them). We need to bridge the gap or move a marker.");
                    break;
                default:
                    Debug.LogError("[Route] PATH INVALID. No route exists between the two markers.");
                    break;
            }
        }

        [MenuItem("Tools/BO Route/Auto-Find Bed and Kitchen")]
        public static void AutoFindBedKitchen()
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Transform bed = FindByKeywords(all, new[] { "bed" });
            Transform kitchen = FindByKeywords(all, new[] { "kitchen", "sink", "stove", "oven", "cooker", "hob", "counter" });

            if (bed == null || kitchen == null)
            {
                Debug.LogWarning($"[Route] Auto-find could not match both. bed = {(bed ? bed.name : "NOT FOUND")}, " +
                                 $"kitchen = {(kitchen ? kitchen.name : "NOT FOUND")}. Listing furniture-like object names so we can pick the right ones:");
                string[] furniture = { "bed", "kitchen", "sofa", "couch", "chair", "table", "sink", "stove",
                                       "lamp", "plant", "desk", "cabinet", "shelf", "sofa", "bath", "toilet", "wardrobe" };
                int shown = 0;
                foreach (var t in all)
                {
                    string n = t.name.ToLowerInvariant();
                    foreach (var f in furniture)
                        if (n.Contains(f)) { Debug.Log("  candidate: " + t.name); shown++; break; }
                    if (shown >= 60) break;
                }
                if (shown == 0) Debug.Log("[Route] No furniture-like names found at all. The pieces may be inside unopened prefabs.");
                return;
            }

            PlaceMarker("RouteStart", Color.green, bed.position);
            PlaceMarker("RouteGoal", Color.red, kitchen.position);
            Debug.Log($"[Route] Auto-placed RouteStart on '{bed.name}' and RouteGoal on '{kitchen.name}'. Drawing the path...");
            ShowPath();
        }

        private static Transform FindByKeywords(Transform[] all, string[] keys)
        {
            foreach (var t in all)
            {
                string n = t.name.ToLowerInvariant();
                foreach (var k in keys)
                    if (n.Contains(k)) return t;
            }
            return null;
        }

        private static void PlaceMarker(string name, Color color, Vector3 pos)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                pos = hit.position;
            EnsureMarker(name, color).transform.position = pos;
        }

        [MenuItem("Tools/BO Route/Set RouteStart to Selected Object")]
        public static void SetStartToSelected() => SetMarkerToSelected("RouteStart", Color.green);

        [MenuItem("Tools/BO Route/Set RouteGoal to Selected Object")]
        public static void SetGoalToSelected() => SetMarkerToSelected("RouteGoal", Color.red);

        private static void SetMarkerToSelected(string name, Color color)
        {
            var sel = Selection.activeGameObject;
            if (sel == null)
            {
                Debug.LogError($"[Route] Click an object in the scene at the target spot first (for example the bed), then run this to move {name} there.");
                return;
            }
            Vector3 p = sel.transform.position;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                p = hit.position;
            else
                Debug.LogWarning($"[Route] '{sel.name}' is not within 5 m of the walkable area. Placing {name} at its position, but the path may not work until it sits on walkable floor.");

            EnsureMarker(name, color).transform.position = p;
            Debug.Log($"[Route] Moved {name} onto '{sel.name}' at ({p.x:F1}, {p.y:F1}, {p.z:F1}). When both markers are placed, run 'Show Path'.");
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        [MenuItem("Tools/BO Route/Show Path")]
        public static void ShowPath()
        {
            var start = GameObject.Find("RouteStart");
            var goal = GameObject.Find("RouteGoal");
            if (start == null || goal == null)
            {
                Debug.LogError("[Route] Need RouteStart and RouteGoal. Run 'Create Start-Goal and Test Path' first.");
                return;
            }
            TestPath(start.transform.position, goal.transform.position);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }

        /// <summary>Draws the route as a thick green line in the scene so it is visible without gizmos.</summary>
        private static void DrawPath(Vector3[] corners)
        {
            var visual = GetOrCreate("RoutePathVisual");
            var lr = visual.GetComponent<LineRenderer>();
            if (lr == null) lr = visual.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.4f;
            lr.numCornerVertices = 4;
            lr.numCapVertices = 4;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            lr.sharedMaterial = new Material(shader) { color = Color.green };

            lr.positionCount = corners.Length;
            for (int i = 0; i < corners.Length; i++)
                lr.SetPosition(i, corners[i] + Vector3.up * 0.2f);
        }

        /// <summary>Finds or creates a named marker and gives it a visible colored sphere (no collider).</summary>
        private static GameObject EnsureMarker(string name, Color color)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            if (go.GetComponent<MeshFilter>() == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.AddComponent<MeshFilter>().sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mr.sharedMaterial = new Material(shader) { color = color };
                Object.DestroyImmediate(temp);
                go.transform.localScale = Vector3.one * 0.6f;
            }
            return go;
        }

        private static Bounds ComputeBounds(Vector3[] v)
        {
            if (v.Length == 0) return new Bounds();
            var bounds = new Bounds(v[0], Vector3.zero);
            for (int i = 1; i < v.Length; i++) bounds.Encapsulate(v[i]);
            return bounds;
        }
    }
}
