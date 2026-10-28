using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using System.Collections;

public class VRPlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5.5f;
    public Transform characterModel;
    public BoxCollider moveArea;
    public Transform rigTransform;

    [Header("Hit Settings")]
    public BoxCollider opponentCourtArea;
    public Transform racketCenter;
    public float hitRange = 2.8f;
    public float minFlightTime = 1.0f;
    public float maxFlightTime = 1.55f;
    [Range(0, 100)] public float powerShotChance = 15f;
    public float swingVelocityThreshold = 0.9f;

    [Header("Racket Selection")]
    public GameObject[] rackets;

    [Header("Net Settings")]
    public Transform netPoint;
    public float netClearance = 0.5f;

    [Header("Serve Settings")]
    public Transform servePoint;
    public float tossForce = 5.2f;
    public Transform vrLeftHand;
    public Transform vrRightHand;
    public float vrTossThreshold = 0.45f;

    [Header("Animation")]
    public Animator animator;
    public float hitDelay = 0.06f;
    public bool IsSwinging { get; private set; }
    public bool IsServing => isServing;
    public bool IsServingTossed => isServing && isBallTossed;

    [Header("Rally Gauge & Super Smash")]
    public float rallyGauge = 0f;
    public UnityEngine.UI.Image rallyGaugeFill;
    public TMPro.TextMeshProUGUI rallyGaugeText;
    public RectTransform rallyGaugeRoot;
    public bool IsSuperSmashReady => rallyGauge >= 0.99f;
    public float lastHitSwingX { get; private set; } = 0f;

    [Header("Sliding & Recovery")]
    public float slideDuration = 0.28f;
    public float slideSpeed = 16f;
    public float postSlideSlowDuration = 2.0f;
    [Range(0.1f, 1.0f)] public float postSlideSpeedMultiplier = 0.45f;
    private bool isPostSlideSlowed = false;

    [Header("Power Shot & Tracking Camera")]
    public float powerShotTimeScale = 0.22f;
    private bool isPowerShotActive = false;
    private Coroutine activePowerShotCoroutine;
    private Vector2 originalGaugePos;
    private GameObject racketSparkObj;
    private float lastRallySparkHaptic = -10f;
    private float lastSlideCooldownTime = -10f;

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
    private float lastPhysicalHitTime = -10f;
    private float lastTossTime = -10f;
    private float baseMoveSpeed = 5.5f;
    private bool hasMoveXParam = false;
    private bool hasMoveZParam = false;
    private bool hasHitParam = false;

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
        baseMoveSpeed = Mathf.Max(5.0f, moveSpeed);
        CacheAnimatorParameters();
        FindBall();
        SetupControllersAndActions();

        if (vrLeftHand != null) lastLeftHandPos = vrLeftHand.position;
        if (vrRightHand != null) lastRightHandPos = vrRightHand.position;

        if (rallyGaugeRoot != null) originalGaugePos = rallyGaugeRoot.anchoredPosition;

        if (GameManager.Instance != null)
        {
            SelectRacket(GameManager.Instance.selectedRacketIndex);
            ApplyCharacterPreset(GameManager.Instance.selectedCharacterIndex);
        }
    }

    private void CacheAnimatorParameters()
    {
        hasMoveXParam = false;
        hasMoveZParam = false;
        hasHitParam = false;
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (var p in animator.parameters)
        {
            if (p.name == "MoveX" && p.type == AnimatorControllerParameterType.Float) hasMoveXParam = true;
            else if (p.name == "MoveZ" && p.type == AnimatorControllerParameterType.Float) hasMoveZParam = true;
            else if (p.name == "hit" && p.type == AnimatorControllerParameterType.Trigger) hasHitParam = true;
        }
    }

    void LateUpdate()
    {
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
        bool showRacket = (GameManager.Instance != null && GameManager.Instance.isGameStarted && !GameManager.Instance.isGamePaused);

        for (int i = 0; i < rackets.Length; i++)
        {
            if (rackets[i] != null)
            {
                bool isSelected = (i == safeIndex);
                rackets[i].SetActive(isSelected && showRacket);

                Collider col = rackets[i].GetComponent<Collider>();
                if (col != null) col.isTrigger = true;

                if (isSelected)
                {
                    Transform childCenter = rackets[i].transform.Find("Strings");
                    if (childCenter == null) childCenter = rackets[i].transform.Find("Center");
                    racketCenter = (childCenter != null) ? childCenter : rackets[i].transform;
                }
            }
        }
    }

    public void SetRacketVisible(bool visible)
    {
        if (rackets == null || rackets.Length == 0) return;
        int idx = (GameManager.Instance != null) ? GameManager.Instance.selectedRacketIndex : 0;
        int safeIndex = Mathf.Clamp(idx, 0, rackets.Length - 1);

        for (int i = 0; i < rackets.Length; i++)
        {
            if (rackets[i] != null)
            {
                rackets[i].SetActive(visible && (i == safeIndex));
            }
        }
    }

    public void ApplyCharacterPreset(int charIndex)
    {
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
        if (smr != null && smr.sharedMaterial != null)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            smr.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", tintColor);
            mpb.SetColor("_Color", tintColor);
            smr.SetPropertyBlock(mpb);
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

        // 1. 이동 액션 (VR 왼손 조이스틱 + 에디터 테스트용 WASD/화살표)
        var moveAct = new InputAction("VRMove", InputActionType.Value);
        moveAct.AddBinding("<XRController>{LeftHand}/thumbstick");
        moveAct.AddBinding("<XRController>{LeftHand}/primary2DAxis");
        moveAct.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAct.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAct.Enable();
        moveAction = new InputActionProperty(moveAct);

        // 2. 슬라이딩 액션 (VR 왼손 그립/X버튼 + Space + Left Shift)
        var slideAct = new InputAction("VRSlide", InputActionType.Button);
        slideAct.AddBinding("<XRController>{LeftHand}/primaryButton");
        slideAct.AddBinding("<XRController>{LeftHand}/gripPressed");
        slideAct.AddBinding("<Keyboard>/space");
        slideAct.AddBinding("<Keyboard>/leftShift");
        slideAct.Enable();
        slideAction = new InputActionProperty(slideAct);

        // 3. 서브 토스 및 스윙 버튼 액션 (VR 양손 트리거/A버튼 + 마우스 좌클릭 + F키)
        var tossAct = new InputAction("VRTossAndSwing", InputActionType.Button);
        tossAct.AddBinding("<XRController>{LeftHand}/triggerPressed");
        tossAct.AddBinding("<XRController>{RightHand}/triggerPressed");
        tossAct.AddBinding("<XRController>{RightHand}/primaryButton");
        tossAct.AddBinding("<Mouse>/leftButton");
        tossAct.AddBinding("<Keyboard>/f");
        tossAct.AddBinding("<Keyboard>/enter");
        tossAct.Enable();
        tossAction = new InputActionProperty(tossAct);
    }

    private bool WasTriggerOrSwingPressedThisFrame()
    {
        if (tossAction.action != null && tossAction.action.WasPressedThisFrame())
            return true;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
        if (Keyboard.current != null && (Keyboard.current.fKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame))
            return true;
        return false;
    }

    private bool WasSlidePressedThisFrame()
    {
        if (slideAction.action != null && slideAction.action.WasPressedThisFrame())
            return true;
        if (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.leftShiftKey.wasPressedThisFrame))
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

        UpdatePCOrVRRacket();
        UpdateRallyGaugeUI();
        UpdateRacketSparkEffect();

        Vector2 input = ReadVRMoveInput();
        float moveX = input.x;
        float moveZ = input.y;

        if (animator != null && hasMoveXParam && hasMoveZParam)
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

    void UpdatePCOrVRRacket()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted || GameManager.Instance.isGamePaused) return;

        Camera cam = Camera.main;
        if (cam == null || vrRightHand == null) return;

        // PC 마우스 조작 모드 지원: 마우스 이동에 따라 라켓의 위치를 자연스럽게 전방에 배치
        if (Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            float normX = (mousePos.x / Mathf.Max(Screen.width, 1)) - 0.5f;
            float normY = (mousePos.y / Mathf.Max(Screen.height, 1)) - 0.5f;

            // VR 장치가 연결되어 손이 능동적으로 움직이고 있는지 확인 (마우스 이동 시 PC 모드로 라켓 연동)
            bool isManualMouseAim = Mouse.current.delta.ReadValue().sqrMagnitude > 0.05f || Mouse.current.leftButton.isPressed;
            if (isManualMouseAim)
            {
                Vector3 desiredLocalPos = new Vector3(0.28f + normX * 0.45f, 0.95f + normY * 0.35f, 0.55f);
                vrRightHand.localPosition = Vector3.Lerp(vrRightHand.localPosition, desiredLocalPos, Time.unscaledDeltaTime * 14f);
                vrRightHand.localRotation = Quaternion.Euler(-15f + normY * 25f, normX * 30f, -10f);
            }
        }
    }

    void HandleMovement(float moveX, float moveZ)
    {
        if (isSliding) return;

        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
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

        if (WasSlidePressedThisFrame() && (Time.unscaledTime - lastSlideCooldownTime > 0.8f))
        {
            lastSlideCooldownTime = Time.unscaledTime;
            Vector3 slideDir = (moveDir.sqrMagnitude > 0.05f) ? moveDir.normalized : forward;
            StartCoroutine(SlideRoutine(slideDir));
            return;
        }

        float effectiveSpeed = isPostSlideSlowed ? (moveSpeed * postSlideSpeedMultiplier) : moveSpeed;
        targetTf.Translate(moveDir * effectiveSpeed * Time.deltaTime, Space.World);

        if (moveArea != null)
        {
            Bounds b = moveArea.bounds;
            float clampedX = Mathf.Clamp(targetTf.position.x, b.min.x, b.max.x);
            float clampedZ = Mathf.Clamp(targetTf.position.z, b.min.z, b.max.z);
            targetTf.position = new Vector3(clampedX, targetTf.position.y, clampedZ);
        }
    }

    public Vector3 GetServePosition()
    {
        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
        Camera cam = Camera.main;
        Vector3 camPos = (cam != null) ? cam.transform.position : (targetTf.position + Vector3.up * 1.36f);

        // VR 왼손 컨트롤러가 실제로 카메라 전방(> 0.45m)에 위치해 있을 때만 왼손 위에 공 배치
        if (vrLeftHand != null && vrLeftHand.position.y > 0.5f)
        {
            Vector2 camXZ = new Vector2(camPos.x, camPos.z);
            Vector2 handXZ = new Vector2(vrLeftHand.position.x, vrLeftHand.position.z);
            float horizDistFromCam = Vector2.Distance(camXZ, handXZ);
            // 카메라 코앞(0.45m 이내)이나 너무 옆으로 벌어진 초기 프리팹 위치(> 0.65m)가 아닐 때만 사용
            if (horizDistFromCam >= 0.42f && horizDistFromCam <= 0.65f && vrLeftHand.position.z < camPos.z - 0.25f)
            {
                return vrLeftHand.position + Vector3.up * 0.12f;
            }
        }

        // 기본 서브 대기 위치: 플레이어 시야 전방 약 1.15m 앞, 왼쪽(왼손 방향)에 배치하여 오른손 라켓 콜라이더와 겹치지 않고 한눈에 보이도록 설정
        return new Vector3(targetTf.position.x + 0.35f, 1.18f, targetTf.position.z - 1.15f);
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

        // 1. 서브 대기 상태 (공을 서브 위치에 고정)
        if (isServing && !isBallTossed)
        {
            currentBall.SetVisible(true);
            currentBall.transform.position = GetServePosition();

            Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
            if (ballRb != null && !ballRb.isKinematic)
            {
                ballRb.linearVelocity = Vector3.zero;
                ballRb.angularVelocity = Vector3.zero;
                ballRb.isKinematic = true;
            }

            bool tossTriggered = WasTriggerOrSwingPressedThisFrame();
            if (vrLeftHand != null && vrLeftHand.position.y > 0.5f)
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
            return;
        }

        // 토스 직후 0.22초 동안은 공이 떠오르는 시간이므로 즉시 타격 방지
        if (isServing && isBallTossed && (Time.unscaledTime - lastTossTime < 0.22f))
        {
            return;
        }

        // 2. 타격 감지 (서브 토스 후 또는 일반 랠리 중)
        bool canHit = !isServing || (isServing && isBallTossed);

        if (canHit && !IsSwinging && currentBall != null)
        {
            Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
            // 이미 내가 쳐서 상대방 코트(-Z 방향)로 빠르게 날아가는 공은 다시 치지 않음
            if (!isServing && ballRb != null && ballRb.linearVelocity.z < -1.0f)
            {
                return;
            }

            Transform targetTf = (rigTransform != null) ? rigTransform : transform;
            Vector3 playerRefPos = targetTf.position + Vector3.up * 1.2f;
            float distFromPlayer = Vector3.Distance(playerRefPos, currentBall.transform.position);
            float distFromRacket = (racketCenter != null) ? Vector3.Distance(racketCenter.position, currentBall.transform.position) : distFromPlayer;
            float minDist = Mathf.Min(distFromPlayer, distFromRacket);

            bool swingRequested = WasTriggerOrSwingPressedThisFrame() || (rightHandSpeed >= swingVelocityThreshold);
            bool isBallApproaching = (ballRb != null && ballRb.linearVelocity.z > 0.2f);

            // 스윙 버튼/휘두르기를 했을 때, 또는 랠리 중 공이 근접했을 때 타격
            if ((swingRequested && minDist <= (hitRange + 2.2f)) || (!isServing && isBallApproaching && minDist <= hitRange && rightHandSpeed >= 0.25f))
            {
                StartCoroutine(DelayedHitRoutine());
            }
        }
    }

    private IEnumerator DelayedHitRoutine()
    {
        IsSwinging = true;
        if (animator != null && hasHitParam) animator.SetTrigger("hit");

        yield return new WaitForSeconds(hitDelay);

        if (currentBall != null && (GameManager.Instance == null || !GameManager.Instance.isGamePaused))
        {
            Transform targetTf = (rigTransform != null) ? rigTransform : transform;
            Vector3 playerRefPos = targetTf.position + Vector3.up * 1.2f;
            float distFromPlayer = Vector3.Distance(playerRefPos, currentBall.transform.position);
            float distFromRacket = (racketCenter != null) ? Vector3.Distance(racketCenter.position, currentBall.transform.position) : distFromPlayer;

            if (Mathf.Min(distFromPlayer, distFromRacket) <= hitRange + 2.5f)
            {
                ApplyHitVelocity();
            }
        }

        yield return new WaitForSeconds(0.2f);
        IsSwinging = false;
    }

    public void PrepareServe()
    {
        if (activePowerShotCoroutine != null)
        {
            StopCoroutine(activePowerShotCoroutine);
            activePowerShotCoroutine = null;
        }
        isPowerShotActive = false;
        isPostSlideSlowed = false;
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        StopAllCoroutines();
        IsSwinging = false;
        isServing = true;
        isBallTossed = false;
        currentRallySpeedMultiplier = 1.0f;
        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
        targetTf.position = startPosition;
        targetTf.rotation = startRotation;

        if (currentBall == null) FindBall();
        if (currentBall != null)
        {
            currentBall.SetVisible(true);
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
        yield return new WaitForSeconds(0.8f);
        if (isServing && GameManager.Instance != null && GameManager.Instance.isGameStarted)
        {
            PrepareServe();
        }
    }

    public void TossBall()
    {
        if (currentBall == null) return;
        isBallTossed = true;
        lastTossTime = Time.unscaledTime;
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

        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        // 이미 적 코트로 날아가고 있는 공이면 중복 타격 방지
        if (!isServing && ballRb != null && !ballRb.isKinematic && ballRb.linearVelocity.z < -2.0f)
        {
            return;
        }

        // 1. 타격 방향 및 휘두르는 속도 계산 (VR 컨트롤러 + PC 마우스 스와이프 지원)
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
        Vector3 handDelta = (vrRightHand != null) ? (vrRightHand.position - lastRightHandPos) : Vector3.zero;
        float vrSwingSpeed = handDelta.magnitude / dt;

        Camera cam = Camera.main;
        Vector3 camRight = (cam != null) ? cam.transform.right : Vector3.right;
        Vector3 camUp = (cam != null) ? cam.transform.up : Vector3.up;

        float swingX = Vector3.Dot(handDelta, camRight);
        float swingY = Vector3.Dot(handDelta, camUp);

        // PC 마우스 스와이프 감지 (마우스 좌클릭 시 스와이프 벡터 적용)
        if (Mouse.current != null)
        {
            Vector2 mDelta = Mouse.current.delta.ReadValue();
            if (mDelta.sqrMagnitude > 4f)
            {
                swingX = mDelta.x * 0.035f;
                swingY = mDelta.y * 0.035f;
                vrSwingSpeed = Mathf.Max(vrSwingSpeed, mDelta.magnitude * 0.08f + 1.2f);
            }
        }
        lastHitSwingX = swingX;

        // 2. 슬라이딩 감속 중 타격 시 즉시 정상 속도로 복귀하여 시원한 타격감 부여
        isPostSlideSlowed = false;

        // 3. 랠리 게이지 적립 및 슈퍼 스매시/파워샷 판정
        bool performSuperSmash = IsSuperSmashReady;
        if (performSuperSmash)
        {
            rallyGauge = 0f;
            currentBall.ActivateSuperSmashTrail(true);
            SendHaptic(false, 1.0f, 0.35f);
        }
        else
        {
            if (!isServing)
            {
                rallyGauge = Mathf.Clamp01(rallyGauge + 0.34f);
            }
        }

        bool isPowerShot = performSuperSmash || (Random.Range(0f, 100f) < powerShotChance);
        if (isPowerShot && !performSuperSmash)
        {
            currentBall.ActivateSuperSmashTrail(true);
        }

        // 4. 휘두르는 방향(swingX)에 따른 상대 코트 착지 목표점 계산
        Vector3 targetPoint = CalculateTargetFromSwing(swingX, swingY, isPowerShot);

        // 5. 휘두르는 속도(swingSpeed)에 따른 비행 시간 및 공 속도 스케일링
        currentRallySpeedMultiplier = Mathf.Min(1.6f, currentRallySpeedMultiplier + 0.04f);

        float speedFactor = Mathf.Clamp(vrSwingSpeed, 0.6f, 3.5f);
        float baseFlightTime = Mathf.Lerp(1.35f, 0.72f, Mathf.InverseLerp(0.6f, 3.5f, speedFactor));
        float normalFlightTime = Mathf.Max(0.58f, baseFlightTime / currentRallySpeedMultiplier);

        SendHaptic(false, isPowerShot ? 1.0f : 0.65f, isPowerShot ? 0.22f : 0.12f);

        if (currentBall != null)
        {
            currentBall.PlayHitSound(isPowerShot);
            currentBall.SpawnHitImpactEffect(currentBall.transform.position, isPowerShot);
        }

        // 파워샷: "전부 느려지고 공은 빠르게 움직이게 해주고 카메라가 공을 따라가게 해줘"
        if (isPowerShot)
        {
            if (activePowerShotCoroutine != null) StopCoroutine(activePowerShotCoroutine);
            activePowerShotCoroutine = StartCoroutine(PowerShotCameraAndBulletTimeRoutine(targetPoint));
        }
        else
        {
            StartCoroutine(MicroHitStopRoutine(0.035f));

            if (ballRb != null)
            {
                ballRb.isKinematic = false;
                ballRb.linearVelocity = Vector3.zero;
                Vector3 exactVelocity = CalculateVelocity(targetPoint, currentBall.transform.position, normalFlightTime);
                ballRb.linearVelocity = exactVelocity;
            }
        }

        isServing = false;
        isBallTossed = false;
        currentBall.ResetBounceCount();
    }

    public Vector3 CalculateTargetFromSwing(float sX, float sY, bool isSuperSmash)
    {
        Bounds bounds = (opponentCourtArea != null) ? opponentCourtArea.bounds : new Bounds(new Vector3(0, 0.05f, -23f), new Vector3(8.5f, 1f, 11f));
        float marginX = bounds.size.x * 0.15f;
        float minX = bounds.min.x + marginX;
        float maxX = bounds.max.x - marginX;
        float midX = bounds.center.x;

        float targetX;
        // 왼쪽으로 휘두른 경우: 상대 코트 좌측 구역 공략
        if (sX < -0.035f)
        {
            float t = Mathf.Clamp01((-sX - 0.035f) / 0.22f);
            targetX = Mathf.Lerp(midX - 0.6f, minX, t);
        }
        // 오른쪽으로 휘두른 경우: 상대 코트 우측 구역 공략
        else if (sX > 0.035f)
        {
            float t = Mathf.Clamp01((sX - 0.035f) / 0.22f);
            targetX = Mathf.Lerp(midX + 0.6f, maxX, t);
        }
        // 정면/중앙 스윙: 중앙 부근
        else
        {
            targetX = Random.Range(midX - 0.7f, midX + 0.7f);
        }

        float minZ = bounds.min.z + 1.2f;
        float maxZ = bounds.max.z - 2.5f;
        float targetZ = Random.Range(minZ + 1.0f, maxZ);

        if (isSuperSmash)
        {
            // 슈퍼 스매시: 베이스라인 깊숙한 코너에 꽂히는 총알 탄도
            targetX = (sX <= 0f) ? (minX + 0.25f) : (maxX - 0.25f);
            targetZ = minZ + 0.6f;
        }

        return new Vector3(targetX, 0.05f, targetZ);
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
            return new Vector3(Random.Range(-3f, 3f), 0.05f, Random.Range(-24f, -20f));
        }
        Bounds bounds = opponentCourtArea.bounds;
        float marginX = bounds.size.x * 0.15f;
        float marginZ = bounds.size.z * 0.15f;
        float randomX = Random.Range(bounds.min.x + marginX, bounds.max.x - marginX);
        float randomZ = Random.Range(bounds.min.z + marginZ, bounds.max.z - marginZ);
        return new Vector3(randomX, 0.05f, randomZ);
    }

    public void OnRacketHit()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted || GameManager.Instance.isGamePaused) return;
        if (Time.unscaledTime - lastPhysicalHitTime < 0.3f) return;

        if (currentBall == null) FindBall();
        if (currentBall == null) return;

        if (isServing && !isBallTossed)
        {
            // 서브 대기 중에는 정지된 라켓과 겹치더라도 자동 토스되지 않고, 실제로 오른손 컨트롤러를 휘둘렀을 때만 토스 허용
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            float rightSpeed = (vrRightHand != null) ? Vector3.Distance(vrRightHand.position, lastRightHandPos) / dt : 0f;
            if (rightSpeed >= 0.35f)
            {
                lastPhysicalHitTime = Time.unscaledTime;
                TossBall();
                SendHaptic(true, 0.4f, 0.1f);
            }
            return;
        }

        // 토스 직후에는 라켓 충돌로 바로 쳐지지 않도록 보호
        if (isServing && isBallTossed && (Time.unscaledTime - lastTossTime < 0.22f))
        {
            return;
        }

        lastPhysicalHitTime = Time.unscaledTime;
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
        if (direction == Vector3.zero)
        {
            Camera cam = Camera.main;
            direction = (cam != null) ? cam.transform.forward : Vector3.back;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f) direction.Normalize();
            else direction = Vector3.back;
        }

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

        // 슬라이딩이 끝난 시점으로부터 2초간 나(플레이어)만 이동 속도 감속 (공/상대는 정상 속도 유지)
        isPostSlideSlowed = true;

        float slowTimer = 0f;
        while (slowTimer < postSlideSlowDuration && isPostSlideSlowed && GameManager.Instance != null && GameManager.Instance.isGameStarted && !GameManager.Instance.isGamePaused)
        {
            slowTimer += Time.deltaTime;
            yield return null;
        }

        isPostSlideSlowed = false;
    }

    private IEnumerator PowerShotCameraAndBulletTimeRoutine(Vector3 targetPoint)
    {
        isPowerShotActive = true;

        Transform rigTf = (rigTransform != null) ? rigTransform : transform;
        Vector3 savedRigPos = rigTf.position;
        Quaternion savedRigRot = rigTf.rotation;

        // 1. "전부 느려지고": 주변 환경, 상대 캐릭터 불릿 타임 슬로우 모션 (0.22x)
        Time.timeScale = powerShotTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        // 2. "공은 빠르게 움직이게": 현실 시간 기준 약 0.42초만에 날아가도록 물리 속도 계산 (게임 내 ~0.09초)
        Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
        if (ballRb != null)
        {
            ballRb.isKinematic = false;
            ballRb.linearVelocity = Vector3.zero;
            float scaledGameTime = 0.42f * Time.timeScale;
            Vector3 fastVelocity = CalculateVelocity(targetPoint, currentBall.transform.position, scaledGameTime);
            ballRb.linearVelocity = fastVelocity;
        }

        // 3. "카메라가 공을 따라가게": 공 뒤편 상공에서 공을 추적하며 날아가는 카메라 팔로우
        bool ballLanded = false;
        System.Action onBounce = () => { ballLanded = true; };
        if (currentBall != null) currentBall.onBallBounced += onBounce;

        float maxTrackingTime = 0.65f; // 현실 시간(초)
        float elapsed = 0f;

        while (elapsed < maxTrackingTime && !ballLanded && currentBall != null && GameManager.Instance != null && GameManager.Instance.isGameStarted && !GameManager.Instance.isGamePaused)
        {
            elapsed += Time.unscaledDeltaTime;

            Vector3 ballPos = currentBall.transform.position;

            // 공 뒤(+Z) 상단(+Y)에서 공을 따라가며 코트를 내려다보는 역동적인 추적 시점
            Vector3 desiredCamPos = new Vector3(ballPos.x * 0.5f, Mathf.Max(ballPos.y + 1.25f, 1.35f), ballPos.z + 2.8f);
            rigTf.position = Vector3.Lerp(rigTf.position, desiredCamPos, Time.unscaledDeltaTime * 14f);

            Vector3 lookTarget = new Vector3(targetPoint.x, 0.4f, targetPoint.z);
            Vector3 lookDir = (lookTarget - rigTf.position).normalized;
            if (lookDir != Vector3.zero)
            {
                Quaternion desiredRot = Quaternion.LookRotation(lookDir);
                rigTf.rotation = Quaternion.Slerp(rigTf.rotation, desiredRot, Time.unscaledDeltaTime * 12f);
            }

            if (ballPos.z <= (Ball.NET_Z - 1.5f) && ballPos.y <= 0.35f)
            {
                ballLanded = true;
            }

            yield return null;
        }

        if (currentBall != null) currentBall.onBallBounced -= onBounce;

        // 슬로우 모션 해제 및 원래 배속 복귀
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        // 카메라를 원래 플레이어 기준 위치로 부드럽게 복귀 (0.28초)
        float returnDuration = 0.28f;
        float returnElapsed = 0f;
        Vector3 fromPos = rigTf.position;
        Quaternion fromRot = rigTf.rotation;
        Vector3 targetReturnPos = (startPosition != Vector3.zero) ? startPosition : savedRigPos;
        Quaternion targetReturnRot = (startRotation != Quaternion.identity) ? startRotation : savedRigRot;

        while (returnElapsed < returnDuration && GameManager.Instance != null && GameManager.Instance.isGameStarted && !GameManager.Instance.isGamePaused)
        {
            returnElapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, returnElapsed / returnDuration);
            rigTf.position = Vector3.Lerp(fromPos, targetReturnPos, t);
            rigTf.rotation = Quaternion.Slerp(fromRot, targetReturnRot, t);
            yield return null;
        }

        rigTf.position = targetReturnPos;
        rigTf.rotation = targetReturnRot;
        isPowerShotActive = false;
        activePowerShotCoroutine = null;
    }

    private IEnumerator MicroHitStopRoutine(float duration)
    {
        if (isPowerShotActive) yield break;
        float prevTimeScale = Time.timeScale;
        Time.timeScale = 0.05f;
        yield return new WaitForSecondsRealtime(duration);
        if (!isPowerShotActive)
        {
            Time.timeScale = (prevTimeScale > 0.05f) ? prevTimeScale : 1.0f;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }
    }

    void UpdateRallyGaugeUI()
    {
        if (rallyGaugeFill != null)
        {
            rallyGaugeFill.fillAmount = rallyGauge;
            if (IsSuperSmashReady)
            {
                rallyGaugeFill.color = Color.Lerp(new Color(0.1f, 0.95f, 1f), new Color(1f, 0.9f, 0.2f), Mathf.PingPong(Time.unscaledTime * 8f, 1f));
            }
            else
            {
                rallyGaugeFill.color = new Color(0.2f, 0.85f, 1f);
            }
        }

        if (rallyGaugeText != null)
        {
            if (IsSuperSmashReady)
            {
                rallyGaugeText.text = "⚡ SMASH READY! ⚡";
                rallyGaugeText.color = Color.Lerp(Color.yellow, Color.white, Mathf.PingPong(Time.unscaledTime * 10f, 1f));
            }
            else
            {
                rallyGaugeText.text = $"RALLY GAUGE  {Mathf.RoundToInt(rallyGauge * 100f)}%";
                rallyGaugeText.color = new Color(0.5f, 1f, 0.9f);
            }
        }

        if (rallyGaugeRoot != null)
        {
            if (IsSuperSmashReady)
            {
                // 게이지 바 찌릿찌릿 진동 효과 (Electric Jitter)
                Vector2 jitter = new Vector2(Random.Range(-3.0f, 3.0f), Random.Range(-3.0f, 3.0f));
                rallyGaugeRoot.anchoredPosition = originalGaugePos + jitter;
            }
            else
            {
                rallyGaugeRoot.anchoredPosition = originalGaugePos;
            }
        }
    }

    private void UpdateRacketSparkEffect()
    {
        if (racketSparkObj == null && racketCenter != null)
        {
            racketSparkObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            racketSparkObj.name = "Racket_Electric_Spark";
            Collider col = racketSparkObj.GetComponent<Collider>();
            if (col != null) Destroy(col);
            racketSparkObj.transform.SetParent(racketCenter, false);
            racketSparkObj.transform.localPosition = Vector3.zero;
            racketSparkObj.transform.localScale = new Vector3(0.18f, 0.04f, 0.22f);

            Renderer ren = racketSparkObj.GetComponent<Renderer>();
            if (ren != null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                if (s != null)
                {
                    Material mat = new Material(s);
                    mat.color = new Color(0.2f, 1f, 1f, 0.75f);
                    ren.material = mat;
                }
            }
        }

        if (racketSparkObj != null)
        {
            bool ready = IsSuperSmashReady;
            racketSparkObj.SetActive(ready);
            if (ready)
            {
                float scaleJitter = 1.0f + Random.Range(-0.25f, 0.35f);
                racketSparkObj.transform.localScale = new Vector3(0.2f * scaleJitter, 0.05f * scaleJitter, 0.25f * scaleJitter);
                Renderer ren = racketSparkObj.GetComponent<Renderer>();
                if (ren != null && ren.material != null)
                {
                    ren.material.color = Color.Lerp(new Color(0.1f, 0.9f, 1f, 0.85f), new Color(1f, 0.95f, 0.2f, 0.85f), Mathf.PingPong(Time.unscaledTime * 12f, 1f));
                }

                if (Time.unscaledTime - lastRallySparkHaptic > 0.18f)
                {
                    lastRallySparkHaptic = Time.unscaledTime;
                    SendHaptic(false, 0.35f, 0.06f);
                }
            }
        }
    }

    public void ResetToStart()
    {
        StopAllCoroutines();
        if (activePowerShotCoroutine != null)
        {
            StopCoroutine(activePowerShotCoroutine);
            activePowerShotCoroutine = null;
        }
        isPowerShotActive = false;
        isPostSlideSlowed = false;
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        IsSwinging = false;
        isServing = false;
        isBallTossed = false;
        isSliding = false;
        Transform targetTf = (rigTransform != null) ? rigTransform : transform;
        targetTf.position = startPosition;
        targetTf.rotation = startRotation;
    }
}
