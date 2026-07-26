# PROGRESS

Living status log. Pairs with CONTEXT.md (background) and SCOPE.md (plan). Dates are absolute.

Legend: [x] done, [~] in progress, [ ] not started.

## Done (as of 2026-07-23)

- [x] Unity 6000.5 URP project created: BO_Vision_Navigation.
- [x] Indoor environment imported and rendering: ArchVizPRO Interior Vol.7 URP.
- [x] FCG assets imported (outdoor stand in).
- [x] Input handling set to Both; older conflicting asset volumes removed; scene runs.
- [x] Git repository cleaned (large assets excluded) and pushed to usman3656/BO_Vision_Navigation.
- [x] Python 3.13.7 (arm64) installed with torch 2.13.0, botorch 0.18.1, gpytorch, open_clip 3.3.0. GPU (mps) available. All imports verified.
- [x] ViT-B-32 and ViT-g-14 confirmed available in open_clip (using ViT-B-32 only for now).
- [x] BO for Unity framework v1.5.0 obtained; example scene ran standalone, confirming the Unity to Python to BoTorch socket loop and CSV logging.
- [x] Framework merged into the URP project (BOforUnity, QuestionnaireToolkit, StreamingAssets, TextMesh Pro). Newtonsoft JSON package added; BOforUnityManager tag added. Zero compile errors. Confirmed running inside the merged project (system started successfully).
- [x] Removed an early standalone Python embedding folder because it violated the everything on Unity rule. Verified clean.
- [x] engineering_rules.md placed in the project; documentation set (CONTEXT, SCOPE, PROGRESS) created.

## Phase 2 done (2026-07-23)

- [x] Documentation set created and verified.
- [x] Supervisor decision received: route score is VISUAL (ViT model judges what the route looks like), not geometric.
- [x] Architecture chosen and researched: run ViT-B-32 inside Unity via the Inference Engine (Sentis), not the Python backend. Keeps everything on Unity.
- [x] De-risked: exported ViT-B-32 vision tower to a Sentis-compatible ONNX (opset 15, primitive ops, self-contained 335 MB), output matches PyTorch. Model, precomputed affordance text embeddings, and MODEL_NOTES.md placed in Assets/RouteNavigation/Models/ (model gitignored).
- [x] Route builder written and verified: Assets/RouteNavigation/Scripts/RouteEvaluator.cs (reads waypoint parameters, builds a valid NavMesh route, submits the objective the framework's way). Its score is a placeholder, to be replaced by the visual scorer.

## Next steps (Phase 2 continued)

Owner: AUTO means the assistant writes or edits files; BAWANI means the Unity editor or a decision.

1. [ ] Visual scorer script: capture views along the route, run ViT-B-32 in the Inference Engine, compute the score (visual coherence plus walkability affordance), replace the placeholder. Owner: AUTO (verify exact Inference Engine 2.6 API first).
2. [ ] Import the ONNX model into Unity and confirm the Inference Engine loads it. Owner: BAWANI (drag model into the editor, assistant guides).
3. [ ] Bake a NavMesh on the apartment scene. Owner: BAWANI (assistant can prepare a helper).
4. [ ] Place start and goal markers (bed, kitchen), add a capture camera. Owner: BAWANI, assistant guides.
5. [ ] Configure the BO manager parameters and the objective; set advance mode to automatic. Owner: AUTO config, BAWANI presses Play.
6. [ ] Run on the indoor environment, collect the convergence curve. Owner: BAWANI runs, AUTO analyses logs.

## Later phases (unchanged)

- Per-environment context embedding for transfer, run the outdoor environment, compare environments, transfer test on an unknown environment, final in-scene direction markers.

## Upcoming phases

- Phase 3: ViT embeddings as context in the contextual model, produced through the Unity backend.
- Phase 4: run outdoor environment once supplied; produce its curve.
- Phase 5: compare environments.
- Phase 6: transfer test on an unknown environment.
- Phase 7: final visualization, best route in 3D with in scene direction markers.

## Blockers and risks

- Route score definition is unconfirmed; it gates steps 6 to 8.
- Outdoor and unknown environments not yet provided.
- FCG scene may need URP material conversion before it renders correctly.

## What the owner does versus what is automated

- Automated (assistant, by editing files): route parameterization, camera capture script, route score script, BO parameter and objective configuration, log analysis and plots, editor helper scripts.
- Owner (Unity editor or decision): confirm the score definition, bake NavMesh, place start and goal, wire the camera, press Play, review visuals.
