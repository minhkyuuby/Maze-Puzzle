using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Separate component responsible for spawning enemy ghosts after a maze is generated.
// Attach this to the same GameObject as MazeGenerator (or assign maze reference manually).
public class MazeEnemySpawner : MonoBehaviour
{
    [Header("References")] public MazeGenerator maze;

    [Header("Spawning")] public bool autoSpawnOnGenerate = true;
    public GameObject enemyPrefab;
    [Min(0)] public int enemyCount = 4;
    public bool useFullNodeGraph = false; // override: if true force full graph even if maze uses intersections only

    [Header("Enemy Behaviour Defaults")] public float enemyRetargetInterval = 1.5f;
    [Range(0f,1f)] public float enemyChaseChance = 0.65f;

    [Header("Runtime State")] [SerializeField] List<GameObject> _spawned = new List<GameObject>();

    void Awake()
    {
        if (!maze) maze = GetComponent<MazeGenerator>();
    }

    void OnEnable()
    {
        if (maze) maze.onMazeGenerated.AddListener(HandleMazeGenerated);
    }

    void OnDisable()
    {
        if (maze) maze.onMazeGenerated.RemoveListener(HandleMazeGenerated);
    }

    void HandleMazeGenerated()
    {
        if (autoSpawnOnGenerate) Spawn();
    }

    [ContextMenu("Spawn Enemies")] public void Spawn()
    {
        if (!maze) { Debug.LogWarning("MazeEnemySpawner: Maze reference missing."); return; }
        Clear();
        if (enemyCount <= 0) return;
        if (maze.nodes == null || maze.nodes.Count == 0) { Debug.LogWarning("MazeEnemySpawner: Maze has no nodes yet."); return; }
        var nodeList = new List<MazeGenerator.MazeNode>(maze.nodes.Values);
        var rng = maze.useSeed ? new System.Random(maze.seed + 777) : new System.Random();
        for (int i = 0; i < enemyCount; i++)
        {
            var node = nodeList[rng.Next(nodeList.Count)];
            var pos = node.worldPos + Vector3.up * 0.5f;
            GameObject inst = enemyPrefab ? Instantiate(enemyPrefab, maze.transform) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            if (!enemyPrefab)
            {
                inst.transform.SetParent(maze.transform);
                var col = inst.GetComponent<Collider>(); if (col) Destroy(col);
                inst.name = $"EnemyGhost_{i}";
            }
            inst.transform.localPosition = pos;

            // Attach controller if present
            var ghostType = Type.GetType("EnemyGhostController");
            if (ghostType != null)
            {
                var ghost = inst.GetComponent(ghostType) ?? inst.AddComponent(ghostType);
                ghostType.GetField("maze")?.SetValue(ghost, maze);
                ghostType.GetField("retargetInterval")?.SetValue(ghost, enemyRetargetInterval);
                ghostType.GetField("chaseChance")?.SetValue(ghost, enemyChaseChance);
                bool fullGraph = !maze.pointsOnlyAtIntersections || useFullNodeGraph;
                ghostType.GetField("fullNodeGraph")?.SetValue(ghost, fullGraph);
            }
            _spawned.Add(inst);
        }
    }

    [ContextMenu("Clear Enemies")] public void Clear()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i]) Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }
}
