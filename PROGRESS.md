# PROGRESS

Living status log. Updated 2026-07-31 after the supervisor meeting, which changed the design. Pairs with CONTEXT.md and SCOPE.md.

Legend: [x] done, [~] in progress, [ ] not started.

## Design change (2026-07-31)

The supervisor clarified the project. It is NOT about a vision model scoring how natural a route is. It is about optimizing the VISUAL DESIGN of a VR wayfinding path (color, width, height, chevrons, animation), rated by humans in VR, with the vision model used only to embed each environment for the transfer test. See CONTEXT.md and SCOPE.md.

Consequence for what we built:
- Keep: Unity project, BO framework running, ViT-B-32 in Unity, NavMesh path tooling.
- Superseded: the ViT smoothness and walkability route scorer (RouteVisualScorer) and waypoint-position optimization (RouteEvaluator). Route quality is now human-rated, and the design parameters are the path's appearance. These scripts will be reworked or removed.

## Done

- [x] Unity 6000.5 URP project, ArchViz Vol.7 and FCG imported.
- [x] Repo cleaned and pushed (usman3656/BO_Vision_Navigation).
- [x] Python 3.13 with torch, botorch, gpytorch, open_clip. GPU verified.
- [x] BO for Unity v1.5.0 merged and running end to end (demo loop confirmed).
- [x] ViT-B-32 exported and loading inside Unity (Inference Engine).
- [x] NavMesh path tooling built; a walkable route works in the indoor scene.
- [x] Documentation set created, and updated after the meeting.

## Not yet run

- The scored optimization loop has never run on a real task. No Pareto front and no participant data yet.

## Next steps (from the meeting; owner does Unity, assistant supports)

1. [ ] Confirm the three environments. Re-check the German files and re-import the deleted volumes if they are the two indoor rooms. Owner.
2. [ ] Fix one start point per environment.
3. [ ] Capture one clean wide-angle start image per environment, facing the walk direction, with no overlay.
4. [ ] Embed the three images with ViT-B-32; pick the two furthest apart.
5. [ ] Decide hex vs RGB for color, and finalize the six parameters.
6. [ ] Build the visual path renderer driven by the six normalized parameters.
7. [ ] Add speed measurement and the in-VR aesthetic questionnaire (1 to 20).
8. [ ] Set up multi-objective BO with Sobol sampling; iteration budget about 19.
9. [ ] Self-test the full loop in VR (headset from the supervisor) before participants.
10. [ ] Optimize in the two chosen environments; produce a Pareto front each.
11. [ ] Implement transfer for the third environment: closest vs interpolated, and compare.

## Suggested order for the low-availability weeks

Asset confirmation, then start images and embeddings, then finalize parameters and color encoding, then the path renderer and BO with Sobol, then a basic VR loop with questionnaire output. Leave participant sessions until after the dissertation crunch.

## Blockers and risks

- The deleted indoor volumes may need re-importing from the German zip.
- Hex color handling in the optimizer is unverified.
- VR integration and the in-VR questionnaire are not started.
- Owner availability is limited for about 2 to 3 weeks (dissertation).

## What the owner does vs the assistant

- Owner (Unity and VR): asset confirmation, start points, image capture, path-renderer wiring, VR setup, running trials.
- Assistant (files): parameter and objective configuration, Sobol and multi-objective setup guidance, the embedding step, the transfer logic, log analysis and plots.
