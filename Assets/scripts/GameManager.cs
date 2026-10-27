using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using TMPro;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("VR Menu & In-Game Canvases")]
    public GameObject vrMenuCanvas;      // 시작/설정/셀렉트 통합 월드스페이스 캔버스
    public GameObject vrMainMenuPanel;   // 1. 시작 화면 (게임 시작, 설정, 종료)
    public GameObject vrSettingsPanel;   // 2. 설정 화면 (승리 판수, 난이도, 이동속도, 조작법)
    public GameObject vrSelectionUI;     // 3. 캐릭터 & 라켓 선택 화면
    public GameObject vrCanvas;          // 4. 인게임 스코어보드 캔버스 (/XR Origin (XR Rig)/Score)
    public GameObject pauseMenuPanel;    // 5. 일시정지 패널

    [Header("Scoreboard UI")]
    public TextMeshProUGUI vrScoreText;
    public TextMeshProUGUI vrEnemyScoreText;
    public TextMeshProUGUI vrGameScoreText;
    public GameObject victoryPanel;
    public GameObject losePanel;

    [Header("Selection UI Displays")]
    public Image charImageDisplay;
    public Image racketImageDisplay;
    public Image vrCharImageDisplay;
    public Image vrRacketImageDisplay;
    public TextMeshProUGUI charNameText;
    public TextMeshProUGUI racketNameText;

    [Header("Settings UI Displays")]
    public TextMeshProUGUI gamesValueText;
    public TextMeshProUGUI difficultyValueText;
    public TextMeshProUGUI speedValueText;

    [Header("Player Rig & Assets")]
    public GameObject vrPlayerRig;
    public Sprite[] characterSprites;
    public Sprite[] racketSprites;

    [Header("Player Selection & Settings State")]
    public int selectedCharacterIndex = 0;
    public int selectedRacketIndex = 0;
    public int gamesToWin = 3;
    public int aiDifficulty = 1; // 0: EASY, 1: NORMAL, 2: HARD
    public float playerSpeedMultiplier = 1.0f;

    public bool isGameStarted = false;
    public bool isGamePaused = false;
    public bool isVRMode = true;

    [Header("Court Triggers")]
    public GameObject playerBackTrigger;
    public GameObject enemyBackTrigger;

    public bool lastPointWinnerIsPlayer = true;

    private bool isPointProcessing = false;
    private int playerPoints = 0;
    private int enemyPoints = 0;
    private int playerGamesWon = 0;
    private int enemyGamesWon = 0;
    private bool settingsOpenedFromPause = false;

    private readonly string[] tennisScores = { "0", "15", "30", "40", "Adv", "Win" };

    private readonly string[] characterNames = {
        "EMERALD HEROINE\n<size=20><color=#80FFAA>[BALANCED] SPD **  PWR **</color></size>",
        "SKY STRIKER\n<size=20><color=#80D8FF>[SPEED] SPD ***  PWR *</color></size>",
        "CRIMSON SMASHER\n<size=20><color=#FF8A80>[POWER] SPD *  PWR ***</color></size>",
        "GOLDEN CHAMPION\n<size=20><color=#FFE57F>[PRO] SPD ***  PWR **</color></size>"
    };

    private readonly Color[] characterTintColors = {
        Color.white,
        new Color(0.72f, 0.92f, 1.0f, 1f),
        new Color(1.0f, 0.75f, 0.78f, 1f),
        new Color(1.0f, 0.94f, 0.68f, 1f)
    };

    private readonly string[] racketNames = {
        "CLASSIC ORANGE\n<size=20><color=#FFD180>Standard Balanced Racket</color></size>",
        "BLUE ALL-ROUND\n<size=20><color=#82B1FF>Stable Defense & Control</color></size>",
        "GREEN SPEED\n<size=20><color=#B9F6CA>Lightweight Fast Swing</color></size>",
        "PINK CONTROL\n<size=20><color=#FF80AB>Precision Spin & Placement</color></size>"
    };

    private readonly string[] difficultyNames = { "EASY", "NORMAL", "HARD" };

    // VR 컨트롤러 입력 액션 (일시정지, UI 클릭, 썸스틱 선택)
    private InputAction vrPauseAction;
    private InputAction vrUIClickAction;
    private InputAction vrLeftStickAction;
    private InputAction vrRightStickAction;

    private Transform leftControllerTf;
    private Transform rightControllerTf;
    private Button currentHoveredBtn;
    private float lastUIActionTime = -10f;
    private float lastStickSelectTime = -10f;
    private Vector3 savedBallVelocity;
    private Vector3 savedBallAngularVelocity;
    private bool savedBallWasKinematic;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        isVRMode = true;
        Time.timeScale = 1f;
    }

    void Start()
    {
        SetupVRControllerInputs();
        BindAllUIButtons();

        if (vrPlayerRig != null) vrPlayerRig.SetActive(true);

        // 초기 진입 시: VR 시작 화면 표시
        isGameStarted = false;
        isGamePaused = false;
        ShowMainMenu();
        UpdateSelectionVisuals();
        UpdateSettingsUI();
        UpdateScoreUI();
    }

    private void SetupVRControllerInputs()
    {
        if (vrPlayerRig == null)
        {
            var vrCtrl = Object.FindFirstObjectByType<VRPlayerController>();
            if (vrCtrl != null && vrCtrl.rigTransform != null)
                vrPlayerRig = vrCtrl.rigTransform.gameObject;
        }

        if (vrPlayerRig != null)
        {
            leftControllerTf = vrPlayerRig.transform.Find("Camera Offset/Left Controller");
            rightControllerTf = vrPlayerRig.transform.Find("Camera Offset/Right Controller");
        }

        vrPauseAction = new InputAction("VRPause", InputActionType.Button);
        vrPauseAction.AddBinding("<XRController>{LeftHand}/menuButton");
        vrPauseAction.AddBinding("<XRController>{RightHand}/menuButton");
        vrPauseAction.AddBinding("<XRController>{LeftHand}/secondaryButton");
        vrPauseAction.Enable();

        vrUIClickAction = new InputAction("VRUIClick", InputActionType.Button);
        vrUIClickAction.AddBinding("<XRController>{LeftHand}/triggerPressed");
        vrUIClickAction.AddBinding("<XRController>{RightHand}/triggerPressed");
        vrUIClickAction.AddBinding("<XRController>{LeftHand}/primaryButton");
        vrUIClickAction.AddBinding("<XRController>{RightHand}/primaryButton");
        vrUIClickAction.Enable();

        vrLeftStickAction = new InputAction("VRLeftStick", InputActionType.Value);
        vrLeftStickAction.AddBinding("<XRController>{LeftHand}/thumbstick");
        vrLeftStickAction.Enable();

        vrRightStickAction = new InputAction("VRRightStick", InputActionType.Value);
        vrRightStickAction.AddBinding("<XRController>{RightHand}/thumbstick");
        vrRightStickAction.Enable();
    }

    public void BindAllUIButtons()
    {
        Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();
        foreach (Button btn in allButtons)
        {
            if (btn == null || btn.gameObject.scene != gameObject.scene) continue;

            btn.onClick.RemoveAllListeners();
            string n = btn.name;

            // 1. 시작 화면 버튼
            if (n == "StartGameBtn" || n == "SelectPlatformBtn" || n == "VRStartConfirmBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(ShowSelectionPanel));
            else if (n == "OpenSettingsBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => OpenSettings(false)));
            else if (n == "QuitBtn" || n == "QuitGameBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(QuitGame));

            // 2. 설정 화면 버튼
            else if (n == "GamesMinusBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => ChangeGamesToWin(-1)));
            else if (n == "GamesPlusBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => ChangeGamesToWin(1)));
            else if (n == "DiffPrevBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => ChangeDifficulty(-1)));
            else if (n == "DiffNextBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => ChangeDifficulty(1)));
            else if (n == "SpeedMinusBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => ChangePlayerSpeed(-0.15f)));
            else if (n == "SpeedPlusBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => ChangePlayerSpeed(0.15f)));
            else if (n == "CloseSettingsBtn" || n == "BackFromSettingsBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(CloseSettings));

            // 3. 캐릭터 & 라켓 선택 화면 버튼
            else if (n == "CharPrevBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(PrevCharacter));
            else if (n == "CharNextBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(NextCharacter));
            else if (n == "RacketPrevBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(PrevRacket));
            else if (n == "RacketNextBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(NextRacket));
            else if (n == "VRPlayBtn" || n == "PlayBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => GameStart(true)));
            else if (n == "BackToMainBtn" || n == "BackToStartBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(ShowMainMenu));

            // 4. 일시정지 & 결과 화면 버튼
            else if (n == "ResumeBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(ResumeGame));
            else if (n == "PauseSettingsBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => OpenSettings(true)));
            else if (n == "MenuBtn" || n == "GameOverMenuBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(BackToMenu));
            else if (n == "RematchBtn")
                btn.onClick.AddListener(() => ExecuteUIAction(() => GameStart(true)));
        }
    }

    private void ExecuteUIAction(System.Action action)
    {
        if (Time.unscaledTime - lastUIActionTime < 0.2f) return;
        lastUIActionTime = Time.unscaledTime;
        TriggerUIHaptic(0.35f, 0.06f);
        action?.Invoke();
    }

    void Update()
    {
        // 경기 중 일시정지 토글 (VR 메뉴 버튼)
        if (isGameStarted && vrPauseAction != null && vrPauseAction.triggered)
        {
            if (isGamePaused) ResumeGame();
            else PauseGame();
        }

        // UI가 열려 있을 때 VR 컨트롤러 직접 레이캐스트 & 썸스틱 보조 조작 처리
        if (!isGameStarted || isGamePaused)
        {
            UpdateVRControllerUIRaycast();
            UpdateSelectionThumbstickShortcuts();
        }
    }

    private void UpdateVRControllerUIRaycast()
    {
        Button hitBtn = FindButtonAimedByVRControllers();

        if (hitBtn != currentHoveredBtn)
        {
            if (currentHoveredBtn != null)
                currentHoveredBtn.transform.localScale = Vector3.one;

            currentHoveredBtn = hitBtn;

            if (currentHoveredBtn != null)
            {
                currentHoveredBtn.transform.localScale = Vector3.one * 1.06f;
                TriggerUIHaptic(0.15f, 0.03f);
            }
        }

        if (currentHoveredBtn != null && vrUIClickAction != null && vrUIClickAction.triggered)
        {
            currentHoveredBtn.onClick.Invoke();
        }
    }

    private Button FindButtonAimedByVRControllers()
    {
        Transform[] controllers = { rightControllerTf, leftControllerTf };
        Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();

        Button bestBtn = null;
        float bestDist = 15f;

        foreach (Transform ctrl in controllers)
        {
            if (ctrl == null || !ctrl.gameObject.activeInHierarchy) continue;

            Ray ray = new Ray(ctrl.position, ctrl.forward);

            foreach (Button btn in allButtons)
            {
                if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) continue;
                if (btn.gameObject.scene != gameObject.scene) continue;

                RectTransform rt = btn.GetComponent<RectTransform>();
                if (rt == null) continue;

                Plane plane = new Plane(rt.forward, rt.position);
                if (plane.Raycast(ray, out float enter) && enter > 0.05f && enter < bestDist)
                {
                    Vector3 worldHit = ray.GetPoint(enter);
                    Vector3 localHit = rt.InverseTransformPoint(worldHit);
                    Rect rect = rt.rect;
                    // 약간의 여유 판정(Padding)을 주어 VR에서 훨씬 쉽게 조준/클릭 가능하도록 보정
                    Rect padded = new Rect(rect.xMin - 12f, rect.yMin - 12f, rect.width + 24f, rect.height + 24f);
                    if (padded.Contains( new Vector2(localHit.x, localHit.y) ))
                    {
                        bestDist = enter;
                        bestBtn = btn;
                    }
                }
            }
        }

        return bestBtn;
    }

    private void UpdateSelectionThumbstickShortcuts()
    {
        if (vrSelectionUI == null || !vrSelectionUI.activeInHierarchy) return;
        if (Time.unscaledTime - lastStickSelectTime < 0.35f) return;

        // 왼손 조이스틱 좌/우: 캐릭터 변경
        if (vrLeftStickAction != null)
        {
            Vector2 leftStick = vrLeftStickAction.ReadValue<Vector2>();
            if (leftStick.x > 0.65f)
            {
                lastStickSelectTime = Time.unscaledTime;
                ExecuteUIAction(NextCharacter);
            }
            else if (leftStick.x < -0.65f)
            {
                lastStickSelectTime = Time.unscaledTime;
                ExecuteUIAction(PrevCharacter);
            }
        }

        // 오른손 조이스틱 좌/우: 라켓 변경
        if (vrRightStickAction != null)
        {
            Vector2 rightStick = vrRightStickAction.ReadValue<Vector2>();
            if (rightStick.x > 0.65f)
            {
                lastStickSelectTime = Time.unscaledTime;
                ExecuteUIAction(NextRacket);
            }
            else if (rightStick.x < -0.65f)
            {
                lastStickSelectTime = Time.unscaledTime;
                ExecuteUIAction(PrevRacket);
            }
        }
    }

    private void TriggerUIHaptic(float amplitude, float duration)
    {
        if (rightControllerTf != null)
        {
            var haptic = rightControllerTf.GetComponentInChildren<HapticImpulsePlayer>();
            if (haptic != null) haptic.SendHapticImpulse(amplitude, duration);
        }
    }

    private void PositionCanvasInFrontOfVRPlayer(GameObject canvasObj, float distance = 2.0f, float heightOffset = 0.05f)
    {
        if (canvasObj == null) return;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f) forward.Normalize();
            else forward = Vector3.back;

            canvasObj.transform.position = cam.transform.position + forward * distance + Vector3.up * heightOffset;
            canvasObj.transform.rotation = Quaternion.LookRotation(forward);
        }
    }

    // ==================== 1. VR 메뉴 / 설정 / 셀렉트 화면 전환 ====================

    public void ShowMainMenu()
    {
        isGameStarted = false;
        isGamePaused = false;
        Time.timeScale = 1f;

        if (vrPlayerRig != null)
        {
            VRPlayerController vr = vrPlayerRig.GetComponentInChildren<VRPlayerController>();
            if (vr != null) vr.ResetToStart();
        }

        if (vrMenuCanvas != null)
        {
            vrMenuCanvas.SetActive(true);
            PositionCanvasInFrontOfVRPlayer(vrMenuCanvas, 2.1f, 0.1f);
        }

        if (vrMainMenuPanel != null) vrMainMenuPanel.SetActive(true);
        if (vrSettingsPanel != null) vrSettingsPanel.SetActive(false);
        if (vrSelectionUI != null) vrSelectionUI.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (vrCanvas != null) vrCanvas.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);

        Ball b = Object.FindFirstObjectByType<Ball>();
        if (b != null) b.SetKinematic(true);
    }

    public void OpenSettings(bool fromPause)
    {
        settingsOpenedFromPause = fromPause;

        if (vrMenuCanvas != null)
        {
            vrMenuCanvas.SetActive(true);
            PositionCanvasInFrontOfVRPlayer(vrMenuCanvas, 2.1f, 0.1f);
        }

        if (vrMainMenuPanel != null) vrMainMenuPanel.SetActive(false);
        if (vrSelectionUI != null) vrSelectionUI.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (vrSettingsPanel != null) vrSettingsPanel.SetActive(true);

        UpdateSettingsUI();
    }

    public void CloseSettings()
    {
        if (vrSettingsPanel != null) vrSettingsPanel.SetActive(false);

        if (settingsOpenedFromPause && isGameStarted)
        {
            if (vrMenuCanvas != null) vrMenuCanvas.SetActive(false);
            if (pauseMenuPanel != null)
            {
                pauseMenuPanel.SetActive(true);
                PositionCanvasInFrontOfVRPlayer(pauseMenuPanel, 1.8f, 0.05f);
            }
        }
        else
        {
            if (vrMainMenuPanel != null) vrMainMenuPanel.SetActive(true);
        }
    }

    public void ShowSelectionPanel()
    {
        if (vrMenuCanvas != null)
        {
            vrMenuCanvas.SetActive(true);
            PositionCanvasInFrontOfVRPlayer(vrMenuCanvas, 2.1f, 0.1f);
        }

        if (vrMainMenuPanel != null) vrMainMenuPanel.SetActive(false);
        if (vrSettingsPanel != null) vrSettingsPanel.SetActive(false);
        if (vrSelectionUI != null) vrSelectionUI.SetActive(true);

        UpdateSelectionVisuals();
    }

    // ==================== 2. 설정 값 조정 ====================

    public void ChangeGamesToWin(int delta)
    {
        gamesToWin = Mathf.Clamp(gamesToWin + delta, 1, 5);
        UpdateSettingsUI();
    }

    public void ChangeDifficulty(int delta)
    {
        aiDifficulty = (aiDifficulty + delta + 3) % 3;
        EnemyAI enemy = Object.FindFirstObjectByType<EnemyAI>();
        if (enemy != null) enemy.ApplyDifficulty(aiDifficulty);
        UpdateSettingsUI();
    }

    public void ChangePlayerSpeed(float delta)
    {
        playerSpeedMultiplier = Mathf.Clamp(Mathf.Round((playerSpeedMultiplier + delta) * 100f) / 100f, 0.7f, 1.45f);
        if (vrPlayerRig != null)
        {
            VRPlayerController vr = vrPlayerRig.GetComponentInChildren<VRPlayerController>();
            if (vr != null) vr.ApplyCharacterPreset(selectedCharacterIndex);
        }
        UpdateSettingsUI();
    }

    public void UpdateSettingsUI()
    {
        if (gamesValueText != null) gamesValueText.text = $"{gamesToWin} GAMES";
        if (difficultyValueText != null) difficultyValueText.text = difficultyNames[Mathf.Clamp(aiDifficulty, 0, 2)];
        if (speedValueText != null) speedValueText.text = $"{Mathf.RoundToInt(playerSpeedMultiplier * 100f)}%";
    }

    // ==================== 3. 캐릭터 & 라켓 선택 ====================

    public void NextCharacter()
    {
        selectedCharacterIndex = (selectedCharacterIndex + 1) % 4;
        UpdateSelectionVisuals();
    }

    public void PrevCharacter()
    {
        selectedCharacterIndex = (selectedCharacterIndex + 3) % 4;
        UpdateSelectionVisuals();
    }

    public void NextRacket()
    {
        int count = (racketSprites != null && racketSprites.Length > 0) ? racketSprites.Length : 4;
        selectedRacketIndex = (selectedRacketIndex + 1) % count;
        UpdateSelectionVisuals();
    }

    public void PrevRacket()
    {
        int count = (racketSprites != null && racketSprites.Length > 0) ? racketSprites.Length : 4;
        selectedRacketIndex = (selectedRacketIndex + count - 1) % count;
        UpdateSelectionVisuals();
    }

    public void UpdateSelectionVisuals()
    {
        int charIdx = Mathf.Clamp(selectedCharacterIndex, 0, 3);

        // 캐릭터 이미지 및 프리셋 컬러/텍스트 반영
        if (characterSprites != null && characterSprites.Length > 0)
        {
            Sprite sp = characterSprites[charIdx % characterSprites.Length];
            Color tint = characterTintColors[charIdx];

            if (charImageDisplay != null) { charImageDisplay.sprite = sp; charImageDisplay.color = tint; }
            if (vrCharImageDisplay != null) { vrCharImageDisplay.sprite = sp; vrCharImageDisplay.color = tint; }
        }
        if (charNameText != null)
        {
            charNameText.text = characterNames[charIdx];
        }

        // 라켓 이미지 및 텍스트 반영
        if (racketSprites != null && racketSprites.Length > 0)
        {
            int rIdx = Mathf.Clamp(selectedRacketIndex, 0, racketSprites.Length - 1);
            Sprite rSp = racketSprites[rIdx];
            if (racketImageDisplay != null) racketImageDisplay.sprite = rSp;
            if (vrRacketImageDisplay != null) vrRacketImageDisplay.sprite = rSp;
        }
        if (racketNameText != null)
        {
            racketNameText.text = racketNames[Mathf.Clamp(selectedRacketIndex, 0, racketNames.Length - 1)];
        }

        // VR 플레이어 손에 들린 3D 라켓과 캐릭터 능력치 즉시 미리보기 적용
        if (vrPlayerRig != null)
        {
            VRPlayerController vr = vrPlayerRig.GetComponentInChildren<VRPlayerController>();
            if (vr != null)
            {
                vr.SelectRacket(selectedRacketIndex);
                vr.ApplyCharacterPreset(selectedCharacterIndex);
            }
        }
    }

    // ==================== 4. 경기 시작 / 일시정지 / 종료 ====================

    public void GameStart() => GameStart(true);

    public void GameStart(bool useVR)
    {
        isVRMode = true;
        isGameStarted = true;
        isGamePaused = false;
        Time.timeScale = 1f;

        if (vrMenuCanvas != null) vrMenuCanvas.SetActive(false);
        if (vrMainMenuPanel != null) vrMainMenuPanel.SetActive(false);
        if (vrSettingsPanel != null) vrSettingsPanel.SetActive(false);
        if (vrSelectionUI != null) vrSelectionUI.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);

        if (vrCanvas != null) vrCanvas.SetActive(true);
        if (vrScoreText != null) vrScoreText.gameObject.SetActive(true);
        if (vrEnemyScoreText != null) vrEnemyScoreText.gameObject.SetActive(true);
        if (vrGameScoreText != null) vrGameScoreText.gameObject.SetActive(true);

        if (vrPlayerRig != null)
        {
            vrPlayerRig.SetActive(true);
            VRPlayerController vr = vrPlayerRig.GetComponentInChildren<VRPlayerController>();
            if (vr != null)
            {
                vr.SelectRacket(selectedRacketIndex);
                vr.ApplyCharacterPreset(selectedCharacterIndex);
            }
        }

        EnemyAI enemy = Object.FindFirstObjectByType<EnemyAI>();
        if (enemy != null) enemy.ApplyDifficulty(aiDifficulty);

        playerPoints = 0;
        enemyPoints = 0;
        playerGamesWon = 0;
        enemyGamesWon = 0;
        lastPointWinnerIsPlayer = true;

        UpdateScoreUI();
        ResetRound();
    }

    public void PauseGame()
    {
        if (!isGameStarted || isGamePaused) return;
        isGamePaused = true;

        // Time.timeScale을 0으로 만들지 않고 공 물리만 일시 정지하여 VR 레이캐스트 안정화 유지
        Ball b = Object.FindFirstObjectByType<Ball>();
        if (b != null)
        {
            Rigidbody rb = b.GetComponent<Rigidbody>();
            if (rb != null)
            {
                savedBallWasKinematic = rb.isKinematic;
                if (!rb.isKinematic)
                {
                    savedBallVelocity = rb.linearVelocity;
                    savedBallAngularVelocity = rb.angularVelocity;
                }
                b.SetKinematic(true);
            }
        }

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
            PositionCanvasInFrontOfVRPlayer(pauseMenuPanel, 1.8f, 0.05f);
        }
    }

    public void ResumeGame()
    {
        if (!isGamePaused) return;
        isGamePaused = false;

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (vrSettingsPanel != null) vrSettingsPanel.SetActive(false);
        if (vrMenuCanvas != null) vrMenuCanvas.SetActive(false);

        Ball b = Object.FindFirstObjectByType<Ball>();
        if (b != null && !savedBallWasKinematic)
        {
            Rigidbody rb = b.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.linearVelocity = savedBallVelocity;
                rb.angularVelocity = savedBallAngularVelocity;
            }
        }
    }

    public void BackToMenu()
    {
        ShowMainMenu();
    }

    public void QuitGame()
    {
        Debug.Log("[GameManager] Quitting VR Tennis Game...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ==================== 5. 테니스 점수 및 라운드 진행 ====================

    public void NotifyBallOut(GameObject trigger)
    {
        if (!isGameStarted || isGamePaused || isPointProcessing) return;
        isPointProcessing = true;

        bool playerScored = false;
        if (trigger == playerBackTrigger) playerScored = false;
        else if (trigger == enemyBackTrigger) playerScored = true;
        else { isPointProcessing = false; return; }

        AddPoint(playerScored);
        Invoke(nameof(ResetRound), 1.0f);
    }

    public void ResetRound()
    {
        isPointProcessing = false;
        if (!isGameStarted) return;

        VRPlayerController vrPlayer = Object.FindFirstObjectByType<VRPlayerController>();
        EnemyAI enemy = Object.FindFirstObjectByType<EnemyAI>();

        if (lastPointWinnerIsPlayer)
        {
            if (vrPlayer != null) vrPlayer.PrepareServe();
            if (enemy != null) enemy.ResetToStart();
        }
        else
        {
            if (enemy != null) enemy.PrepareServe();
            if (vrPlayer != null) vrPlayer.ResetToStart();
        }
    }

    public void AddPoint(bool isPlayer)
    {
        if (isPlayer) playerPoints++;
        else enemyPoints++;

        lastPointWinnerIsPlayer = isPlayer;
        CheckPointWin();
        UpdateScoreUI();
    }

    void CheckPointWin()
    {
        if (playerPoints > 3)
        {
            playerGamesWon++;
            playerPoints = 0;
            enemyPoints = 0;
            if (playerGamesWon >= gamesToWin) ShowGameOver(true);
            else ResetRound();
        }
        else if (enemyPoints > 3)
        {
            enemyGamesWon++;
            playerPoints = 0;
            enemyPoints = 0;
            if (enemyGamesWon >= gamesToWin) ShowGameOver(false);
            else ResetRound();
        }
        else
        {
            ResetRound();
        }
    }

    void ShowGameOver(bool isVictory)
    {
        isGameStarted = false;
        isGamePaused = false;

        Ball b = Object.FindFirstObjectByType<Ball>();
        if (b != null) b.SetKinematic(true);

        if (isVictory)
        {
            if (victoryPanel != null) victoryPanel.SetActive(true);
        }
        else
        {
            if (losePanel != null) losePanel.SetActive(true);
        }
    }

    void UpdateScoreUI()
    {
        string pScore = GetTennisScore(playerPoints);
        string eScore = GetTennisScore(enemyPoints);

        if (vrScoreText != null)
        {
            if (vrEnemyScoreText != null)
            {
                vrScoreText.text = $"PLAYER: {pScore}";
                vrEnemyScoreText.text = $"AI: {eScore}";
            }
            else
            {
                vrScoreText.text = $"{pScore} : {eScore}";
            }
        }

        string gameScoreStr = $"SET  {playerGamesWon} : {enemyGamesWon}";
        if (vrGameScoreText != null) vrGameScoreText.text = gameScoreStr;
    }

    string GetTennisScore(int points)
    {
        if (points >= 0 && points <= 3) return tennisScores[points];
        return "40";
    }
}
