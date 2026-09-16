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

## Run it (one click)
1. Open the project in Unity.
2. Open the scene: `Assets/ArchVizPRO_Interior_Vol.7_URP/3D SCENE/ArchVizPRO_Interior_Vol.7_URP.unity`
3. In the Hierarchy select **TrialRunner** → set **Participant Id** (and **Condition Id**, e.g. `Vol7`).
4. Press **Play**, wait ~10–30 s for Python, then click **START**.
5. Walk to the **red goal** (WASD + mouse) following the arrows → rate the look **1–10** → repeat
   (~19 trials). The whole thing self-configures on Play — no editor menus needed.

## Where the data goes
- **One master file:** `Assets/StreamingAssets/BOData/LogData/AllTrials_master.csv`
  — one row per trial: `Timestamp; Participant; Condition; Trial; WalkTimeSeconds; Aesthetics; R; G; B; Opacity; Size; Height`.
- Per‑run detail: `LogData/<participant>/<condition>/run/ObservationsPerEvaluation.csv`
  (plus `HypervolumePerEvaluation.csv`, `ExecutionTimes.csv`).

## Editor tools (only needed to change the route, under **Tools > BO Route**)
- **Set Path Start Here / Set Path Goal Here** — drop the start/goal markers at the Scene‑view focus.
- **Add Path Waypoint Here** — drop a waypoint along the route.
- **Snap ALL Path Points to Ground** — drop every point onto its floor.
- **Build Wayfinding Test Objects** — (re)create + wire GuidancePath and TrialRunner.
- **Hide BO Manager UI** — turn off the BO tool's canvas.

The route is a set of hand‑placed waypoints (green Start, red Goal, cyan waypoints) that the
arrows follow; only the arrows' appearance changes each trial.
