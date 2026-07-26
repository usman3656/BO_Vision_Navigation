using System.Collections;
using System.Collections.Generic;
using BOforUnity;
using UnityEngine;
using UnityEngine.AI;

namespace RouteNavigation
{
    /// <summary>
    /// Automatic Bayesian Optimization task for the BO_Vision_Navigation project.
    ///
    /// The optimizer proposes intermediate waypoint positions. This component turns
    /// them into a walkable route (Start, waypoints, Goal) on the baked NavMesh,
    /// scores that route, and reports the score back to the optimizer. No human
    /// questionnaire is involved.
    ///
    /// It follows the framework's proven automatic-task convention:
    ///   read design parameters in Awake, write one objective value, then call
    ///   OptimizationStart() and (in ExternalSignal mode) RequestNextIteration().
    /// See ColorGuesser.cs (parameter reads) and FittsLawTask.cs (objective submit).
    ///
    /// The score itself is VISUAL and produced by RouteVisualScorer (ViT-B-32 in the
    /// Inference Engine). This component only builds the route and submits the score.
    ///
    /// SCENE SETUP (done by MR BAWANI in the editor):
    ///   1. Bake a NavMesh on the environment.
    ///   2. Place two empty GameObjects for Start and Goal, assign them below.
    ///   3. Add a RouteVisualScorer (with the model, text embeddings, and capture
    ///      camera assigned) and reference it in visualScorer below.
    ///   4. On the BOforUnityManager: add 2 parameters per waypoint, all bounds 0..1
    ///      (order: wp0_x, wp0_z, wp1_x, wp1_z, ...), and add ONE objective
    ///      (key e.g. "RouteNaturalness", smallerIsBetter = FALSE, lower bound = worstScore).
    ///   5. Set iterationAdvanceMode = ExternalSignal for a fully automatic loop.
    ///   6. Set xBounds/zBounds below to cover the walkable area of the scene.
    /// </summary>
    public class RouteEvaluator : MonoBehaviour
    {
        [Header("Endpoints (assign empty GameObjects placed in the scene)")]
        public Transform start;
        public Transform goal;

        [Header("Route parameters")]
        [Tooltip("Intermediate waypoints. The optimizer uses 2 parameters (x, z) per waypoint.")]
        public int waypointCount = 2;

        [Tooltip("World-space X range the normalized waypoint x maps into. Cover the walkable area.")]
        public Vector2 xBounds = new Vector2(-5f, 5f);
        [Tooltip("World-space Z range the normalized waypoint z maps into. Cover the walkable area.")]
        public Vector2 zBounds = new Vector2(-5f, 5f);

        [Header("NavMesh")]
        [Tooltip("How far a raw waypoint may be snapped onto the NavMesh before it counts as off-mesh.")]
        public float snapMaxDistance = 2f;

        [Header("Visual scoring")]
        [Tooltip("Runs ViT-B-32 to score the route. If unassigned, every route gets worstScore.")]
        public RouteVisualScorer visualScorer;
        [Tooltip("Score for an impossible route (off-mesh or unreachable). Set the objective LOWER bound to this. Objective must be MAXIMIZE (smallerIsBetter = false).")]
        public float worstScore = -10f;

        [Header("Debug")]
        public bool drawRouteGizmo = true;

        private BoForUnityManager _bo;
        private List<Vector3> _lastCorners = new List<Vector3>();
        private bool _lastValid;

        private void Awake()
        {
            waypointCount = Mathf.Max(0, waypointCount);
            _bo = FindAnyObjectByType<BoForUnityManager>();
            StartCoroutine(RunIteration());
        }

        private IEnumerator RunIteration()
        {
            // Let the manager finish applying this iteration's parameters and the
            // NavMesh finish loading with the scene.
            yield return null;

            if (_bo == null)
            {
                Debug.LogError("RouteEvaluator: no BoForUnityManager in the scene. Aborting iteration.");
                yield break;
            }

            Vector3[] waypoints = ReadWaypoints();
            _lastValid = TryBuildRoute(waypoints, out _lastCorners);

            float score = worstScore;
            if (_lastValid)
            {
                if (visualScorer != null)
                {
                    float captured = worstScore;
                    yield return visualScorer.ScoreRoute(_lastCorners, v => captured = v);
                    score = captured;
                }
                else
                {
                    Debug.LogWarning("RouteEvaluator: no visualScorer assigned; submitting worstScore.");
                }
            }
            SubmitObjective(score);
        }

        // --- Parameter reading -------------------------------------------------

        /// <summary>Reads the normalized waypoint parameters and maps them to world XY(Z) points.</summary>
        private Vector3[] ReadWaypoints()
        {
            var points = new Vector3[waypointCount];
            for (int w = 0; w < waypointCount; w++)
            {
                float nx = 0.5f, nz = 0.5f;
                if (!TryGetNormalizedParameterByIndex(2 * w, out nx) ||
                    !TryGetNormalizedParameterByIndex(2 * w + 1, out nz))
                {
                    Debug.LogWarning(
                        $"RouteEvaluator: missing parameters for waypoint {w}. Using scene centre.");
                }

                float x = Mathf.Lerp(xBounds.x, xBounds.y, nx);
                float z = Mathf.Lerp(zBounds.x, zBounds.y, nz);
                points[w] = new Vector3(x, 0f, z);
            }
            return points;
        }

        /// <summary>Reads the validIndex-th valid BO parameter, clamped to 0..1. Mirrors ColorGuesser.</summary>
        private bool TryGetNormalizedParameterByIndex(int validIndex, out float normalizedValue)
        {
            normalizedValue = 0.5f;
            if (_bo == null || _bo.parameters == null || validIndex < 0)
                return false;

            int seenValid = 0;
            for (int i = 0; i < _bo.parameters.Count; i++)
            {
                var parameter = _bo.parameters[i];
                if (parameter == null || parameter.value == null || string.IsNullOrWhiteSpace(parameter.key))
                    continue;

                if (seenValid == validIndex)
                {
                    normalizedValue = Mathf.Clamp01(parameter.value.Value);
                    return true;
                }
                seenValid++;
            }
            return false;
        }

        // --- Route building ----------------------------------------------------

        /// <summary>
        /// Builds a walkable route Start, waypoints, Goal by snapping each point onto the
        /// NavMesh and pathing every consecutive pair, then stitching the corners.
        /// Returns false if any point is off-mesh or any segment is not a complete path.
        /// </summary>
        private bool TryBuildRoute(Vector3[] waypoints, out List<Vector3> corners)
        {
            corners = new List<Vector3>();

            if (start == null || goal == null)
            {
                Debug.LogError("RouteEvaluator: Start or Goal not assigned.");
                return false;
            }

            // Ordered list of raw points: start, waypoints, goal.
            var raw = new List<Vector3>(waypointCount + 2) { start.position };
            raw.AddRange(waypoints);
            raw.Add(goal.position);

            // Snap every point onto the NavMesh.
            var snapped = new List<Vector3>(raw.Count);
            foreach (var p in raw)
            {
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, snapMaxDistance, NavMesh.AllAreas))
                    snapped.Add(hit.position);
                else
                    return false; // point is not near any walkable surface
            }

            // Path each consecutive pair and stitch, dropping the duplicated join corner.
            var path = new NavMeshPath();
            for (int i = 0; i < snapped.Count - 1; i++)
            {
                if (!NavMesh.CalculatePath(snapped[i], snapped[i + 1], NavMesh.AllAreas, path))
                    return false;
                if (path.status != NavMeshPathStatus.PathComplete)
                    return false;

                int startIdx = corners.Count == 0 ? 0 : 1; // skip repeated corner between segments
                for (int c = startIdx; c < path.corners.Length; c++)
                    corners.Add(path.corners[c]);
            }

            return corners.Count >= 2;
        }

        // --- Objective submission ---------------------------------------------

        /// <summary>Writes the score to objective 0 and hands control back to the optimizer.</summary>
        private void SubmitObjective(float score)
        {
            if (_bo.objectives == null || _bo.objectives.Count == 0 ||
                _bo.objectives[0] == null || _bo.objectives[0].value == null)
            {
                Debug.LogError("RouteEvaluator: no objective defined on the BoForUnityManager.");
                return;
            }

            _bo.objectives[0].value.values.Add(score);
            _bo.OptimizationStart();

            if (_bo.iterationAdvanceMode == BoForUnityManager.IterationAdvanceMode.ExternalSignal &&
                _bo.optimizationRunning)
            {
                _bo.RequestNextIteration();
            }
        }

        // --- Editor visualisation ---------------------------------------------

        private void OnDrawGizmos()
        {
            if (!drawRouteGizmo || _lastCorners == null || _lastCorners.Count < 2)
                return;

            Gizmos.color = _lastValid ? Color.green : Color.red;
            for (int i = 1; i < _lastCorners.Count; i++)
                Gizmos.DrawLine(_lastCorners[i - 1], _lastCorners[i]);
        }
    }
}
