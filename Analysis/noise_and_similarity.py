# Reproducible answers to Prof. Colley's two "send me first" asks:
#   1) rating noise estimate, 2) the two embedding similarities (Vol6 vs each source).
# Run: python3 Analysis/noise_and_similarity.py   (from the project root)
import csv, math, os
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
T = os.path.join(ROOT, "Assets/StreamingAssets/BOData/LogData/TransferValidation.csv")
E = os.path.join(ROOT, "Assets/RouteNavigation/Embeddings/embeddings.csv")

# ---------- 1) rating noise ----------
# The "closer" and "distant" candidates are DETERMINISTIC fixed appearances (same params each run),
# and they were shown across two separate Vol.6 sessions. Each (participant, kind, lambda) therefore
# has repeated ratings of an identical arrow -> a direct estimate of per-rating noise.
rows = list(csv.DictReader(open(T), delimiter=';'))
groups = {}
for r in rows:
    if r["Kind"] not in ("closer", "distant"):
        continue
    key = (r["Participant"], r["Kind"], r["Lambda"])
    groups.setdefault(key, {"aes": [], "easy": [], "p": set()})
    groups[key]["aes"].append(float(r["ActualAesthetic"]))
    groups[key]["easy"].append(float(r["ActualEasyToFollow"]))
    groups[key]["p"].add(tuple(round(float(r[c]), 3) for c in ("R", "G", "B", "Opacity", "Size", "Height")))

def pooled_sd(values_per_group):
    # within-group variance pooled across groups that have >=2 reps of an identical stimulus
    num = 0.0; dof = 0
    for v in values_per_group:
        if len(v) < 2: continue
        m = sum(v) / len(v)
        num += sum((x - m) ** 2 for x in v); dof += len(v) - 1
    return math.sqrt(num / dof) if dof > 0 else float("nan"), dof

reps = [g for g in groups.values() if len(g["aes"]) >= 2]
# sanity: params identical within each repeated group
mixed = sum(1 for g in reps if len(g["p"]) > 1)
sd_aes, dof_a = pooled_sd([g["aes"] for g in reps])
sd_easy, dof_e = pooled_sd([g["easy"] for g in reps])
print("=== 1) RATING NOISE (repeats of identical fixed appearances) ===")
print(f"  repeated fixed-appearance groups: {len(reps)} (params identical within group: {len(reps)-mixed}/{len(reps)})")
print(f"  Aesthetic:    pooled SD = {sd_aes:.2f} points (on the 1-10 scale)")
print(f"  EasyToFollow: pooled SD = {sd_easy:.2f} points")
print("  NOTE: this is from 2 repeats each. Prof's proper protocol = 1 fixed appearance x5 reps per environment.")

# ---------- 2) embedding similarities ----------
emb = {}
for r in csv.reader(open(E)):
    if r[0] == "image" or not r: continue
    emb[r[0]] = np.array([float(x) for x in r[1:]], dtype=float)

def cos(a, b):
    return float(np.dot(emb[a], emb[b]) / (np.linalg.norm(emb[a]) * np.linalg.norm(emb[b])))

print("\n=== 2) EMBEDDING SIMILARITIES (ViT-B-32, cosine) ===")
print(f"  Vol6 vs Vol7 (closer):  {cos('Vol6_living','Vol7_bedroom'):.3f}")
print(f"  Vol6 vs FCG  (distant): {cos('Vol6_living','FCG_street'):.3f}")
print(f"  Vol7 vs FCG:            {cos('Vol7_bedroom','FCG_street'):.3f}")
sa, sb = cos('Vol6_living','Vol7_bedroom'), cos('Vol6_living','FCG_street')
print(f"  -> interpolation weights: Vol7 {sa/(sa+sb):.2f} / FCG {sb/(sa+sb):.2f}")
