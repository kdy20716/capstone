using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using System.Collections;

public class VRPlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float slideSpeed = 12f;
    public float slideDuration = 0.4f;
    public Transform characterModel;
    public BoxCollider moveArea;
    public Transform rigTransform;

    [Header("Hit Settings")]
    public BoxCollider opponentCourtArea;
    public Transform racketCenter;
    public float hitRange = 2.4f;
    public float minFlightTime = 1.0f;
    public float maxFlightTime = 1.6f;
    [Range(0, 100)] public float powerShotChance = 15f;
    public float swingVelocityThreshold = 1.1f;

    [Header("Racket Selection")]
    public GameObject[] rackets;

    [Header("Net Settings")]
    public Transform netPoint;
    public float netClearance = 0.5f;

    [Header("Serve Settings")]
    public Transform servePoint;
    public float tossForce = 5f;
    public Transform vrLeftHand;
    public Transform vrRightHand;
    public float vrTossThreshold = 0.45f;

    [Header("Animation")]
    public Animator animator;
    public float hitDelay = 0.08f;
    public bool IsSwinging { get; private set; }

    [Header("VR Input Actions")]
    public InputActionProperty moveAction;
    public InputActionProperty slideAction;
    public InputActionProperty tossAction;

    private Ball currentBall;
    private bool isSliding = false;
    private bool isServing = false;
    private bool isBallTossed = false;
    private Vector3 lastLeftHandPos;
    private Vector3 lastRightHandPos;
    private Vector3 startPosition = new Vector3(-0.06f, 0f, -1.499f);
    private Quaternion startRotation = Quaternion.Euler(0f, 180f, 0f);
    private float currentRallySpeedMultiplier = 1.0f;
    private float lastPhysicalHitTime = 0f;
    private float baseMoveSpeed = 5f;

    void Start()
    {
        if (rigTransform == null)
        {
            if (transform.parent != null && (transform.parent.name.Contains("XR Origin") || transform.parent.name.Contains("Rig")))
                rigTransform = transform.parent;
            else if (transform.root != null && (transform.root.name.Contains("XR Origin") || transform.root.name.Contains("Rig")))
                rigTransform = transform.root;
            else
                rigTransform = transform;
        }

        startPosition = new Vector3(-0.06f, 0f, -1.499f);
        startRotation = Quaternion.Euler(0f, 180f, 0f);
        baseMoveSpeed = moveSpeed;
        FindBall();
        SetupControllersAndActions();

        if (vrLeftHand != null) lastLeftHandPos = vrLeftHand.position;
        if (vrRightHand != null) lastRightHandPos = vrRightHand.position;

        if (GameManager.Instance != null)
        {
            SelectRacket(GameManager.Instance.selectedRacketIndex);
            ApplyCharacterPreset(GameManager.Instance.selectedCharacterIndex);
        }
    }

    void LateUpdate()
    {
        // 로비/오버뷰 시점(게임 시작 전)에서는 XR Origin이 상단 뷰에 있더라도 캐릭터 모델은 코트 베이스라인에 서 있도록 유지
        if (GameManager.Instance != null && !GameManager.Instance.isGameStarted)
        {
            transform.position = new Vector3(-0.06f, 1.013f, -1.499f);
            transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        }
        else if (rigTransform != null && transform.parent == rigTransform)
        {
            transform.localPosition = new Vector3(0f, 1.013f, 0f);
            transform.localRotation = Quaternion.identity;
        }
    }

    public void SelectRacket(int index)
    {
        if (rackets == null || rackets.Length == 0) return;
        int safeIndex = Mathf.Clamp(index, 0, rackets.Length - 1);

        for (int i = 0; i < rackets.Length; i++)
        {
            if (rackets[i] != null)
            {
                bool active = (i == safeIndex);
                rackets[i].SetActive(active);

                // UI 레이캐스트를 가리지 않도록 라켓 콜라이더를 Trigger로 유지
                Collider col = rackets[i].GetComponent<Collider>();
                if (col != null) col.isTrigger = true;

                if (active)
                {
                    Transform childCenter = rackets[i].transform.Find("Strings");
                    if (childCenter == null) childCenter = rackets[i].transform.Find("Center");
                    racketCenter = (childCenter != null) ? childCenter : rackets[i].transform;
                }
            }
        }
    }

    public void ApplyCharacterPreset(int charIndex)
    {
        // 캐릭터 인덱스에 따른 능력치 및 외형 컬러 프리셋 적용
        float speedBonus = 1.0f;
        Color tintColor = Color.white;

        switch (charIndex % 4)
        {
            case 0: // 에메랄드 히로인 (밸런스)
                speedBonus = 1.0f;
                powerShotChance = 15f;
                tintColor = Color.white;
                break;
            case 1: // 스카이 스트라이커 (스피드)
                speedBonus = 1.25f;
                powerShotChance = 10f;
                tintColor = new Color(0.7f, 0.9f, 1.0f, 1f);
                break;
            case 2: // 크림슨 스매셔 (파워)
                speedBonus = 0.92f;
                powerShotChance = 35f;
                tintColor = new Color(1.0f, 0.72f, 0.75f, 1f);
                break;
            case 3: // 골든 챔피언 (올라운더)
                speedBonus = 1.12f;
                powerShotChance = 25f;
                tintColor = new Color(1.0f, 0.92f, 0.65f, 1f);
                break;
        }

        float settingMultiplier = (GameManager.Instance != null) ? GameManager.Instance.playerSpeedMultiplier : 1.0f;
        moveSpeed = baseMoveSpeed * speedBonus * settingMultiplier;

        SkinnedMeshRenderer smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.material != null)
        {
            if (smr.material.HasProperty("_BaseColor"))
                smr.material.SetColor("_BaseColor", tintColor);
            else if (smr.material.HasProperty("_Color"))
                smr.material.color = tintColor;
        }
    }

    void SetupControllersAndActions()
    {
        if (vrLeftHand == null && rigTransform != null)
        {
            Transform leftTf = rigTransform.Find("Camera Offset/Left Controller");
            if (leftTf != null) vrLeftHand = leftTf;
        }
        if (vrRightHand == null && rigTransform != null)
        {
            Transform rightTf = rigTransform.Find("Camera Offset/Right Controller");
            if (rightTf != null) vrRightHand = rightTf;
        }

        // VR 컨트롤러 전용 입력 바인딩
        if (moveAction.action == null || moveAction.action.bindings.Count == 0)
        {
            var action = new InputAction("VRMove", InputActionType.Value);
            action.AddBinding("<XRController>{LeftHand}/thumbstick");
            action.AddBinding("<XRController>{LeftHand}/primary2DAxis");
            action.Enable();
            moveAction = new InputActionProperty(action);
        }
        else
        {
            moveAction.action.Enable();
        }

        if (slideAction.action == null || slideAction.action.bindings.Count == 0)
        {
            var action = new InputAction("VRSlide", InputActionType.Button);
            action.AddBinding("<XRController>{LeftHand}/primaryButton");
            action.AddBinding("<XRController>{LeftHand}/gripPressed");
            action.Enable();
            slideAction = new InputActionProperty(action);
        }
        else
        {
            slideAction.action.Enable();
        }

        if (tossAction.action == null || tossAction.action.bindings.Count == 0)
        {
            var action = new InputAction("VRTossAndSwing", InputActionType.Button);
            action.AddBinding("<XRController>{LeftHand}/triggerPressed");
            action.AddBinding("<XRController>{RightHand}/triggerPressed");
            action.AddBinding("<XRController>{RightHand}/primaryButton");
            action.Enable();
            tossAction = new InputActionProperty(action);
        }
        else
        {
            tossAction.action.Enable();
        }
    }

    private bool IsVRTriggerOrSwingButtonPressed()
    {
        if (tossAction.action != null && (tossAction.action.triggered || tossAction.action.IsPressed()))
            return true;
        return false;
    }

    private bool IsVRSlidePressed()
    {
        if (slideAction.action != null && slideAction.action.triggered)
            return true;
        return false;
    }

    private Vector2 ReadVRMoveInput()
    {
        if (moveAction.action != null)
        {
            return moveAction.action.ReadValue<Vector2>();
        }
        return Vector2.zero;
    }

    void Update()
    {
        if (currentBall == null) FindBall();

        Vector2 input = ReadVRMoveInput();
        float moveX = input.x;
        float moveZ = input.y;

        if (animator != null)
        {
            float animSpeed = isSliding ? 1.5f : 1.0f;
            animator.SetFloat("MoveX", moveX * animSpeed, 0.1f, Time.deltaTime);
            animator.SetFloat("MoveZ", moveZ * animSpeed, 0.1f, Time.deltaTime);
        }

        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted || GameManager.Instance.isGamePaused)
        {
            if (vrLeftHand != null) lastLeftHandPos = vrLeftHand.position;
            if (vrRightHand != null) lastRightHandPos = vrRightHand.position;
            return;
        }

        HandleMovement(moveX, moveZ);
        HandleServeAndHit();

        if (vrLeftHand != null) lastLeftHandPos = vrLeftHand.position;
        if (vrRightHand != null) lastRightHandPos = vrRightHand.position;
    }

    void HandleMovement(float moveX, float moveZ)
    {
        if (isSliding) return;

        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
        // 코트 방향(카메라가 -Z를 바라봄)에 맞춰 조이스틱 입력을 월드 이동으로 변환
        Camera cam = Camera.main;
        Vector3 forward = Vector3.back;
        Vector3 right = Vector3.left;
        if (cam != null)
        {
            forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f) forward.Normalize();
            else forward = Vector3.back;

            right = cam.transform.right;
            right.y = 0f;
            if (right.sqrMagnitude > 0.001f) right.Normalize();
            else right = Vector3.left;
        }

        Vector3 moveDir = (right * moveX + forward * moveZ);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        if (IsVRSlidePressed() && moveDir.sqrMagnitude > 0.05f)
        {
            StartCoroutine(SlideRoutine(moveDir.normalized));
            return;
        }

        targetTf.Translate(moveDir * moveSpeed * Time.deltaTime, Space.World);

        if (moveArea != null)
        {
            Bounds b = moveArea.bounds;
            float clampedX = Mathf.Clamp(targetTf.position.x, b.min.x, b.max.x);
            float clampedZ = Mathf.Clamp(targetTf.position.z, b.min.z, b.max.z);
            targetTf.position = new Vector3(clampedX, targetTf.position.y, clampedZ);
        }
    }

    Vector3 GetServePosition()
    {
        if (vrLeftHand != null && vrLeftHand.position.y > 0.4f && Vector3.Distance(vrLeftHand.position, Vector3.zero) > 0.5f)
        {
            return vrLeftHand.position + Vector3.up * 0.12f;
        }
        if (servePoint != null)
        {
            return servePoint.position;
        }
        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
        return targetTf.position + Vector3.back * 0.8f + Vector3.up * 1.4f;
    }

    void HandleServeAndHit()
    {
        if (currentBall == null)
        {
            FindBall();
            if (currentBall == null) return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.001f);
        float rightHandSpeed = (vrRightHand != null) ? Vector3.Distance(vrRightHand.position, lastRightHandPos) / dt : 0f;

        // 1. 서브 대기 상태 (왼손 또는 서브 포인트에 공 고정)
        if (isServing && !isBallTossed)
        {
            currentBall.transform.position = GetServePosition();

            Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
            if (ballRb != null && !ballRb.isKinematic)
            {
                ballRb.linearVelocity = Vector3.zero;
                ballRb.angularVelocity = Vector3.zero;
                ballRb.isKinematic = true;
            }

            bool tossTriggered = IsVRTriggerOrSwingButtonPressed();
            if (vrLeftHand != null && vrLeftHand.position.y > 0.4f)
            {
                float handVelocityY = (vrLeftHand.position.y - lastLeftHandPos.y) / dt;
                if (handVelocityY > vrTossThreshold) tossTriggered = true;
            }

            if (tossTriggered)
            {
                TossBall();
                SendHaptic(true, 0.4f, 0.1f);
                return;
            }
        }

        // 2. 타격 감지 (서브 토스 후 또는 일반 랠리 중)
        bool canHit = !isServing || (isServing && isBallTossed);

        if (canHit && !IsSwinging && currentBall != null)
        {
            Transform hitOrigin = (racketCenter != null) ? racketCenter : transform;
            float dist = Vector3.Distance(hitOrigin.position, currentBall.transform.position);

            bool swingRequested = IsVRTriggerOrSwingButtonPressed() || (rightHandSpeed >= swingVelocityThreshold);
            Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
            bool isBallApproaching = (ballRb != null && ballRb.linearVelocity.z > 0.2f);

            if ((swingRequested && dist <= (hitRange + 1.8f)) || (dist <= hitRange && isBallApproaching))
            {
                StartCoroutine(DelayedHitRoutine());
            }
        }
    }

    private IEnumerator DelayedHitRoutine()
    {
        IsSwinging = true;
        if (animator != null) animator.SetTrigger("hit");

        yield return new WaitForSeconds(hitDelay);

        if (currentBall != null && (GameManager.Instance == null || !GameManager.Instance.isGamePaused))
        {
            Transform hitOrigin = (racketCenter != null) ? racketCenter : transform;
            float dist = Vector3.Distance(hitOrigin.position, currentBall.transform.position);
            if (dist <= hitRange + 2.0f)
            {
                ApplyHitVelocity();
            }
        }

        yield return new WaitForSeconds(0.2f);
        IsSwinging = false;
    }

    public void PrepareServe()
    {
        isServing = true;
        isBallTossed = false;
        currentRallySpeedMultiplier = 1.0f;
        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
        targetTf.position = startPosition;
        targetTf.rotation = startRotation;

        if (currentBall == null) FindBall();
        if (currentBall != null)
        {
            currentBall.SetKinematic(true);
            currentBall.transform.position = GetServePosition();
            currentBall.ResetBounceCount();
        }
    }

    public void OnServeMissed()
    {
        if (isServing && isBallTossed)
        {
            StartCoroutine(RecoverServeRoutine());
        }
    }

    private IEnumerator RecoverServeRoutine()
    {
        yield return new WaitForSeconds(1.0f);
        if (isServing)
        {
            PrepareServe();
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

    public void ApplyHitVelocity()
    {
        if (currentBall == null) return;

        currentRallySpeedMultiplier += 0.05f;
        bool isPowerShot = Random.Range(0f, 100f) < powerShotChance;

        SendHaptic(false, isPowerShot ? 0.85f : 0.55f, 0.12f);

        Vector3 randomTarget = GetRandomTargetPoint();
        float baseFlightTime = isPowerShot ? minFlightTime * 0.85f : Random.Range(minFlightTime, maxFlightTime);
        float finalFlightTime = baseFlightTime / currentRallySpeedMultiplier;

        if (isPowerShot && opponentCourtArea != null)
        {
            randomTarget = Vector3.Lerp(randomTarget, opponentCourtArea.bounds.center, 0.4f);
        }

        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.isKinematic = false;
            ballRb.linearVelocity = Vector3.zero;

            Vector3 exactVelocity = CalculateVelocity(randomTarget, currentBall.transform.position, finalFlightTime);
            ballRb.linearVelocity = exactVelocity;
        }

        isServing = false;
        isBallTossed = false;
        currentBall.ResetBounceCount();
    }

    private void SendHaptic(bool leftHand, float amplitude, float duration)
    {
        Transform handTf = leftHand ? vrLeftHand : vrRightHand;
        if (handTf != null)
        {
            var haptic = handTf.GetComponentInChildren<HapticImpulsePlayer>();
            if (haptic != null)
            {
                haptic.SendHapticImpulse(amplitude, duration);
            }
        }
    }

    Vector3 GetRandomTargetPoint()
    {
        if (opponentCourtArea == null)
        {
            return new Vector3(Random.Range(-3f, 3f), 0.1f, Random.Range(-24f, -20f));
        }
        Bounds bounds = opponentCourtArea.bounds;
        float marginX = bounds.size.x * 0.15f;
        float marginZ = bounds.size.z * 0.15f;
        float randomX = Random.Range(bounds.min.x + marginX, bounds.max.x - marginX);
        float randomZ = Random.Range(bounds.min.z + marginZ, bounds.max.z - marginZ);
        return new Vector3(randomX, bounds.min.y, randomZ);
    }

    public void OnRacketHit()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted || GameManager.Instance.isGamePaused) return;
        if (Time.unscaledTime - lastPhysicalHitTime < 0.25f) return;
        lastPhysicalHitTime = Time.unscaledTime;

        if (currentBall == null) FindBall();
        if (currentBall == null) return;

        if (isServing && !isBallTossed)
        {
            TossBall();
            SendHaptic(true, 0.4f, 0.1f);
            return;
        }

        ApplyHitVelocity();
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

    IEnumerator SlideRoutine(Vector3 direction)
    {
        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
        if (direction == Vector3.zero) direction = Vector3.back;
        isSliding = true;
        float timer = 0f;
        while (timer < slideDuration)
        {
            targetTf.Translate(direction * slideSpeed * Time.deltaTime, Space.World);
            if (moveArea != null)
            {
                Bounds b = moveArea.bounds;
                float clampedX = Mathf.Clamp(targetTf.position.x, b.min.x, b.max.x);
                float clampedZ = Mathf.Clamp(targetTf.position.z, b.min.z, b.max.z);
                targetTf.position = new Vector3(clampedX, targetTf.position.y, clampedZ);
            }
            timer += Time.deltaTime;
            yield return null;
        }
        isSliding = false;
    }

    public void ResetToStart()
    {
        isServing = false;
        isBallTossed = false;
        if (GameManager.Instance == null || GameManager.Instance.isGameStarted)
        {
            Transform targetTf = (rigTransform != null) ? rigTransform : transform;
            targetTf.position = startPosition;
            targetTf.rotation = startRotation;
        }
    }
}
