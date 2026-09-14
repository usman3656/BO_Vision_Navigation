using System.Collections.Generic;
using BOforUnity;
using UnityEditor;
using UnityEngine;

namespace RouteNavigation.EditorTools
{
    /// <summary>
    /// One-click configuration of the BoForUnityManager for the wayfinding-path study, so the
    /// six parameters and two objectives are never mistyped by hand.
    ///
    /// Menu: Tools > BO Route > Configure BO Manager (Wayfinding)
    ///
    /// Sets: 6 parameters (R,G,B,Opacity,Size,Height, all 0..1), 2 objectives
    /// (WalkTime smaller-is-better 0..60 s, Aesthetics larger-is-better 1..20), a fixed seed for
    /// reproducible Sobol, ExternalSignal advance (the trial runner drives it), MetaTAF backend
    /// (your professor's fork) with Require Sources OFF for the first run, and 5 optimization
    /// iterations on top of the auto 2*(6+1)=14 Sobol samples = ~19 trials.
    /// </summary>
    public static class WayfindingSceneConfigurator
    {
        [MenuItem("Tools/BO Route/Configure BO Manager (Wayfinding)")]
        public static void Configure()
        {
            var bo = Object.FindAnyObjectByType<BoForUnityManager>();
            if (bo == null)
            {
                Debug.LogError("[Config] No BoForUnityManager found in the open scene. " +
                               "Drag in the BOforUnityManager prefab first, then run this again.");
                return;
            }

            Undo.RecordObject(bo, "Configure Wayfinding BO");

            bo.parameters = new List<ParameterEntry>
            {
                new ParameterEntry("R",       new ParameterArgs(0f, 1f)),
                new ParameterEntry("G",       new ParameterArgs(0f, 1f)),
                new ParameterEntry("B",       new ParameterArgs(0f, 1f)),
                new ParameterEntry("Opacity", new ParameterArgs(0f, 1f)),
                new ParameterEntry("Size",    new ParameterArgs(0f, 1f)),
                new ParameterEntry("Height",  new ParameterArgs(0f, 1f)),
            };

            bo.objectives = new List<ObjectiveEntry>
            {
                // WalkTime: seconds, smaller is better. Aesthetics: 1..20, larger is better.
                new ObjectiveEntry("WalkTime",   new ObjectiveArgs(0f, 60f, true, 1)),
                new ObjectiveEntry("Aesthetics", new ObjectiveArgs(1f, 20f, false, 1)),
            };

            bo.numSamplingIterations = 14;   // 2*(6+1); auto-recomputed for 6 params anyway
            bo.numOptimizationIterations = 5; // 14 + 5 = 19 total trials
            bo.seed = 42;                     // fixed seed -> identical Sobol start for every participant
            bo.iterationAdvanceMode = BoForUnityManager.IterationAdvanceMode.ExternalSignal;
            bo.reloadSceneOnIterationAdvance = true;
            bo.optimizerBackend = BoForUnityManager.OptimizerBackend.MetaTAF; // professor's fork (openbo)
            bo.metaRequireSources = false;   // first run has no population models -> runs as plain MOBO

            EditorUtility.SetDirty(bo);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            Debug.Log("[Config] BO manager configured for wayfinding.\n" +
                      "Parameters: R, G, B, Opacity, Size, Height (all 0..1).\n" +
                      "Objectives: WalkTime (min, 0..60 s) + Aesthetics (max, 1..20).\n" +
                      "Seed 42, ExternalSignal advance, backend = MetaTAF (Require Sources OFF), " +
                      "~14 Sobol + 5 optimization = 19 trials.");
        }
    }
}
