using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace RouteNavigation
{
    /// <summary>
    /// Bakes a walkable NavMesh over ONLY a small subset of the map: the box that contains the
    /// Start and the Goal, plus a margin on every side. Baking the whole FCG city hangs, so we
    /// use CollectObjects.Volume to bake just a couple of streets, which finishes in a moment.
    ///
    /// SETUP:
    ///   1. Put this on its own empty GameObject at the world origin (a NavMeshSurface is added
    ///      automatically). Leave its Transform at position 0, rotation 0, scale 1.
    ///   2. Assign Start Point and Goal Point (the same objects the path runs between).
    ///   3. Press Play - it bakes the subset once on Awake. Or use the right-click
    ///      "Bake Subset Now" menu on the component to preview the bake in the editor.
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    public class NavMeshSubsetBaker : MonoBehaviour
    {
        [Header("The two points the route runs between")]
        public Transform startPoint;
        public Transform goalPoint;

        [Header("Subset region")]
        [Tooltip("Extra walkable metres kept around the Start-Goal box on every side. Bigger = more streets, slower bake.")]
        public float margin = 20f;
        [Tooltip("Vertical span of the bake box in metres. Must reach above the buildings so they carve the navmesh.")]
        public float verticalExtent = 80f;

        [Header("Geometry")]
        [Tooltip("Physics Colliders avoids the 'read access' warnings (use for scenes with colliders, e.g. Vol.7). " +
                 "Automatically falls back to Render Meshes if colliders bake nothing (e.g. FCG).")]
        public NavMeshCollectGeometry geometry = NavMeshCollectGeometry.PhysicsColliders;

        [Header("Result (read-only)")]
        public bool baked;
        public Vector3 lastRegionSize;

        private NavMeshSurface _surface;

        private void Awake() => EnsureBaked();

        /// <summary>Bakes the subset NavMesh once. Safe to call repeatedly; only the first call bakes. Returns true if a walkable mesh exists.</summary>
        public bool EnsureBaked()
        {
            if (baked) return true;
            if (startPoint == null || goalPoint == null)
            {
                Debug.LogError("[SubsetBake] Assign Start Point and Goal Point before baking.");
                return false;
            }
            if (_surface == null) _surface = GetComponent<NavMeshSurface>();

            if (transform.rotation != Quaternion.identity || transform.lossyScale != Vector3.one)
                Debug.LogWarning("[SubsetBake] For an accurate bake box, keep this object's rotation at 0 and scale at 1.");

            // Box that contains both points plus a margin on every side.
            Vector3 a = startPoint.position, b = goalPoint.position;
            Vector3 worldCenter = (a + b) * 0.5f;
            float sizeX = Mathf.Abs(a.x - b.x) + margin * 2f;
            float sizeZ = Mathf.Abs(a.z - b.z) + margin * 2f;
            Vector3 size = new Vector3(sizeX, Mathf.Max(1f, verticalExtent), sizeZ);

            _surface.collectObjects = CollectObjects.Volume;               // only geometry inside the box -> fast subset bake
            _surface.center = transform.InverseTransformPoint(worldCenter); // volume centre in this object's local space
            _surface.size = size;
            _surface.agentTypeID = NavMesh.GetSettingsByIndex(0).agentTypeID; // built-in Humanoid
            lastRegionSize = size;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int verts = BakeWith(geometry);
            // If colliders produced nothing (scene has none, e.g. FCG), fall back to render meshes.
            if (verts == 0 && geometry == NavMeshCollectGeometry.PhysicsColliders)
                verts = BakeWith(NavMeshCollectGeometry.RenderMeshes);
            sw.Stop();

            baked = verts > 0;
            if (!baked)
                Debug.LogError($"[SubsetBake] Baked an EMPTY navmesh over {sizeX:F0}x{sizeZ:F0} m. " +
                               "No walkable ground was found in the box - move Start/Goal onto walkable floor or increase margin.");
            else
                Debug.Log($"[SubsetBake] Baked subset {sizeX:F0}x{sizeZ:F0} m around Start-Goal in {sw.ElapsedMilliseconds} ms. " +
                          $"Walkable vertices: {verts}.");
            return baked;
        }

        private int BakeWith(NavMeshCollectGeometry geo)
        {
            _surface.useGeometry = geo;
            _surface.BuildNavMesh();
            var tri = NavMesh.CalculateTriangulation();
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
