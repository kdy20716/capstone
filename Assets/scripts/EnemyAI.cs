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
    public float hitDelay = 0.12f;

    private Ball currentBall;
    private float hitCooldown = 0f;
    private Vector3 startPosition;
    private bool isServing = false;
    private bool isBallTossed = false;
    private bool isServeHitScheduled = false;
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
                diffSpeedMult = 0.82f;
                diffFlightMult = 1.20f;
                break;
            case 1: // NORMAL
                diffSpeedMult = 1.05f;
                diffFlightMult = 1.0f;
                break;
            case 2: // HARD
                diffSpeedMult = 1.35f;
                diffFlightMult = 0.82f;
                break;
        }

        float charSpeedBase = 4.8f;
        float charMinFlight = 0.9f;
        float charMaxFlight = 1.4f;
        Color outfitTint = Color.white;

        switch (currentOpponentIndex)
        {
            case 0: // EMERALD RIVAL (Balanced)
                charSpeedBase = 4.8f;
                charMinFlight = 0.92f;
                charMaxFlight = 1.40f;
                outfitTint = Color.white;
                break;
            case 1: // SKY SPEEDSTER (Fast Footwork)
                charSpeedBase = 6.0f;
                charMinFlight = 0.95f;
                charMaxFlight = 1.45f;
                outfitTint = new Color(0.55f, 0.85f, 1.0f, 1f);
                break;
            case 2: // CRIMSON BASHER (Power Shots)
                charSpeedBase = 4.4f;
                charMinFlight = 0.78f;
                charMaxFlight = 1.18f;
                outfitTint = new Color(1.0f, 0.58f, 0.62f, 1f);
                break;
            case 3: // GOLDEN CHAMPION (All-Round Pro)
                charSpeedBase = 5.8f;
                charMinFlight = 0.80f;
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
        StopAllCoroutines();
        isServing = true;
        isBallTossed = false;
        isServeHitScheduled = false;
        serveTimer = 1.1f;
        transform.position = startPosition;

        if (currentBall == null) FindBall();

        if (currentBall != null)
        {
            currentBall.SetVisible(true);
            currentBall.SetKinematic(true);
            Vector3 servePos = (servePoint != null) ? servePoint.position : (transform.position + new Vector3(0.3f, 1.1f, 1.2f));
            currentBall.transform.position = servePos;
            currentBall.ResetBounceCount();
        }
    }

    public void ResetToStart()
    {
        StopAllCoroutines();
        isServing = false;
        isBallTossed = false;
        isServeHitScheduled = false;
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

        if (hitCooldown > 0f) hitCooldown -= Time.deltaTime;

        if (isServing)
        {
            HandleAIServe();
            return;
        }

        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        bool isBallComing = (ballRb != null && !ballRb.isKinematic && ballRb.linearVelocity.z < -0.2f);

        Vector3 targetPosition;
        if (isBallComing)
        {
            float targetX = Mathf.Clamp(currentBall.transform.position.x, -5.5f, 5.5f);
            float targetZ = Mathf.Clamp(currentBall.transform.position.z - 0.6f, -27.2f, -18.5f);
            targetPosition = new Vector3(targetX, transform.position.y, targetZ);
        }
        else
        {
            targetPosition = new Vector3(startPosition.x, transform.position.y, startPosition.z);
        }

        Vector3 oldPos = transform.position;
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        if (animator != null && animator.runtimeAnimatorController != null && Time.deltaTime > 0.0001f)
        {
            Vector3 velocity = (transform.position - oldPos) / Time.deltaTime;
            Vector3 relativeVelocity = transform.InverseTransformDirection(velocity);

            float moveX = Mathf.Clamp(relativeVelocity.x / Mathf.Max(0.1f, moveSpeed), -1f, 1f);
            float moveZ = Mathf.Clamp(relativeVelocity.z / Mathf.Max(0.1f, moveSpeed), -1f, 1f);

            animator.SetFloat("MoveX", moveX, 0.1f, Time.deltaTime);
            animator.SetFloat("MoveZ", moveZ, 0.1f, Time.deltaTime);
        }

        // AI 타격 거리 판정: racketCenter가 땅 밑에 있더라도 AI 몸통 중심(높이 +1.1m) 기준으로 정확히 거리 계산
        Vector3 aiChestPos = transform.position + Vector3.up * 1.1f;
        Vector3 ballPos = currentBall.transform.position;
        float horizontalDist = Vector2.Distance(new Vector2(aiChestPos.x, aiChestPos.z), new Vector2(ballPos.x, ballPos.z));
        float verticalDiff = Mathf.Abs(ballPos.y - aiChestPos.y);

        float effectiveRange = Mathf.Max(hitRange, 3.2f);
        if (isBallComing && hitCooldown <= 0f && horizontalDist <= effectiveRange && verticalDiff <= 2.6f)
        {
            EnemyHit();
        }
    }

    void HandleAIServe()
    {
        if (!isBallTossed)
        {
            Vector3 servePos = (servePoint != null) ? servePoint.position : (transform.position + new Vector3(0.3f, 1.1f, 1.2f));
            currentBall.transform.position = servePos;
            serveTimer -= Time.deltaTime;
            if (serveTimer <= 0f)
            {
                TossBall();
            }
        }
        else if (!isServeHitScheduled)
        {
            Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
            if (ballRb != null && ballRb.linearVelocity.y <= 0.8f)
            {
                isServeHitScheduled = true;
                EnemyHit();
            }
        }
    }

    void TossBall()
    {
        if (currentBall == null) return;
        isBallTossed = true;
        isServeHitScheduled = false;
        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.isKinematic = false;
            ballRb.linearVelocity = Vector3.up * tossForce;
        }
    }

    void EnemyHit()
    {
        hitCooldown = 0.9f;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetTrigger("hit");
        }
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

        if (currentBall != null)
        {
            currentBall.PlayHitSound();
        }

        isServing = false;
        isBallTossed = false;
        isServeHitScheduled = false;
        currentBall.ResetBounceCount();
    }

    Vector3 GetRandomTargetPoint()
    {
        if (targetCourtArea == null)
        {
            return new Vector3(Random.Range(-3f, 3f), 0.05f, Random.Range(-7f, -4f));
        }
        Bounds bounds = targetCourtArea.bounds;
        float marginX = bounds.size.x * 0.15f;
        float marginZ = bounds.size.z * 0.15f;
        float randomX = Random.Range(bounds.min.x + marginX, bounds.max.x - marginX);
        float randomZ = Random.Range(bounds.min.z + marginZ, bounds.max.z - marginZ);
        return new Vector3(randomX, 0.05f, randomZ);
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
