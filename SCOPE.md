# SCOPE

The plan: what we build, how, and what counts as done. Updated 2026-07-31 after the supervisor meeting. Pairs with CONTEXT.md (background) and PROGRESS.md (status).

## 1. Objective

Use Bayesian Optimization to find the visual design of a VR wayfinding path (color, width, height, chevrons, animation) that participants rate as most aesthetic while walking fast, in two environments, and test whether the result transfers to a third unseen environment.

## 2. Research questions

1. What path appearance best trades off walking speed against aesthetic rating in a given environment (the Pareto front)?
2. For a new, unseen environment, does using the closest environment's result, or interpolating between two environments by embedding proximity, give the better path?
3. Do a VLM's aesthetic ratings agree with human ratings on the same 1 to 20 scale?

## 3. Design parameters (about 6, all 0 to 1)

1. Color (hex as one parameter if compatible, otherwise RGB as three).
2. Width (0 minimal, 1 maximum).
3. Height above the floor (0 on the floor, 1 about 2 to 3 m up).
4. Chevrons on or off.
5. Animation or movement (optional, to confirm).
6. One further visual parameter, to be decided.

Keep the count small. Each parameter raises the iteration budget in section 7.

## 4. Objectives

- Walking speed: measured while the participant walks. Not optimized directly, but recorded as an objective.
- Aesthetics: rated by the participant in VR on a 1 to 20 scale, and optionally by a VLM on the same scale.

Multi-objective, producing a Pareto front (speed vs aesthetics) per environment.

## 5. Method and pipeline

1. Confirm three environments: two indoor, one outdoor (Fantastic City). Re-check the German files; the deleted volumes may be the indoor rooms.
2. Fix one start point per environment.
3. Capture one wide-angle image at each start marker, facing the walk direction, with no overlay.
4. Embed the three images with ViT-B-32, and find the two furthest apart. Optimize on those two; the third is the transfer target.
5. Build the visual path renderer driven by the six normalized parameters.
6. In VR, the participant walks the path. Record walking speed, and show a questionnaire for the aesthetic rating.
7. Run BO with Sobol initial sampling, multi-objective, producing a Pareto front per environment. Iterations 2(d+1)+5, about 19 for 6 parameters.
8. Transfer: for the third environment, compare using the closest Pareto front against the interpolated one.

## 6. Environments

- Two indoor (different rooms or houses). Re-import the deleted ArchViz volumes if they are the needed rooms.
- One outdoor: Fantastic City.
- A held-out third environment for the transfer test.

## 7. Deliverables

- A Unity plus VR scene per environment with the path renderer, speed logging, and the in-VR questionnaire.
- Three environment embeddings and the furthest-apart pair identified.
- A Pareto front per optimized environment.
- The transfer comparison in the third environment (closest vs interpolated) as the main result, with convergence curves as supporting evidence.
- Optional: a comparison of VLM against human aesthetic ratings.

## 8. Constraints and notes

- ViT-B-32 only for now.
- Sobol sampling, not random.
- Counterbalance the environment order across participants.
- The owner has limited availability for about 2 to 3 weeks (dissertation deadline). Prioritize setup that does not need participants: asset confirmation, embeddings, parameter definition, and the Unity plus VR implementation.

## 9. Open items to confirm

1. Hex color as one parameter, or RGB as three. Verify hex handling in the optimizer.
2. The sixth parameter, and whether animation is included.
3. Whether the deleted ArchViz volumes are the two indoor environments.

## 10. References

See CONTEXT.md section 10.
