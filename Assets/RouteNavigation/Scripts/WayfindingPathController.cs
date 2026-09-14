using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace RouteNavigation
{
    /// <summary>
    /// Draws a guidance path from a Start to a Goal and reacts to the six Bayesian Optimization
    /// parameters (all 0..1): R, G, B, opacity, size (width), and height above the floor.
    ///
    /// The route itself is fixed. By default it follows the baked NavMesh shortest walkable path
    /// between Start and Goal (bake a subset first with NavMeshSubsetBaker so the FCG city doesn't
    /// hang). Dijkstra and a straight line are kept as fallbacks. The route is computed once and
    /// cached; the six parameters only change the appearance, so they are cheap to set every trial.
    ///
    /// SETUP:
    ///   1. Add this component to an empty GameObject (a LineRenderer is added automatically).
    ///   2. Waypoints: element 0 = Start, last = Goal.
    ///   3. Routing = NavMesh (default). Assign a NavMeshSubsetBaker so it bakes the subset first.
    ///   4. Press Play. The optimizer calls ApplyParameters(...) each trial; the sliders preview it.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class WayfindingPathController : MonoBehaviour
    {
        public enum RoutingMode { NavMesh, Dijkstra, Straight }

        [Header("Route: element 0 = Start, last = Goal")]
        public Transform[] waypoints;

        [Header("Routing")]
        public RoutingMode routing = RoutingMode.NavMesh;
        [Tooltip("NavMesh mode: optional. If set, bakes the subset NavMesh around Start-Goal before routing.")]
        public NavMeshSubsetBaker navMeshBaker;
        [Tooltip("NavMesh mode: how far (metres) to snap Start/Goal onto the nearest walkable navmesh point.")]
        public float navSampleRadius = 5f;
        [Tooltip("Dijkstra mode only: grid pathfinder used to route around buildings.")]
        public DijkstraGridPathfinder pathfinder;

        [Header("Parameters (0..1) - the optimizer sets these")]
        [Range(0f, 1f)] public float r = 0.2f;
        [Range(0f, 1f)] public float g = 0.6f;
        [Range(0f, 1f)] public float b = 1f;
        [Range(0f, 1f)] public float opacity = 1f;
        [Range(0f, 1f)] public float size = 0.3f;
        [Range(0f, 1f)] public float height = 0f;

        [Header("Real-world ranges the 0..1 params map into")]
        public float minWidth = 0.05f;
        public float maxWidth = 0.6f;
        public float maxHeight = 2.5f;
        public int sampleCount = 48;

        private LineRenderer _lr;
        private Material _mat;
        private List<Vector3> _route;   // cached route centerline (world XZ + base Y)

        private void Awake()
        {
            EnsureInit();
            RebuildRoute();
        }

        /// <summary>Lazily sets up the LineRenderer and material, so ApplyParameters is safe even if the optimizer calls it before Awake.</summary>
        private void EnsureInit()
        {
            if (_lr == null)
            {
                _lr = GetComponent<LineRenderer>();
                _lr.alignment = LineAlignment.TransformZ;   // stereo-consistent in VR
                _lr.useWorldSpace = true;
                _lr.numCornerVertices = 4;
                _lr.numCapVertices = 4;
                _lr.generateLightingData = false;
                _lr.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (_mat == null)
            {
                _mat = CreateTransparentUnlit();
                _lr.material = _mat;
            }
        }

        private void OnValidate()
        {
            if (_lr != null && _mat != null && Application.isPlaying)
                ApplyParameters(r, g, b, opacity, size, height);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        /// <summary>Recomputes the fixed route (Dijkstra shortest path around buildings, or straight). Call when Start or Goal move.</summary>
        public void RebuildRoute()
        {
            _route = ComputeRoute();
            if (_lr != null && _mat != null)
                ApplyParameters(r, g, b, opacity, size, height);
        }

        private List<Vector3> ComputeRoute()
        {
            if (waypoints == null || waypoints.Length < 2) return null;
            var raw = new List<Vector3>(waypoints.Length);
            foreach (var w in waypoints)
                if (w != null) raw.Add(w.position);
            if (raw.Count < 2) return null;

            Vector3 start = raw[0], goal = raw[raw.Count - 1];

            switch (routing)
            {
                case RoutingMode.NavMesh:
                {
                    List<Vector3> nav = NavMeshRoute(start, goal);
                    if (nav != null && nav.Count >= 2) return nav;
                    Debug.LogWarning("[Path] NavMesh route failed: nothing baked, Start/Goal off the navmesh, or the path only partly reached the Goal. " +
                                     "If the subset box is clipping the route around a building, increase the NavMeshSubsetBaker margin. Drawing a straight line for now.");
                    return raw;
                }
                case RoutingMode.Dijkstra:
                {
                    if (pathfinder != null)
                    {
                        List<Vector3> route = pathfinder.FindPath(start, goal);
                        if (route != null && route.Count >= 2) return route;
                    }
                    return raw;
                }
                default:
                    return raw;
            }
        }

        /// <summary>Shortest walkable path across the baked NavMesh, as a list of corner points, or null if none.</summary>
        private List<Vector3> NavMeshRoute(Vector3 start, Vector3 goal)
        {
            if (navMeshBaker != null && !navMeshBaker.EnsureBaked()) return null;

            if (!NavMesh.SamplePosition(start, out NavMeshHit sHit, navSampleRadius, NavMesh.AllAreas)) return null;
            if (!NavMesh.SamplePosition(goal, out NavMeshHit gHit, navSampleRadius, NavMesh.AllAreas)) return null;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(sHit.position, gHit.position, NavMesh.AllAreas, path)) return null;
            // PathPartial means it could not fully reach the Goal (often the subset box clipped a detour around a building).
            // Reject it so we don't silently draw a path that stops short; the caller falls back and warns.
            if (path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2) return null;

            var pts = new List<Vector3>(path.corners.Length);
            foreach (Vector3 c in path.corners) pts.Add(c);
            return pts;
        }

        /// <summary>The optimizer calls this each trial with the six parameters.</summary>
        public void ApplyParameters(float rr, float gg, float bb, float op, float sz, float ht)
        {
            r = Mathf.Clamp01(rr);
            g = Mathf.Clamp01(gg);
            b = Mathf.Clamp01(bb);
            opacity = Mathf.Clamp01(op);
            size = Mathf.Clamp01(sz);
            height = Mathf.Clamp01(ht);

            DrawRoute();
            _lr.widthMultiplier = Mathf.Lerp(minWidth, maxWidth, size);
            _mat.SetColor("_BaseColor", new Color(r, g, b, opacity));
        }

        private void DrawRoute()
        {
            List<Vector3> center = _route;
            if (center == null || center.Count < 2)
            {
                _lr.positionCount = 0;
                return;
            }

            var seg = new float[center.Count - 1];
            float total = 0f;
            for (int i = 1; i < center.Count; i++)
            {
                seg[i - 1] = Vector3.Distance(center[i - 1], center[i]);
                total += seg[i - 1];
            }
            if (total < 1e-4f)
            {
                _lr.positionCount = 0;
                return;
            }

            int n = Mathf.Max(2, sampleCount);
            _lr.positionCount = n;
            float lift = Mathf.Lerp(0f, maxHeight, height);
            for (int k = 0; k < n; k++)
            {
                float t = (float)k / (n - 1);
                float target = t * total;
                float acc = 0f;
                int s = 0;
                while (s < seg.Length - 1 && acc + seg[s] < target) { acc += seg[s]; s++; }
                float f = seg[s] > 1e-4f ? (target - acc) / seg[s] : 0f;
                Vector3 p = Vector3.Lerp(center[s], center[s + 1], f);
                p.y += lift * Mathf.Sin(t * Mathf.PI); // 0 at the ends, peaks in the middle
                _lr.SetPosition(k, p);
            }
        }

        private Material CreateTransparentUnlit()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var mat = new Material(sh);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }
    }
}
