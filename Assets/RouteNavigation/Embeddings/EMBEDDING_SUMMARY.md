# ViT-B-32 embeddings of the three start images

Model: open_clip ViT-B-32 (laion2b_s34b_b79k). Each image -> a 512-number vector (see embeddings.csv).
Recomputed after re-capturing the FCG (Scene-Demo) start image.

## Pairwise similarity (1.0 = identical, lower = more different)

- Vol6_living vs Vol7_bedroom: 0.735
- Vol7_bedroom vs FCG_street: 0.478
- Vol6_living vs FCG_street: 0.510

## Decision

- OPTIMIZE on the two most different: **Vol7_bedroom + FCG_street** (sim 0.478).
- TRANSFER-TEST on the remaining: **Vol6_living**.
