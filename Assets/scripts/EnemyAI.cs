using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 4.5f;

    [Header("Hit Settings")]
    public BoxCollider targetCourtArea;
    public Transform racketCenter;
    public float hitRange = 3.5f;

    public float minFlightTime = 0.9f;
    public float maxFlightTime = 1.4f;

    [Header("Net Settings")]
    public Transform netPoint;
    public float netClearance = 0.5f;

    [Header("Serve Settings")]
    public Transform servePoint;
    public float tossForce = 5f;

    [Header("Animation")]
    public Animator animator;

    [Header("Timing Settings")]
    public float hitDelay = 0.15f;

    private Ball currentBall;
    private float hitCooldown = 0f;
    private Vector3 startPosition;
    private bool isServing = false;
    private bool isBallTossed = false;
    private float serveTimer = 0f;
    private int currentDifficulty = 1;
    private int currentOpponentIndex = 0;

    void Start()
    {
        startPosition = transform.position;
        FindBall();
        if (GameManager.Instance != null)
        {
            ApplyOpponentPreset(GameManager.Instance.selectedCharacterIndex, GameManager.Instance.aiDifficulty);
        }
    }

    public void ApplyDifficulty(int difficultyIndex)
    {
        currentDifficulty = Mathf.Clamp(difficultyIndex, 0, 2);
        ApplyOpponentPreset(currentOpponentIndex, currentDifficulty);
    }

    public void ApplyOpponentPreset(int opponentIndex, int difficultyIndex)
    {
        currentOpponentIndex = ((opponentIndex % 4) + 4) % 4;
        currentDifficulty = Mathf.Clamp(difficultyIndex, 0, 2);

        float diffSpeedMult = 1.0f;
        float diffFlightMult = 1.0f;

        switch (currentDifficulty)
        {
            case 0: // EASY
                diffSpeedMult = 0.78f;
                diffFlightMult = 1.22f;
                break;
            case 1: // NORMAL
                diffSpeedMult = 1.0f;
                diffFlightMult = 1.0f;
                break;
            case 2: // HARD
                diffSpeedMult = 1.32f;
                diffFlightMult = 0.82f;
                break;
        }

        float charSpeedBase = 4.5f;
        float charMinFlight = 0.9f;
        float charMaxFlight = 1.4f;
        Color outfitTint = Color.white;

        switch (currentOpponentIndex)
        {
            case 0: // EMERALD RIVAL (Balanced)
                charSpeedBase = 4.5f;
                charMinFlight = 0.92f;
                charMaxFlight = 1.40f;
                outfitTint = Color.white;
                break;
            case 1: // SKY SPEEDSTER (Fast Footwork)
                charSpeedBase = 5.8f;
                charMinFlight = 0.95f;
                charMaxFlight = 1.45f;
                outfitTint = new Color(0.55f, 0.85f, 1.0f, 1f);
                break;
            case 2: // CRIMSON BASHER (Power Shots)
                charSpeedBase = 4.1f;
                charMinFlight = 0.76f;
                charMaxFlight = 1.18f;
                outfitTint = new Color(1.0f, 0.58f, 0.62f, 1f);
                break;
            case 3: // GOLDEN CHAMPION (All-Round Pro)
                charSpeedBase = 5.5f;
                charMinFlight = 0.78f;
                charMaxFlight = 1.20f;
                outfitTint = new Color(1.0f, 0.88f, 0.45f, 1f);
                break;
        }

        moveSpeed = charSpeedBase * diffSpeedMult;
        minFlightTime = Mathf.Max(0.55f, charMinFlight * diffFlightMult);
        maxFlightTime = Mathf.Max(minFlightTime + 0.2f, charMaxFlight * diffFlightMult);

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", outfitTint);
            mpb.SetColor("_Color", outfitTint);
            r.SetPropertyBlock(mpb);
        }
    }

    void FindBall()
    {
        currentBall = Object.FindFirstObjectByType<Ball>();
        if (currentBall != null)
        {
            Collider ballCollider = currentBall.GetComponent<Collider>();
            Collider myCollider = GetComponent<Collider>();
            if (ballCollider != null && myCollider != null)
            {
                Physics.IgnoreCollision(ballCollider, myCollider);
            }
        }
    }

    public void PrepareServe()
    {
        isServing = true;
        isBallTossed = false;
        serveTimer = 1.5f;
        if (currentBall == null) FindBall();

        if (currentBall != null)
        {
            currentBall.SetKinematic(true);
            if (servePoint != null) currentBall.transform.position = servePoint.position;
            currentBall.ResetBounceCount();
        }
    }

    public void ResetToStart()
    {
        isServing = false;
        isBallTossed = false;
        transform.position = startPosition;
    }

    void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted || GameManager.Instance.isGamePaused) return;
        if (currentBall == null)
        {
            FindBall();
            if (currentBall == null) return;
        }

        if (isServing)
        {
            HandleAIServe();
            return;
        }

        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        bool isBallComing = (ballRb != null && ballRb.linearVelocity.z < -0.2f);

        Vector3 targetPosition;
        if (isBallComing)
        {
            float targetX = Mathf.Clamp(currentBall.transform.position.x, -5.0f, 5.0f);
            float targetZ = Mathf.Clamp(currentBall.transform.position.z - 0.8f, -27.0f, -19.0f);
            targetPosition = new Vector3(targetX, transform.position.y, targetZ);
        }
        else
        {
            targetPosition = new Vector3(startPosition.x, transform.position.y, startPosition.z);
        }

        Vector3 oldPos = transform.position;
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        if (animator != null && Time.deltaTime > 0.0001f)
        {
            Vector3 velocity = (transform.position - oldPos) / Time.deltaTime;
            Vector3 relativeVelocity = transform.InverseTransformDirection(velocity);

            float moveX = Mathf.Clamp(relativeVelocity.x / moveSpeed, -1f, 1f);
            float moveZ = Mathf.Clamp(relativeVelocity.z / moveSpeed, -1f, 1f);

            animator.SetFloat("MoveX", moveX, 0.1f, Time.deltaTime);
            animator.SetFloat("MoveZ", moveZ, 0.1f, Time.deltaTime);
        }

        if (hitCooldown > 0) hitCooldown -= Time.deltaTime;

        Transform hitOrigin = (racketCenter != null) ? racketCenter : transform;
        float distance = Vector3.Distance(hitOrigin.position, currentBall.transform.position);
        if (distance <= hitRange && hitCooldown <= 0f && isBallComing)
        {
            EnemyHit();
        }
    }

    void HandleAIServe()
    {
        if (!isBallTossed)
        {
            if (servePoint != null) currentBall.transform.position = servePoint.position;
            serveTimer -= Time.deltaTime;
            if (serveTimer <= 0)
            {
                TossBall();
            }
        }
        else
        {
            Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
            if (ballRb != null && ballRb.linearVelocity.y < 0.5f)
            {
                EnemyHit();
                isServing = false;
                isBallTossed = false;
            }
        }
    }

    void TossBall()
    {
        if (currentBall == null) return;
        isBallTossed = true;
        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.isKinematic = false;
            ballRb.linearVelocity = Vector3.up * tossForce;
        }
    }

    void EnemyHit()
    {
        hitCooldown = 1.2f;
        if (animator != null) animator.SetTrigger("hit");
        StartCoroutine(DelayedHitRoutine());
    }

    private System.Collections.IEnumerator DelayedHitRoutine()
    {
        yield return new WaitForSeconds(hitDelay);
        if (currentBall != null && (GameManager.Instance == null || !GameManager.Instance.isGamePaused))
        {
            ApplyHitVelocity();
        }
    }

    private void ApplyHitVelocity()
    {
        if (currentBall == null) return;
        Vector3 randomTarget = GetRandomTargetPoint();
        float randomFlightTime = Random.Range(minFlightTime, maxFlightTime);

        Vector3 exactVelocity = CalculateVelocity(randomTarget, currentBall.transform.position, randomFlightTime);
        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.isKinematic = false;
            ballRb.linearVelocity = exactVelocity;
        }

        isServing = false;
        isBallTossed = false;
        currentBall.ResetBounceCount();
    }

    Vector3 GetRandomTargetPoint()
    {
        if (targetCourtArea == null)
        {
            return new Vector3(Random.Range(-3f, 3f), 0.1f, Random.Range(-7f, -4f));
        }
        Bounds bounds = targetCourtArea.bounds;
        float marginX = bounds.size.x * 0.15f;
        float marginZ = bounds.size.z * 0.15f;
        float randomX = Random.Range(bounds.min.x + marginX, bounds.max.x - marginX);
        float randomZ = Random.Range(bounds.min.z + marginZ, bounds.max.z - marginZ);
        return new Vector3(randomX, bounds.min.y, randomZ);
    }

    Vector3 CalculateVelocity(Vector3 target, Vector3 origin, float time)
    {
        Vector3 distance = target - origin;
        Vector3 distanceXZ = distance;
        distanceXZ.y = 0f;
        float distXZMag = distanceXZ.magnitude;
        if (distXZMag < 0.001f) return Vector3.up * 5f;

        float gravity = Mathf.Abs(Physics.gravity.y);
        float flightTime = Mathf.Max(0.5f, time);

        if (netPoint != null)
        {
            Vector3 toNet = netPoint.position - origin;
            toNet.y = 0f;
            float netDist = toNet.magnitude;

            if (netDist > 0.1f && netDist < distXZMag)
            {
                float alpha = netDist / distXZMag;
                float targetNetHeight = netPoint.position.y + netClearance;
                float requiredNetArc = targetNetHeight - origin.y - (target.y - origin.y) * alpha;

                if (requiredNetArc > 0f)
                {
                    float denom = 0.5f * gravity * alpha * (1f - alpha);
                    if (denom > 0.001f)
                    {
                        float minTime = Mathf.Sqrt(requiredNetArc / denom);
                        if (flightTime < minTime)
                        {
                            flightTime = minTime;
                        }
                    }
                }
            }
        }

        float Vxz = distXZMag / flightTime;
        float Vy = (target.y - origin.y) / flightTime + 0.5f * gravity * flightTime;

        Vector3 result = distanceXZ.normalized * Vxz;
        result.y = Vy;
        return result;
    }
}
