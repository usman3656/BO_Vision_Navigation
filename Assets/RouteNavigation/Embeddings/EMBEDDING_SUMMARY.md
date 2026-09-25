# ViT-B-32 embeddings of the three start images

Model: open_clip ViT-B-32 (laion2b_s34b_b79k). Each start image -> a 512-number vector (embeddings.csv).
Recomputed 2026-09-25 after re-capturing the FCG (Scene-Demo) and Vol.6 (AVP6_Desktop) start images.
NOTE: the Vol.7 start image is still the older (Sept 3) bedroom capture; re-capturing it may shift the
numbers slightly.

## Pairwise similarity (1.0 = identical, lower = more different)

- Vol6_living vs Vol7_bedroom: 0.735   (both interiors — similar)
- Vol7_bedroom vs FCG_street:  0.474
- Vol6_living  vs FCG_street:  0.445

## Reading

FCG (outdoor city) is the clear outlier: both interiors are ~0.45 from it, while the two interiors are
close to each other (0.735). So the natural design is: optimize on one interior + FCG (spanning the
space), and transfer-test on the held-out interior.

## Two valid splits (the gap between them is within noise, ~0.03)

1. **Strict "most-dissimilar pair"** -> optimize on **Vol6 + FCG** (0.445), transfer-test on **Vol7**.
2. **Practical (reuse existing data)** -> optimize on **Vol7 + FCG**, transfer-test on **Vol6**.
   Preferred if the completed Vol.7 run (P01_17) is to be used as an optimize environment.

Both put one interior + the outdoor city in the optimize set and hold out the other interior, which is
the meaningful transfer target. Recommendation: confirm the final split with Prof. Colley; option 2 keeps
the Vol.7 data already collected.
