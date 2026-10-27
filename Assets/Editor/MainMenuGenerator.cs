using UnityEngine;
using UnityEditor;

public class MainMenuGenerator : Editor
{
    [MenuItem("Tools/Generate Modern Main Menu")]
    public static void GenerateMenu()
    {
        SetupMenuAndVRIndependence.Execute();
    }
}
