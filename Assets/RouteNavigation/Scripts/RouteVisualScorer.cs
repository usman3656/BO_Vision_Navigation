using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.InferenceEngine;
using UnityEngine;

namespace RouteNavigation
{
    /// <summary>
    /// Scores a route VISUALLY by running ViT-B-32 (open_clip, laion2b_s34b_b79k)
    /// inside Unity via the Inference Engine.
    ///
    /// For a route (given as NavMesh corners), it samples N viewpoints along the
    /// path, renders each with a dedicated capture camera, embeds each view with
    /// the vision model, and combines the embeddings into one score:
    ///   score = coherenceWeight * (mean cosine similarity of consecutive views)
    ///         + affordanceWeight * (mean of  cos(view, "walkable") - cos(view, "blocked") )
    /// Higher is more natural. The BO objective must be set to maximize (smallerIsBetter = false).
    ///
    /// Preprocessing matches MODEL_NOTES.md exactly: 224x224, scale to 0..1,
    /// per-channel CLIP mean/std, L2-normalize the output embedding.
    /// </summary>
    public class RouteVisualScorer : MonoBehaviour
    {
        [Header("Model and data (assign in inspector)")]
        [Tooltip("clip_vitb32_visual.onnx imported as a Model Asset.")]
        public ModelAsset visionModel;
        [Tooltip("affordance_text_embeddings.json imported as a Text Asset.")]
        public TextAsset affordanceEmbeddings;

        [Header("Capture")]
        [Tooltip("A dedicated camera, normally disabled, used only to snapshot the route.")]
        public Camera captureCamera;
        public int resolution = 224;
        [Tooltip("Camera height above the walkable surface, in metres.")]
        public float eyeHeight = 1.6f;
        [Tooltip("How many views are sampled evenly along the route.")]
        public int sampleCount = 8;

        [Header("Score weights")]
        public float coherenceWeight = 1f;
        public float affordanceWeight = 1f;

        // CLIP normalization constants (see MODEL_NOTES.md).
        private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
        private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

        private Worker _worker;
        private float[] _walkable;   // 512-dim, L2-normalized
        private float[] _blocked;    // 512-dim, L2-normalized
        private RenderTexture _rt;
        private Texture2D _readTex;
        private bool _ready;

        // Parsed by Newtonsoft only (not Unity serialization), so no [Serializable] needed.
        private class AffordanceJson { public List<List<float>> embeddings; }

        private void Awake()
        {
            if (visionModel == null || affordanceEmbeddings == null || captureCamera == null)
            {
                Debug.LogError("RouteVisualScorer: assign visionModel, affordanceEmbeddings, and captureCamera.");
                return;
            }

            _worker = new Worker(ModelLoader.Load(visionModel), BackendType.GPUCompute);

            var data = JsonConvert.DeserializeObject<AffordanceJson>(affordanceEmbeddings.text);
            if (data == null || data.embeddings == null || data.embeddings.Count < 2)
            {
                Debug.LogError("RouteVisualScorer: affordance embeddings file is malformed.");
                return;
            }
            _walkable = data.embeddings[0].ToArray();
            _blocked = data.embeddings[1].ToArray();

            _rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            _readTex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            captureCamera.enabled = false;
            _ready = true;
        }

        private void OnDestroy()
        {
            _worker?.Dispose();
            if (_rt != null) _rt.Release();
            if (_readTex != null) Destroy(_readTex);
        }

        /// <summary>
        /// Coroutine. Captures views along the route, scores it, and returns the score via onScore.
        /// </summary>
        public IEnumerator ScoreRoute(List<Vector3> corners, Action<float> onScore)
        {
            if (!_ready || corners == null || corners.Count < 2)
            {
                onScore?.Invoke(0f);
                yield break;
            }

            List<ViewSample> samples = SampleAlong(corners, sampleCount, eyeHeight);
            var embeddings = new List<float[]>(samples.Count);

            captureCamera.targetTexture = _rt;
            foreach (var s in samples)
            {
                captureCamera.transform.SetPositionAndRotation(
                    s.Position, Quaternion.LookRotation(s.Forward, Vector3.up));
                captureCamera.enabled = true;
                yield return new WaitForEndOfFrame(); // let URP render this camera into _rt
                captureCamera.enabled = false;

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _readTex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                _readTex.Apply();
                RenderTexture.active = prev;

                embeddings.Add(Embed(_readTex));
            }
            captureCamera.targetTexture = null;

            onScore?.Invoke(ComputeVisualScore(embeddings));
        }

        // --- Embedding ---------------------------------------------------------

        /// <summary>Preprocesses a 224x224 view and runs the vision model, returning an L2-normalized embedding.</summary>
        private float[] Embed(Texture2D tex)
        {
            int n = resolution;
            Color[] pixels = tex.GetPixels(); // 0..1, row 0 = bottom
            var chw = new float[3 * n * n];
            for (int y = 0; y < n; y++)
            {
                int srcRow = (n - 1 - y) * n; // flip so row 0 = top, matching PIL
                int dstRow = y * n;
                for (int x = 0; x < n; x++)
                {
                    Color c = pixels[srcRow + x];
                    int p = dstRow + x;
                    chw[0 * n * n + p] = (c.r - Mean[0]) / Std[0];
                    chw[1 * n * n + p] = (c.g - Mean[1]) / Std[1];
                    chw[2 * n * n + p] = (c.b - Mean[2]) / Std[2];
                }
            }

            using var input = new Tensor<float>(new TensorShape(1, 3, n, n), chw);
            _worker.Schedule(input);
            if (_worker.PeekOutput() is not Tensor<float> output)
            {
                Debug.LogError("RouteVisualScorer: model output is not a float tensor.");
                return new float[512];
            }
            float[] emb = output.DownloadToArray(); // synchronous readback, fine for offline BO
            L2Normalize(emb);
            return emb;
        }

        // --- Scoring -----------------------------------------------------------

        private float ComputeVisualScore(List<float[]> embeddings)
        {
            if (embeddings.Count == 0)
                return 0f;

            float coherence = 1f;
            if (embeddings.Count > 1)
            {
                float sum = 0f;
                for (int i = 1; i < embeddings.Count; i++)
                    sum += Dot(embeddings[i - 1], embeddings[i]);
                coherence = sum / (embeddings.Count - 1);
            }

            float affordance = 0f;
            foreach (var e in embeddings)
                affordance += Dot(e, _walkable) - Dot(e, _blocked);
            affordance /= embeddings.Count;

            return coherenceWeight * coherence + affordanceWeight * affordance;
        }

        // --- Sampling ----------------------------------------------------------

        private struct ViewSample
        {
            public Vector3 Position;
            public Vector3 Forward;
        }

        /// <summary>Evenly spaced samples along the polyline of corners, with a level facing direction.</summary>
        private static List<ViewSample> SampleAlong(List<Vector3> corners, int count, float eye)
        {
            var result = new List<ViewSample>();
            var segLen = new List<float>();
            float total = 0f;
            for (int i = 1; i < corners.Count; i++)
            {
                float l = Vector3.Distance(corners[i - 1], corners[i]);
                segLen.Add(l);
                total += l;
            }

            if (total <= 1e-4f || count < 1)
            {
                result.Add(new ViewSample { Position = corners[0] + Vector3.up * eye, Forward = Vector3.forward });
                return result;
            }

            for (int k = 0; k < count; k++)
            {
                float t = count == 1 ? 0f : k * (total / (count - 1));
                float acc = 0f;
                int si = 0;
                while (si < segLen.Count - 1 && acc + segLen[si] < t)
                {
                    acc += segLen[si];
                    si++;
                }
                float frac = segLen[si] > 1e-6f ? (t - acc) / segLen[si] : 0f;
                Vector3 pos = Vector3.Lerp(corners[si], corners[si + 1], frac) + Vector3.up * eye;
                Vector3 fwd = corners[si + 1] - corners[si];
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                result.Add(new ViewSample { Position = pos, Forward = fwd });
            }
            return result;
        }

        // --- Vector helpers ----------------------------------------------------

        private static float Dot(float[] a, float[] b)
        {
            float s = 0f;
            int n = Mathf.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) s += a[i] * b[i];
            return s;
        }

        private static void L2Normalize(float[] v)
        {
            float s = 0f;
            for (int i = 0; i < v.Length; i++) s += v[i] * v[i];
            float norm = Mathf.Sqrt(s);
            if (norm > 1e-8f)
                for (int i = 0; i < v.Length; i++) v[i] /= norm;
        }
    }
}
