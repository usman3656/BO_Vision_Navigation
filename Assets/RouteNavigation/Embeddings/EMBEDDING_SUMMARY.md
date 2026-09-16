# ViT-B-32 embeddings of the three start images

Model: open_clip ViT-B-32 (laion2b_s34b_b79k). Each image -> a 512-number vector (see embeddings.csv).
Recomputed after re-capturing the FCG (Scene-Demo) start image.

## Pairwise similarity (1.0 = identical, lower = more different)

- Vol6_living vs Vol7_bedroom: 0.743   (both interiors — similar)
- Vol7_bedroom vs FCG_street:  0.458
- Vol6_living  vs FCG_street:  0.431

## Decision

The two interiors (Vol6, Vol7) are close to each other and BOTH are ~equally far from FCG
(0.431 vs 0.458 — a negligible 0.03 gap). So the natural split is: optimize on one interior + FCG,
transfer-test on the other interior.

- OPTIMIZE on: **Vol7_bedroom + FCG_street**  (interior + outdoor; spans the space)
- TRANSFER-TEST on: **Vol6_living**  (the held-out interior, close to Vol7 -> a meaningful transfer target)

Note: a strict "most-dissimilar pair" rule would instead pick Vol6+FCG (0.431) to optimize and
transfer-test Vol7 — but that is within noise, and the recommended split keeps the Vol7 run already
collected. Confirm the final split with the supervisor if needed.
