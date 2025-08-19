using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;

public class MazePlayerController : MonoBehaviour
{
    [Header("References")]
    public MazeGenerator maze;

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float arriveThreshold = 0.05f;
    public bool clickToMove = true;
    public LayerMask floorMask = ~0;

    [Header("Goal Visual")] 
    public GameObject goalPrefab; 
    public Color goalGizmoColor = Color.green; 

    [Header("Mid Corridor Target")] 
    public bool allowMidCorridorClick = true; 
    public float corridorCaptureRadius = 0.45f; // world units from corridor centerline

    [Header("Closest Node Line Of Sight")]
    public bool preventStartThroughWalls = true; // when true choose nearest visible node only
    public LayerMask wallMask = 0;               // assign the layer(s) your maze walls use
    public float losHeight = 0.4f;               // y offset for ray
    public float losRadius = 0.0f;               // >0 switches to spherecast
    public int extraEdgeSamples = 2;             // sample slight left/right offsets to reduce false negatives

    [Header("Stuck Handling")] 
    public bool autoRepathIfStuck = true; 
    public float stuckCheckInterval = 0.5f; 
    public float stuckGraceTime = 1.25f; 
    public float minProgressPerCheck = 0.05f; 
    public bool snapSmallNudgeIfStillStuck = true; 

    [Header("Effects")] 
    public ParticleSystem attackParticle;

    [Header("Events")] 
    public UnityEvent onPathStarted; 
    public UnityEvent onPathCompleted; 
    public UnityEvent onGoalReached; 

    GameObject goalInstance;
    MazeGenerator.MazeNode goalNode;         // node used for path target (last graph node)
    Vector3? goalWorldPos;                   // actual world position for final goal (node or corridor point)

    readonly Queue<Vector3> waypointQueue = new Queue<Vector3>();
    Vector3? activeWaypoint;

    // Stuck tracking
    float stuckTimer;
    float progressTimer;
    float lastDistance;
    bool monitoringProgress;

    void Awake() {}

    void Start()
    {
        if (!maze)
        {
#if UNITY_2023_1_OR_NEWER
            maze = UnityEngine.Object.FindFirstObjectByType<MazeGenerator>();
#else
            maze = UnityEngine.Object.FindObjectOfType<MazeGenerator>();
#endif
        }
        if (!maze)
        {
            Debug.LogError("MazePlayerController: Maze reference missing.");
            enabled = false;
        }
        else
        {
            // Place player at maze start once generated (in case generation happens later)
            maze.onMazeGenerated.AddListener(OnMazeGeneratedPlacePlayer);
            // If maze already generated (nodes exist), place immediately
            if (maze.nodes != null && maze.nodes.Count > 0)
            {
                OnMazeGeneratedPlacePlayer();
            }
        }
    }

    void Update()
    {
        HandleClickInput();
        TickMovement();
        HandleSpacebarEffect();
    }

    void HandleClickInput()
    {
        if (clickToMove && Input.GetMouseButtonDown(0))
        {
            if (TryRaycastFloor(out var hit))
                SetDestination(hit.point); // unified: always sets goal + path
        }
    }

    bool TryRaycastFloor(out RaycastHit hit)
    {
        var cam = Camera.main; if (!cam) { hit = new RaycastHit(); return false; }
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out hit, 500f, floorMask, QueryTriggerInteraction.Ignore);
    }

    // Public API --------------------------------------------------------
    public void ClearGoal()
    {
        goalNode = null;
        goalWorldPos = null;
        if (goalInstance) goalInstance.SetActive(false);
    }

    // Unified method: sets path and visual goal
    public void SetDestination(Vector3 worldPoint)
    {
        if (!maze) return;
        var start = FindClosestNode(transform.position);
        if (start == null) return;

        goalNode = null;
        goalWorldPos = null;
        waypointQueue.Clear();

        Vector3 finalPoint = worldPoint;

        // Try mid-corridor capture first
        if (allowMidCorridorClick && TryFindCorridorPoint(worldPoint, out var corridorPoint, out var a, out var b))
        {
            var pathA = AStar(start, a);
            var pathB = AStar(start, b);
            var chosen = (PathCost(pathA) <= PathCost(pathB)) ? pathA : pathB;
            BuildWaypointsFromPath(chosen);
            waypointQueue.Enqueue(AdjustHeight(corridorPoint));
            goalNode = (chosen != null && chosen.Count > 0) ? chosen[chosen.Count - 1] : null;
            finalPoint = corridorPoint;
        }
        else
        {
            var dest = FindClosestNode(worldPoint);
            if (dest == null) return;
            var path = AStar(start, dest);
            BuildWaypointsFromPath(path);
            goalNode = dest;
            finalPoint = dest.worldPos;
        }

        goalWorldPos = finalPoint;
        PlaceOrMoveGoal(finalPoint);

        if (waypointQueue.Count > 0)
        {
            activeWaypoint = waypointQueue.Dequeue();
            onPathStarted?.Invoke();
        }
        ResetStuck();
    }

    void PlaceOrMoveGoal(Vector3 worldPos)
    {
        EnsureGoalInstance();
        var p = worldPos; p.y += 0.2f;
        goalInstance.transform.position = p;
        goalInstance.SetActive(true);
    }

    // Movement ---------------------------------------------------------
    void TickMovement()
    {
        if (activeWaypoint == null) return;
        var target = activeWaypoint.Value; target.y = transform.position.y;
        Vector3 delta = target - transform.position; delta.y = 0f;
        float dist = delta.magnitude;
        float threshold = arriveThreshold;
        if (dist <= threshold)
        {
            if (waypointQueue.Count > 0)
            {
                activeWaypoint = waypointQueue.Dequeue();
            }
            else
            {
                activeWaypoint = null;
                onPathCompleted?.Invoke();
                if (goalWorldPos.HasValue && (transform.position - goalWorldPos.Value).sqrMagnitude < 0.25f)
                {
                    onGoalReached?.Invoke();
                    if (goalInstance) goalInstance.SetActive(false); // disable goal visual when reached
                }
            }
            ResetStuck();
            return;
        }

        Vector3 dir = delta / dist;
        transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
        // Removed automatic rotation on move per user request

        HandleStuck(dist, dir);
    }

    // Stuck logic ------------------------------------------------------
    void HandleStuck(float currentDistance, Vector3 moveDir)
    {
        if (!autoRepathIfStuck || activeWaypoint == null) return;
        progressTimer += Time.deltaTime;
        stuckTimer += Time.deltaTime;
        if (!monitoringProgress)
        {
            monitoringProgress = true;
            lastDistance = currentDistance;
            progressTimer = 0f;
            stuckTimer = 0f;
        }
        if (progressTimer < stuckCheckInterval) return;

        float progress = lastDistance - currentDistance;
        if (progress < minProgressPerCheck)
        {
            if (stuckTimer >= stuckGraceTime)
            {
                // Repath to remaining target (goal preferred)
                if (goalWorldPos.HasValue)
                    SetDestination(goalWorldPos.Value);
                else if (activeWaypoint.HasValue)
                    SetDestination(activeWaypoint.Value);
                stuckTimer = 0f;
                if (snapSmallNudgeIfStillStuck && Mathf.Abs(progress) < 0.001f)
                {
                    transform.position += moveDir * Mathf.Min(currentDistance * 0.4f, 0.4f);
                }
            }
        }
        else
        {
            stuckTimer = 0f; // made progress
        }
        lastDistance = currentDistance;
        progressTimer = 0f;
    }

    void ResetStuck()
    {
        monitoringProgress = false;
        progressTimer = 0f;
        stuckTimer = 0f;
        lastDistance = 0f;
    }

    // Path / Graph helpers --------------------------------------------
    MazeGenerator.MazeNode FindClosestNode(Vector3 world)
    {
        if (!preventStartThroughWalls || wallMask.value == 0)
        {
            MazeGenerator.MazeNode bestSimple = null; float bestSimpleSq = float.MaxValue;
            foreach (var n in maze.Nodes)
            {
                float d = (n.worldPos - world).sqrMagnitude;
                if (d < bestSimpleSq) { bestSimpleSq = d; bestSimple = n; }
            }
            return bestSimple;
        }

        var candidates = new List<(MazeGenerator.MazeNode node, float distSq)>();
        foreach (var n in maze.Nodes)
        {
            float d = (n.worldPos - world).sqrMagnitude;
            candidates.Add((n, d));
        }
        candidates.Sort((a,b)=>a.distSq.CompareTo(b.distSq));

        Vector3 origin = world; origin.y += losHeight;
        for (int i = 0; i < candidates.Count; i++)
        {
            var node = candidates[i].node;
            if (NodeVisible(origin, node.worldPos)) return node;
        }
        return candidates.Count>0?candidates[0].node:null;
    }

    bool NodeVisible(Vector3 origin, Vector3 nodePos)
    {
        Vector3 target = nodePos; target.y = origin.y;
        Vector3 dir = (target - origin); float dist = dir.magnitude;
        if (dist < 0.001f) return true;
        dir /= dist;
        if (extraEdgeSamples < 0) extraEdgeSamples = 0;
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        for (int s = -extraEdgeSamples; s <= extraEdgeSamples; s++)
        {
            Vector3 o = origin + side * (s * 0.15f);
            bool blocked = (losRadius > 0f) ? Physics.SphereCast(o, losRadius, dir, out var _, dist, wallMask, QueryTriggerInteraction.Ignore)
                                           : Physics.Raycast(o, dir, dist, wallMask, QueryTriggerInteraction.Ignore);
            if (!blocked) return true;
        }
        return false;
    }

    void BuildWaypointsFromPath(List<MazeGenerator.MazeNode> path)
    {
        if (path == null) return;
        for (int i = 0; i < path.Count; i++)
            waypointQueue.Enqueue(path[i].worldPos + Vector3.up * 0.1f);
    }

    float PathCost(List<MazeGenerator.MazeNode> path)
    {
        if (path == null || path.Count < 2) return 0f;
        float cost = 0f;
        for (int i = 1; i < path.Count; i++)
            cost += Vector3.Distance(path[i - 1].worldPos, path[i].worldPos);
        return cost;
    }

    Vector3 AdjustHeight(Vector3 p)
    { p.y = transform.position.y + 0.1f; return p; }

    bool TryFindCorridorPoint(Vector3 worldPoint, out Vector3 corridorPoint, out MazeGenerator.MazeNode nodeA, out MazeGenerator.MazeNode nodeB)
    {
        corridorPoint = worldPoint; nodeA = null; nodeB = null;
        float bestSq = float.MaxValue;
        float captureSq = corridorCaptureRadius * corridorCaptureRadius;
        Vector3 flatClick = worldPoint; flatClick.y = 0f;
        foreach (var n in maze.Nodes)
        {
            var p0 = n.worldPos; p0.y = 0f;
            foreach (var neigh in n.neighbors)
            {
                if (neigh == null) continue;
                if (neigh.GetHashCode() < n.GetHashCode()) continue;
                var p1 = neigh.worldPos; p1.y = 0f;
                Vector3 seg = p1 - p0; float len = seg.magnitude; if (len < 0.001f) continue;
                Vector3 d = seg / len;
                float t = Vector3.Dot(flatClick - p0, d) / len; // 0..1 param
                if (t <= 0f || t >= 1f) continue; // interior only
                Vector3 closest = p0 + d * (t * len);
                float dSq = (closest - flatClick).sqrMagnitude;
                if (dSq < captureSq && dSq < bestSq)
                {
                    bestSq = dSq; corridorPoint = closest; nodeA = n; nodeB = neigh;
                }
            }
        }
        return nodeA != null;
    }

    class NodeRecord : IComparable<NodeRecord>
    {
        public MazeGenerator.MazeNode node; public float f;
        public int CompareTo(NodeRecord other) => f.CompareTo(other.f);
    }

    List<MazeGenerator.MazeNode> AStar(MazeGenerator.MazeNode start, MazeGenerator.MazeNode goal)
    {
        if (start == null || goal == null) return null;
        var came = new Dictionary<MazeGenerator.MazeNode, MazeGenerator.MazeNode>();
        var gScore = new Dictionary<MazeGenerator.MazeNode, float> { [start] = 0f };
        var openHeap = new MinHeap<NodeRecord>();
        openHeap.Push(new NodeRecord { node = start, f = Heuristic(start, goal) });
        var openSet = new HashSet<MazeGenerator.MazeNode> { start };

        while (openHeap.Count > 0)
        {
            var currentRec = openHeap.Pop();
            var current = currentRec.node;
            if (current == goal) return Reconstruct(came, current);
            openSet.Remove(current);
            foreach (var neigh in current.neighbors)
            {
                float tentativeG = gScore[current] + Vector3.Distance(current.worldPos, neigh.worldPos);
                if (!gScore.TryGetValue(neigh, out var gExisting) || tentativeG < gExisting)
                {
                    came[neigh] = current;
                    gScore[neigh] = tentativeG;
                    float f = tentativeG + Heuristic(neigh, goal);
                    if (!openSet.Contains(neigh))
                    {
                        openSet.Add(neigh);
                        openHeap.Push(new NodeRecord { node = neigh, f = f });
                    }
                    else
                    {
                        openHeap.UpdateIfBetter(r => r.node == neigh && f < r.f, rec => rec.f = f);
                    }
                }
            }
        }
        return null;
    }

    float Heuristic(MazeGenerator.MazeNode a, MazeGenerator.MazeNode b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    List<MazeGenerator.MazeNode> Reconstruct(Dictionary<MazeGenerator.MazeNode, MazeGenerator.MazeNode> came, MazeGenerator.MazeNode current)
    {
        var list = new List<MazeGenerator.MazeNode> { current };
        while (came.ContainsKey(current)) { current = came[current]; list.Add(current); }
        list.Reverse();
        return list;
    }

    void EnsureGoalInstance()
    {
        if (goalInstance) return;
        if (goalPrefab) goalInstance = Instantiate(goalPrefab);
        else
        {
            goalInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            goalInstance.transform.localScale = Vector3.one * 0.45f;
            var col = goalInstance.GetComponent<Collider>(); if (col) Destroy(col);
        }
        goalInstance.name = "MazeGoal";
    }

    class MinHeap<T> where T : IComparable<T>
    {
        readonly List<T> data = new List<T>();
        public int Count => data.Count;
        public void Push(T item) { data.Add(item); SiftUp(data.Count - 1); }
        public T Pop() { var root = data[0]; var last = data[^1]; data.RemoveAt(data.Count - 1); if (data.Count > 0) { data[0] = last; SiftDown(0); } return root; }
        public void UpdateIfBetter(Func<T,bool> predicate, Action<T> applyChange)
        {
            for (int i = 0; i < data.Count; i++)
            {
                if (predicate(data[i])) { applyChange(data[i]); SiftUp(i); SiftDown(i); return; }
            }
        }
        void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2; if (data[i].CompareTo(data[p]) >= 0) break; (data[i], data[p]) = (data[p], data[i]); i = p;
            }
        }
        void SiftDown(int i)
        {
            int n = data.Count;
            while (true)
            {
                int l = i * 2 + 1; if (l >= n) break; int r = l + 1; int best = (r < n && data[r].CompareTo(data[l]) < 0) ? r : l; if (data[best].CompareTo(data[i]) >= 0) break; (data[i], data[best]) = (data[best], data[i]); i = best;
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (goalWorldPos.HasValue)
        {
            Gizmos.color = goalGizmoColor;
            Gizmos.DrawWireSphere(goalWorldPos.Value + Vector3.up * 0.2f, 0.35f);
        }
        if (activeWaypoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(activeWaypoint.Value, 0.15f);
        }
    }
#endif

    void HandleSpacebarEffect()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (attackParticle)
            {
                attackParticle.Play();
            }
        }
    }

    void OnDestroy()
    {
        if (maze) maze.onMazeGenerated.RemoveListener(OnMazeGeneratedPlacePlayer);
    }

    void OnMazeGeneratedPlacePlayer()
    {
        // Start cell is (0,0). Convert using same logic as node worldPos: x*cellSize + originOffset
        if (!maze) return;
        // Try to find node (0,0) if intersection-only it should exist; otherwise compute directly.
        MazeGenerator.MazeNode startNode = null;
        foreach (var n in maze.Nodes) { if (n.x == 0 && n.y == 0) { startNode = n; break; } }
        Vector3 targetPos = startNode != null ? startNode.worldPos : (new Vector3(0,0,0));
        // Maintain current Y (e.g., character controller height)
        targetPos.y = transform.position.y;
        transform.position = targetPos;
        ClearGoal();
        waypointQueue.Clear();
        activeWaypoint = null;
    }
}
