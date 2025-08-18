using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(MazeGenerator))]
public class MazeGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gen = (MazeGenerator)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Maze Controls", EditorStyles.boldLabel);
        if (GUILayout.Button("Generate Maze"))
        {
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Generate Maze");
            gen.Generate();
            EditorUtility.SetDirty(gen.gameObject);
        }
        if (GUILayout.Button("Clear Children"))
        {
            ClearChildrenWithUndo(gen);
            EditorUtility.SetDirty(gen.gameObject);
        }
    }

    private void ClearChildrenWithUndo(MazeGenerator gen)
    {
        var root = gen.transform;
        int count = root.childCount;
        if (count == 0) return;

        // For large numbers of children, doing per-object undo can overflow memory.
        const int perObjectUndoThreshold = 400; // tweakable

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear Maze");

        // If using pooling, just deactivate children (cheap) and record root once.
        if (gen.useWallPooling)
        {
            Undo.RegisterCompleteObjectUndo(root.gameObject, "Clear Maze (pooled)");
            // Deactivate (do not destroy) so pool can reuse
            List<GameObject> temp = new List<GameObject>();
            foreach (Transform t in root) temp.Add(t.gameObject);
            foreach (var go in temp)
            {
                if (go != null && go != gen.gameObject) go.SetActive(false);
            }
            Undo.CollapseUndoOperations(group);
            return;
        }

        List<GameObject> children = new List<GameObject>();
        foreach (Transform t in root)
        {
            if (t.gameObject == gen.gameObject) continue;
            children.Add(t.gameObject);
        }

        if (children.Count > perObjectUndoThreshold)
        {
            // Single snapshot of full hierarchy, then raw destroy (fewer undo records, avoids overflow)
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Clear Maze (bulk)");
            foreach (var go in children)
            {
                if (go != null) GameObject.DestroyImmediate(go);
            }
        }
        else
        {
            // Safe count: record each destruction so undo resurrects objects individually
            foreach (var go in children)
            {
                if (go != null) Undo.DestroyObjectImmediate(go);
            }
        }
        Undo.CollapseUndoOperations(group);
    }
}
