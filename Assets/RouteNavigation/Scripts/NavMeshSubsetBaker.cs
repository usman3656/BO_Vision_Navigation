using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RouteNavigation
{
    /// <summary>
    /// Bakes a walkable NavMesh over ONLY a subset box around Start+Goal, using a CONFIGURABLE (small)
    /// agent radius so indoor doorways and gaps between furniture stay connected. It builds with
    /// NavMeshBuilder directly (not NavMeshSurface) so the agent radius / voxel size can be controlled -
    /// the default Humanoid radius of 0.5 m is too wide to pass through a normal ~0.8 m doorway.
    ///
    /// SETUP: put this on its own empty GameObject, assign Start Point and Goal Point.
    /// </summary>
    public class NavMeshSubsetBaker : MonoBehaviour
    {
        [Header("The two points the route runs between")]
        public Transform startPoint;
        public Transform goalPoint;

        [Header("Subset region")]
        [Tooltip("Extra walkable metres kept around the Start-Goal box on every side.")]
        public float margin = 20f;
        [Tooltip("Vertical span of the bake box in metres.")]
        public float verticalExtent = 40f;

        [Header("Agent (smaller radius fits through indoor doorways)")]
        [Min(0.05f)] public float agentRadius = 0.2f;
        [Min(0.5f)] public float agentHeight = 1.8f;
        [Tooltip("Max step height the agent can climb (metres).")]
        public float agentClimb = 0.4f;
        [Tooltip("Max walkable slope in degrees.")]
        [Range(0f, 60f)] public float agentSlope = 45f;

        [Header("Geometry")]
        [Tooltip("Physics Colliders avoids 'read access' warnings and carves walls that have colliders (Vol.7). " +
                 "Falls back to Render Meshes automatically if colliders bake nothing (e.g. FCG).")]
        public NavMeshCollectGeometry geometry = NavMeshCollectGeometry.PhysicsColliders;

        [Header("Result (read-only)")]
        public bool baked;
        public Vector3 lastRegionSize;

        private NavMeshDataInstance _instance;

        private void Awake() => EnsureBaked();
        private void OnDestroy() { if (_instance.valid) NavMesh.RemoveNavMeshData(_instance); }

        /// <summary>Bakes the subset NavMesh once. Safe to call repeatedly. Returns true if a walkable mesh exists.</summary>
        public bool EnsureBaked()
        {
            if (baked) return true;
            if (startPoint == null || goalPoint == null)
            {
                Debug.LogError("[SubsetBake] Assign Start Point and Goal Point before baking.");
                return false;
            }

            Vector3 a = startPoint.position, b = goalPoint.position;
            Vector3 center = (a + b) * 0.5f;
            float sizeX = Mathf.Abs(a.x - b.x) + margin * 2f;
            float sizeZ = Mathf.Abs(a.z - b.z) + margin * 2f;
            Vector3 size = new Vector3(sizeX, Mathf.Max(4f, verticalExtent), sizeZ);
            lastRegionSize = size;
            var bounds = new Bounds(center, size);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int verts = Build(bounds, geometry);
            if (verts == 0) // chosen geometry found nothing -> try the other kind
                verts = Build(bounds, geometry == NavMeshCollectGeometry.RenderMeshes
                    ? NavMeshCollectGeometry.PhysicsColliders
                    : NavMeshCollectGeometry.RenderMeshes);
            sw.Stop();

            baked = verts > 0;
            if (!baked)
                Debug.LogError($"[SubsetBake] Baked an EMPTY navmesh over {sizeX:F0}x{sizeZ:F0} m. " +
                               "No walkable ground found - move Start/Goal onto walkable floor or increase margin.");
            else
                Debug.Log($"[SubsetBake] Baked subset {sizeX:F0}x{sizeZ:F0} m (agent radius {agentRadius:F2} m) in " +
                          $"{sw.ElapsedMilliseconds} ms. Walkable vertices: {verts}.");
            return baked;
        }

        private int Build(Bounds bounds, NavMeshCollectGeometry geo)
        {
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0); // built-in Humanoid as the base
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = agentClimb;
            settings.agentSlope = agentSlope;
            settings.overrideVoxelSize = true;
            settings.voxelSize = Mathf.Max(0.04f, agentRadius / 3f); // fine enough to capture doorways/thin walls

            var markups = new List<NavMeshBuildMarkup>();
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, geo, 0, markups, sources);

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) return 0;

            if (_instance.valid) NavMesh.RemoveNavMeshData(_instance);
            data.name = "SubsetNavMesh";
            _instance = NavMesh.AddNavMeshData(data);

            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            return tri.vertices != null ? tri.vertices.Length : 0;
        }

        [ContextMenu("Bake Subset Now")]
        private void BakeSubsetNow()
        {
            baked = false;
            EnsureBaked();
        }
    }
}
