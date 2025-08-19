using UnityEngine;
using System.Collections.Generic;

public class EnemyGhostController : MonoBehaviour
{
    public MazeGenerator maze;
    public float moveSpeed = 3f;
    [Tooltip("Time between retarget decisions")] public float retargetInterval = 3f;
    [Range(0f,1f)] public float chaseChance = 0.5f;
    [Tooltip("If true use every cell as node (override sparse graph)")] public bool fullNodeGraph = false;
    [Tooltip("Random scatter seed offset")] public int scatterSeedOffset = 999;
    public bool drawDebugGizmos = false;

    float timer;
    Queue<Vector3> path = new Queue<Vector3>();
    Vector3? active;
    System.Random rnd;
    bool chasing;

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
        rnd = maze && maze.useSeed ? new System.Random(maze.seed + scatterSeedOffset + GetInstanceID()) : new System.Random();
        Retarget();
    }

    void Update()
    {
        if (!maze) return;
        timer += Time.deltaTime;
        if (timer >= retargetInterval)
        {
            timer = 0f;
            Retarget();
        }
        MoveTick();
    }

    void Retarget()
    {
        chasing = rnd.NextDouble() < chaseChance;
        MazeGenerator.MazeNode start = FindClosestNode(transform.position);
        if (start == null) return;
        MazeGenerator.MazeNode goal = null;
        if (chasing)
        {
            var player = FindObjectOfType<MazePlayerController>();
            if (player)
                goal = FindClosestNode(player.transform.position);
        }
        if (goal == null)
        {
            // scatter: pick random corner-ish node
            var nodes = new List<MazeGenerator.MazeNode>(maze.Nodes);
            if (nodes.Count == 0) return;
            goal = nodes[rnd.Next(nodes.Count)];
        }
        var nodePath = AStar(start, goal);
        path.Clear();
        if (nodePath != null)
        {
            foreach (var n in nodePath)
                path.Enqueue(n.worldPos + Vector3.up * 0.5f);
        }
        active = path.Count > 0 ? path.Dequeue() : (Vector3?)null;
    }

    void MoveTick()
    {
        if (active == null) return;
        var target = active.Value; target.y = transform.position.y;
        var delta = target - transform.position; delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 0.05f)
        {
            if (path.Count > 0)
                active = path.Dequeue();
            else
                active = null;
            return;
        }
        transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
    }

    MazeGenerator.MazeNode FindClosestNode(Vector3 world)
    {
        MazeGenerator.MazeNode best = null; float bestSq = float.MaxValue;
        foreach (var n in maze.Nodes)
        {
            float d = (n.worldPos - world).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = n; }
        }
        return best;
    }

    class NodeRecord { public MazeGenerator.MazeNode node; public float g; public float f; public NodeRecord parent; }

    List<MazeGenerator.MazeNode> AStar(MazeGenerator.MazeNode start, MazeGenerator.MazeNode goal)
    {
        if (start == null || goal == null) return null;
        var open = new List<NodeRecord>();
        var all = new Dictionary<MazeGenerator.MazeNode, NodeRecord>();
        NodeRecord StartRec(MazeGenerator.MazeNode n, float g, float f, NodeRecord p){var r=new NodeRecord{node=n,g=g,f=f,parent=p};open.Add(r);all[n]=r;return r;}
        StartRec(start,0,Heuristic(start,goal),null);
        while(open.Count>0){open.Sort((a,b)=>a.f.CompareTo(b.f));var cur=open[0];open.RemoveAt(0);if(cur.node==goal)return Reconstruct(cur);foreach(var neigh in cur.node.neighbors){float g=cur.g+Vector3.Distance(cur.node.worldPos,neigh.worldPos);if(!all.TryGetValue(neigh,out var nr)||g<nr.g){float f=g+Heuristic(neigh,goal);if(nr==null){StartRec(neigh,g,f,cur);}else{nr.g=g;nr.f=f;nr.parent=cur;}}}}return null;}
        float Heuristic(MazeGenerator.MazeNode a, MazeGenerator.MazeNode b)=>Mathf.Abs(a.x-b.x)+Mathf.Abs(a.y-b.y);
        List<MazeGenerator.MazeNode> Reconstruct(NodeRecord end){var list=new List<MazeGenerator.MazeNode>();var c=end;while(c!=null){list.Add(c.node);c=c.parent;}list.Reverse();return list;}
    
#if UNITY_EDITOR
    void OnDrawGizmosSelected(){ if(!drawDebugGizmos||active==null)return; Gizmos.color=chasing?Color.red:Color.magenta; Gizmos.DrawWireSphere(active.Value,0.25f);}    
#endif
}
