# ViT-B-32 embeddings of the three start images

Model: open_clip ViT-B-32 (laion2b_s34b_b79k). Each image -> a 512-number vector (see embeddings.csv).

## Pairwise similarity (1.0 = identical, lower = more different)

- Vol6_living vs Vol7_bedroom: 0.743
- Vol7_bedroom vs FCG_street: 0.386
- Vol6_living vs FCG_street: 0.358

## Decision

- OPTIMIZE on the two most different: **Vol6_living + FCG_street** (sim 0.358).
- TRANSFER-TEST on the remaining: **Vol7_bedroom**.
