using UnityEngine;
using UnityEditor;

public class TennisUIGenerator : EditorWindow
{
    [MenuItem("Tools/Generate Tennis UI")]
    public static void GenerateUI()
    {
        SetupMenuAndVRIndependence.Execute();
    }
}
