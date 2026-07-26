# SCOPE

The full plan for the project: what we are building, how, and what counts as done. Pairs with CONTEXT.md (background) and PROGRESS.md (status).

## 1. Objective

Build a Unity system that finds the most natural human route between two points in an environment, using Bayesian Optimization guided by ViT visual embeddings, and evaluate how this transfers across environments.

## 2. Research questions

1. Can Bayesian Optimization, guided by ViT view embeddings, find routes that match how a person would naturally walk?
2. How does the optimization behave in an indoor environment versus an outdoor one (comparison of curves)?
3. Do embeddings learned on known environments transfer to an unknown environment, so it finds good routes with little or no retraining?

## 3. Method and pipeline

1. Walkable area: bake a NavMesh in the environment so routes can only exist where a person can walk.
2. Endpoints: place a start and a goal (example: bed and kitchen).
3. Route parameters: describe a route as start, then a few waypoints, then goal. Waypoint coordinates are the parameters BO tunes.
4. Views and embeddings: place a Unity camera at points along the route, render the views, and embed each with open_clip ViT-B-32 through the Unity driven backend.
5. Route score: measure how natural or good the route is (definition is an open question, see section 6).
6. Optimization: BoTorch proposes new waypoints, the route is rescored, and it converges to the best route. Embeddings enter as context in the contextual model (LCEMGP).
7. Comparison: plot best route quality against iteration for indoor, then outdoor, and compare.
8. Transfer: apply the learned embeddings to an unknown environment and measure whether it still finds good routes.

## 4. Deliverables

- A Unity scene per environment with NavMesh, endpoints, and the optimization wired in.
- Route parameterization, camera capture, and route scoring, all inside Unity.
- ViT embeddings produced through the Unity backend.
- Logged results (CSV) and convergence curves per environment plus a comparison.
- Transfer test result on an unknown environment.
- Final visualization: the best route drawn in the 3D scene with in scene direction markers, so that from a given spot the user sees markers pointing along the best path.

## 5. Environments

- Indoor: ArchVizPRO Interior Vol.7 URP (available now).
- Outdoor: to be supplied by the supervisor later; FCG assets are present as a stand in.
- Unknown: a further environment used only for the transfer test.

## 6. Open questions to confirm with supervisor

1. Definition of best or natural route. Options: the owner rates each candidate route (human in the loop, the framework supports this), an automatic score (short, smooth, on the walkable area, visually sensible using embeddings), or a mix. This choice shapes the scoring and the transfer test, so confirm before finalising.
2. Which outdoor environment and which unknown environment will be used.

## 7. Success criteria

- The optimizer runs end to end in Unity for the indoor environment and produces a converging curve.
- The chosen route score reflects natural routes in a way the supervisor accepts.
- The comparison across environments is produced and readable.
- The transfer test gives a clear yes or no on whether embeddings help on an unknown environment.
- The best route is shown in the 3D scene with working direction markers.

## 8. Out of scope for now

- ViT-g-14 (deferred; ViT-B-32 only).
- Any pipeline that runs outside Unity.
- The outdoor and unknown environments until the supervisor provides them.

## 9. References

See CONTEXT.md section 10.
