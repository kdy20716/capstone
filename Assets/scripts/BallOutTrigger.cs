using UnityEngine;

public class BallOutTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball") || other.GetComponent<Ball>() != null)
        {
            HandleBallOut();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ball") || collision.gameObject.GetComponent<Ball>() != null)
        {
            HandleBallOut();
        }
    }

    private void HandleBallOut()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyBallOut(this.gameObject);
        }
    }
}
