using UnityEngine;

namespace RouteNavigation
{
    /// <summary>
    /// Minimal desktop first-person walker for testing the wayfinding path without a headset.
    /// WASD to move, mouse to look. It spawns EXACTLY at the start marker (no navmesh snap) and
    /// follows the floor via a downward raycast where colliders exist.
    ///
    /// It times the walk from the FIRST movement to arrival at the goal, which the trial runner
    /// reads back as the "walk time" objective.
    ///
    /// SETUP: put this on an empty GameObject (the "Player"). A camera is created automatically
    /// if the object has none. Disable any other camera in the scene so this one renders.
    /// </summary>
    public class DesktopWalkController : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 15f;   // ~5x, for covering the large outdoor FCG map quickly
        public float lookSpeed = 2f;
        public float eyeHeight = 1.6f;
        [Tooltip("XZ distance to the goal (metres) that counts as 'arrived'.")]
        public float arriveRadius = 1.5f;

        public Camera Cam { get; private set; }
        public bool Finished { get; private set; }
        public bool Walking { get; private set; }
        public float ElapsedSeconds { get; private set; }

        private Transform _goal;
        private float _pitch;
        private bool _timing;

        private void Awake()
        {
            Cam = GetComponentInChildren<Camera>();
            if (Cam == null)
            {
                var camGo = new GameObject("WalkCamera");
                camGo.transform.SetParent(transform, false);
                camGo.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
                Cam = camGo.AddComponent<Camera>();
            }
        }

        /// <summary>Teleports the player to a start pose and clears the timer/arrival state.</summary>
        public void ResetTo(Transform startPose)
        {
            Vector3 p = startPose.position;
            p.y = GroundY(p, p.y);   // floor height, but keep EXACT X/Z (no sideways navmesh snap)
            transform.position = p;
            transform.rotation = Quaternion.Euler(0f, startPose.eulerAngles.y, 0f);
            _pitch = 0f;
            if (Cam != null) Cam.transform.localRotation = Quaternion.identity;
            ElapsedSeconds = 0f;
            _timing = false;
            Finished = false;
            Walking = false;
        }

        /// <summary>Enables control and starts watching for arrival at the goal.</summary>
        public void Begin(Transform goal)
        {
            _goal = goal;
            // Face the goal so the arrow trail is ahead of the player at the start.
            if (goal != null)
            {
                Vector3 dir = goal.position - transform.position; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            _pitch = 0f;
            if (Cam != null) Cam.transform.localRotation = Quaternion.identity;
            Finished = false;
            Walking = true;
            _timing = false;
            ElapsedSeconds = 0f;
            LockCursor(true);
        }

        public void StopWalk()
        {
            Walking = false;
            LockCursor(false);
        }

        private void Update()
        {
            if (!Walking) return;

            // Mouse look: yaw on the body, pitch on the camera.
            float mx = Input.GetAxis("Mouse X") * lookSpeed;
            float my = Input.GetAxis("Mouse Y") * lookSpeed;
            transform.Rotate(0f, mx, 0f, Space.World);
            _pitch = Mathf.Clamp(_pitch - my, -80f, 80f);
            if (Cam != null) Cam.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            // Movement on the XZ plane, height taken from the navmesh.
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = transform.right; right.y = 0f; right.Normalize();
            Vector3 move = fwd * v + right * h;
            if (move.sqrMagnitude > 0.0001f)
            {
                _timing = true; // the clock starts on the first movement
                Vector3 pos = transform.position + move.normalized * moveSpeed * Time.deltaTime;
                pos.y = GroundY(pos, transform.position.y); // follow the floor where colliders exist, else keep height
                transform.position = pos;
            }

            if (_timing) ElapsedSeconds += Time.deltaTime;

            // Arrival check on XZ only.
            if (_goal != null)
            {
                Vector3 a = transform.position; a.y = 0f;
                Vector3 b = _goal.position; b.y = 0f;
                if (Vector3.Distance(a, b) <= arriveRadius)
                {
                    Finished = true;
                    StopWalk();
                }
            }
        }

        /// <summary>Floor height under a point via a downward raycast; returns the fallback if nothing is hit.</summary>
        private static float GroundY(Vector3 p, float fallback)
        {
            return Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 8f) ? hit.point.y : fallback;
        }

        private void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
