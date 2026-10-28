using UnityEngine;

public class Ball : MonoBehaviour
{
    [Header("Launch Settings")]
    public float speed = 5f;

    [Header("Bounce Settings")]
    public float fixedBounceHeight = 1.15f;

    [Header("Physics Settings")]
    public float minHorizontalSpeed = 3.2f;
    public float dragOnFloor = 0.98f;

    [Header("Audio Settings")]
    public AudioClip bounceSound;
    public AudioClip hitSound;
    public AudioClip powerHitSound;
    public System.Action onBallBounced;

    private AudioSource audioSource;
    private TrailRenderer trailRenderer;
    private bool isSuperSmash = false;
    public bool IsSuperSmash => isSuperSmash;

    private Rigidbody rb;
    private Renderer[] ballRenderers;
    private GameObject lastHitter;
    private int playerSideBounceCount = 0;
    private int enemySideBounceCount = 0;
    private float lowSpeedTimer = 0f;

    public const float NET_Z = -13.8f;

    public void PlayHitSound(bool isPower = false)
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            if (isPower && powerHitSound != null)
            {
                audioSource.pitch = Random.Range(0.98f, 1.02f);
                audioSource.PlayOneShot(powerHitSound, 1.0f);
            }
            else if (hitSound != null)
            {
                audioSource.pitch = Random.Range(0.96f, 1.04f);
                audioSource.PlayOneShot(hitSound, 1.0f);
            }
        }
    }

    public void SpawnHitImpactEffect(Vector3 position, bool isPower)
    {
        StartCoroutine(HitImpactRoutine(position, isPower));
    }

    private System.Collections.IEnumerator HitImpactRoutine(Vector3 pos, bool isPower)
    {
        GameObject effectGo = new GameObject(isPower ? "Power_Tang_Impact" : "Tang_Impact");
        effectGo.transform.position = pos;

        // 1. Shockwave Ring (Circle outline)
        GameObject ringGo = new GameObject("ShockwaveRing");
        ringGo.transform.SetParent(effectGo.transform, false);
        ringGo.transform.position = pos;

        LineRenderer lr = ringGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = 28;
        lr.startWidth = isPower ? 0.055f : 0.038f;
        lr.endWidth = isPower ? 0.055f : 0.038f;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        Material ringMat = new Material(sh);
        Color baseColor = isPower ? new Color(0.15f, 0.95f, 1f, 1f) : new Color(1f, 0.94f, 0.45f, 1f);
        ringMat.color = baseColor;
        lr.material = ringMat;

        for (int i = 0; i < 28; i++)
        {
            float angle = (float)i / 28f * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            ringGo.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
        }

        // 2. Small Radial Sparks (6-8 sparks)
        int sparkCount = isPower ? 8 : 6;
        Transform[] sparkTfs = new Transform[sparkCount];
        Vector3[] sparkDirs = new Vector3[sparkCount];

        for (int i = 0; i < sparkCount; i++)
        {
            GameObject sp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sp.name = "Spark_" + i;
            Collider col = sp.GetComponent<Collider>();
            if (col != null) Destroy(col);
            sp.transform.SetParent(effectGo.transform, false);
            sp.transform.position = pos;
            float sSize = isPower ? 0.065f : 0.048f;
            sp.transform.localScale = Vector3.one * sSize;

            Renderer spRen = sp.GetComponent<Renderer>();
            if (spRen != null)
            {
                Material spMat = new Material(sh);
                spMat.color = isPower ? new Color(0.4f, 1f, 1f, 1f) : new Color(1f, 0.98f, 0.75f, 1f);
                spRen.material = spMat;
            }

            float rad = (float)i / sparkCount * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
            Vector3 camRight = cam != null ? cam.transform.right : Vector3.right;
            Vector3 camUp = cam != null ? cam.transform.up : Vector3.up;
            sparkDirs[i] = (camRight * Mathf.Cos(rad) + camUp * Mathf.Sin(rad) + Random.insideUnitSphere * 0.2f).normalized;
            sparkTfs[i] = sp.transform;
        }

        float duration = 0.16f;
        float elapsed = 0f;
        float maxRadius = isPower ? 0.68f : 0.44f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float currentRadius = Mathf.Lerp(0.08f, maxRadius, Mathf.Sin(t * Mathf.PI * 0.5f));
            ringGo.transform.localScale = Vector3.one * currentRadius;
            Color c = baseColor;
            c.a = Mathf.Lerp(1.0f, 0.0f, t);
            ringMat.color = c;
            lr.startWidth = Mathf.Lerp(isPower ? 0.055f : 0.038f, 0.005f, t);
            lr.endWidth = lr.startWidth;

            for (int i = 0; i < sparkCount; i++)
            {
                if (sparkTfs[i] != null)
                {
                    float dist = Mathf.Lerp(0.02f, maxRadius * 1.15f, t);
                    sparkTfs[i].position = pos + sparkDirs[i] * dist;
                    float sScale = Mathf.Lerp(isPower ? 0.065f : 0.048f, 0.01f, t);
                    sparkTfs[i].localScale = Vector3.one * sScale;
                }
            }

            yield return null;
        }

        Destroy(effectGo);
    }

    public void PlayBounceSound()
    {
        if (bounceSound != null)
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.pitch = Random.Range(0.95f, 1.05f);
                audioSource.PlayOneShot(bounceSound, 0.8f);
            }
        }
    }

    public void ResetBounceCount()
    {
        playerSideBounceCount = 0;
        enemySideBounceCount = 0;
        lowSpeedTimer = 0f;
    }

    public void ActivateSuperSmashTrail(bool active)
    {
        isSuperSmash = active;
        if (trailRenderer == null) SetupTrailRenderer();
        if (trailRenderer != null)
        {
            if (active)
            {
                trailRenderer.Clear();
                trailRenderer.emitting = true;
            }
            else
            {
                trailRenderer.emitting = false;
            }
        }
    }

    private void SetupTrailRenderer()
    {
        trailRenderer = GetComponent<TrailRenderer>();
        if (trailRenderer == null) trailRenderer = gameObject.AddComponent<TrailRenderer>();

        trailRenderer.time = 0.4f;
        trailRenderer.startWidth = 0.2f;
        trailRenderer.endWidth = 0.02f;
        trailRenderer.minVertexDistance = 0.05f;

        Shader trailShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (trailShader == null) trailShader = Shader.Find("Sprites/Default");
        if (trailShader != null)
        {
            Material trailMat = new Material(trailShader);
            trailMat.color = new Color(0.2f, 0.95f, 1f, 1f);
            trailRenderer.material = trailMat;
        }

        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.2f, 1f, 1f), 0.0f),
                new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.6f),
                new GradientColorKey(new Color(1f, 0.3f, 0.1f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.85f, 0.5f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        trailRenderer.colorGradient = grad;
        trailRenderer.emitting = false;
    }

    public void SetVisible(bool visible)
    {
        if (ballRenderers == null || ballRenderers.Length == 0)
            ballRenderers = GetComponentsInChildren<Renderer>(true);

        foreach (var r in ballRenderers)
        {
            if (r != null) r.enabled = visible;
        }
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
        ballRenderers = GetComponentsInChildren<Renderer>(true);
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.5f;
        }

#if UNITY_EDITOR
        if (hitSound == null) hitSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_tang_hit.wav") ?? UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_racket_hit.wav");
        if (powerHitSound == null) powerHitSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_power_tang.wav");
        if (bounceSound == null) bounceSound = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_court_bounce.wav");
#endif
    }

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.05f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        SetupTrailRenderer();

        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted)
        {
            SetKinematic(true);
        }
    }

    void FixedUpdate()
    {
        if (GameManager.Instance == null || !GameManager.Instance.isGameStarted || GameManager.Instance.isGamePaused) return;
        if (rb == null || rb.isKinematic)
        {
            lowSpeedTimer = 0f;
            return;
        }

        Vector3 pos = transform.position;

        // 1. 코트 외부 이탈(Out of Bounds) 안전 판정
        if (pos.y < -1.5f || pos.z > 3.2f || pos.z < -30.5f || Mathf.Abs(pos.x) > 15f)
        {
            TriggerOutByPosition(pos);
            return;
        }

        // 2. 수평 속도 유지 및 정체 방지
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float currentSpeed = horizontalVelocity.magnitude;

        if (currentSpeed < minHorizontalSpeed && Mathf.Abs(rb.linearVelocity.y) < 0.2f)
        {
            Vector3 nudgeDir = horizontalVelocity.normalized;
            if (nudgeDir == Vector3.zero)
            {
                nudgeDir = (pos.z > NET_Z) ? Vector3.back : Vector3.forward;
            }
            rb.AddForce(nudgeDir * 3f, ForceMode.Acceleration);
        }

        // 3. 네트 걸림 등 공이 코트 위에서 멈춰버렸을 때 자동 라운드 진행 (Fail-safe)
        if (rb.linearVelocity.magnitude < 0.8f)
        {
            lowSpeedTimer += Time.fixedDeltaTime;
            if (lowSpeedTimer >= 2.2f)
            {
                lowSpeedTimer = 0f;
                TriggerOutByPosition(pos);
            }
        }
        else
        {
            lowSpeedTimer = 0f;
        }
    }

    private void TriggerOutByPosition(Vector3 pos)
    {
        if (GameManager.Instance == null) return;

        // 플레이어 진영(z > NET_Z)에서 멈추거나 뒤로 나간 경우 -> 적 득점 (playerBackTrigger)
        // 적 진영(z <= NET_Z)에서 멈추거나 뒤로 나간 경우 -> 플레이어 득점 (enemyBackTrigger)
        if (pos.z > NET_Z)
        {
            if (GameManager.Instance.playerBackTrigger != null)
                GameManager.Instance.NotifyBallOut(GameManager.Instance.playerBackTrigger);
        }
        else
        {
            if (GameManager.Instance.enemyBackTrigger != null)
                GameManager.Instance.NotifyBallOut(GameManager.Instance.enemyBackTrigger);
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

        // 2. 'Net'에 부딪혔을 때 (네트 감속)
        if ((layerName == "Net" || collision.gameObject.CompareTag("Net") || collision.gameObject.name == "pCube4" || collision.gameObject.name == "pCube5")
            && collision.gameObject.name.IndexOf("Racket", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x * 0.35f, rb.linearVelocity.y * 0.35f, rb.linearVelocity.z * 0.25f);
            }
            return;
        }

        // 3. 'Floor' 또는 'Court'에 부딪혔을 때
        bool isCourtOrFloor = layerName == "Floor" || layerName == "Court"
            || collision.gameObject.CompareTag("Court")
            || collision.gameObject.name.IndexOf("Court", System.StringComparison.OrdinalIgnoreCase) >= 0
            || collision.gameObject.name.IndexOf("Floor", System.StringComparison.OrdinalIgnoreCase) >= 0
            || collision.gameObject.name.IndexOf("pPlane", System.StringComparison.OrdinalIgnoreCase) >= 0;

        if (isCourtOrFloor)
        {
            VRPlayerController vrPlayer = Object.FindFirstObjectByType<VRPlayerController>();
            if (vrPlayer != null && vrPlayer.IsServingTossed)
            {
                vrPlayer.OnServeMissed();
                return;
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
                PlayBounceSound();
                if (isSuperSmash) ActivateSuperSmashTrail(false);
                onBallBounced?.Invoke();

                if (GameManager.Instance != null && GameManager.Instance.isGameStarted)
                {
                    if (transform.position.z > NET_Z)
                    {
                        playerSideBounceCount++;
                        enemySideBounceCount = 0;
                        if (playerSideBounceCount >= 2)
                        {
                            if (GameManager.Instance.playerBackTrigger != null)
                                GameManager.Instance.NotifyBallOut(GameManager.Instance.playerBackTrigger);
                        }
                    }
                    else
                    {
                        enemySideBounceCount++;
                        playerSideBounceCount = 0;
                        if (enemySideBounceCount >= 2)
                        {
                            if (GameManager.Instance.enemyBackTrigger != null)
                                GameManager.Instance.NotifyBallOut(GameManager.Instance.enemyBackTrigger);
                        }
                    }
                }
            }
        }
    }
}
