using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RouteNavigation
{
    /// <summary>
    /// Shows a wayfinding guide as a trail of flat 3D arrows along a HAND-PLACED waypoint route:
    /// waypoints[0] = Start, middle elements = waypoints, last = Goal.
    ///
    /// The six Bayesian Optimization parameters (all 0..1) change ONLY the appearance:
    ///   R, G, B  -> arrow colour
    ///   opacity  -> transparency (never fully invisible)
    ///   size     -> arrow scale and spacing
    ///   height   -> small lift off the floor
    ///
    /// Arrows always sit on the floor UNDER the Start point (raycast), so they can never climb to
    /// another storey no matter where the waypoints' Y values are. No NavMesh, no baking.
    /// </summary>
    [DisallowMultipleComponent]
    public class WayfindingPathController : MonoBehaviour
    {
        [Header("Route: element 0 = Start, middle = waypoints, last = Goal")]
        public Transform[] waypoints;
        [Tooltip("Smooth the route into a curve through the waypoints instead of straight segments.")]
        public bool smoothPath = true;
        [Range(2, 20)] public int smoothingPerSegment = 8;

        [Header("Parameters (0..1) - the optimizer sets these")]
        [Range(0f, 1f)] public float r = 0.2f;
        [Range(0f, 1f)] public float g = 0.6f;
        [Range(0f, 1f)] public float b = 1f;
        [Range(0f, 1f)] public float opacity = 1f;
        [Range(0f, 1f)] public float size = 0.4f;
        [Range(0f, 1f)] public float height = 0.2f;

        [Header("Appearance ranges (kept sensible + always visible)")]
        public float minArrowScale = 0.25f;   // ~0.25 m wide
        public float maxArrowScale = 0.7f;    // ~0.7 m wide (smaller than a doorway)
        public float minSpacing = 0.8f;       // metres between arrows at size 0
        public float maxSpacing = 2.0f;       // metres between arrows at size 1
        [Range(0f, 1f)] public float minOpacity = 0.35f; // never fully transparent
        public float minHeight = 0.02f;       // basically on the floor
        public float maxHeight = 0.15f;       // just above the floor at most

        /// <summary>True when a drawable route (>= 2 points) exists.</summary>
        public bool RouteValid { get; private set; }

        private readonly List<Vector3> _route = new List<Vector3>();
        private readonly List<GameObject> _arrows = new List<GameObject>();
        private Material _mat;
        private Mesh _arrowMesh;
        private Transform _arrowParent;

        private void Awake()
        {
            EnsureInit();
            RebuildRoute();
            HideWaypointMeshes(); // in the game, show only the arrows; markers stay visible in the Scene view (gizmos)
        }

        /// <summary>Hides the Start/Goal/waypoint spheres during play. The route still shows in the Scene view via OnDrawGizmos.</summary>
        private void HideWaypointMeshes()
        {
            if (waypoints == null) return;
            foreach (Transform w in waypoints)
            {
                if (w == null) continue;
                foreach (var mr in w.GetComponentsInChildren<MeshRenderer>(true))
                    mr.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_arrowMesh != null) Destroy(_arrowMesh);
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

        /// <summary>Recomputes the route from the waypoints. Call when a waypoint moves.</summary>
        public void RebuildRoute()
        {
            EnsureInit();
            BuildRoute();
            ApplyParameters(r, g, b, opacity, size, height);
        }

        /// <summary>The optimizer calls this each trial with the six appearance parameters.</summary>
        public void ApplyParameters(float rr, float gg, float bb, float op, float sz, float ht)
        {
            EnsureInit();
            r = Mathf.Clamp01(rr);
            g = Mathf.Clamp01(gg);
            b = Mathf.Clamp01(bb);
            opacity = Mathf.Clamp01(op);
            size = Mathf.Clamp01(sz);
            height = Mathf.Clamp01(ht);

            float a = Mathf.Lerp(minOpacity, 1f, opacity);
            Color c = new Color(r, g, b, a);
            _mat.color = c;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);

            LayoutArrows();
        }

        // --- Route -------------------------------------------------------------

        private void BuildRoute()
        {
            _route.Clear();
            RouteValid = false;
            if (waypoints == null || waypoints.Length < 2) return;

            var pts = new List<Vector3>(waypoints.Length);
            foreach (Transform w in waypoints)
                if (w != null) pts.Add(w.position);
            if (pts.Count < 2) return;

            _route.AddRange(smoothPath && pts.Count >= 3 ? Smooth(pts, smoothingPerSegment) : pts);
            RouteValid = _route.Count >= 2;
        }

        private static List<Vector3> Smooth(List<Vector3> pts, int seg)
        {
            seg = Mathf.Max(2, seg);
            var outPts = new List<Vector3>((pts.Count - 1) * seg + 1);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 p0 = pts[Mathf.Max(0, i - 1)];
                Vector3 p1 = pts[i];
                Vector3 p2 = pts[i + 1];
                Vector3 p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
                for (int s = 0; s < seg; s++)
                    outPts.Add(CatmullRom(p0, p1, p2, p3, s / (float)seg));
            }
            outPts.Add(pts[pts.Count - 1]);
            return outPts;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // --- Arrows ------------------------------------------------------------

        private void LayoutArrows()
        {
            float lift = Mathf.Lerp(minHeight, maxHeight, height);
            float scale = Mathf.Lerp(minArrowScale, maxArrowScale, size);
            float spacing = Mathf.Max(0.3f, Mathf.Lerp(minSpacing, maxSpacing, size));

            int used = 0;
            if (RouteValid)
            {
                float floorY = FloorYUnder(_route[0]); // the storey the Start sits on
                float travelled = 0f;
                float nextAt = spacing * 0.5f;
                for (int i = 1; i < _route.Count; i++)
                {
                    Vector3 A = _route[i - 1], B = _route[i];
                    Vector3 seg = B - A; seg.y = 0f;             // horizontal only
                    float segLen = seg.magnitude;
                    if (segLen < 1e-4f) continue;
                    Quaternion rot = Quaternion.LookRotation(seg / segLen, Vector3.up);

                    while (nextAt <= travelled + segLen)
                    {
                        float t = (nextAt - travelled) / segLen;
                        Vector3 pos = Vector3.Lerp(A, B, t);
                        pos.y = FloorYAt(pos.x, pos.z, floorY) + lift; // sit on the Start storey
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

        /// <summary>Floor height directly under a point (raycast just above it, so it hits the local floor).</summary>
        private static float FloorYUnder(Vector3 p)
        {
            return Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 8f) ? hit.point.y : p.y;
        }

        /// <summary>Floor height at an XZ, but only accepted if it's on the same storey as floorY (never the roof).</summary>
        private static float FloorYAt(float x, float z, float floorY)
        {
            if (Physics.Raycast(new Vector3(x, floorY + 2f, z), Vector3.down, out RaycastHit hit, 5f)
                && Mathf.Abs(hit.point.y - floorY) < 2f)
                return hit.point.y;
            return floorY;
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
            go.AddComponent<MeshFilter>().sharedMesh = _arrowMesh;
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
            mesh.vertices = new[]
            {
                new Vector3( 0.0f, 0f,  0.5f), // 0 tip
                new Vector3(-0.5f, 0f,  0.1f), // 1 left barb
                new Vector3( 0.5f, 0f,  0.1f), // 2 right barb
                new Vector3(-0.2f, 0f,  0.1f), // 3 left shoulder
                new Vector3( 0.2f, 0f,  0.1f), // 4 right shoulder
                new Vector3(-0.2f, 0f, -0.5f), // 5 left tail
                new Vector3( 0.2f, 0f, -0.5f), // 6 right tail
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 6, 3, 6, 5 };
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
            mat.SetFloat("_Surface", 1f);                               // transparent
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);                                  // double-sided
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        /// <summary>Draws the waypoint order in the Scene view (green Start, red Goal, cyan middles, yellow links).</summary>
        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length < 2) return;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.color = i == 0 ? Color.green : (i == waypoints.Length - 1 ? Color.red : Color.cyan);
                Gizmos.DrawSphere(waypoints[i].position, 0.2f);
                if (i > 0 && waypoints[i - 1] != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(waypoints[i - 1].position, waypoints[i].position);
                }
            }
        }
    }
}
