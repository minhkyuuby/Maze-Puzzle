using UnityEngine;
// NOTE: Uses WallPooler MonoBehaviour for wall pooling
using System.Collections.Generic;
using System;
using UnityEngine.Events;
using System.Collections;

public partial class MazeGenerator : MonoBehaviour
{
	[Header("Maze Size")]
	public int width = 10; // m
	public int height = 10; // n

	[Header("Visuals")]
	public GameObject wallPrefab; // optional, if null we'll use cubes
	public float cellSize = 1f;
	public float wallHeight = 2f;
	public float wallThickness = 0.1f;
	public Material pathMaterial;
	public Material floorMaterial;
	[Header("Optimization")]
	[Tooltip("Reuse (pool) wall GameObjects instead of destroying/instantiating every generation.")]
	public bool useWallPooling = true;
	[Tooltip("Optional pre-allocation size for wall pool (0 = no prewarm)." )]
	public int initialWallPoolSize = 0;

	// Removed: MazePoint GameObject spawning (graph uses virtual nodes only)
	[Tooltip("If true, graph nodes are created only at intersections (dead ends, corners, junctions, start/end). If false, every cell becomes a graph node.")]
	public bool pointsOnlyAtIntersections = true;
	[Tooltip("Merge contiguous wall segments into longer pieces to reduce GameObjects.")]
	public bool mergeWalls = true;

	[Header("Generation")]
	public bool generateOnStart = true;
	public enum GenerationAlgorithm { DepthFirstSearch, RandomizedPrim }
	[Tooltip("Choose which algorithm to use for maze generation.")]
	public GenerationAlgorithm algorithm = GenerationAlgorithm.DepthFirstSearch;
	[Tooltip("Optional fixed seed (used when Use Seed is checked)." )]
	public int seed = 0;
	public bool useSeed = false;

	[Header("Events")]
	public UnityEvent onMazeGenerated;

	[Header("Gizmos")] 
	public bool drawGraphGizmos = true; 
	public Color gizmoNodeColor = Color.yellow; 
	public Color gizmoEdgeColor = new Color(0f, 0.8f, 1f, 0.6f); 
	[Range(0.01f,0.5f)] public float gizmoNodeRadius = 0.12f; 
	public bool drawWhilePlaying = true; 
	public bool drawOnlySelected = true;
	[Tooltip("Automatically reduce gizmo detail when node/edge counts exceed limits.")]
	public bool gizmoAutoCull = true;
	[Tooltip("Max nodes to draw before sampling (only applies if Gizmo Auto Cull on).")]
	public int gizmoMaxNodes = 500;
	[Tooltip("Max edges to draw (deduped) before stopping (only applies if Gizmo Auto Cull on).")]
	public int gizmoMaxEdges = 800;

	// internal
	class Cell { public bool[] wall = new bool[4]; /* 0=up,1=right,2=down,3=left */ }
	Cell[,] cells;
	bool[,] visited;
	Vector2Int[,] parent;
	Vector3 originOffset; // centers maze around (0,0,0)
	public Vector3 OriginOffset => originOffset;

	// Graph data (intersection-based)
	public class MazeNode { public int x; public int y; public Vector3 worldPos; public List<MazeNode> neighbors = new List<MazeNode>(); }
	public readonly Dictionary<(int,int), MazeNode> nodes = new Dictionary<(int,int), MazeNode>();
	public IEnumerable<MazeNode> Nodes => nodes.Values;
	// Track last option states for dynamic graph rebuild
	bool _lastPointsOnlyAtIntersections;

	// Pooling
	[SerializeField] global::WallPooler wallPooler; // assigned or auto-added
	GameObject _floorObj; // cached floor (not included inside wall pool)

	void Start()
	{
		if (useWallPooling && wallPooler == null)
		{
			wallPooler = GetComponentInChildren<global::WallPooler>();
			if (wallPooler == null)
			{
				var go = new GameObject("WallPooler");
				go.transform.SetParent(transform, false);
				wallPooler = go.AddComponent<global::WallPooler>();
				wallPooler.autoInitialize = false; // we'll init manually with our prefab & prewarm
			}
		}
		if (generateOnStart)
			Generate();
	}

	// Public entry
	public void Generate()
	{
		if (width <= 0 || height <= 0)
		{
			Debug.LogError("Width and Height must be > 0");
			return;
		}

		ClearChildren();
		InitGrid();
		originOffset = new Vector3(-(width - 1) * cellSize / 2f, 0f, -(height - 1) * cellSize / 2f);
		var rnd = useSeed ? new System.Random(seed) : new System.Random();
		switch (algorithm)
		{
			case GenerationAlgorithm.DepthFirstSearch:
				CarveMazeDFS(new Vector2Int(0, 0), rnd);
				break;
			case GenerationAlgorithm.RandomizedPrim:
				CarveMazePrim(new Vector2Int(0, 0), rnd);
				break;
			// case GenerationAlgorithm.Eller:
			// 	CarveMazeEller(rnd);
				// break;
		}
		OpenEntrances();
		BuildVisuals();
		BuildGraph(); // construct navigation graph from maze points / layout
		HighlightPath();
		onMazeGenerated?.Invoke();
	}

	void ClearChildren()
	{
		if (!useWallPooling)
		{
			var toDestroy = new List<GameObject>();
			foreach (Transform t in transform)
				toDestroy.Add(t.gameObject);
			for (int i = 0; i < toDestroy.Count; i++)
				DestroyImmediate(toDestroy[i]);
			_floorObj = null;
		}
		else
		{
			if (wallPooler == null)
			{
				wallPooler = GetComponentInChildren<global::WallPooler>();
				if (wallPooler == null)
				{
					var go = new GameObject("WallPooler");
					go.transform.SetParent(transform, false);
					wallPooler = go.AddComponent<global::WallPooler>();
				}
				wallPooler.autoInitialize = false;
			}
			wallPooler.SetWallPrefab(wallPrefab);
			wallPooler.DeactivateAll();
			// ensure prewarm only once when empty
			if (initialWallPoolSize > 0 && wallPooler.TotalCount == 0)
			{
				wallPooler.prewarm = initialWallPoolSize;
				wallPooler.Initialize();
			}
			if (_floorObj != null) _floorObj.SetActive(false);
		}
	}

	void InitGrid()
	{
		cells = new Cell[width, height];
		visited = new bool[width, height];
		parent = new Vector2Int[width, height];

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				cells[x, y] = new Cell();
				for (int i = 0; i < 4; i++) cells[x, y].wall[i] = true;
				visited[x, y] = false;
				parent[x, y] = new Vector2Int(-1, -1);
			}
		}
	}

	void CarveMazeDFS(Vector2Int start, System.Random rnd)
	{
		var stack = new Stack<Vector2Int>();
		visited[start.x, start.y] = true;
		stack.Push(start);

		while (stack.Count > 0)
		{
			var cur = stack.Peek();
			var neighbors = new List<Vector2Int>();

			// neighbors: up, right, down, left
			var dirs = new Vector2Int[] { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
			for (int i = 0; i < 4; i++)
			{
				var n = cur + dirs[i];
				if (n.x >= 0 && n.x < width && n.y >= 0 && n.y < height && !visited[n.x, n.y])
					neighbors.Add(n);
			}

			if (neighbors.Count > 0)
			{
				var next = neighbors[rnd.Next(neighbors.Count)];
				// remove wall between cur and next
				int dirIndex = GetDirectionIndex(cur, next);
				if (dirIndex >= 0)
				{
					cells[cur.x, cur.y].wall[dirIndex] = false;
					cells[next.x, next.y].wall[(dirIndex + 2) % 4] = false; // opposite
				}

				visited[next.x, next.y] = true;
				parent[next.x, next.y] = cur;
				stack.Push(next);
			}
			else
			{
				stack.Pop();
			}
		}
	}

	// Randomized Prim's algorithm
	void CarveMazePrim(Vector2Int start, System.Random rnd)
	{
		visited[start.x, start.y] = true;
		var frontier = new List<(Vector2Int cell, int dir)>();
		AddFrontierWalls(start, frontier);
		while (frontier.Count > 0)
		{
			int idx = rnd.Next(frontier.Count);
			var item = frontier[idx];
			frontier.RemoveAt(idx);
			var cell = item.cell;
			int dir = item.dir; // wall belongs to cell going outward
			var next = cell + DirVector(dir);
			if (!InBounds(next)) continue;
			if (visited[next.x, next.y] && visited[cell.x, cell.y]) continue; // both visited -> skip
			// determine which is visited and which is new
			Vector2Int from, to;
			if (visited[cell.x, cell.y] && !visited[next.x, next.y]) { from = cell; to = next; }
			else if (!visited[cell.x, cell.y] && visited[next.x, next.y]) { from = next; to = cell; dir = (dir + 2) % 4; }
			else { continue; }
			// carve
			cells[from.x, from.y].wall[dir] = false;
			cells[to.x, to.y].wall[(dir + 2) % 4] = false;
			visited[to.x, to.y] = true;
			parent[to.x, to.y] = from;
			AddFrontierWalls(to, frontier);
		}
	}

	void AddFrontierWalls(Vector2Int cell, List<(Vector2Int,int)> list)
	{
		for (int d = 0; d < 4; d++)
		{
			var n = cell + DirVector(d);
			if (InBounds(n) && !visited[n.x, n.y])
			{
				list.Add((cell, d));
			}
		}
	}

	Vector2Int DirVector(int dir)
	{
		return dir switch
		{
			0 => new Vector2Int(0, 1),
			1 => new Vector2Int(1, 0),
			2 => new Vector2Int(0, -1),
			3 => new Vector2Int(-1, 0),
			_ => Vector2Int.zero
		};
	}

	bool InBounds(Vector2Int p) => p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;

	// Eller's algorithm (row-by-row perfect maze)
	void CarveMazeEller(System.Random rnd)
	{
		int nextSetId = 1;
		int[] setIds = new int[width];
		for (int x = 0; x < width; x++) setIds[x] = nextSetId++;

		for (int y = 0; y < height; y++)
		{
			// Horizontal joins
			for (int x = 0; x < width - 1; x++)
			{
				bool shouldJoin = rnd.NextDouble() < 0.5; // tune density
				if (shouldJoin && setIds[x] != setIds[x + 1])
				{
					// carve right wall
					cells[x, y].wall[1] = false;
					cells[x + 1, y].wall[3] = false;
					int fromSet = setIds[x + 1];
					int toSet = setIds[x];
					for (int k = 0; k < width; k++) if (setIds[k] == fromSet) setIds[k] = toSet;
				}
			}

			if (y == height - 1) break; // last row, verticals done later

			// Determine vertical connections per set
			var setToCells = new Dictionary<int, List<int>>();
			for (int x = 0; x < width; x++)
			{
				if (!setToCells.ContainsKey(setIds[x])) setToCells[setIds[x]] = new List<int>();
				setToCells[setIds[x]].Add(x);
			}

			// Track which cells connect down
			bool[] willCarveDown = new bool[width];
			foreach (var kv in setToCells)
			{
				var cellsInSet = kv.Value;
				int carveCount = 0;
				foreach (var cx in cellsInSet)
				{
					if (rnd.NextDouble() < 0.5)
					{
						willCarveDown[cx] = true; carveCount++;
					}
				}
				if (carveCount == 0)
				{
					int forceIndex = cellsInSet[rnd.Next(cellsInSet.Count)];
					willCarveDown[forceIndex] = true;
				}
			}

			// Carve verticals and assign next row set ids
			int[] nextRowSetIds = new int[width];
			for (int x = 0; x < width; x++)
			{
				if (willCarveDown[x])
				{
					cells[x, y].wall[2] = false; // down
					cells[x, y + 1].wall[0] = false; // corresponding up
					nextRowSetIds[x] = setIds[x];
				}
				else
				{
					nextRowSetIds[x] = nextSetId++;
				}
			}
			setIds = nextRowSetIds;
		}

		// Last row: ensure all cells connected horizontally
		for (int x = 0; x < width - 1; x++)
		{
			if (setIds[x] != setIds[x + 1])
			{
				cells[x, height - 1].wall[1] = false;
				cells[x + 1, height - 1].wall[3] = false;
				int fromSet = setIds[x + 1];
				int toSet = setIds[x];
				for (int k = 0; k < width; k++) if (setIds[k] == fromSet) setIds[k] = toSet;
			}
		}
	}

	int GetDirectionIndex(Vector2Int a, Vector2Int b)
	{
		var diff = b - a;
		if (diff == new Vector2Int(0, 1)) return 0; // up
		if (diff == new Vector2Int(1, 0)) return 1; // right
		if (diff == new Vector2Int(0, -1)) return 2; // down
		if (diff == new Vector2Int(-1, 0)) return 3; // left
		return -1;
	}

	void BuildVisuals()
	{
		// floor (pooled separately)
		if (_floorObj == null)
		{
			_floorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
			_floorObj.name = "MazeFloor";
			_floorObj.transform.parent = transform;
		}
		_floorObj.transform.localScale = new Vector3(width * cellSize, 0.1f, height * cellSize);
		_floorObj.transform.localPosition = new Vector3(0f, -0.05f, 0f);
		if (floorMaterial != null)
		{
			var rend = _floorObj.GetComponent<Renderer>();
			if (rend) rend.sharedMaterial = floorMaterial; // shared to avoid instancing
		}
		_floorObj.SetActive(true);

	// walls: create up and right for each cell, and left/bottom boundaries (MazePoint objects removed)

		if (mergeWalls)
		{
			BuildMergedWalls();
		}
		else
		{
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					var cellCenter = new Vector3(x * cellSize, 0, y * cellSize) + originOffset;
					if (cells[x, y].wall[0]) CreateWall(new Vector3(cellCenter.x, wallHeight / 2f, cellCenter.z + cellSize / 2f), new Vector3(cellSize, wallHeight, wallThickness));
					if (cells[x, y].wall[1]) CreateWall(new Vector3(cellCenter.x + cellSize / 2f, wallHeight / 2f, cellCenter.z), new Vector3(wallThickness, wallHeight, cellSize));
					if (x == 0 && cells[x, y].wall[3]) CreateWall(new Vector3(cellCenter.x - cellSize / 2f, wallHeight / 2f, cellCenter.z), new Vector3(wallThickness, wallHeight, cellSize));
					if (y == 0 && cells[x, y].wall[2]) CreateWall(new Vector3(cellCenter.x, wallHeight / 2f, cellCenter.z - cellSize / 2f), new Vector3(cellSize, wallHeight, wallThickness));
				}
			}
		}

		// (MazePoint GameObjects omitted)
	}

	bool ShouldCreatePoint(int x, int y)
	{
		if (!pointsOnlyAtIntersections) return true; // all cells when disabled
		// Always create at start and end
		if ((x == 0 && y == 0) || (x == width - 1 && y == height - 1)) return true;
		// Count open neighbors
		int openCount = 0;
		bool openUp = !cells[x, y].wall[0];
		bool openRight = !cells[x, y].wall[1];
		bool openDown = !cells[x, y].wall[2];
		bool openLeft = !cells[x, y].wall[3];
		if (openUp) openCount++;
		if (openRight) openCount++;
		if (openDown) openCount++;
		if (openLeft) openCount++;
		// Dead end or junction (0/1/3/4 openings) -> intersection
		if (openCount != 2) return true;
		// Exactly two openings: check if straight corridor (opposite) -> then not intersection
		bool straight = (openUp && openDown) || (openLeft && openRight);
		// Corner (perpendicular) acts as intersection for navigation
		return !straight; // only create if corner
	}

	void BuildGraph()
	{
		nodes.Clear();
		// Create nodes at intersection / point cells for compact graph
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				if (!ShouldCreatePoint(x, y)) continue;
				var pos = new Vector3(x * cellSize, 0, y * cellSize) + originOffset;
				var node = new MazeNode { x = x, y = y, worldPos = pos };
				nodes[(x, y)] = node;
			}
		}
		// Connect nodes by tracing corridors
		foreach (var entry in nodes)
		{
			var a = entry.Value;
			TryConnect(a, 0); // up
			TryConnect(a, 1); // right
			TryConnect(a, 2); // down
			TryConnect(a, 3); // left
		}
	}

	void TryConnect(MazeNode a, int dir)
	{
		// only proceed in primary directions to avoid duplicate: up/right only unless neighbor not yet added
		if (dir == 2 || dir == 3) return; // prevent duplicates; handled from opposite nodes
		if (cells[a.x, a.y].wall[dir]) return;
		var delta = DirVector(dir);
		int cx = a.x + delta.x;
		int cy = a.y + delta.y;
		int prevDir = (dir + 2) % 4;
		while (cx >= 0 && cx < width && cy >= 0 && cy < height)
		{
			// if wall between previous cell and current in traversal direction break
			int backX = cx - delta.x;
			int backY = cy - delta.y;
			if (cells[backX, backY].wall[dir]) break;
			if (nodes.TryGetValue((cx, cy), out var b))
			{
				if (!a.neighbors.Contains(b)) a.neighbors.Add(b);
				if (!b.neighbors.Contains(a)) b.neighbors.Add(a);
				break;
			}
			// if current cell is a branching point (not straight corridor), but not registered as node (pointsOnlyAtIntersections false) we still treat intersection; else continue
			bool straight = (!cells[cx, cy].wall[0] && !cells[cx, cy].wall[2]) ^ (!cells[cx, cy].wall[1] && !cells[cx, cy].wall[3]);
			// move forward
			cx += delta.x; cy += delta.y;
		}
	}

	void BuildMergedWalls()
	{
		// Merge horizontal interior north edges & bottom boundary (south edges of row 0)
		// North edges
		for (int y = 0; y < height; y++)
		{
			int x = 0;
			while (x < width)
			{
				if (cells[x, y].wall[0])
				{
					int start = x;
					while (x < width && cells[x, y].wall[0]) x++;
					int len = x - start;
					float z = (y * cellSize) + cellSize / 2f + originOffset.z;
					float centerX = (start + (len / 2f) - 0.5f) * cellSize + originOffset.x;
					CreateWall(new Vector3(centerX, wallHeight / 2f, z), new Vector3(len * cellSize, wallHeight, wallThickness));
				}
				else x++;
			}
		}
		// South boundary edges (row 0, wall[2])
		{
			int x = 0;
			int y = 0;
			while (x < width)
			{
				if (cells[x, y].wall[2])
				{
					int start = x;
					while (x < width && cells[x, y].wall[2]) x++;
					int len = x - start;
					float z = (-cellSize / 2f) + originOffset.z;
					float centerX = (start + (len / 2f) - 0.5f) * cellSize + originOffset.x;
					CreateWall(new Vector3(centerX, wallHeight / 2f, z), new Vector3(len * cellSize, wallHeight, wallThickness));
				}
				else x++;
			}
		}

		// Merge vertical interior east edges & west boundary
		// East edges
		for (int x = 0; x < width; x++)
		{
			int y = 0;
			while (y < height)
			{
				if (cells[x, y].wall[1])
				{
					int start = y;
					while (y < height && cells[x, y].wall[1]) y++;
					int len = y - start;
					float xPos = (x * cellSize) + cellSize / 2f + originOffset.x;
					float centerZ = (start + (len / 2f) - 0.5f) * cellSize + originOffset.z;
					CreateWall(new Vector3(xPos, wallHeight / 2f, centerZ), new Vector3(wallThickness, wallHeight, len * cellSize));
				}
				else y++;
			}
		}
		// West boundary (column 0, wall[3])
		{
			int y = 0;
			int x = 0;
			while (y < height)
			{
				if (cells[x, y].wall[3])
				{
					int start = y;
					while (y < height && cells[x, y].wall[3]) y++;
					int len = y - start;
					float xPos = (-cellSize / 2f) + originOffset.x;
					float centerZ = (start + (len / 2f) - 0.5f) * cellSize + originOffset.z;
					CreateWall(new Vector3(xPos, wallHeight / 2f, centerZ), new Vector3(wallThickness, wallHeight, len * cellSize));
				}
				else y++;
			}
		}
	}

	void OpenEntrances()
	{
		// Open start (0,0) on the south (down) boundary and end (width-1,height-1) on the north (up) boundary
		if (cells == null) return;
		cells[0, 0].wall[2] = false; // down (south)
		cells[width - 1, height - 1].wall[0] = false; // up (north)
		if (width == 1 && height == 1)
		{
			// trivial maze, open both sides for clarity
			cells[0, 0].wall[0] = false;
			cells[0, 0].wall[2] = false;
		}
	}

	void CreateWall(Vector3 worldPos, Vector3 scale)
	{
		GameObject go;
		if (useWallPooling)
		{
			go = wallPooler.Get();
		}
		else
		{
			if (wallPrefab != null)
				go = Instantiate(wallPrefab, transform);
			else
			{
				go = GameObject.CreatePrimitive(PrimitiveType.Cube);
				go.transform.parent = transform;
			}
			go.name = "Wall";
		}
		go.transform.localPosition = worldPos;
		go.transform.localScale = scale;
	}

	void HighlightPath()
	{
		// reconstruct path from end to start using parent[][]
		var end = new Vector2Int(width - 1, height - 1);
		if (parent[end.x, end.y].x == -1 && (end.x != 0 || end.y != 0))
		{
			// Build path parents via BFS through carved maze (works for any algorithm)
			BuildParentPath();
			if (parent[end.x, end.y].x == -1)
			{
				Debug.LogWarning("Could not find path after BFS (unexpected for perfect maze)");
				return;
			}
		}

		var path = new List<Vector2Int>();
		var cur = end;
		path.Add(cur);
		while (!(cur.x == 0 && cur.y == 0))
		{
			cur = parent[cur.x, cur.y];
			if (cur.x == -1) break;
			path.Add(cur);
		}

		// place small markers along the path
		// for (int i = 0; i < path.Count; i++)
		// {
		// 	var p = path[i];
		// 	var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		// 	marker.transform.parent = transform;
		// 	marker.transform.localScale = Vector3.one * (cellSize * 0.3f);
		// 	marker.transform.localPosition = new Vector3(p.x * cellSize, 0.3f, p.y * cellSize) + originOffset;
		// 	marker.name = "PathMarker" + i;
		// 	if (pathMaterial != null)
		// 	{
		// 		var rend = marker.GetComponent<Renderer>();
		// 		if (rend) rend.material = pathMaterial;
		// 	}
		// }
	}

	void BuildParentPath()
	{
		var q = new Queue<Vector2Int>();
		bool[,] seen = new bool[width, height];
		var start = new Vector2Int(0, 0);
		q.Enqueue(start);
		seen[start.x, start.y] = true;
		var dirs = new Vector2Int[] { new Vector2Int(0,1), new Vector2Int(1,0), new Vector2Int(0,-1), new Vector2Int(-1,0) };
		while (q.Count > 0)
		{
			var cur = q.Dequeue();
			if (cur.x == width - 1 && cur.y == height - 1) return; // end found
			for (int d = 0; d < 4; d++)
			{
				var n = cur + dirs[d];
				if (!InBounds(n)) continue;
				// if no wall between cur and n in direction d
				if (!cells[cur.x, cur.y].wall[d] && !seen[n.x, n.y])
				{
					seen[n.x, n.y] = true;
					parent[n.x, n.y] = cur;
					q.Enqueue(n);
				}
			}
		}
	}

	// editor helper
	void OnValidate()
	{
		width = Mathf.Max(1, width);
		height = Mathf.Max(1, height);
		// If settings related to node density changed, rebuild graph (runtime only to avoid editor spam before generation)
		if (Application.isPlaying && (pointsOnlyAtIntersections != _lastPointsOnlyAtIntersections))
		{
			RebuildGraphOnly();
		}
		_lastPointsOnlyAtIntersections = pointsOnlyAtIntersections;
	}

	public void RebuildGraphOnly()
	{
		if (cells == null) return; // maze not generated yet
		BuildGraph();
	}

#if UNITY_EDITOR
	void OnDrawGizmos()
	{
		if (drawOnlySelected) return; // skip here, drawn in selected callback
		InternalDrawGraphGizmos();
	}

	void OnDrawGizmosSelected()
	{
		InternalDrawGraphGizmos();
	}

	void InternalDrawGraphGizmos()
	{
		if (!drawGraphGizmos) return;
		if (!Application.isPlaying && nodes.Count == 0) return; // graph built only after generate
		if (Application.isPlaying == false && !generateOnStart) return; // nothing yet
		int nodeCount = nodes.Count;
		int nodeStep = 1;
		if (gizmoAutoCull && nodeCount > gizmoMaxNodes && gizmoMaxNodes > 0)
		{
			nodeStep = Mathf.CeilToInt((float)nodeCount / gizmoMaxNodes);
		}
		Gizmos.color = gizmoNodeColor;
		int i = 0;
		foreach (var kv in nodes)
		{
			if ((i++ % nodeStep) != 0) continue;
			Gizmos.DrawSphere(transform.TransformPoint(kv.Value.worldPos), gizmoNodeRadius);
		}
		// Draw edges with limit
		Gizmos.color = gizmoEdgeColor;
		HashSet<ulong> drawn = new HashSet<ulong>();
		int edgesDrawn = 0;
		foreach (var a in nodes.Values)
		{
			foreach (var b in a.neighbors)
			{
				ulong key = a.x <= b.x ? ((ulong)(uint)a.x << 48) | ((ulong)(uint)a.y << 32) | ((ulong)(uint)b.x << 16) | (uint)b.y
					: ((ulong)(uint)b.x << 48) | ((ulong)(uint)b.y << 32) | ((ulong)(uint)a.x << 16) | (uint)a.y;
				if (drawn.Contains(key)) continue;
				if (gizmoAutoCull && gizmoMaxEdges > 0 && edgesDrawn >= gizmoMaxEdges) break;
				drawn.Add(key);
				Gizmos.DrawLine(transform.TransformPoint(a.worldPos), transform.TransformPoint(b.worldPos));
				edgesDrawn++;
			}
			if (gizmoAutoCull && gizmoMaxEdges > 0 && edgesDrawn >= gizmoMaxEdges) break;
		}
	}
#endif
}