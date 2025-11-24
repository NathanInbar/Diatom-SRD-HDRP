using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(Transform))]
public class RandomChildRotationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        Transform parentTransform = (Transform)target;

        if (GUILayout.Button("Apply Random Rotation to Children"))
        {
            ApplyRandomRotation(parentTransform);
        }
    }

    private void ApplyRandomRotation(Transform parent)
    {
        Undo.RecordObject(parent, "Randomize Child Rotations");

        foreach (Transform child in parent)
        {
            Undo.RecordObject(child, "Randomize Rotation");
            child.localRotation = Random.rotation;
        }

        EditorUtility.SetDirty(parent);
    }
}
#endif