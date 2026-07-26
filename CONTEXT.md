# CONTEXT

Full working context for the BO_Vision_Navigation project. Read this first to understand what the project is, how it is built, and where it stands.

## 1. One line

Find the most natural human route between two points in a Unity environment using Bayesian Optimization guided by ViT visual embeddings, compare environments, then test whether the learned embeddings transfer to an unseen environment.

## 2. People and framing

- Owner: MR BAWANI (UCL masters project).
- Supervisor gives the assets and the environments (indoor now, outdoor later) and defines the study intent.
- Beginner in Unity, machine learning, and Bayesian Optimization, so the build is incremental and each step is verified.

## 3. Research goal

Given a start and a goal in an environment (example: bed to kitchen), find the route a person would naturally walk, not just the shortest line. Do this indoors first, outdoors later, compare the two, then test on an unknown environment to see whether the ViT embeddings still work (transfer). The ViT embedding is the visual context that makes cross environment transfer possible. This is why the supervisor specified the contextual BO variant.

## 4. Method in brief

- The route is described by a small number of waypoints. Their coordinates are the parameters the optimizer tunes.
- Bayesian Optimization (BoTorch) proposes waypoint sets, the route quality is scored, and the optimizer converges to the best route.
- At points along the route a Unity camera renders the view, and open_clip ViT-B-32 turns each view into an embedding vector. These embeddings act as the context in a contextual multi task Gaussian Process (BoTorch LCEMGP).
- Two environments become two contexts, which lets the model share learning and later generalise to an unknown environment.
- Outputs are convergence curves per environment and a comparison, plus the transfer test.

## 5. Architecture and stack

- Unity 6000.5 (editor 6000.5.4f1), Universal Render Pipeline (URP).
- Bayesian Optimization for Unity, Pascal Jansen framework v1.5.0. Unity launches a Python backend and talks to it over a local socket. BoTorch does the optimization. v1.5.0 already supports image based context embeddings via open_clip, so embeddings run inside this Unity driven pipeline.
- Python 3.13.7 (Apple Silicon arm64) at `/Library/Frameworks/Python.framework/Versions/3.13/bin/python3`, with torch 2.13.0, botorch 0.18.1, gpytorch, open_clip 3.3.0. GPU acceleration is available (Metal, mps).
- NavMesh (com.unity.ai.navigation) is already in the project and will define walkable area for routes.

## 6. Repository layout (key items)

- `Assets/ArchVizPRO_Interior_Vol.7_URP/` indoor environment (URP, renders correctly).
- `Assets/FCG/` Fantastic City Generator assets (outdoor style; the supervisor will supply the final outdoor environment later).
- `Assets/BOforUnity/` the optimization engine, scenes, prefabs, scripts (merged in from the framework).
- `Assets/QuestionnaireToolkit/` supports human rating mode if used.
- `Assets/StreamingAssets/BOData/` the Python backend, installer, and log output.
- `Assets/TextMesh Pro/` UI text support needed by the toolkit.
- `engineering_rules.md` binding project rules (see below).
- `CONTEXT.md`, `SCOPE.md`, `PROGRESS.md` this documentation set.

Large asset folders are excluded from git and kept local. GitHub remote: usman3656/BO_Vision_Navigation.

## 7. Hard constraints

- Everything runs inside Unity. No standalone side pipelines. Embeddings and results are produced and shown through the Unity driven flow.
- ViT-B-32 only for now. The larger ViT-g-14 is deferred.
- `engineering_rules.md` is absolute: address the owner as MR BAWANI, no em dashes, concise writing, research before building, parallel agents for real tasks, a separate verifier agent checks every change, token efficient model choices. New instructions get appended to that file and are equally binding.

## 8. Key decisions made

- Merge direction: the BO engine was merged into the existing URP project, not the reverse, because the framework project used the built in renderer while the environments and the working project are URP. This avoided a fragile render pipeline conversion.
- Standalone Python embedding scripts were built early, then removed, because they violated the everything on Unity rule.

## 9. Glossary

- Embedding: a list of numbers that represents an image, so that similar views give similar numbers.
- ViT (Vision Transformer): the model (via open_clip) that produces the embedding.
- Bayesian Optimization (BO): a method that finds the best settings of a few parameters using as few trials as possible.
- BoTorch: the Python library that runs the BO.
- LCEMGP: the contextual multi task model in BoTorch that lets context (the embeddings) transfer knowledge between environments.
- NavMesh: Unity data describing where an agent can walk.
- Convergence curve: best route quality plotted against optimization iteration.

## 10. References

- BO for Unity: https://github.com/Pascal-Jansen/Bayesian-Optimization-for-Unity (release v1.5.0)
- Contextual model (LCEMGP): https://github.com/meta-pytorch/botorch/blob/main/botorch/models/contextual_multioutput.py
- open_clip: https://github.com/mlfoundations/open_clip
- Indoor asset: https://assetstore.unity.com/packages/3d/environments/urban/archvizpro-interior-vol-7-urp-226477
- Outdoor style asset: https://assetstore.unity.com/packages/3d/environments/urban/fantastic-city-generator-157625
- Study materials: https://cloudstore.uni-ulm.de/s/PdHmKdkSJ8qY7Zn
