using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour wall pooler. Attach as child (or same object) of MazeGenerator.
/// </summary>
[AddComponentMenu("Maze/Wall Pooler")]
public class WallPooler : MonoBehaviour
{
    [Tooltip("Prefab to use for pooled walls. If null, primitive cubes are created.")]
    public GameObject wallPrefab;
    [Tooltip("Optional prewarm size (created on first Initialize or Awake if Auto Initialize). 0 = none.")]
    public int prewarm = 0;
    [Tooltip("Automatically initialize pool on Awake.")]
    public bool autoInitialize = true;

    [SerializeField] private List<GameObject> pool = new List<GameObject>();
    [SerializeField] private int activeCount = 0;
    bool initialized = false;

    public int ActiveCount => activeCount;
    public int TotalCount => pool.Count;

    void Awake()
    {
        if (autoInitialize) Initialize();
    }

    public void Initialize()
    {
        if (initialized) return;
        if (prewarm > 0 && pool.Count == 0)
        {
            Prewarm(prewarm);
        }
        initialized = true;
    }

    public void SetWallPrefab(GameObject prefab)
    {
        wallPrefab = prefab;
    }

    public void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var go = CreateNew();
            go.name = "Wall";
            go.SetActive(false);
            pool.Add(go);
        }
    }

    GameObject CreateNew()
    {
        GameObject go;
        if (wallPrefab != null)
            go = Instantiate(wallPrefab, transform);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.parent = transform;
        }
        go.SetActive(true);
        return go;
    }

    public GameObject Get()
    {
        if (!initialized) Initialize();
        if (activeCount < pool.Count)
        {
            var existing = pool[activeCount];
            if (existing == null)
            {
                existing = CreateNew();
                existing.name = "Wall";
                pool[activeCount] = existing;
            }
            existing.SetActive(true);
            activeCount++;
            return existing;
        }
        var go = CreateNew();
        go.name = "Wall";
        pool.Add(go);
        activeCount++;
        return go;
    }

    public void DeactivateAll()
    {
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            var item = pool[i];
            if (item == null)
            {
                pool.RemoveAt(i);
                continue;
            }
            item.SetActive(false);
        }
        activeCount = 0;
    }

    public void DestroyAll()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null) DestroyImmediate(pool[i]);
        }
        pool.Clear();
        activeCount = 0;
    }
}
