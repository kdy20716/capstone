using UnityEngine;

public class Ball : MonoBehaviour
{
    [Header("Launch Settings")]
    public float speed = 5f;

    [Header("Bounce Settings")]
    public float fixedBounceHeight = 1.1f;

    [Header("Physics Settings")]
    public float minHorizontalSpeed = 3f;
    public float dragOnFloor = 0.98f;

    private Rigidbody rb;
    private GameObject lastHitter;
    private int playerSideBounceCount = 0;
    private int enemySideBounceCount = 0;

    private const float NET_Z = -13.8f;

    public void ResetBounceCount()
    {
        playerSideBounceCount = 0;
        enemySideBounceCount = 0;
    }

    public void SetKinematic(bool kinematic)
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (kinematic)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
            }
            else
            {
                rb.isKinematic = false;
            }
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.05f;

        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted)
        {
            SetKinematic(true);
        }
    }

    void FixedUpdate()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted || GameManager.Instance.isGamePaused) return;
        if (rb == null || rb.isKinematic) return;

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        if (currentSpeed < minHorizontalSpeed && Mathf.Abs(rb.linearVelocity.y) < 0.2f)
        {
            Vector3 nudgeDir = horizontalVelocity.normalized;
            if (nudgeDir == Vector3.zero)
            {
                nudgeDir = (transform.position.z > NET_Z) ? Vector3.back : Vector3.forward;
            }
            rb.AddForce(nudgeDir * 3f, ForceMode.Acceleration);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (GameManager.Instance != null && GameManager.Instance.isGamePaused) return;

        string layerName = LayerMask.LayerToName(collision.gameObject.layer);

        // 1. 플레이어나 AI 몸에 부딪혔을 때
        if (layerName == "Player" || collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("Enemy"))
        {
            lastHitter = collision.gameObject;

            VRPlayerController vr = collision.gameObject.GetComponent<VRPlayerController>();
            if (vr != null && vr.IsSwinging)
            {
                vr.ApplyHitVelocity();
                return;
            }
        }

        // 2. 'Floor' 또는 'Court'에 부딪혔을 때
        bool isCourtOrFloor = layerName == "Floor" || layerName == "Court"
            || collision.gameObject.CompareTag("Court")
            || collision.gameObject.name.IndexOf("Court", System.StringComparison.OrdinalIgnoreCase) >= 0
            || collision.gameObject.name.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0
            || collision.gameObject.name.IndexOf("pPlane", System.StringComparison.OrdinalIgnoreCase) >= 0;

        if (isCourtOrFloor)
        {
            VRPlayerController vrPlayer = Object.FindFirstObjectByType<VRPlayerController>();
            if (vrPlayer != null)
            {
                vrPlayer.OnServeMissed();
            }

            if (collision.contacts.Length > 0 && collision.contacts[0].normal.y > 0.3f)
            {
                Vector3 currentVelocity = rb.linearVelocity;
                float requiredUpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * fixedBounceHeight);

                float hX = currentVelocity.x;
                float hZ = currentVelocity.z;
                Vector3 hVel = new Vector3(hX, 0, hZ);

                if (hVel.magnitude < minHorizontalSpeed)
                {
                    Vector3 boostDir = hVel.normalized;
                    if (boostDir == Vector3.zero)
                    {
                        boostDir = (transform.position.z > NET_Z) ? Vector3.back : Vector3.forward;
                    }
                    hX = boostDir.x * minHorizontalSpeed;
                    hZ = boostDir.z * minHorizontalSpeed;
                }

                rb.linearVelocity = new Vector3(hX, requiredUpVelocity, hZ);

                if (GameManager.Instance != null && GameManager.Instance.isGameStarted)
                {
                    if (transform.position.z > NET_Z)
                    {
                        playerSideBounceCount++;
                        if (playerSideBounceCount >= 2)
                        {
                            if (GameManager.Instance.playerBackTrigger != null)
                                GameManager.Instance.NotifyBallOut(GameManager.Instance.playerBackTrigger);
                        }
                    }
                    else
                    {
                        enemySideBounceCount++;
                        if (enemySideBounceCount >= 2)
                        {
                            if (GameManager.Instance.enemyBackTrigger != null)
                                GameManager.Instance.NotifyBallOut(GameManager.Instance.enemyBackTrigger);
                        }
                    }
                }
            }
        }
        else if ((layerName == "Net" || collision.gameObject.CompareTag("Net")) && collision.gameObject.name.IndexOf("Racket", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            rb.linearVelocity *= 0.5f;
        }
    }
}
