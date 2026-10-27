using UnityEngine;
using UnityEditor;

public class VRSceneSetup : Editor
{
    [MenuItem("Tools/Setup VR Scene")]
    public static void SetupVRScene()
    {
        SetupMenuAndVRIndependence.Execute();
    }
}
