# CONTEXT

Full working context. Read this first. Updated 2026-07-31 after the supervisor meeting, which changed the design.

## 1. One line

Use Bayesian Optimization to find the visual design of a VR wayfinding path (its color, width, height, chevrons, animation) that participants rate as most aesthetic while letting them walk fast. Do this in two environments, then test whether the result transfers to a third, unseen environment. A ViT embedding of each environment is the context that drives the transfer.

## 2. People

- Owner: MR BAWANI, MSc Emerging Digital Technologies, University College London.
- Supervisor: Professor Mark Colley. Provides the assets and a VR headset, and defines the study.
- Beginner in Unity, machine learning, and Bayesian Optimization, so builds are incremental and verified.

## 3. What we are really doing (corrected design)

A user in VR walks a fixed route from a start to a goal, guided by a visual path, like an arrow or line laid over the floor. We use Bayesian Optimization to find the path appearance that participants rate as most aesthetic while still walking quickly. We do this in two environments, then test whether we can predict a good path for a third, unseen environment, either by reusing the closest environment's result or by interpolating between the two.

Correction from the supervisor: the vision model does NOT judge route quality. It only produces one embedding per environment, the environment fingerprint, which feeds the contextual model for transfer. Humans rate the paths in VR. Optionally a VLM also rates them on the same 1 to 20 scale, so VLM and human judgment can be compared.

## 4. Method in brief

- Design parameters (about 6, all 0 to 1): the path's color, width, height above the floor, chevrons on or off, animation, and one more to be decided. Keep the count small, since more parameters means more iterations.
- Objectives (multi-objective): walking speed (measured) and aesthetics (rated by the participant in VR, scale 1 to 20). The optimizer produces a Pareto front per environment, the tradeoff between speed and aesthetics.
- Sampling: Sobol for the initial exploration (even spacing), then optimization rounds. Iterations follow 2(d+1) + 5, about 19 for 6 parameters.
- Environment embedding: one wide-angle image captured at the fixed start marker, facing the walk direction, with NO overlay (no path line, markers, or gizmos). ViT-B-32 embeds it. Three environments give three embeddings; the two furthest apart are the ones to optimize on.
- Transfer: for the third environment, either use the closest environment's Pareto front, or interpolate between the two based on embedding proximity. The research question is which of these works better.

## 5. Environments (3 total)

- Two indoor (different rooms or houses). The deleted ArchViz volumes (Vol.2, Vol.6) may be these; re-check the files from Germany and re-import.
- One outdoor: Fantastic City (FCG), confirmed.
- A fixed start point per environment.

## 6. Architecture and stack

- Unity 6000.5 URP, plus VR (headset provided by the supervisor).
- BO for Unity (Pascal Jansen v1.5.0): Unity launches a Python BoTorch backend over a socket. It supports multi-objective (mobo), Sobol sampling, questionnaire input, and per-environment image context embeddings via open_clip.
- Python 3.13.7 (Apple Silicon arm64), torch 2.13.0, botorch 0.18.1, gpytorch, open_clip 3.3.0, GPU (Metal, mps).
- ViT-B-32 also exported to run inside Unity via the Inference Engine (Assets/RouteNavigation/Models), available if we prefer in-engine embedding over the Python side.
- NavMesh (com.unity.ai.navigation) defines the fixed walkable route the visual path follows.

## 7. What is built, and what changed

Built and still useful:
- Unity project, BO framework merged and running end to end (verified with the demo loop).
- ViT-B-32 loads and runs inside Unity.
- NavMesh path tooling (bake, test, markers) under Assets/RouteNavigation/Editor.

Superseded by the new design (to remove or rework):
- The route quality scorer that judged smoothness and walkability with ViT (RouteVisualScorer). The vision model no longer scores routes.
- Optimizing waypoint positions as the design parameters (RouteEvaluator). The design parameters are now the path's visual appearance, and the route itself is fixed.

## 8. Hard constraints

- ViT-B-32 only for now.
- engineering_rules.md is absolute: address the owner as MR BAWANI, no em dashes, concise writing, research before building, parallel agents for real tasks, a verifier agent checks every change, token-efficient model choices.

## 9. Glossary

- Embedding: numbers representing an image, so that similar scenes give similar numbers. Here, one per environment.
- ViT / open_clip: the model that produces the embedding.
- Bayesian Optimization (BO): finds good settings in as few trials as possible.
- Sobol sampling: an even, low-discrepancy way to choose the first trial points.
- Objective: a measured outcome the optimizer cares about. Here, walking speed and aesthetics.
- Pareto front: the set of best tradeoffs between two objectives, where no point is better on both.
- Contextual model (LCEMGP): the BoTorch model that uses the per-environment embedding to relate environments and transfer.
- Chevrons: the arrow marks along a wayfinding path.
- VLM: a vision-language model that can rate an image, used here as an optional second judge alongside humans.
- Transfer (closest vs interpolated): predicting a good path for a new environment by reusing the nearest environment's result, or by blending two based on embedding distance.

## 10. References

- Supervisor: Professor Mark Colley.
- Meeting notes (Granola): https://notes.granola.ai/t/bc813e10-60bf-4992-8628-e622be6070fc-00demib2
- BO for Unity: https://github.com/Pascal-Jansen/Bayesian-Optimization-for-Unity (v1.5.0)
- Contextual model (LCEMGP): https://github.com/meta-pytorch/botorch/blob/main/botorch/models/contextual_multioutput.py
- open_clip: https://github.com/mlfoundations/open_clip
- Study materials: https://cloudstore.uni-ulm.de/s/PdHmKdkSJ8qY7Zn
