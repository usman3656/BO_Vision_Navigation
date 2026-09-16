using System.IO;
using UnityEditor;
using UnityEngine;

namespace RouteNavigation.EditorTools
{
    /// <summary>
    /// Captures one clean, wide-angle start image AND records the start pose, so the path Start
    /// stays linked to the exact spot the photo was taken from.
    ///
    /// Two ways to aim it:
    ///   EDIT MODE - fly the Scene view to the start viewpoint, run the menu. It saves the image,
    ///               the pose, and immediately drops a green "PathStart" marker.
    ///   PLAY MODE - press Play, walk in first person to the spot, run the menu. It saves the image
    ///               and the pose. Because Play-mode objects vanish on stop, you then STOP Play and
    ///               run "Place Start Marker" to drop the marker from the saved pose.
    ///
    /// Menus:  Tools > BO Route > Capture Start Image   and   Tools > BO Route > Place Start Marker.
    /// Output: Assets/RouteNavigation/StartImages/<sceneName>_start.png  (+ _start.pose.json).
    /// </summary>
    public static class StartImageCapture
    {
        private const int MaxWidth = 1920;  // cap the width; the image keeps the view's aspect ratio
        private const float EyeHeight = 1.6f;
        private const string Dir = "Assets/RouteNavigation/StartImages";

        [System.Serializable]
        private class StartPose { public Vector3 position; public Vector3 forward; }

        [MenuItem("Tools/BO Route/Capture Start Image")]
        public static void Capture()
        {
            Camera main = ResolveCamera();
            if (main == null) { Debug.LogError("[Start] No camera in the scene to render with."); return; }

            bool playing = Application.isPlaying;
            SceneView sv = null;
            Camera svCam = null;
            if (!playing)
            {
                sv = SceneView.lastActiveSceneView;
                if (sv == null || sv.camera == null)
                {
                    Debug.LogError("[Start] Open a Scene view and frame the start viewpoint, or press Play and walk to the spot first.");
                    return;
                }
                svCam = sv.camera;
            }

            Vector3 pos = main.transform.position;
            Quaternion rot = main.transform.rotation;
            float fov = main.fieldOfView;
            RenderTexture prevTarget = main.targetTexture;

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene)) scene = "Untitled";

            // Match the framing you see: use the Scene view camera's FOV + aspect (or the Game screen in
            // Play mode), instead of a forced wide square that made everything look small and centred.
            int w, h; float useFov;
            if (!playing) { w = svCam.pixelWidth; h = svCam.pixelHeight; useFov = svCam.fieldOfView; }
            else { w = Screen.width; h = Screen.height; useFov = main.fieldOfView; }
            if (w < 16 || h < 16) { w = 1600; h = 900; }
            if (w > MaxWidth) { float s = MaxWidth / (float)w; w = MaxWidth; h = Mathf.Max(16, Mathf.RoundToInt(h * s)); }

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D tex = null;
            try
            {
                if (!playing)
                {
                    // Reconstruct the CURRENT Scene view viewpoint from its pivot/rotation/distance.
                    // (sv.camera.transform is stale when read from a menu callback, which captured the wrong spot.)
                    Vector3 camPos = sv.pivot - (sv.rotation * Vector3.forward) * sv.cameraDistance;
                    main.transform.SetPositionAndRotation(camPos, sv.rotation);
                }
                main.fieldOfView = useFov;
                main.targetTexture = rt;
                main.Render();

                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                Directory.CreateDirectory(Dir);
                string imgPath = Path.Combine(Dir, scene + "_start.png");
                File.WriteAllBytes(imgPath, tex.EncodeToPNG());

                // Save the start pose (floor-projected) so the Start point stays linked to the photo.
                StartPose sp = ComputeStartPose(main.transform.position, main.transform.forward);
                File.WriteAllText(Path.Combine(Dir, scene + "_start.pose.json"), JsonUtility.ToJson(sp));
                AssetDatabase.Refresh();

                // NOTE: capturing no longer moves or creates PathStart. The start image is taken independently,
                // so an already-placed PathStart is never disturbed. Use "Place Start Marker" only if you
                // deliberately want to drop PathStart at the captured pose.
                Debug.Log($"[Start] Saved {imgPath} and the start pose. PathStart was left untouched.");
            }
            finally
            {
                if (!playing)
                    main.transform.SetPositionAndRotation(pos, rot);
                main.fieldOfView = fov;
                main.targetTexture = prevTarget;
                if (rt != null) rt.Release();
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }

        [MenuItem("Tools/BO Route/Place Start Marker")]
        public static void PlaceStartMarker()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[Start] Stop Play first, then run this in edit mode so the marker persists.");
                return;
            }
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string posePath = Path.Combine(Dir, scene + "_start.pose.json");
            if (!File.Exists(posePath))
            {
                Debug.LogError($"[Start] No saved start pose for this scene. Capture a start image here first. Looked for: {posePath}");
                return;
            }
            var sp = JsonUtility.FromJson<StartPose>(File.ReadAllText(posePath));
            CreateOrUpdateStartMarker(sp);
            Debug.Log("[Start] Placed the green PathStart marker at the captured viewpoint. Use it as the path Start.");
        }

        private static Camera ResolveCamera()
        {
            Camera c = Camera.main;
            if (c == null)
            {
                var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                c = cams.Length > 0 ? cams[0] : null;
            }
            return c;
        }

        private static StartPose ComputeStartPose(Vector3 camPos, Vector3 camForward)
        {
            Vector3 floor = camPos;
            if (Physics.Raycast(camPos, Vector3.down, out RaycastHit hit, 10f))
                floor = hit.point;
            else
                floor.y = camPos.y - EyeHeight;

            Vector3 fwd = camForward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            return new StartPose { position = floor, forward = fwd.normalized };
        }

        private static void CreateOrUpdateStartMarker(StartPose sp)
        {
            var go = GameObject.Find("PathStart");
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "PathStart";
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                go.transform.localScale = Vector3.one * 0.3f;
                var mr = go.GetComponent<MeshRenderer>();
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                if (sh != null && mr != null) mr.sharedMaterial = new Material(sh) { color = Color.green };
            }
            go.transform.position = sp.position;
            Vector3 fwd = sp.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }
    }
}
