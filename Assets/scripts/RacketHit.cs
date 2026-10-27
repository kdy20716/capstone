using UnityEngine;

public class RacketHit : MonoBehaviour
{
    private VRPlayerController vrPlayer;
    private float lastHitTime = 0f;

    void Start()
    {
        vrPlayer = Object.FindFirstObjectByType<VRPlayerController>();
    }

    private void TryHit(GameObject ballObj)
    {
        if (Time.unscaledTime - lastHitTime < 0.25f) return;
        lastHitTime = Time.unscaledTime;

        if (vrPlayer == null) vrPlayer = Object.FindFirstObjectByType<VRPlayerController>();
        if (vrPlayer != null)
        {
            vrPlayer.OnRacketHit();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ball") || collision.gameObject.GetComponent<Ball>() != null)
        {
            TryHit(collision.gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball") || other.GetComponent<Ball>() != null)
        {
            TryHit(other.gameObject);
        }
    }
}
