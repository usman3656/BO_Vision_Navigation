using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace RouteNavigation
{
    /// <summary>
    /// Shows a wayfinding guide as a trail of 3D ARROWS from Start to Goal, and reacts to the six
    /// Bayesian Optimization parameters (all 0..1): R, G, B, opacity, size (arrow scale + spacing),
    /// and height (how high the arrows float above the floor).
    ///
    /// The route is computed once (NavMesh shortest path over a baked subset, or Dijkstra, or straight)
    /// and cached; the six parameters only change how the arrows look, so they are cheap to set per trial.
    /// </summary>
    public class WayfindingPathController : MonoBehaviour
    {
        public enum RoutingMode { NavMesh, Dijkstra, Straight }

        [Header("Route: element 0 = Start, last = Goal")]
        public Transform[] waypoints;

        [Header("Routing")]
        public RoutingMode routing = RoutingMode.NavMesh;
        [Tooltip("NavMesh mode: bakes the subset NavMesh around Start-Goal before routing.")]
        public NavMeshSubsetBaker navMeshBaker;
        [Tooltip("NavMesh mode: how far to snap Start/Goal onto the nearest walkable navmesh point.")]
        public float navSampleRadius = 5f;
        [Tooltip("Dijkstra mode only: grid pathfinder used to route around buildings.")]
        public DijkstraGridPathfinder pathfinder;

        [Header("Parameters (0..1) - the optimizer sets these")]
        [Range(0f, 1f)] public float r = 0.2f;
        [Range(0f, 1f)] public float g = 0.6f;
        [Range(0f, 1f)] public float b = 1f;
        [Range(0f, 1f)] public float opacity = 1f;
        [Range(0f, 1f)] public float size = 0.4f;
        [Range(0f, 1f)] public float height = 0.1f;

        [Header("Real-world ranges the 0..1 params map into")]
        public float minArrowScale = 0.4f;
        public float maxArrowScale = 2.5f;
        public float minSpacing = 1.2f;   // metres between arrows at size 0
        public float maxSpacing = 3.5f;   // metres between arrows at size 1
        public float maxHeight = 2.5f;    // metres the arrows float at height 1

        private List<Vector3> _route;
        private Material _mat;
        private Mesh _arrowMesh;
        private Transform _arrowParent;
        private readonly List<GameObject> _arrows = new List<GameObject>();

        private void Awake()
        {
            EnsureInit();
            RebuildRoute();
        }

        private void EnsureInit()
        {
            if (_arrowMesh == null) _arrowMesh = BuildArrowMesh();
            if (_mat == null) _mat = CreateArrowMaterial();
            if (_arrowParent == null)
            {
                var go = new GameObject("Arrows");
                go.transform.SetParent(transform, false);
                _arrowParent = go.transform;
            }
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_arrowMesh != null) Destroy(_arrowMesh);
        }

        /// <summary>Recomputes the fixed route. Call when Start or Goal move.</summary>
        public void RebuildRoute()
        {
            EnsureInit();
            _route = ComputeRoute();
            ApplyParameters(r, g, b, opacity, size, height);
        }

        /// <summary>The optimizer calls this each trial with the six parameters.</summary>
        public void ApplyParameters(float rr, float gg, float bb, float op, float sz, float ht)
        {
            EnsureInit();
            r = Mathf.Clamp01(rr);
            g = Mathf.Clamp01(gg);
            b = Mathf.Clamp01(bb);
            opacity = Mathf.Clamp01(op);
            size = Mathf.Clamp01(sz);
            height = Mathf.Clamp01(ht);

            Color c = new Color(r, g, b, opacity);
            _mat.color = c;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);

            LayoutArrows();
        }

        // --- Route computation -------------------------------------------------

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
                    Debug.LogWarning("[Path] NavMesh route failed: nothing baked, Start/Goal off the navmesh, or only a partial path. " +
                                     "If the subset box clips the route around a building, increase the NavMeshSubsetBaker margin. Drawing a straight line for now.");
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

        private List<Vector3> NavMeshRoute(Vector3 start, Vector3 goal)
        {
            if (navMeshBaker != null && !navMeshBaker.EnsureBaked()) return null;
            if (!NavMesh.SamplePosition(start, out NavMeshHit sHit, navSampleRadius, NavMesh.AllAreas)) return null;
            if (!NavMesh.SamplePosition(goal, out NavMeshHit gHit, navSampleRadius, NavMesh.AllAreas)) return null;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(sHit.position, gHit.position, NavMesh.AllAreas, path)) return null;
            if (path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2) return null;

            var pts = new List<Vector3>(path.corners.Length);
            foreach (Vector3 c in path.corners) pts.Add(c);
            return pts;
        }

        // --- Arrow layout ------------------------------------------------------

        private void LayoutArrows()
        {
            float lift = Mathf.Lerp(0f, maxHeight, height);
            float scale = Mathf.Lerp(minArrowScale, maxArrowScale, size);
            float spacing = Mathf.Max(0.3f, Mathf.Lerp(minSpacing, maxSpacing, size));

            int used = 0;
            if (_route != null && _route.Count >= 2)
            {
                float travelled = 0f;
                float nextAt = spacing * 0.5f; // first arrow a little way in from the start
                for (int i = 1; i < _route.Count; i++)
                {
                    Vector3 a = _route[i - 1], b2 = _route[i];
                    Vector3 seg = b2 - a;
                    float segLen = seg.magnitude;
                    if (segLen < 1e-4f) continue;

                    Vector3 flatDir = new Vector3(seg.x, 0f, seg.z);
                    if (flatDir.sqrMagnitude < 1e-6f) { travelled += segLen; continue; }
                    Quaternion rot = Quaternion.LookRotation(flatDir.normalized, Vector3.up);

                    while (nextAt <= travelled + segLen)
                    {
                        float t = (nextAt - travelled) / segLen;
                        Vector3 pos = Vector3.Lerp(a, b2, t) + Vector3.up * lift;
                        GameObject arrow = GetArrow(used++);
                        arrow.transform.SetPositionAndRotation(pos, rot);
                        arrow.transform.localScale = Vector3.one * scale;
                        nextAt += spacing;
                    }
                    travelled += segLen;
                }
            }

            for (int i = used; i < _arrows.Count; i++)
                if (_arrows[i] != null && _arrows[i].activeSelf) _arrows[i].SetActive(false);
        }

        private GameObject GetArrow(int index)
        {
            while (_arrows.Count <= index) _arrows.Add(CreateArrow());
            GameObject go = _arrows[index];
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        private GameObject CreateArrow()
        {
            var go = new GameObject("Arrow");
            go.transform.SetParent(_arrowParent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _arrowMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>A flat 3D arrow lying in the local XZ plane, pointing +Z (double-sided material).</summary>
        private static Mesh BuildArrowMesh()
        {
            var mesh = new Mesh { name = "GuidanceArrow" };
            Vector3[] v =
            {
                new Vector3( 0.0f, 0f,  0.5f), // 0 tip
                new Vector3(-0.5f, 0f,  0.1f), // 1 left barb
                new Vector3( 0.5f, 0f,  0.1f), // 2 right barb
                new Vector3(-0.2f, 0f,  0.1f), // 3 left shoulder
                new Vector3( 0.2f, 0f,  0.1f), // 4 right shoulder
                new Vector3(-0.2f, 0f, -0.5f), // 5 left tail
                new Vector3( 0.2f, 0f, -0.5f), // 6 right tail
            };
            int[] tris =
            {
                0, 1, 2,   // head
                3, 4, 6,   // shaft
                3, 6, 5,
            };
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateArrowMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            mat.SetFloat("_Surface", 1f);                              // transparent
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);                                 // double-sided so arrows are visible from any angle
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }
    }
}
