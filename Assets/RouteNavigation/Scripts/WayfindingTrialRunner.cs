using System.Collections;
using System.Collections.Generic;
using BOforUnity;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace RouteNavigation
{
    /// <summary>
    /// Drives the whole wayfinding-path study in ONE continuous scene (no per-trial scene reload).
    /// It uses the scene's OWN first-person player (e.g. Vol.7's FirstPersonAIO) so we don't fight its
    /// camera or controller.
    ///
    /// OPTIMISE environments (Vol.7, FCG): each trial reads the 6 chosen parameters (R,G,B,Opacity,Size,
    /// Height, 0..1), paints the path, times the walk, then asks two 1..10 ratings that genuinely trade off,
    /// Aesthetic (fits the surroundings) and EasyToFollow (attention-grabbing), and submits both as objectives.
    /// Walk time is still logged as a side measure but is not an objective.
    ///
    /// TRANSFER TARGET (Vol.6): no optimiser. ValidationLoop presents the parameter sets transferred from the
    /// participant's own Vol.7 + FCG fronts (closer / distant / interpolation) and measures each.
    ///
    /// The player is auto-found (any component whose type is named "FirstPersonAIO"). For scenes without
    /// one (e.g. FCG, Vol.6) a DesktopWalkController is spawned automatically.
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
        // The two objectives that genuinely trade off (a real Pareto front): a cue that blends into the
        // surroundings cannot also be maximally attention-grabbing. Both are 1..10, both MAXIMISED.
        public string aestheticKey = "Aesthetic";     // does the guidance go well with the surroundings
        public string easyKey = "EasyToFollow";       // how attention-grabbing / easy to follow it is

        private BoForUnityManager _bo;
        private bool _awaitingStart;
        private bool _startPressed;
        private bool _awaitingRating;
        private bool _ratingConfirmed;
        private int _ratingAesthetic = 5;
        private int _ratingEasy = 5;
        private bool _validationMode;   // Vol.6 (transfer target): present transferred params, no optimiser
        private string _status = "Starting up...";
        private int _trial;

        /// <summary>Forces the condition label from the active scene, so each environment's data is
        /// always tagged correctly (Vol7 / FCG / Vol6) regardless of any serialized default.</summary>
        private static string ConditionForScene(string scene, string fallback)
        {
            switch (scene)
            {
                case "Scene-Demo": return "FCG";
                case "ArchVizPRO_Interior_Vol.7_URP": return "Vol7";
                case "AVP6_Desktop": return "Vol6";
                default: return fallback;
            }
        }

        private void Awake()
        {
            conditionId = ConditionForScene(SceneManager.GetActiveScene().name, conditionId);

            // Vol.6 is the TRANSFER TARGET: it is not optimised. It presents the parameters transferred from
            // the participant's own Vol.7 + FCG fronts and just measures them, so it needs no optimiser/Python.
            _validationMode = string.Equals(conditionId, "Vol6", System.StringComparison.OrdinalIgnoreCase);

            if (!_validationMode)
            {
                _bo = FindAnyObjectByType<BoForUnityManager>();
                if (_bo == null)
                {
                    // No manager saved in this scene (e.g. the Vol.7 scene has markers+runner but no manager):
                    // spawn one from Resources so the study still runs on a plain Play. Self-healing = never
                    // depends on the BO manager being saved in the scene.
                    var prefab = Resources.Load<GameObject>("BOforUnityManager");
                    if (prefab != null)
                    {
                        var go = Instantiate(prefab);
                        go.name = "BOforUnityManager";
                        _bo = go.GetComponentInChildren<BoForUnityManager>(true);
                    }
                    if (_bo == null) _bo = FindAnyObjectByType<BoForUnityManager>();
                }
                if (_bo != null) ConfigureManager(_bo); // full config at runtime, before the manager's Start() -> no menus needed
            }

            // Self-wire any missing references so the runner works even if Build didn't connect them.
            if (path == null) path = FindAnyObjectByType<WayfindingPathController>();
            if (path != null)
            {
                // Enforce the intended look in BOTH scenes, regardless of any old serialized values:
                path.minHeight = 0.02f;   // on the floor
                path.maxHeight = 0.9f;    // up to waist height
                path.smoothPath = false;  // straight segments between waypoints
            }
            if (startPoint == null) { var s = GameObject.Find("PathStart"); if (s != null) startPoint = s.transform; }
            if (goalPoint == null) { var g = GameObject.Find("PathGoal"); if (g != null) goalPoint = g.transform; }

            // The spawn point + arrow origin must match the VISIBLE marker sphere the participant sees.
            // If a marker's sphere has drifted from its pivot, move the pivot onto the sphere so they align.
            AlignMarkerToVisual(startPoint);
            AlignMarkerToVisual(goalPoint);

            if (playerRoot == null) AutoFindFirstPersonPlayer();
            if (playerRoot == null && walker == null)
            {
                // No first-person controller in the scene (e.g. FCG) -> spawn a simple colliderless walker.
                var pgo = new GameObject("WayfindingPlayer");
                walker = pgo.AddComponent<DesktopWalkController>();
            }
            EnsurePlayerCamera();                                       // make the active player's camera the only one rendering
            if (playerController != null) playerController.enabled = true;

            if (_validationMode) StartCoroutine(ValidationLoop());
            else StartCoroutine(RunLoop());
        }

        /// <summary>Makes a marker's PIVOT coincide with its VISIBLE sphere child, then re-centers the sphere
        /// on the pivot. This guarantees the spawn/route point is exactly where the participant sees the marker,
        /// fixing a marker whose visible sphere had drifted from its pivot. Clean markers are left untouched.</summary>
        private static void AlignMarkerToVisual(Transform marker)
        {
            if (marker == null) return;
            var mr = marker.GetComponentInChildren<MeshRenderer>(true);
            if (mr == null || mr.transform == marker) return;      // no separate visible sphere
            Transform sphere = mr.transform;
            Vector3 flatOffset = sphere.position - marker.position; flatOffset.y = 0f;
            if (flatOffset.magnitude < 0.1f) return;               // already aligned; don't disturb clean markers
            Vector3 visualWorld = sphere.position;
            marker.position = visualWorld;                          // pivot jumps to where the sphere is seen
            sphere.position = marker.position + Vector3.up * 0.3f;  // sphere sits neatly back on the pivot
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
                    EnsurePlayerCamera(); // walker's own camera is the only one rendering
                    walker.Begin(goalPoint);
                    while (!walker.Finished) yield return null;
                    walkSeconds = walker.ElapsedSeconds;
                }

                // (3) Pause the player and rate the two objectives (both 1..10).
                if (useFps && playerController != null) playerController.enabled = false;
                yield return AskTwoRatings();
                int aesthetic = _ratingAesthetic;
                int easy = _ratingEasy;
                if (useFps && playerController != null) playerController.enabled = true;

                // (4) Submit both objectives and wait for the optimizer's next parameters.
                AddObjectiveByKey(aestheticKey, aesthetic);
                AddObjectiveByKey(easyKey, easy);
                AppendMasterRow(_trial, r, g, b, opacity, size, height, walkSeconds, aesthetic, easy);
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

        /// <summary>Frees the cursor, resets both ratings, and waits for the participant to confirm the
        /// two 1..10 ratings on screen (Aesthetic fit + EasyToFollow). Shared by the study and transfer loops.</summary>
        private IEnumerator AskTwoRatings()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _ratingAesthetic = 5;
            _ratingEasy = 5;
            _ratingConfirmed = false;
            _awaitingRating = true;
            while (!_ratingConfirmed) yield return null;
            _awaitingRating = false;
        }

        /// <summary>Vol.6 transfer test: no optimiser. Presents the parameter sets transferred from the
        /// participant's own Vol.7 + FCG fronts (closer, distant, interpolation at three tradeoff levels) and
        /// measures each. Success is read from the results: does the closer source beat the distant one, and is
        /// the interpolation at least as good as the better single source.</summary>
        private IEnumerator ValidationLoop()
        {
            if (path == null || startPoint == null || goalPoint == null)
            {
                Debug.LogError("[Transfer] Assign path, startPoint and goalPoint."); yield break;
            }
            bool useFps = playerRoot != null;
            if (!useFps && walker == null) { Debug.LogError("[Transfer] No player found."); yield break; }

            if (useFps) { EnsurePlayerCamera(); TeleportPlayer(startPoint.position); }

            // START gate collects the participant ID; their own Vol.7 + FCG data drives the transfer.
            if (useFps && playerController != null) playerController.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _startPressed = false;
            _awaitingStart = true;
            _status = "Transfer test. Enter the Participant ID (same as their Vol.7 + FCG runs) and press START.";
            while (!_startPressed) yield return null;
            _awaitingStart = false;

            var candidates = EmbeddingTransfer.BuildCandidates(participantId, conditionId, out string err);
            if (candidates == null || candidates.Count == 0)
            {
                _status = "Transfer test could not start: " + (err ?? "no candidates.");
                Debug.LogError("[Transfer] " + _status);
                yield break;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                _trial = i + 1;

                path.RebuildRoute();
                path.ApplyParameters(c.r, c.g, c.b, c.opacity, c.size, c.height);

                float walkSeconds;
                _status = $"Transfer {_trial}/{candidates.Count} ({c.kind}, λ={c.lambda:F2}): walk to the RED goal.";
                if (useFps)
                {
                    TeleportPlayer(startPoint.position);
                    if (playerController != null) playerController.enabled = true;
                    EnsurePlayerCamera();
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    yield return WalkFps();
                    walkSeconds = _lastWalkSeconds;
                }
                else
                {
                    walker.ResetTo(startPoint);
                    EnsurePlayerCamera();
                    walker.Begin(goalPoint);
                    while (!walker.Finished) yield return null;
                    walkSeconds = walker.ElapsedSeconds;
                }

                if (useFps && playerController != null) playerController.enabled = false;
                yield return AskTwoRatings();
                int aesthetic = _ratingAesthetic;
                int easy = _ratingEasy;
                if (useFps && playerController != null) playerController.enabled = true;

                AppendMasterRow(_trial, c.r, c.g, c.b, c.opacity, c.size, c.height, walkSeconds, aesthetic, easy);
                AppendValidationRow(c, walkSeconds, aesthetic, easy);
            }

            _status = "Transfer test complete. Thank you!";
        }

        /// <summary>Logs one transfer candidate (with its kind, tradeoff level, and predicted vs actual
        /// objectives) to a dedicated CSV for the transfer analysis.</summary>
        private void AppendValidationRow(EmbeddingTransfer.Candidate c, float walkSeconds, int aesthetic, int easy)
        {
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "BOData", "LogData");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, "TransferValidation.csv");
                if (!System.IO.File.Exists(file))
                    System.IO.File.AppendAllText(file,
                        "Timestamp;Participant;Condition;Trial;Kind;Lambda;ActualAesthetic;ActualEasyToFollow;" +
                        "PredAesthetic;PredEasyToFollow;WalkTimeSeconds;R;G;B;Opacity;Size;Height\n");
                string row = string.Format(inv,
                    "{0};{1};{2};{3};{4};{5:F2};{6};{7};{8:F2};{9:F2};{10:F3};{11:F3};{12:F3};{13:F3};{14:F3};{15:F3};{16:F3}\n",
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    participantId, conditionId, _trial, c.kind, c.lambda, aesthetic, easy,
                    c.predAesthetic, c.predEasy, walkSeconds, c.r, c.g, c.b, c.opacity, c.size, c.height);
                System.IO.File.AppendAllText(file, row);
                Debug.Log($"[Transfer] Logged {c.kind} (λ={c.lambda:F2}) for {participantId}.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Transfer] Could not write TransferValidation.csv: " + e.Message);
            }
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
            Transform root = playerRoot != null ? playerRoot : (walker != null ? walker.transform : null);
            if (root == null) return;
            Camera mine = root.GetComponentInChildren<Camera>(true);
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

        internal const string MasterHeader =
            "Timestamp;Participant;Condition;Trial;WalkTimeSeconds;Aesthetic;EasyToFollow;R;G;B;Opacity;Size;Height";

        /// <summary>Appends ONE row per trial (all participants) to a single master CSV:
        /// Assets/StreamingAssets/BOData/LogData/AllTrials_master.csv. Walk time is still logged as a side
        /// measure; the two OBJECTIVES are Aesthetic and EasyToFollow.</summary>
        private void AppendMasterRow(int trial, float r, float g, float b, float op, float sz, float ht, float walkSeconds, int aesthetic, int easyToFollow)
        {
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "BOData", "LogData");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, "AllTrials_master.csv");
                if (!System.IO.File.Exists(file))
                    System.IO.File.AppendAllText(file, MasterHeader + "\n");
                string row = string.Format(inv,
                    "{0};{1};{2};{3};{4:F3};{5};{6};{7:F3};{8:F3};{9:F3};{10:F3};{11:F3};{12:F3}\n",
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    participantId, conditionId, trial, walkSeconds, aesthetic, easyToFollow, r, g, b, op, sz, ht);
                System.IO.File.AppendAllText(file, row);
                Debug.Log($"[Trial] Appended trial {trial} for {participantId} to master CSV.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Trial] Could not write master CSV: " + e.Message);
            }
        }

        /// <summary>Configures the BO manager entirely at runtime (params, objectives, seed, backend), so the
        /// study runs on a plain Play with no editor menus. Runs in Awake, before the manager's Start().</summary>
        private void ConfigureManager(BoForUnityManager bo)
        {
            if (!string.IsNullOrWhiteSpace(participantId)) bo.userId = participantId.Trim();
            if (!string.IsNullOrWhiteSpace(conditionId)) bo.conditionId = conditionId.Trim();
            bo.reloadSceneOnIterationAdvance = false;

            // ALWAYS overwrite params + objectives so the study is correct in ANY scene, even if the
            // manager still had the demo objectives (e.g. FCG had 'Trust' etc.).
            bo.parameters = new List<ParameterEntry>
            {
                new ParameterEntry("R",       new ParameterArgs(0f, 1f)),
                new ParameterEntry("G",       new ParameterArgs(0f, 1f)),
                new ParameterEntry("B",       new ParameterArgs(0f, 1f)),
                new ParameterEntry("Opacity", new ParameterArgs(0f, 1f)),
                new ParameterEntry("Size",    new ParameterArgs(0f, 1f)),
                new ParameterEntry("Height",  new ParameterArgs(0f, 1f)),
            };
            // Two MAXIMISED objectives that genuinely trade off (guaranteed Pareto front): blending into the
            // surroundings vs grabbing attention. Walk time is no longer an objective (it barely varied and
            // was confounded by the learning effect); it is still logged as a side measure.
            bo.objectives = new List<ObjectiveEntry>
            {
                new ObjectiveEntry("Aesthetic",    new ObjectiveArgs(1f, 10f, false, 1)),
                new ObjectiveEntry("EasyToFollow", new ObjectiveArgs(1f, 10f, false, 1)),
            };

            bo.numSamplingIterations = 14;
            bo.numOptimizationIterations = 5;
            bo.seed = 42;
            bo.iterationAdvanceMode = BoForUnityManager.IterationAdvanceMode.ExternalSignal;
            bo.optimizerBackend = BoForUnityManager.OptimizerBackend.MetaTAF;

            // Automatic transfer pipeline, keyed off the scene's condition:
            //   Vol7, FCG  -> SOURCE environments: cold-start (don't consume) AND export themselves as
            //                 population models. Running both auto-populates MetaSources/.
            //   Vol6       -> TRANSFER TARGET: consume the Vol7+FCG models, require them, don't export.
            //   anything else (e.g. a "Vol6_baseline" cold-start control) -> no consume, no export.
            string cond = (conditionId ?? "").Trim();
            bool isSource = cond.Equals("Vol7", System.StringComparison.OrdinalIgnoreCase)
                         || cond.Equals("FCG", System.StringComparison.OrdinalIgnoreCase);
            bool isTransferTarget = cond.Equals("Vol6", System.StringComparison.OrdinalIgnoreCase);

            bo.metaConsumeSources = isTransferTarget;   // only Vol6 transfers from the sources
            bo.metaRequireSources = isTransferTarget;   // Vol6 must have the sources; sources/baseline must not
            bo.metaExportSource   = isSource;           // Vol7/FCG write themselves as population models
            bo.metaExportName     = isSource && !string.IsNullOrWhiteSpace(bo.userId)
                                    ? (bo.userId.Trim() + "_" + cond) : "";
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
                float bw = 460f, bh = 260f;
                var b = new Rect((Screen.width - bw) / 2f, (Screen.height - bh) / 2f, bw, bh);
                GUI.Box(b, GUIContent.none);

                var lbl = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
                var fld = new GUIStyle(GUI.skin.textField) { fontSize = 22, fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(b.x + 30f, b.y + 20f, bw - 60f, 26f), $"Participant ID   (Condition: {conditionId})", lbl);
                participantId = GUI.TextField(new Rect(b.x + 30f, b.y + 50f, bw - 60f, 40f), participantId, fld);

                var startStyle = new GUIStyle(GUI.skin.button) { fontSize = 42, fontStyle = FontStyle.Bold };
                if (GUI.Button(new Rect(b.x + 30f, b.y + 108f, bw - 60f, bh - 140f), "▶  START", startStyle))
                {
                    if (string.IsNullOrWhiteSpace(participantId)) participantId = "P01";
                    participantId = participantId.Trim();
                    if (_bo != null) _bo.userId = participantId;
                    _startPressed = true;
                }
                return;
            }

            if (_awaitingRating)
            {
                float w = Mathf.Min(1000f, Screen.width * 0.95f);
                float h = 470f;
                var box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
                GUI.Box(box, GUIContent.none);

                var title = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
                var numBtn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
                var confirmBtn = new GUIStyle(GUI.skin.button) { fontSize = 28, fontStyle = FontStyle.Bold };

                GUILayout.BeginArea(new Rect(box.x + 24f, box.y + 18f, w - 48f, h - 36f));

                // Objective 1: aesthetic fit with the surroundings.
                GUILayout.Label($"Does the guidance fit the surroundings?   (1 = clashes,  10 = blends in)   [{_ratingAesthetic}]", title);
                GUILayout.BeginHorizontal();
                for (int n = 1; n <= 10; n++)
                    if (GUILayout.Button(n.ToString(), numBtn, GUILayout.Height(60f))) _ratingAesthetic = n;
                GUILayout.EndHorizontal();

                GUILayout.Space(16f);

                // Objective 2: how easy to follow / attention-grabbing.
                GUILayout.Label($"How easy to follow / attention-grabbing?   (1 = easy to miss,  10 = impossible to miss)   [{_ratingEasy}]", title);
                GUILayout.BeginHorizontal();
                for (int n = 1; n <= 10; n++)
                    if (GUILayout.Button(n.ToString(), numBtn, GUILayout.Height(60f))) _ratingEasy = n;
                GUILayout.EndHorizontal();

                GUILayout.Space(16f);
                if (GUILayout.Button($"CONFIRM   (fit {_ratingAesthetic}, follow {_ratingEasy})", confirmBtn, GUILayout.Height(66f)))
                    _ratingConfirmed = true;
                GUILayout.EndArea();
            }
        }
    }
}
