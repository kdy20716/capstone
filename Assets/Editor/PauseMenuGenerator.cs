using UnityEngine;
using UnityEditor;

public class PauseMenuGenerator : Editor
{
    [MenuItem("Tools/Generate Pause Menu UI")]
    public static void GeneratePauseMenu()
    {
        SetupMenuAndVRIndependence.Execute();
    }
}
