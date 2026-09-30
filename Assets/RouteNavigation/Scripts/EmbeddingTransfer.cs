using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RouteNavigation
{
    /// <summary>
    /// Embedding-weighted transfer of the guidance appearance to a held-out environment, computed entirely
    /// in Unity (no Python step). For the transfer target (e.g. Vol.6) it builds the candidate parameter sets
    /// the participant will be tested on:
    ///
    ///   1. Read THIS participant's own runs in the two source environments (e.g. Vol.7 and FCG) from the
    ///      master CSV, and form each source's Pareto front over the two objectives (Aesthetic, EasyToFollow).
    ///   2. Read the per-environment ViT embeddings and weight the two sources by cosine similarity to the
    ///      target (closer environment counts more).
    ///   3. At three matched tradeoff levels (favour-follow, balanced, favour-aesthetic) take each source's
    ///      best point and produce four candidates: the CLOSER source's solution, the DISTANT source's
    ///      solution, the raw-average INTERPOLATION (interp_raw, kept for comparison), and the coherent
    ///      colour-space INTERPOLATION (interp_ridge).
    ///
    /// Professor's success test then reads directly from the results: does the closer source beat the distant
    /// one, and is the interpolation at least as good as the better single source.
    /// </summary>
    public static class EmbeddingTransfer
    {
        // Tradeoff weights on the Aesthetic objective (1 - lambda weights EasyToFollow):
        // 0.25 = favour easy-to-follow, 0.5 = balanced, 0.75 = favour aesthetic.
        private static readonly float[] Lambdas = { 0.25f, 0.5f, 0.75f };

        // Condition label (scene) -> row name in embeddings.csv.
        private static readonly Dictionary<string, string> EmbeddingName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Vol7", "Vol7_bedroom" },
            { "FCG",  "FCG_street" },
            { "Vol6", "Vol6_living" },
        };

        public class Candidate
        {
            public string kind;      // "closer", "distant", "interp_raw", or "interp_ridge"
            public float lambda;     // tradeoff level this candidate was formed at
            public float r, g, b, opacity, size, height;
            public float predAesthetic;   // expected Aesthetic (source value, or weighted blend)
            public float predEasy;        // expected EasyToFollow
        }

        private class Point
        {
            public float aesthetic, easy;
            public float[] p;   // 6 params in order R,G,B,Opacity,Size,Height
        }

        /// <summary>Builds the 12 candidates (3 tradeoff levels x {closer, distant, interp_raw, interp_ridge})
        /// for one participant. Returns null with an explanation in <paramref name="error"/> if source data is missing.</summary>
        public static List<Candidate> BuildCandidates(string participantId, string targetCondition, out string error)
        {
            error = null;
            string master = Path.Combine(Application.streamingAssetsPath, "BOData", "LogData", "AllTrials_master.csv");
            if (!File.Exists(master)) { error = "master CSV not found: " + master; return null; }

            // Group this participant's rows by condition.
            Dictionary<string, List<Point>> byCondition;
            try { byCondition = ReadParticipantPoints(master, participantId); }
            catch (Exception e) { error = "could not read master CSV: " + e.Message; return null; }

            // Source environments = the participant's non-target conditions that also have an embedding.
            var sources = new List<string>();
            foreach (var kv in byCondition)
            {
                if (string.Equals(kv.Key, targetCondition, StringComparison.OrdinalIgnoreCase)) continue;
                if (EmbeddingName.ContainsKey(kv.Key) && kv.Value.Count > 0) sources.Add(kv.Key);
            }
            if (sources.Count != 2)
            {
                error = $"need exactly 2 source environments with data for '{participantId}', found {sources.Count} " +
                        $"({string.Join(", ", sources)}). Run both source environments for this participant first.";
                return null;
            }

            // Embedding similarity of each source to the target -> weights.
            Dictionary<string, float[]> emb;
            try { emb = ReadEmbeddings(); }
            catch (Exception e) { error = "could not read embeddings.csv: " + e.Message; return null; }

            if (!TryEmbedding(emb, targetCondition, out float[] tVec)) { error = "no embedding for target " + targetCondition; return null; }
            if (!TryEmbedding(emb, sources[0], out float[] aVec)) { error = "no embedding for " + sources[0]; return null; }
            if (!TryEmbedding(emb, sources[1], out float[] bVec)) { error = "no embedding for " + sources[1]; return null; }

            float simA = Mathf.Max(0f, Cosine(tVec, aVec));
            float simB = Mathf.Max(0f, Cosine(tVec, bVec));
            if (simA + simB <= 1e-6f) { error = "source embeddings have zero similarity to target"; return null; }

            // closer = higher similarity.
            string closer = simA >= simB ? sources[0] : sources[1];
            string distant = simA >= simB ? sources[1] : sources[0];
            float simCloser = Mathf.Max(simA, simB);
            float simDistant = Mathf.Min(simA, simB);
            float wCloser = simCloser / (simCloser + simDistant);
            float wDistant = 1f - wCloser;

            var closerFront = ParetoFront(byCondition[closer]);
            var distantFront = ParetoFront(byCondition[distant]);

            var candidates = new List<Candidate>();
            foreach (float lambda in Lambdas)
            {
                Point pc = PickTradeoffPoint(closerFront, lambda);
                Point pd = PickTradeoffPoint(distantFront, lambda);
                if (pc == null || pd == null) continue;

                candidates.Add(MakeCandidate("closer", lambda, pc.p, pc.aesthetic, pc.easy));
                candidates.Add(MakeCandidate("distant", lambda, pd.p, pd.aesthetic, pd.easy));

                // Predicted objectives = embedding-weighted blend of the two matched points (a rough estimate).
                float predAes = wCloser * pc.aesthetic + wDistant * pd.aesthetic;
                float predEasy = wCloser * pc.easy + wDistant * pd.easy;

                // interp_raw: the OLD blend that averages the six parameters directly, kept for the before/after
                // comparison. Averaging the RGB numbers is exactly what produces the grey "mud" middle.
                var ipRaw = new float[6];
                for (int j = 0; j < 6; j++) ipRaw[j] = Mathf.Clamp01(wCloser * pc.p[j] + wDistant * pd.p[j]);
                candidates.Add(MakeCandidate("interp_raw", lambda, ipRaw, predAes, predEasy));

                // interp_ridge: the coherent blend, mix the COLOUR in HSV (so blue+orange never collapses to
                // grey) and average only the scalar knobs (opacity, size, height). Stays a real, plausible arrow.
                var ipRidge = RidgeBlend(pc.p, pd.p, wCloser);
                candidates.Add(MakeCandidate("interp_ridge", lambda, ipRidge, predAes, predEasy));
            }

            if (candidates.Count == 0) { error = "no candidates could be formed (empty source fronts)"; return null; }
            Debug.Log($"[Transfer] {participantId}: closer={closer} (w={wCloser:F2}), distant={distant} (w={wDistant:F2}); " +
                      $"{candidates.Count} candidates.");
            return candidates;
        }

        private static Candidate MakeCandidate(string kind, float lambda, float[] p, float predAesth, float predEasy)
        {
            return new Candidate
            {
                kind = kind, lambda = lambda,
                r = p[0], g = p[1], b = p[2], opacity = p[3], size = p[4], height = p[5],
                predAesthetic = predAesth, predEasy = predEasy,
            };
        }

        /// <summary>Coherent "ridge" blend: mix the COLOUR in HSV (hue on the shortest arc, so blue + orange
        /// never collapses to grey mud) and average only the scalar knobs (opacity, size, height) linearly.
        /// Keeps the blended arrow a real, plausible setting instead of a valley point between two hilltops.
        /// wa is the weight on the CLOSER source's parameters <paramref name="a"/>.</summary>
        private static float[] RidgeBlend(float[] a, float[] b, float wa)
        {
            float wb = 1f - wa;
            Color.RGBToHSV(new Color(Mathf.Clamp01(a[0]), Mathf.Clamp01(a[1]), Mathf.Clamp01(a[2])), out float ha, out float sa, out float va);
            Color.RGBToHSV(new Color(Mathf.Clamp01(b[0]), Mathf.Clamp01(b[1]), Mathf.Clamp01(b[2])), out float hb, out float sb, out float vb);
            float h = BlendHueShortest(ha, hb, wa);
            float s = wa * sa + wb * sb;
            float v = wa * va + wb * vb;
            Color m = Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), Mathf.Clamp01(v));
            return new float[]
            {
                Mathf.Clamp01(m.r), Mathf.Clamp01(m.g), Mathf.Clamp01(m.b),
                Mathf.Clamp01(wa * a[3] + wb * b[3]),   // opacity
                Mathf.Clamp01(wa * a[4] + wb * b[4]),   // size
                Mathf.Clamp01(wa * a[5] + wb * b[5]),   // height
            };
        }

        /// <summary>Interpolate hue around the colour wheel along the SHORTER arc; result weighted wa toward h1.</summary>
        private static float BlendHueShortest(float h1, float h2, float wa)
        {
            float diff = h2 - h1;
            if (diff > 0.5f) diff -= 1f; else if (diff < -0.5f) diff += 1f;
            return Mathf.Repeat(h1 + (1f - wa) * diff, 1f);
        }

        // --- data reading -----------------------------------------------------

        private static Dictionary<string, List<Point>> ReadParticipantPoints(string masterPath, string participantId)
        {
            var lines = File.ReadAllLines(masterPath);
            var result = new Dictionary<string, List<Point>>(StringComparer.OrdinalIgnoreCase);
            if (lines.Length < 2) return result;

            string[] header = lines[0].Split(';');
            int iPart = IndexOf(header, "Participant");
            int iCond = IndexOf(header, "Condition");
            int iAes = IndexOf(header, "Aesthetic");
            int iEasy = IndexOf(header, "EasyToFollow");
            int iR = IndexOf(header, "R"), iG = IndexOf(header, "G"), iB = IndexOf(header, "B");
            int iOp = IndexOf(header, "Opacity"), iSz = IndexOf(header, "Size"), iHt = IndexOf(header, "Height");
            int need = Mathf.Max(iPart, Mathf.Max(iCond, Mathf.Max(iAes, Mathf.Max(iEasy, Mathf.Max(iR, Mathf.Max(iG, Mathf.Max(iB, Mathf.Max(iOp, Mathf.Max(iSz, iHt)))))))));
            if (iPart < 0 || iCond < 0 || iAes < 0 || iEasy < 0 || iR < 0 || iG < 0 || iB < 0 || iOp < 0 || iSz < 0 || iHt < 0)
                throw new FormatException("master CSV header missing required columns (expected " + WayfindingTrialRunner.MasterHeader + ")");

            var inv = CultureInfo.InvariantCulture;
            for (int r = 1; r < lines.Length; r++)
            {
                if (string.IsNullOrWhiteSpace(lines[r])) continue;
                string[] c = lines[r].Split(';');
                if (c.Length <= need) continue;
                if (!string.Equals(c[iPart].Trim(), participantId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryF(c[iAes], inv, out float aes) || !TryF(c[iEasy], inv, out float easy)) continue;
                var p = new float[6];
                if (!TryF(c[iR], inv, out p[0]) || !TryF(c[iG], inv, out p[1]) || !TryF(c[iB], inv, out p[2]) ||
                    !TryF(c[iOp], inv, out p[3]) || !TryF(c[iSz], inv, out p[4]) || !TryF(c[iHt], inv, out p[5])) continue;

                string cond = c[iCond].Trim();
                if (!result.TryGetValue(cond, out var list)) { list = new List<Point>(); result[cond] = list; }
                list.Add(new Point { aesthetic = aes, easy = easy, p = p });
            }
            return result;
        }

        private static Dictionary<string, float[]> ReadEmbeddings()
        {
            string path = Path.Combine(Application.dataPath, "RouteNavigation", "Embeddings", "embeddings.csv");
            var result = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(path);
            var inv = CultureInfo.InvariantCulture;
            for (int r = 1; r < lines.Length; r++)   // row 0 = header
            {
                if (string.IsNullOrWhiteSpace(lines[r])) continue;
                string[] c = lines[r].Split(',');
                if (c.Length < 2) continue;
                var v = new float[c.Length - 1];
                for (int j = 1; j < c.Length; j++) TryF(c[j], inv, out v[j - 1]);
                result[c[0].Trim()] = v;
            }
            return result;
        }

        private static bool TryEmbedding(Dictionary<string, float[]> emb, string condition, out float[] vec)
        {
            vec = null;
            return EmbeddingName.TryGetValue(condition, out string name) && emb.TryGetValue(name, out vec) && vec.Length > 0;
        }

        // --- math -------------------------------------------------------------

        private static float Cosine(float[] a, float[] b)
        {
            int n = Mathf.Min(a.Length, b.Length);
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < n; i++) { dot += (double)a[i] * b[i]; na += (double)a[i] * a[i]; nb += (double)b[i] * b[i]; }
            if (na <= 0 || nb <= 0) return 0f;
            return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
        }

        /// <summary>Non-dominated points, maximising BOTH aesthetic and easy.</summary>
        private static List<Point> ParetoFront(List<Point> pts)
        {
            var front = new List<Point>();
            for (int i = 0; i < pts.Count; i++)
            {
                bool dominated = false;
                for (int j = 0; j < pts.Count; j++)
                {
                    if (j == i) continue;
                    Point q = pts[j], p = pts[i];
                    if (q.aesthetic >= p.aesthetic && q.easy >= p.easy && (q.aesthetic > p.aesthetic || q.easy > p.easy))
                    { dominated = true; break; }
                }
                if (!dominated) front.Add(pts[i]);
            }
            return front.Count > 0 ? front : pts;   // if everything ties, keep all
        }

        /// <summary>Point on the front that best satisfies a tradeoff weight lambda on Aesthetic
        /// (1-lambda on EasyToFollow), after min-max normalising each objective within the front.</summary>
        private static Point PickTradeoffPoint(List<Point> front, float lambda)
        {
            if (front == null || front.Count == 0) return null;
            float aMin = float.MaxValue, aMax = float.MinValue, eMin = float.MaxValue, eMax = float.MinValue;
            foreach (var p in front)
            {
                aMin = Mathf.Min(aMin, p.aesthetic); aMax = Mathf.Max(aMax, p.aesthetic);
                eMin = Mathf.Min(eMin, p.easy);      eMax = Mathf.Max(eMax, p.easy);
            }
            Point best = null; float bestScore = float.MinValue;
            foreach (var p in front)
            {
                float an = aMax > aMin ? (p.aesthetic - aMin) / (aMax - aMin) : 0.5f;
                float en = eMax > eMin ? (p.easy - eMin) / (eMax - eMin) : 0.5f;
                float score = lambda * an + (1f - lambda) * en;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        private static int IndexOf(string[] header, string name)
        {
            for (int i = 0; i < header.Length; i++)
                if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static bool TryF(string s, IFormatProvider inv, out float v)
            => float.TryParse((s ?? "").Trim(), NumberStyles.Float, inv, out v);
    }
}
