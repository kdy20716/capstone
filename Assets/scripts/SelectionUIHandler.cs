using UnityEngine;

public class SelectionUIHandler : MonoBehaviour
{
    void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.BindAllUIButtons();
        }
    }
}
