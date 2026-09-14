using System.Collections;
using BOforUnity;
using UnityEngine;

namespace RouteNavigation
{
    /// <summary>
    /// Runs ONE Bayesian Optimization trial of the wayfinding-path study, then advances the loop.
    /// The framework reloads the scene each trial, so this component's Awake is the per-trial entry.
    ///
    /// Per trial it:
    ///   1. waits until the optimizer has real parameters ready,
    ///   2. reads the 6 chosen parameters (R,G,B,Opacity,Size,Height, all 0..1) and paints the path,
    ///   3. lets the user walk Start->Goal (DesktopWalkController times it),
    ///   4. asks for a 1..20 look rating (on-screen, no extra UI setup),
    ///   5. submits both objectives (WalkTime, Aesthetics) and requests the next iteration.
    ///
    /// Configure the BO manager once with: Tools > BO Route > Configure BO Manager (Wayfinding).
    /// </summary>
    public class WayfindingTrialRunner : MonoBehaviour
    {
        [Header("Scene references")]
        public WayfindingPathController path;
        public DesktopWalkController walker;
        public Transform startPoint;
        public Transform goalPoint;

        [Header("Objective keys (must match the BO manager objectives)")]
        public string walkTimeKey = "WalkTime";
        public string aestheticsKey = "Aesthetics";

        private BoForUnityManager _bo;
        private bool _awaitingRating;
        private bool _ratingConfirmed;
        private int _rating = 10;
        private string _status = "";

        private void Awake()
        {
            _bo = FindAnyObjectByType<BoForUnityManager>();
            StartCoroutine(RunTrial());
        }

        private IEnumerator RunTrial()
        {
            if (_bo == null) { Debug.LogError("[Trial] No BoForUnityManager in the scene."); yield break; }

            // Wait until the optimizer has initialised. On first play the manager initialises then
            // auto-advances (reloading the scene), so the real trial runs on the reloaded scene where
            // this trial's parameter Values are already applied. We gate only on `initialized` (like the
            // shipped ColorGuesser/TargetClicker tasks) because the manager clears
            // hasNewDesignParameterValues before each reload, and we stop once the study is finished.
            while (!_bo.optimizationFinished && !_bo.initialized)
                yield return null;

            if (_bo.optimizationFinished) { _status = "Study complete. Thank you."; yield break; }
            yield return null; // one more frame so the scene + navmesh are fully settled

            if (path == null || walker == null || startPoint == null || goalPoint == null)
            {
                Debug.LogError("[Trial] Assign path, walker, startPoint and goalPoint on WayfindingTrialRunner.");
                yield break;
            }

            // Hide the optimizer's status panels so they don't cover the game during the walk + rating.
            HideManagerPanels();

            // (1) Read the 6 chosen parameters (already 0..1 because their bounds are 0..1).
            TryGetParam(0, out float r);
            TryGetParam(1, out float g);
            TryGetParam(2, out float b);
            TryGetParam(3, out float opacity);
            TryGetParam(4, out float size);
            TryGetParam(5, out float height);

            // (2) Build the route (subset NavMesh) and paint it with this trial's appearance.
            path.RebuildRoute();
            path.ApplyParameters(r, g, b, opacity, size, height);

            // (3) Walk Start -> Goal, timed.
            walker.ResetTo(startPoint);
            _status = "Walk to the goal.";
            walker.Begin(goalPoint);
            while (!walker.Finished) yield return null;
            float walkSeconds = walker.ElapsedSeconds;

            // (4) Rate the look, 1..20 (on-screen panel below).
            _rating = 10;
            _ratingConfirmed = false;
            _awaitingRating = true;
            while (!_ratingConfirmed) yield return null;
            _awaitingRating = false;
            int aesthetics = _rating;

            // (5) Submit both objectives and advance.
            AddObjectiveByKey(walkTimeKey, walkSeconds);
            AddObjectiveByKey(aestheticsKey, aesthetics);
            _status = "Submitted. Loading the next path...";

            _bo.OptimizationStart();
            if (_bo.iterationAdvanceMode == BoForUnityManager.IterationAdvanceMode.ExternalSignal && _bo.optimizationRunning)
                _bo.RequestNextIteration();
        }

        /// <summary>Hides the BO manager's welcome/optimizer/loading UI so the game is visible during the walk.</summary>
        private void HideManagerPanels()
        {
            if (_bo == null) return;
            if (_bo.welcomePanel != null) _bo.welcomePanel.SetActive(false);
            if (_bo.optimizerStatePanel != null) _bo.optimizerStatePanel.SetActive(false);
            if (_bo.loadingObj != null) _bo.loadingObj.SetActive(false);
        }

        /// <summary>Reads the index-th valid BO parameter, clamped to 0..1. Mirrors ColorGuesser.</summary>
        private bool TryGetParam(int index, out float value)
        {
            value = 0.5f;
            if (_bo?.parameters == null || index < 0) return false;
            int seen = 0;
            foreach (var p in _bo.parameters)
            {
                if (p?.value == null || string.IsNullOrWhiteSpace(p.key)) continue;
                if (seen == index) { value = Mathf.Clamp01(p.value.Value); return true; }
                seen++;
            }
            Debug.LogWarning($"[Trial] Parameter index {index} not found; using 0.5. Did you configure 6 parameters?");
            return false;
        }

        /// <summary>Appends a value to the objective with the given key (case/space-insensitive).</summary>
        private bool AddObjectiveByKey(string key, float value)
        {
            if (_bo?.objectives == null) return false;
            foreach (var o in _bo.objectives)
            {
                if (o?.value == null || string.IsNullOrWhiteSpace(o.key)) continue;
                if (string.Equals(o.key.Trim(), key.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    o.value.values.Add(value);
                    return true;
                }
            }
            Debug.LogError($"[Trial] No objective named '{key}'. Run Tools > BO Route > Configure BO Manager (Wayfinding).");
            return false;
        }

        private void OnGUI()
        {
            if (_awaitingRating)
            {
                const float w = 540f, h = 150f;
                var box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
                GUI.Box(box, "Rate the PATH APPEARANCE");
                GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 34f, w - 40f, h - 44f));
                GUILayout.Label($"1 = ugly,  20 = beautiful.     Your rating: {_rating}");
                _rating = Mathf.RoundToInt(GUILayout.HorizontalSlider(_rating, 1f, 20f));
                GUILayout.Space(12f);
                if (GUILayout.Button("Confirm rating")) _ratingConfirmed = true;
                GUILayout.EndArea();
                return;
            }

            if (walker != null && walker.Walking)
                GUI.Label(new Rect(20f, 20f, 500f, 30f), $"Walk to the goal.   Time: {walker.ElapsedSeconds:F1}s");

            if (!string.IsNullOrEmpty(_status))
                GUI.Label(new Rect(20f, 46f, 600f, 30f), _status);
        }
    }
}
