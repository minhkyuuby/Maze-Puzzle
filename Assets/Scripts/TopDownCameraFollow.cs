using UnityEngine;

// Simple top-down / high angle follow camera.
// Attach this to a Camera. Assign target (player). If left empty it will try to find a MazePlayerController at runtime.
public class TopDownCameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;             // Player to follow
    public string playerTag = "";        // Optional tag to search (takes priority over type search if set)

    [Header("Offset / Framing")]
    [Tooltip("World space offset from target position (applied after clamping to follow axes).")]
    public Vector3 offset = new Vector3(0f, 12f, -10f);
    [Tooltip("If true the camera will look at the target each frame (ignores fixedRotation).")]
    public bool lookAtTarget = true;
    public Vector3 rotationEuler = new Vector3(55f, 0f, 0f); // used if lookAtTarget == false
    [Tooltip("Ignore vertical difference when looking at target (stabilizes rotation for top-down).")] public bool flattenLookAt = true;
    [Tooltip("Keep fixed pitch when flattenLookAt is true (uses rotationEuler.x for tilt). ")] public bool keepPitchOnLook = true;

    [Header("Follow Axes")]
    public bool followX = true;
    public bool followY = false; // usually false for top-down so Y comes only from offset
    public bool followZ = true;

    [Header("Smoothing")]
    public bool smoothFollow = true;
    [Range(0.01f, 1f)] public float smoothTime = 0.15f; // approximate time to reach target
    public float maxSpeed = 100f;
    [Header("Rotation Smoothing")] public bool smoothRotation = true;
    [Range(0.1f,30f)] public float rotationLerpSpeed = 12f; // higher = snappier

    [Header("Bounds (Optional)")]
    public bool clampInsideMaze = false;
    public MazeGenerator maze;              // provide to clamp; if null tries to find one
    public float clampPadding = 1f;         // extra space around maze edges

    Vector3 _velocity; // SmoothDamp velocity

    void Awake()
    {
        if (!target)
        {
            if (!string.IsNullOrEmpty(playerTag))
            {
                var tagged = GameObject.FindGameObjectWithTag(playerTag);
                if (tagged) target = tagged.transform;
            }
            if (!target)
            {
#if UNITY_2023_1_OR_NEWER
                var pc = UnityEngine.Object.FindFirstObjectByType<MazePlayerController>();
#else
                var pc = UnityEngine.Object.FindObjectOfType<MazePlayerController>();
#endif
                if (pc) target = pc.transform;
            }
        }
        if (clampInsideMaze && !maze)
        {
#if UNITY_2023_1_OR_NEWER
            maze = UnityEngine.Object.FindFirstObjectByType<MazeGenerator>();
#else
            maze = UnityEngine.Object.FindObjectOfType<MazeGenerator>();
#endif
        }
    }

    void LateUpdate()
    {
        if (!target) return;
        // Build desired position from target directly (prevents feedback jitter)
        Vector3 desired = new Vector3(
            followX ? target.position.x : transform.position.x,
            followY ? target.position.y : transform.position.y,
            followZ ? target.position.z : transform.position.z
        ) + offset;

        if (clampInsideMaze && maze)
        {
            // Maze origin is centered; width & height in cells. We approximate bounds in world using cellSize and origin offset
            float w = Mathf.Max(1, maze.width) * maze.cellSize;
            float h = Mathf.Max(1, maze.height) * maze.cellSize;
            // Maze centered around maze.transform.position + originOffset (originOffset is internal but nodes/world positions already incorporate it)
            Vector3 center = maze.transform.position; // since maze positions are already offset, using transform as center works if generator stays at origin
            float halfW = w * 0.5f + clampPadding;
            float halfH = h * 0.5f + clampPadding;
            desired.x = Mathf.Clamp(desired.x, center.x - halfW, center.x + halfW);
            desired.z = Mathf.Clamp(desired.z, center.z - halfH, center.z + halfH);
        }

        if (smoothFollow)
        {
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime, maxSpeed);
        }
        else
        {
            transform.position = desired;
        }

        if (lookAtTarget && target)
        {
            Vector3 toTarget = target.position - transform.position;
            if (flattenLookAt) toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
                toTarget = transform.forward; // avoid zero
            Quaternion desiredRot;
            if (flattenLookAt && keepPitchOnLook)
            {
                float yaw = Quaternion.LookRotation(toTarget.normalized, Vector3.up).eulerAngles.y;
                desiredRot = Quaternion.Euler(rotationEuler.x, yaw, 0f);
            }
            else
            {
                desiredRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            }
            if (smoothRotation)
            {
                float t = 1f - Mathf.Exp(-rotationLerpSpeed * Time.unscaledDeltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, t);
            }
            else transform.rotation = desiredRot;
        }
        else if (!lookAtTarget)
        {
            if (smoothRotation)
            {
                Quaternion desiredRot = Quaternion.Euler(rotationEuler);
                float t = 1f - Mathf.Exp(-rotationLerpSpeed * Time.unscaledDeltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, t);
            }
            else transform.rotation = Quaternion.Euler(rotationEuler);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (target)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(target.position, target.position + offset);
            Gizmos.DrawWireSphere(target.position + offset, 0.3f);
        }
    }
#endif
}
