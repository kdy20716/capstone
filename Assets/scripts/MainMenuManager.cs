using UnityEngine;

public class MainMenuManager : MonoBehaviour
{
    void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.BindAllUIButtons();
        }
    }
}
