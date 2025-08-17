using UnityEditor;
using UnityEngine;

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
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Clear Maze");
            ClearChildren(gen.transform);
            EditorUtility.SetDirty(gen.gameObject);
        }
    }

    private void ClearChildren(Transform root)
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (Transform t in root) list.Add(t.gameObject);
        foreach (var go in list) Undo.DestroyObjectImmediate(go);
    }
}
