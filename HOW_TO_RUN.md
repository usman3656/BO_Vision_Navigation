# BO Vision Navigation — How to Run

A study that uses **Bayesian Optimization** to optimize the visual appearance of a wayfinding
**arrow path** (colour R, G, B, opacity, size, height) against two objectives — **walking speed**
and a human **aesthetics rating (1–10)** — producing a Pareto front per environment.
Backend: **MetaTAF** (the `openbo` fork) via BOforUnity.

## Requirements
- **Unity 6000.5.4f1** (URP).
- **Python 3.13** with torch / botorch / gpytorch and **openbo** (M‑Colley fork). The BO manager
  launches Python automatically. On a fresh machine:
  - run `Assets/StreamingAssets/BOData/Installation/MacOs/install_python.sh`
  - then `python3 -m pip install --user "open-bo @ git+https://github.com/M-Colley/openbo@main"`

## Run it — literally just Play (both environments)

Everything is built **in code** on Play (`WayfindingBootstrap`): the BO manager, the Start/Goal
markers, the arrow path, and the trial runner are all created and wired automatically. **Nothing
needs to be set up in the scene.**

**Indoor (ArchVizPRO Vol.7):**
1. Open `Assets/ArchVizPRO_Interior_Vol.7_URP/3D SCENE/ArchVizPRO_Interior_Vol.7_URP.unity`
2. Press **Play**, wait ~10–30 s for Python to start.
3. Type a **Participant ID** on the start panel (or leave the default), click **START**.
4. Walk to the **red goal** (WASD + mouse) following the arrows → rate the look **1–10** → repeat
   (~19 trials).

**Outdoor (Fantastic City Generator):**
1. Open `Assets/Fantastic City Generator/Scenes/Scene-Demo.unity`
2. Press **Play**, type a Participant ID, click **START**, walk to the goal, rate 1–10, repeat.
   (A simple desktop walker is spawned automatically since this scene has no first-person player.)

The **Condition** label (Vol7 / FCG) is set automatically per scene, so each environment's data
stays separate.

## Where the data goes
- **One master file (every trial, every participant, both environments):**
  `Assets/StreamingAssets/BOData/LogData/AllTrials_master.csv`
  — one row per trial: `Timestamp; Participant; Condition; Trial; WalkTimeSeconds; Aesthetics; R; G; B; Opacity; Size; Height`.
- Per‑run detail: `LogData/<participant>/<condition>/run/ObservationsPerEvaluation.csv`
  (plus `HypervolumePerEvaluation.csv`, `ExecutionTimes.csv`).

## Changing the route (only if you want to move Start/Goal/waypoints)
The route coordinates are hardcoded in **`Assets/RouteNavigation/Scripts/WayfindingBootstrap.cs`**
(one entry per scene: `start`, `goal`, `waypoints`). Edit them there and the change is permanent —
no scene save needed. Alternatively, place `PathStart` / `PathGoal` markers in the scene and the
bootstrap will use those instead.

The optimizer settings (6 parameters, 2 objectives, seed 42, MetaTAF) are hardcoded in
`WayfindingTrialRunner.cs` and applied on Play — no editor menus are required.

## Study design (environments)
ViT‑B‑32 image embeddings of the three start views (see `Assets/RouteNavigation/Embeddings/`) put the
two interiors close together and both far from the outdoor city. Plan: **optimize on Vol.7 + FCG**,
**transfer‑test on Vol.6** using the MetaTAF population models built from the two optimize runs.
