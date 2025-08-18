using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple reusable pool for wall GameObjects. Managed by MazeGenerator.
/// </summary>
[System.Serializable]
public class WallPool
{
    [SerializeField] private List<GameObject> _pool = new List<GameObject>();
    [SerializeField] private int _activeCount = 0;
    private Transform _parent;
    private GameObject _prefab;

    public int ActiveCount => _activeCount;
    public int TotalCount => _pool.Count;

    public void Initialize(Transform parent, GameObject prefab, int prewarm)
    {
        _parent = parent;
        _prefab = prefab;
        if (prewarm > 0 && _pool.Count == 0)
        {
            Prewarm(prewarm);
        }
    }

    public void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var go = CreateNew();
            go.name = "Wall";
            go.SetActive(false);
            _pool.Add(go);
        }
    }

    GameObject CreateNew()
    {
        GameObject go;
        if (_prefab != null)
            go = Object.Instantiate(_prefab, _parent);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.parent = _parent;
        }
        go.SetActive(true);
        return go;
    }

    public GameObject Get()
    {
        if (_activeCount < _pool.Count)
        {
            var existing = _pool[_activeCount];
            if (existing == null)
            {
                existing = CreateNew();
                existing.name = "Wall";
                _pool[_activeCount] = existing;
            }
            existing.SetActive(true);
            _activeCount++;
            return existing;
        }
        var go = CreateNew();
        go.name = "Wall";
        _pool.Add(go);
        _activeCount++;
        return go;
    }

    public void DeactivateAll()
    {
        for (int i = _pool.Count - 1; i >= 0; i--)
        {
            var item = _pool[i];
            if (item == null)
            {
                _pool.RemoveAt(i);
                continue;
            }
            item.SetActive(false);
        }
        _activeCount = 0;
    }

    public void DestroyAll()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i] != null)
                Object.DestroyImmediate(_pool[i]);
        }
        _pool.Clear();
        _activeCount = 0;
    }
}
