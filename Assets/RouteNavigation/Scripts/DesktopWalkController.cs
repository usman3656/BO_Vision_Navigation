using UnityEngine;
using UnityEngine.AI;

namespace RouteNavigation
{
    /// <summary>
    /// Minimal desktop first-person walker for testing the wayfinding path without a headset.
    /// WASD to move, mouse to look. No colliders are required: the player snaps to the baked
    /// NavMesh for ground height, so it works on FCG (no colliders) and indoor scenes alike.
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
        public float moveSpeed = 3f;
        public float lookSpeed = 2f;
        public float eyeHeight = 1.6f;
        [Tooltip("XZ distance to the goal (metres) that counts as 'arrived'.")]
        public float arriveRadius = 1.5f;
        [Tooltip("How far to search the navmesh for ground height under the player.")]
        public float groundSnap = 4f;

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
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, groundSnap, NavMesh.AllAreas)) p = hit.position;
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
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, groundSnap, NavMesh.AllAreas))
                    pos.y = hit.position.y;
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

        private void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
