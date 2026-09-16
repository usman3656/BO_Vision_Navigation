using System.Collections;
using BOforUnity;
using UnityEngine;
using UnityEngine.AI;

namespace RouteNavigation
{
    /// <summary>
    /// Drives the whole wayfinding-path study in ONE continuous scene (no per-trial scene reload).
    /// It uses the scene's OWN first-person player (e.g. Vol.7's FirstPersonAIO) so we don't fight its
    /// camera or controller. Each trial it:
    ///   1. reads the 6 chosen parameters (R,G,B,Opacity,Size,Height, 0..1) and paints the path,
    ///   2. teleports the player to Start and lets them walk to Goal (timed),
    ///   3. pauses the player and asks for a 1..20 look rating (on-screen),
    ///   4. submits both objectives (WalkTime, Aesthetics) and waits for the optimizer's next parameters.
    ///
    /// The player is auto-found (any component whose type is named "FirstPersonAIO"). For scenes without
    /// one (e.g. FCG) assign a DesktopWalkController in the Walker slot instead.
    /// </summary>
    public class WayfindingTrialRunner : MonoBehaviour
    {
        [Header("Scene references")]
        public WayfindingPathController path;
        public Transform startPoint;
        public Transform goalPoint;

        [Header("Player (auto-found if left empty)")]
        [Tooltip("The scene's first-person player object to teleport and track. Auto-found (FirstPersonAIO) if empty.")]
        public Transform playerRoot;
        [Tooltip("The player's controller script; it is paused during the rating so the cursor is free. Auto-found.")]
        public Behaviour playerController;
        [Tooltip("Fallback walker for scenes with no first-person controller (e.g. FCG).")]
        public DesktopWalkController walker;

        [Header("Participant (set BEFORE pressing Play, for each person)")]
        [Tooltip("Unique ID per participant. Each person gets their own data folder under LogData. Change this for every participant.")]
        public string participantId = "P01";
        [Tooltip("Environment/condition label for this run (e.g. Vol7, FCG, Vol6). Keeps each environment's data separate.")]
        public string conditionId = "Vol7";

        [Header("Tuning")]
        [Tooltip("XZ distance to the goal (metres) that counts as arrived.")]
        public float arriveRadius = 2f;
        public string walkTimeKey = "WalkTime";
        public string aestheticsKey = "Aesthetics";

        private BoForUnityManager _bo;
        private bool _awaitingStart;
        private bool _startPressed;
        private bool _awaitingRating;
        private bool _ratingConfirmed;
        private int _rating = 5;
        private string _status = "Starting up...";
        private int _trial;

        private void Awake()
        {
            _bo = FindAnyObjectByType<BoForUnityManager>();
            if (_bo != null)
            {
                // Set participant + condition BEFORE the manager reserves its log folder in its Start().
                // (All Awakes run before any Start, so this always lands in time.)
                if (!string.IsNullOrWhiteSpace(participantId)) _bo.userId = participantId.Trim();
                if (!string.IsNullOrWhiteSpace(conditionId)) _bo.conditionId = conditionId.Trim();
                _bo.reloadSceneOnIterationAdvance = false; // one persistent scene; we loop here
                SetObjectiveBounds(aestheticsKey, 1f, 10f); // rating scale is 1..10
            }

            // Self-wire any missing references so the runner works even if Build didn't connect them.
            if (path == null) path = FindAnyObjectByType<WayfindingPathController>();
            if (startPoint == null) { var s = GameObject.Find("PathStart"); if (s != null) startPoint = s.transform; }
            if (goalPoint == null) { var g = GameObject.Find("PathGoal"); if (g != null) goalPoint = g.transform; }

            if (playerRoot == null) AutoFindFirstPersonPlayer();
            if (playerRoot != null)
            {
                EnsurePlayerCamera();                                   // undo any camera left disabled by an old build
                if (playerController != null) playerController.enabled = true;
            }
            StartCoroutine(RunLoop());
        }

        /// <summary>Finds the scene's first-person controller by type name, so we don't hard-depend on the asset.</summary>
        private void AutoFindFirstPersonPlayer()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mb != null && mb.GetType().Name == "FirstPersonAIO")
                {
                    playerRoot = mb.transform;
                    playerController = mb;
                    return;
                }
            }
        }

        private IEnumerator RunLoop()
        {
            if (_bo == null) { Debug.LogError("[Trial] No BoForUnityManager in the scene."); yield break; }
            if (path == null || startPoint == null || goalPoint == null)
            {
                Debug.LogError("[Trial] Assign path, startPoint and goalPoint on WayfindingTrialRunner.");
                yield break;
            }
            bool useFps = playerRoot != null;
            if (!useFps && walker == null)
            {
                Debug.LogError("[Trial] No player found. Assign a Walker, or add a FirstPersonAIO player to the scene.");
                yield break;
            }

            if (useFps) { EnsurePlayerCamera(); TeleportPlayer(startPoint.position); }
            _status = "Starting optimizer (Python), please wait...";
            while (!_bo.initialized && !_bo.optimizationFinished) yield return null;

            HideManagerPanels();
            if (useFps) TeleportPlayer(startPoint.position);

            // Wait for the participant to press START before the first trial begins.
            if (useFps && playerController != null) playerController.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _startPressed = false;
            _awaitingStart = true;
            _status = "Ready. Press START to begin.";
            while (!_startPressed && !_bo.optimizationFinished) yield return null;
            _awaitingStart = false;
            if (useFps && playerController != null) playerController.enabled = true;

            while (!_bo.optimizationFinished)
            {
                HideManagerPanels();
                _trial++;

                // (1) Read the 6 chosen parameters and paint the path.
                TryGetParam(0, out float r);
                TryGetParam(1, out float g);
                TryGetParam(2, out float b);
                TryGetParam(3, out float opacity);
                TryGetParam(4, out float size);
                TryGetParam(5, out float height);
                path.RebuildRoute();
                path.ApplyParameters(r, g, b, opacity, size, height);
                ClearObjectiveValues();

                // (2) Walk Start -> Goal, timed.
                float walkSeconds;
                _status = $"Trial {_trial}: walk to the RED goal (WASD + mouse).";
                if (useFps)
                {
                    TeleportPlayer(startPoint.position);
                    if (playerController != null) playerController.enabled = true;
                    EnsurePlayerCamera(); // make sure we're looking through the player's own camera
                    Cursor.lockState = CursorLockMode.Locked; // capture the mouse for first-person look
                    Cursor.visible = false;
                    yield return WalkFps();
                    walkSeconds = _lastWalkSeconds;
                }
                else
                {
                    walker.ResetTo(startPoint);
                    walker.Begin(goalPoint);
                    while (!walker.Finished) yield return null;
                    walkSeconds = walker.ElapsedSeconds;
                }

                // (3) Pause the player and rate the look, 1..20.
                if (useFps && playerController != null) playerController.enabled = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _rating = 5;
                _ratingConfirmed = false;
                _awaitingRating = true;
                while (!_ratingConfirmed) yield return null;
                _awaitingRating = false;
                int aesthetics = _rating;
                if (useFps && playerController != null) playerController.enabled = true;

                // (4) Submit both objectives and wait for the optimizer's next parameters.
                AddObjectiveByKey(walkTimeKey, walkSeconds);
                AddObjectiveByKey(aestheticsKey, aesthetics);
                _status = "Submitted. The optimizer is thinking...";
                int iterationBefore = _bo.currentIteration;
                _bo.OptimizationStart();
                while (!_bo.optimizationFinished && _bo.currentIteration == iterationBefore)
                {
                    HideManagerPanels();
                    yield return null;
                }
            }

            _status = "Study complete. Thank you!";
        }

        private float _lastWalkSeconds;

        /// <summary>Times the first-person walk from first movement to arrival at the goal (XZ).</summary>
        private IEnumerator WalkFps()
        {
            Vector3 startXz = Flat(playerRoot.position);
            bool moving = false;
            float elapsed = 0f;
            while (true)
            {
                Vector3 hereXz = Flat(playerRoot.position);
                if (!moving && Vector3.Distance(hereXz, startXz) > 0.5f) moving = true;
                if (moving) elapsed += Time.deltaTime;
                // Only count arrival once the player has actually started walking, so a start
                // placed near the goal can't instantly "complete" the trial and lock the controller.
                if (moving && Vector3.Distance(hereXz, Flat(goalPoint.position)) <= arriveRadius) break;
                yield return null;
            }
            _lastWalkSeconds = elapsed;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Forces the first-person player to the start marker (lifted so the capsule clears the floor).
        /// The controller is disabled during the move so its physics can't fight the teleport.</summary>
        private void TeleportPlayer(Vector3 floorPos)
        {
            if (playerRoot == null) return;
            Vector3 target = floorPos + Vector3.up * 1.1f;

            bool wasEnabled = playerController != null && playerController.enabled;
            if (playerController != null) playerController.enabled = false;

            var rb = playerRoot.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = target;
            }
            playerRoot.position = target;

            if (playerController != null) playerController.enabled = wasEnabled;
        }

        /// <summary>Makes the player's own camera the one that renders: enables it, disables every other camera
        /// (e.g. a leftover rival player's camera or a scene camera an old build disabled).</summary>
        private void EnsurePlayerCamera()
        {
            if (playerRoot == null) return;
            Camera mine = playerRoot.GetComponentInChildren<Camera>(true);
            if (mine == null) { Debug.LogWarning("[Trial] Could not find the player's own camera to render through."); return; }
            mine.gameObject.SetActive(true);
            mine.enabled = true;
            mine.depth = 100f; // win over anything else that is still enabled
            // Make the player's camera the ONLY one rendering, so the view follows the player,
            // not a static leftover/scene camera.
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cam != mine) cam.enabled = false;
        }

        private Canvas _boCanvas;

        private void HideManagerPanels()
        {
            if (_bo == null) return;
            if (_bo.welcomePanel != null) _bo.welcomePanel.SetActive(false);
            if (_bo.optimizerStatePanel != null) _bo.optimizerStatePanel.SetActive(false);
            if (_bo.loadingObj != null) _bo.loadingObj.SetActive(false);
            // Turn off the whole BO UI canvas so its white/pink overlay never shows during the walk.
            if (_boCanvas == null && _bo.welcomePanel != null) _boCanvas = _bo.welcomePanel.GetComponentInParent<Canvas>();
            if (_boCanvas != null) _boCanvas.enabled = false;
        }

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

        private void ClearObjectiveValues()
        {
            if (_bo?.objectives == null) return;
            foreach (var o in _bo.objectives)
                if (o?.value?.values != null) o.value.values.Clear();
        }

        private void SetObjectiveBounds(string key, float low, float high)
        {
            if (_bo?.objectives == null) return;
            foreach (var o in _bo.objectives)
            {
                if (o?.value == null || string.IsNullOrWhiteSpace(o.key)) continue;
                if (string.Equals(o.key.Trim(), key.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    o.value.lowerBound = low;
                    o.value.upperBound = high;
                    return;
                }
            }
        }

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
            GUI.Label(new Rect(16f, 12f, 900f, 28f), _status);

            // Live diagnostics so we can see what the loop is doing.
            if (playerRoot != null && goalPoint != null)
            {
                float d = Vector3.Distance(Flat(playerRoot.position), Flat(goalPoint.position));
                string ctl = playerController != null ? (playerController.enabled ? "ON" : "OFF") : "none";
                GUI.Label(new Rect(16f, 38f, 900f, 28f), $"[debug] distance to goal: {d:F1} m   |   player control: {ctl}");
            }
            GUI.Label(new Rect(16f, 64f, 900f, 24f), $"Participant: {participantId}    |    Condition: {conditionId}");

            if (path != null && !path.RouteValid)
            {
                var warn = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true };
                GUI.color = Color.yellow;
                GUI.Label(new Rect(16f, 90f, 900f, 60f),
                    "No route - arrows hidden. Assign at least a Start and Goal to GuidancePath (Build Wayfinding Test Objects).", warn);
                GUI.color = Color.white;
            }

            if (_awaitingStart)
            {
                float bw = 440f, bh = 170f;
                var b = new Rect((Screen.width - bw) / 2f, (Screen.height - bh) / 2f, bw, bh);
                GUI.Box(b, GUIContent.none);
                var startStyle = new GUIStyle(GUI.skin.button) { fontSize = 42, fontStyle = FontStyle.Bold };
                if (GUI.Button(new Rect(b.x + 30f, b.y + 34f, bw - 60f, bh - 68f), "▶  START", startStyle))
                    _startPressed = true;
                return;
            }

            if (_awaitingRating)
            {
                float w = Mathf.Min(1000f, Screen.width * 0.95f);
                float h = 360f;
                var box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
                GUI.Box(box, GUIContent.none);

                var title = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
                var bigNum = new GUIStyle(GUI.skin.label) { fontSize = 90, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                var numBtn = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold };
                var confirmBtn = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold };

                GUILayout.BeginArea(new Rect(box.x + 24f, box.y + 18f, w - 48f, h - 36f));
                GUILayout.Label("Rate how this path LOOKS    (1 = ugly,  10 = beautiful)", title);
                GUILayout.Label(_rating.ToString(), bigNum);

                GUILayout.BeginHorizontal();
                for (int n = 1; n <= 10; n++)
                    if (GUILayout.Button(n.ToString(), numBtn, GUILayout.Height(66f))) _rating = n;
                GUILayout.EndHorizontal();

                GUILayout.Space(14f);
                if (GUILayout.Button("CONFIRM  (score " + _rating + ")", confirmBtn, GUILayout.Height(70f)))
                    _ratingConfirmed = true;
                GUILayout.EndArea();
            }
        }
    }
}
