import csv, os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

MASTER = "/Users/bawani/Windows/masters1/unity/BO_Vision_Navigation/Assets/StreamingAssets/BOData/LogData/AllTrials_master.csv"
OUT = "/Users/bawani/Windows/masters1/unity/BO_Vision_Navigation/Pareto_fronts.png"

rows = []
with open(MASTER) as f:
    for r in csv.DictReader(f, delimiter=';'):
        if r.get("Participant", "").strip() != "P01":   # real participant only
            continue
        try:
            rows.append((r["Condition"].strip(), float(r["WalkTimeSeconds"]), float(r["Aesthetics"]),
                         float(r["R"]), float(r["G"]), float(r["B"]), float(r["Opacity"]), float(r["Size"]), float(r["Height"])))
        except (ValueError, KeyError):
            pass

conds = ["Vol7", "FCG", "Vol6"]
by = {c: [x for x in rows if x[0] == c] for c in conds}

def pareto(points):
    # objective: minimize time (idx1), maximize aesthetics (idx2)
    front = []
    for i, p in enumerate(points):
        dominated = False
        for j, q in enumerate(points):
            if j == i: continue
            if q[1] <= p[1] and q[2] >= p[2] and (q[1] < p[1] or q[2] > p[2]):
                dominated = True; break
        if not dominated: front.append(p)
    # unique + sort by time
    seen = set(); uniq = []
    for p in sorted(front, key=lambda z: (z[1], -z[2])):
        k = (round(p[1], 2), round(p[2], 2))
        if k not in seen: seen.add(k); uniq.append(p)
    return uniq

fig, axes = plt.subplots(1, 3, figsize=(16, 5))
colors = {"Vol7": "#2c7fb8", "FCG": "#d95f0e", "Vol6": "#31a354"}
for ax, c in zip(axes, conds):
    pts = by[c]
    ax.set_title(f"{c}  (n={len(pts)})", fontsize=13, fontweight="bold")
    ax.set_xlabel("Walk time (s)  ← better")
    ax.set_ylabel("Aesthetics (1-10)  better →")
    ax.set_ylim(0.5, 10.5)
    ax.grid(True, alpha=0.3)
    if not pts:
        ax.text(0.5, 0.5, "no data", transform=ax.transAxes, ha="center"); continue
    ax.scatter([p[1] for p in pts], [p[2] for p in pts], s=35, c="#bbbbbb", label="all trials", zorder=2)
    front = pareto(pts)
    fx = [p[1] for p in front]; fy = [p[2] for p in front]
    ax.plot(fx, fy, "-o", color=colors[c], lw=2, ms=8, label="Pareto front", zorder=3)
    print(f"\n=== {c}: {len(pts)} trials, {len(front)} Pareto-optimal ===")
    for p in front:
        print(f"  time={p[1]:5.1f}s  aesth={p[2]:.0f}  RGB=({p[3]:.2f},{p[4]:.2f},{p[5]:.2f}) op={p[6]:.2f} size={p[7]:.2f} h={p[8]:.2f}")
    ax.legend(loc="lower right", fontsize=9)

fig.suptitle("Pareto fronts: walking speed vs. aesthetics (participant P01)", fontsize=14, fontweight="bold")
fig.tight_layout(rect=[0, 0, 1, 0.96])
fig.savefig(OUT, dpi=130)
print(f"\nSaved {OUT}")
