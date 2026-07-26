# Vision model notes

The route score is visual. It runs ViT-B-32 inside Unity via the Inference Engine
(Sentis, com.unity.ai.inference). These files are the model and the precomputed
text side, produced by a one-time conversion in Python. The embedding itself runs
inside Unity at run time.

## Files

- `clip_vitb32_visual.onnx` (about 335 MB, gitignored): the open_clip ViT-B-32
  vision tower, weights `laion2b_s34b_b79k`, vision tower only.
  - Output: a 512-dim image embedding, identical to open_clip `encode_image`.
  - ONNX opset 15, primitive operators only (LayerNorm is decomposed into
    ReduceMean, Sub, Pow, Sqrt, Div; GELU uses Erf). This keeps it inside the
    Inference Engine supported operator and opset range. Weights are embedded in
    the single file, so no external `.onnx.data` is needed.
- `affordance_text_embeddings.json`: two precomputed, L2-normalized 512-dim text
  embeddings, one per prompt. The text tower is not needed in Unity because of
  this precompute.
  - Prompt 0: "a clear open walkable indoor path"
  - Prompt 1: "blocked by a wall or furniture"

## Preprocessing to replicate in Unity (must match exactly)

For each captured view:
1. Resize the shortest side to 224 (bicubic, antialias).
2. Center crop to 224 by 224.
3. Scale pixels to the 0..1 range.
4. Normalize per channel: mean (0.48145466, 0.4578275, 0.40821073),
   std (0.26862954, 0.26130258, 0.27577711).
5. Feed as a tensor of shape [1, 3, 224, 224].
6. L2-normalize the 512-dim output before any cosine similarity.

A wrong preprocessing step silently degrades the embedding, so verify a few Unity
embeddings against the Python model before trusting the score.

## How to regenerate (one-time, Python 3.13 with torch, open_clip, onnx, onnxscript)

Export the vision tower with the legacy exporter so the graph stays at opset 15
with primitive ops and embedded weights:

    torch.onnx.export(model.visual, dummy_1x3x224x224, "clip_vitb32_visual.onnx",
        opset_version=15, dynamo=False,
        input_names=["pixel_values"], output_names=["image_embeds"],
        dynamic_axes={"pixel_values": {0: "batch"}, "image_embeds": {0: "batch"}})

Precompute the text embeddings with `model.encode_text(tokenizer(prompts))`, then
L2-normalize.
