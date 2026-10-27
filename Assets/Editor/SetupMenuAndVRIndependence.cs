using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class SetupMenuAndVRIndependence
{
    [MenuItem("Tools/Capstone/Setup Menu and VR Independence")]
    public static void Execute()
    {
        Debug.Log("<color=cyan>[VR Tennis Setup]</color> Starting unified VR scene setup and PC removal...");

        // 1. Open MainGame_VR.unity
        string vrScenePath = "Assets/_캡스톤_기말/MainGame_VR.unity";
        var scene = EditorSceneManager.OpenScene(vrScenePath, OpenSceneMode.Single);

        // 2. Delete obsolete PC & separate menu scenes/assets
        DeleteObsoletePCAssets();

        // 3. Configure Build Settings (VR scene only at index 0)
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene(vrScenePath, true)
        };

        // 4. Find core scene objects
        var allGos = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(g => g != null && g.scene == scene)
            .ToArray();

        GameObject xrOriginGo = allGos.FirstOrDefault(g => g.name.Contains("XR Origin"));
        Camera mainCam = null;
        if (xrOriginGo != null)
        {
            xrOriginGo.SetActive(true);
            mainCam = xrOriginGo.GetComponentInChildren<Camera>(true);
            if (mainCam != null)
            {
                mainCam.gameObject.SetActive(true);
                mainCam.enabled = true;
                mainCam.tag = "MainCamera";
            }
        }

        // Remove old misplaced Spatial Panel Manipulator Model and any old VR_MenuCanvas
        foreach (var go in allGos)
        {
            if (go == null) continue;
            if (go.name == "Spatial Panel Manipulator Model" || go.name == "VR_MenuCanvas" ||
                go.name == "pcSelectionUI" || go.name == "pcCanvas" || go.name == "playCamera" || go.name == "PC_Player")
            {
                Object.DestroyImmediate(go);
            }
        }

        // Refresh allGos after cleanup
        allGos = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(g => g != null && g.scene == scene)
            .ToArray();

        // 5. Configure EventSystem & XRUIInputModule
        GameObject eventSystemGo = allGos.FirstOrDefault(g => g.name == "EventSystem");
        ConfigureEventSystem(eventSystemGo, mainCam);

        // 6. Configure VRPlayerController, Rackets, and Colliders
        ConfigureVRPlayerAndColliders(allGos, xrOriginGo);

        // 7. Build Unified World-Space VR_MenuCanvas (Start, Settings, Selection)
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        Sprite titleBannerSprite = LoadFirstSpriteAtPath("Assets/asset/메인 타이틀.png");
        Sprite vrControlSprite = LoadFirstSpriteAtPath("Assets/asset/VR조작.png");
        Sprite charSprite = LoadFirstSpriteAtPath("Assets/prefeb/메인케릭터.png");
        Sprite[] racketSprites = new Sprite[]
        {
            LoadFirstSpriteAtPath("Assets/_Tennis_Racket/0.png"),
            LoadFirstSpriteAtPath("Assets/_Tennis_Racket/1.png"),
            LoadFirstSpriteAtPath("Assets/_Tennis_Racket/2.png"),
            LoadFirstSpriteAtPath("Assets/_Tennis_Racket/3.png")
        }.Where(s => s != null).ToArray();

        GameObject vrMenuCanvas = BuildVRMenuCanvas(mainCam, fontAsset, titleBannerSprite, vrControlSprite, charSprite, racketSprites,
            out GameObject mainMenuPanel,
            out GameObject settingsPanel,
            out GameObject selectionPanel,
            out Image charImg,
            out Image racketImg,
            out TextMeshProUGUI charNameTxt,
            out TextMeshProUGUI racketNameTxt,
            out TextMeshProUGUI gamesValTxt,
            out TextMeshProUGUI diffValTxt,
            out TextMeshProUGUI speedValTxt);

        // 8. Upgrade Pause Canvas and Scoreboard GameOver Panels
        GameObject pauseGo = allGos.FirstOrDefault(g => g != null && (g.name == "pause" || g.name == "PauseMenuCanvas"));
        pauseGo = RebuildPauseCanvas(pauseGo, mainCam, fontAsset);

        GameObject scoreGo = xrOriginGo != null ? xrOriginGo.transform.Find("Score")?.gameObject : null;
        UpgradeScoreAndGameOverCanvas(scoreGo, mainCam, fontAsset,
            out TextMeshProUGUI vrScoreTxt,
            out TextMeshProUGUI vrEnemyScoreTxt,
            out TextMeshProUGUI vrGameScoreTxt,
            out GameObject victoryPanel,
            out GameObject losePanel);

        // 9. Configure GameManager
        GameObject gmGo = allGos.FirstOrDefault(g => g != null && g.name == "GameManager");
        if (gmGo != null)
        {
            GameManager gm = gmGo.GetComponent<GameManager>();
            if (gm != null)
            {
                gm.isVRMode = true;
                gm.isGameStarted = false;
                gm.isGamePaused = false;
                gm.vrPlayerRig = xrOriginGo;

                gm.vrMenuCanvas = vrMenuCanvas;
                gm.vrMainMenuPanel = mainMenuPanel;
                gm.vrSettingsPanel = settingsPanel;
                gm.vrSelectionUI = selectionPanel;
                gm.pauseMenuPanel = pauseGo;
                gm.vrCanvas = scoreGo;

                gm.charImageDisplay = charImg;
                gm.vrCharImageDisplay = charImg;
                gm.racketImageDisplay = racketImg;
                gm.vrRacketImageDisplay = racketImg;
                gm.charNameText = charNameTxt;
                gm.racketNameText = racketNameTxt;

                gm.gamesValueText = gamesValTxt;
                gm.difficultyValueText = diffValTxt;
                gm.speedValueText = speedValTxt;

                if (vrScoreTxt != null) gm.vrScoreText = vrScoreTxt;
                if (vrEnemyScoreTxt != null) gm.vrEnemyScoreText = vrEnemyScoreTxt;
                if (vrGameScoreTxt != null) gm.vrGameScoreText = vrGameScoreTxt;
                if (victoryPanel != null) gm.victoryPanel = victoryPanel;
                if (losePanel != null) gm.losePanel = losePanel;

                if (charSprite != null) gm.characterSprites = new Sprite[] { charSprite };
                if (racketSprites.Length > 0) gm.racketSprites = racketSprites;

                EditorUtility.SetDirty(gm);
            }
        }

        // 10. Save Scene and Assets
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("<color=green>[VR Tennis Setup]</color> Unified VR scene setup complete! PC version removed.");
    }

    private static void DeleteObsoletePCAssets()
    {
        string[] pathsToDelete = new string[]
        {
            "Assets/_캡스톤_기말/MainGame_PC.unity",
            "Assets/_캡스톤_기말/MainManu.unity",
            "Assets/ingame_scenes/main_manu.unity",
            "Assets/EventSystem_PC.prefab",
            "Assets/scripts/PlayerController.cs",
            "Assets/scripts/CameraManager.cs",
            "Assets/scripts/SelectionCameraHandler.cs"
        };

        foreach (string p in pathsToDelete)
        {
            if (AssetDatabase.LoadMainAssetAtPath(p) != null || System.IO.File.Exists(p))
            {
                AssetDatabase.DeleteAsset(p);
                Debug.Log($"[VR Tennis Setup] Deleted obsolete PC/Menu asset: {p}");
            }
        }
    }

    private static Sprite LoadFirstSpriteAtPath(string path)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets != null)
        {
            foreach (var a in assets)
            {
                if (a is Sprite s) return s;
            }
        }
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null)
        {
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void ConfigureEventSystem(GameObject eventSystemGo, Camera mainCam)
    {
        if (eventSystemGo == null) return;
        var uiModule = eventSystemGo.GetComponent<XRUIInputModule>();
        if (uiModule == null) return;

        if (mainCam != null) uiModule.uiCamera = mainCam;
        uiModule.enableXRInput = true;
        uiModule.enableMouseInput = true;
        uiModule.enableBuiltinActionsAsFallback = true;

        string[] actionGuids = AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset");
        if (actionGuids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(actionGuids[0]);
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            InputActionReference FindRef(string actionName)
            {
                foreach (var obj in subAssets)
                {
                    if (obj is InputActionReference r && r.action != null && r.action.name == actionName && r.name.Contains("UI"))
                        return r;
                }
                foreach (var obj in subAssets)
                {
                    if (obj is InputActionReference r && r.action != null && r.action.name == actionName)
                        return r;
                }
                return null;
            }

            var pt = FindRef("Point");
            var clk = FindRef("Click");
            var scr = FindRef("ScrollWheel");
            var nav = FindRef("Navigate");
            var sub = FindRef("Submit");
            var can = FindRef("Cancel");

            if (pt != null) uiModule.pointAction = pt;
            if (clk != null) uiModule.leftClickAction = clk;
            if (scr != null) uiModule.scrollWheelAction = scr;
            if (nav != null) uiModule.navigateAction = nav;
            if (sub != null) uiModule.submitAction = sub;
            if (can != null) uiModule.cancelAction = can;
        }

        EditorUtility.SetDirty(uiModule);
    }

    private static void ConfigureVRPlayerAndColliders(GameObject[] allGos, GameObject xrOriginGo)
    {
        // Make move-limit trigger so it doesn't block VR rays or ball
        GameObject moveLimit = allGos.FirstOrDefault(g => g.name == "move-limit");
        if (moveLimit != null)
        {
            BoxCollider bc = moveLimit.GetComponent<BoxCollider>();
            if (bc != null)
            {
                bc.isTrigger = true;
                EditorUtility.SetDirty(bc);
            }
        }

        GameObject courtGo = allGos.FirstOrDefault(g => g.name.Contains("Tennis_Court"));
        if (courtGo != null)
        {
            courtGo.tag = "Court";
            foreach (Transform t in courtGo.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.tag = "Court";
            }
            EditorUtility.SetDirty(courtGo);
        }

        VRPlayerController vrPlayer = allGos.Select(g => g.GetComponent<VRPlayerController>()).FirstOrDefault(c => c != null);
        if (vrPlayer != null && xrOriginGo != null)
        {
            vrPlayer.rigTransform = xrOriginGo.transform;
            vrPlayer.animator = vrPlayer.GetComponent<Animator>();

            Transform leftCtrl = xrOriginGo.transform.Find("Camera Offset/Left Controller");
            Transform rightCtrl = xrOriginGo.transform.Find("Camera Offset/Right Controller");
            if (leftCtrl != null) vrPlayer.vrLeftHand = leftCtrl;
            if (rightCtrl != null) vrPlayer.vrRightHand = rightCtrl;

            // Ordered to match 0.png (Orange), 1.png (Blue), 2.png (Lightgreen), 3.png (Pink)
            string[] orderedRacketNames = { "Tennis_Racket", "Tennis_Racket_Blue", "Tennis_Racket_Lightgreen", "Tennis_Racket_Pink" };
            List<GameObject> orderedRackets = new List<GameObject>();

            foreach (string rName in orderedRacketNames)
            {
                GameObject rGo = allGos.FirstOrDefault(g => g.name == rName);
                if (rGo != null)
                {
                    if (rGo.CompareTag("Net")) rGo.tag = "Untagged";
                    if (rGo.GetComponent<RacketHit>() == null) rGo.AddComponent<RacketHit>();
                    Collider col = rGo.GetComponent<Collider>();
                    if (col != null)
                    {
                        // Must be trigger so Right Controller UI raycast passes through unobstructed!
                        col.isTrigger = true;
                    }
                    rGo.SetActive(orderedRackets.Count == 0);
                    orderedRackets.Add(rGo);
                    EditorUtility.SetDirty(rGo);
                }
            }

            if (orderedRackets.Count > 0)
            {
                vrPlayer.rackets = orderedRackets.ToArray();
                Transform stringsTf = orderedRackets[0].transform.Find("Strings");
                vrPlayer.racketCenter = stringsTf != null ? stringsTf : orderedRackets[0].transform;
            }

            EditorUtility.SetDirty(vrPlayer);
        }
    }

    private static GameObject BuildVRMenuCanvas(
        Camera mainCam,
        TMP_FontAsset font,
        Sprite titleSprite,
        Sprite vrControlSprite,
        Sprite charSprite,
        Sprite[] racketSprites,
        out GameObject mainMenuPanel,
        out GameObject settingsPanel,
        out GameObject selectionPanel,
        out Image charImg,
        out Image racketImg,
        out TextMeshProUGUI charNameTxt,
        out TextMeshProUGUI racketNameTxt,
        out TextMeshProUGUI gamesValTxt,
        out TextMeshProUGUI diffValTxt,
        out TextMeshProUGUI speedValTxt)
    {
        GameObject canvasObj = new GameObject("VR_MenuCanvas");
        canvasObj.layer = LayerMask.NameToLayer("UI");
        RectTransform canvasRT = canvasObj.AddComponent<RectTransform>();
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = mainCam;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObj.AddComponent<GraphicRaycaster>();
        canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();
        canvasObj.AddComponent<SelectionUIHandler>();

        // Position directly in front of VR Player at (-0.06, 1.48, -3.65) facing player (rotation 0, 180, 0)
        canvasRT.sizeDelta = new Vector2(1040, 700);
        canvasRT.position = new Vector3(-0.06f, 1.48f, -3.65f);
        canvasRT.rotation = Quaternion.Euler(0f, 180f, 0f);
        canvasRT.localScale = new Vector3(0.002f, 0.002f, 0.002f);

        // ==================== PANEL 1: VR MAIN MENU (START SCREEN) ====================
        mainMenuPanel = CreatePanel("VR_MainMenuPanel", canvasObj.transform, new Color(0.05f, 0.08f, 0.14f, 0.95f));
        CreateBorderOutline(mainMenuPanel.transform, new Color(0.2f, 0.85f, 0.55f, 0.8f));

        if (titleSprite != null)
        {
            GameObject bannerGo = CreateUIObject("BannerImage", mainMenuPanel.transform);
            SetRect(bannerGo, new Vector2(0.5f, 0.5f), new Vector2(0, 175), new Vector2(640, 250));
            Image bImg = bannerGo.AddComponent<Image>();
            bImg.sprite = titleSprite;
            bImg.preserveAspect = true;
            bImg.raycastTarget = false;
        }

        CreateLabel("MainTitle", mainMenuPanel.transform, font, "VR TENNIS CHAMPIONSHIP", 44, FontStyles.Bold, new Color(0.95f, 1f, 0.98f), new Vector2(0, 15), new Vector2(900, 60));
        CreateLabel("SubTitle", mainMenuPanel.transform, font, "Aim with VR Controller Laser & Pull Trigger to Select", 22, FontStyles.Normal, new Color(0.65f, 0.85f, 1f), new Vector2(0, -30), new Vector2(850, 40));

        CreateStyledButton("StartGameBtn", mainMenuPanel.transform, font, "START GAME", 32, new Vector2(0, -110), new Vector2(460, 74), new Color(0.12f, 0.68f, 0.38f, 1f));
        CreateStyledButton("OpenSettingsBtn", mainMenuPanel.transform, font, "SETTINGS & CONTROLS", 28, new Vector2(0, -200), new Vector2(460, 68), new Color(0.18f, 0.45f, 0.78f, 1f));
        CreateStyledButton("QuitBtn", mainMenuPanel.transform, font, "EXIT GAME", 26, new Vector2(0, -285), new Vector2(460, 62), new Color(0.68f, 0.22f, 0.25f, 1f));

        // ==================== PANEL 2: VR SETTINGS & CONTROLS ====================
        settingsPanel = CreatePanel("VR_SettingsPanel", canvasObj.transform, new Color(0.05f, 0.08f, 0.14f, 0.96f));
        CreateBorderOutline(settingsPanel.transform, new Color(0.3f, 0.7f, 1f, 0.8f));
        settingsPanel.SetActive(false);

        CreateLabel("SettingsTitle", settingsPanel.transform, font, "VR SETTINGS & CONTROLLER GUIDE", 38, FontStyles.Bold, new Color(0.4f, 0.9f, 1f), new Vector2(0, 285), new Vector2(900, 55));

        // Left Column: Interactive Settings
        GameObject leftBox = CreateUIObject("SettingsLeftBox", settingsPanel.transform);
        SetRect(leftBox, new Vector2(0.5f, 0.5f), new Vector2(-240, 20), new Vector2(460, 430));
        leftBox.AddComponent<Image>().color = new Color(0.09f, 0.14f, 0.22f, 0.9f);

        // 1. Games to Win
        CreateLabel("GamesLabel", leftBox.transform, font, "GAMES TO WIN MATCH", 22, FontStyles.Bold, new Color(0.8f, 0.9f, 1f), new Vector2(0, 165), new Vector2(400, 35));
        CreateStyledButton("GamesMinusBtn", leftBox.transform, font, "-", 36, new Vector2(-150, 115), new Vector2(75, 58), new Color(0.22f, 0.35f, 0.55f, 1f));
        gamesValTxt = CreateLabel("GamesValueText", leftBox.transform, font, "3 GAMES", 28, FontStyles.Bold, Color.yellow, new Vector2(0, 115), new Vector2(200, 55));
        CreateStyledButton("GamesPlusBtn", leftBox.transform, font, "+", 36, new Vector2(150, 115), new Vector2(75, 58), new Color(0.22f, 0.35f, 0.55f, 1f));

        // 2. AI Difficulty
        CreateLabel("DiffLabel", leftBox.transform, font, "AI OPPONENT DIFFICULTY", 22, FontStyles.Bold, new Color(0.8f, 0.9f, 1f), new Vector2(0, 35), new Vector2(400, 35));
        CreateStyledButton("DiffPrevBtn", leftBox.transform, font, "<", 32, new Vector2(-150, -15), new Vector2(75, 58), new Color(0.22f, 0.35f, 0.55f, 1f));
        diffValTxt = CreateLabel("DiffValueText", leftBox.transform, font, "NORMAL", 28, FontStyles.Bold, new Color(0.5f, 1f, 0.7f), new Vector2(0, -15), new Vector2(200, 55));
        CreateStyledButton("DiffNextBtn", leftBox.transform, font, ">", 32, new Vector2(150, -15), new Vector2(75, 58), new Color(0.22f, 0.35f, 0.55f, 1f));

        // 3. Player Move Speed
        CreateLabel("SpeedLabel", leftBox.transform, font, "VR PLAYER MOVE SPEED", 22, FontStyles.Bold, new Color(0.8f, 0.9f, 1f), new Vector2(0, -95), new Vector2(400, 35));
        CreateStyledButton("SpeedMinusBtn", leftBox.transform, font, "-", 36, new Vector2(-150, -145), new Vector2(75, 58), new Color(0.22f, 0.35f, 0.55f, 1f));
        speedValTxt = CreateLabel("SpeedValueText", leftBox.transform, font, "100%", 28, FontStyles.Bold, Color.cyan, new Vector2(0, -145), new Vector2(200, 55));
        CreateStyledButton("SpeedPlusBtn", leftBox.transform, font, "+", 36, new Vector2(150, -145), new Vector2(75, 58), new Color(0.22f, 0.35f, 0.55f, 1f));

        // Right Column: VR Controls Guide
        GameObject rightBox = CreateUIObject("ControlsRightBox", settingsPanel.transform);
        SetRect(rightBox, new Vector2(0.5f, 0.5f), new Vector2(240, 20), new Vector2(460, 430));
        rightBox.AddComponent<Image>().color = new Color(0.09f, 0.14f, 0.22f, 0.9f);

        CreateLabel("CtrlHeader", rightBox.transform, font, "VR CONTROLLER GUIDE", 24, FontStyles.Bold, new Color(1f, 0.88f, 0.4f), new Vector2(0, 175), new Vector2(420, 35));
        if (vrControlSprite != null)
        {
            GameObject ctrlImgGo = CreateUIObject("ControllerDiagram", rightBox.transform);
            SetRect(ctrlImgGo, new Vector2(0.5f, 0.5f), new Vector2(0, 55), new Vector2(360, 180));
            Image cImg = ctrlImgGo.AddComponent<Image>();
            cImg.sprite = vrControlSprite;
            cImg.preserveAspect = true;
            cImg.raycastTarget = false;
        }
        string ctrlDesc =
            "• Left Stick : Move on Court\n" +
            "• Left Grip / X : Quick Slide\n" +
            "• Serve Toss : Lift Left Hand or Trigger\n" +
            "• Swing Hit : Swing Right Hand or Trigger\n" +
            "• Pause Menu : Menu Button or Y Button";
        var ctrlTxt = CreateLabel("CtrlDesc", rightBox.transform, font, ctrlDesc, 19, FontStyles.Normal, Color.white, new Vector2(0, -115), new Vector2(420, 145));
        ctrlTxt.alignment = TextAlignmentOptions.Left;

        CreateStyledButton("CloseSettingsBtn", settingsPanel.transform, font, "BACK", 28, new Vector2(0, -265), new Vector2(340, 68), new Color(0.55f, 0.25f, 0.28f, 1f));

        // ==================== PANEL 3: CHARACTER & RACKET SELECTION ====================
        selectionPanel = CreatePanel("vrSelectionUI", canvasObj.transform, new Color(0.05f, 0.08f, 0.14f, 0.96f));
        CreateBorderOutline(selectionPanel.transform, new Color(0.25f, 0.95f, 0.6f, 0.85f));
        selectionPanel.SetActive(false);

        CreateLabel("SelectionTitle", selectionPanel.transform, font, "SELECT CHARACTER & RACKET", 38, FontStyles.Bold, Color.white, new Vector2(0, 290), new Vector2(920, 50));
        CreateLabel("SelectionHint", selectionPanel.transform, font, "Click [<] [>] with VR Controller Trigger or Flick Thumbstick Left/Right", 20, FontStyles.Normal, new Color(0.65f, 0.9f, 1f), new Vector2(0, 245), new Vector2(920, 32));

        // Left Card: Character Selection
        GameObject charCard = CreateUIObject("CharacterCard", selectionPanel.transform);
        SetRect(charCard, new Vector2(0.5f, 0.5f), new Vector2(-245, 5), new Vector2(450, 410));
        charCard.AddComponent<Image>().color = new Color(0.09f, 0.14f, 0.22f, 0.95f);

        CreateLabel("CharLabel", charCard.transform, font, "CHARACTER", 26, FontStyles.Bold, new Color(0.4f, 1f, 0.75f), new Vector2(0, 165), new Vector2(380, 40));
        CreateStyledButton("CharPrevBtn", charCard.transform, font, "<", 38, new Vector2(-165, 15), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));
        CreateStyledButton("CharNextBtn", charCard.transform, font, ">", 38, new Vector2(165, 15), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));

        GameObject charBorder = CreateUIObject("CharImage_Border", charCard.transform);
        SetRect(charBorder, new Vector2(0.5f, 0.5f), new Vector2(0, 15), new Vector2(220, 220));
        charBorder.AddComponent<Image>().color = new Color(0.15f, 0.22f, 0.32f, 1f);

        GameObject charImgGo = CreateUIObject("CharImage", charBorder.transform);
        SetRectFull(charImgGo, 8f);
        charImg = charImgGo.AddComponent<Image>();
        if (charSprite != null) charImg.sprite = charSprite;
        charImg.preserveAspect = true;
        charImg.raycastTarget = false;

        charNameTxt = CreateLabel("CharNameText", charCard.transform, font, "EMERALD HEROINE\n<size=20><color=#80FFAA>[BALANCED] SPD **  PWR **</color></size>", 23, FontStyles.Bold, Color.white, new Vector2(0, -145), new Vector2(420, 75));

        // Right Card: Racket Selection
        GameObject racketCard = CreateUIObject("RacketCard", selectionPanel.transform);
        SetRect(racketCard, new Vector2(0.5f, 0.5f), new Vector2(245, 5), new Vector2(450, 410));
        racketCard.AddComponent<Image>().color = new Color(0.09f, 0.14f, 0.22f, 0.95f);

        CreateLabel("RacketLabel", racketCard.transform, font, "TENNIS RACKET", 26, FontStyles.Bold, new Color(1f, 0.85f, 0.4f), new Vector2(0, 165), new Vector2(380, 40));
        CreateStyledButton("RacketPrevBtn", racketCard.transform, font, "<", 38, new Vector2(-165, 15), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));
        CreateStyledButton("RacketNextBtn", racketCard.transform, font, ">", 38, new Vector2(165, 15), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));

        GameObject racketBorder = CreateUIObject("RacketImage_Border", racketCard.transform);
        SetRect(racketBorder, new Vector2(0.5f, 0.5f), new Vector2(0, 15), new Vector2(220, 220));
        racketBorder.AddComponent<Image>().color = new Color(0.15f, 0.22f, 0.32f, 1f);

        GameObject racketImgGo = CreateUIObject("RacketImage", racketBorder.transform);
        SetRectFull(racketImgGo, 8f);
        racketImg = racketImgGo.AddComponent<Image>();
        if (racketSprites != null && racketSprites.Length > 0) racketImg.sprite = racketSprites[0];
        racketImg.preserveAspect = true;
        racketImg.raycastTarget = false;

        racketNameTxt = CreateLabel("RacketNameText", racketCard.transform, font, "CLASSIC ORANGE\n<size=20><color=#FFD180>Standard Balanced Racket</color></size>", 23, FontStyles.Bold, Color.white, new Vector2(0, -145), new Vector2(420, 75));

        // Bottom Buttons: Back & Start Match
        CreateStyledButton("BackToMainBtn", selectionPanel.transform, font, "BACK TO MENU", 26, new Vector2(-210, -265), new Vector2(320, 72), new Color(0.48f, 0.22f, 0.25f, 1f));
        CreateStyledButton("VRPlayBtn", selectionPanel.transform, font, "START MATCH!", 32, new Vector2(190, -265), new Vector2(420, 72), new Color(0.12f, 0.72f, 0.38f, 1f));

        EditorUtility.SetDirty(canvasObj);
        return canvasObj;
    }

    private static GameObject RebuildPauseCanvas(GameObject oldPauseGo, Camera mainCam, TMP_FontAsset font)
    {
        if (oldPauseGo != null)
        {
            Object.DestroyImmediate(oldPauseGo);
        }

        GameObject pauseCanvasGo = new GameObject("pause");
        pauseCanvasGo.layer = LayerMask.NameToLayer("UI");
        RectTransform rt = pauseCanvasGo.AddComponent<RectTransform>();
        Canvas c = pauseCanvasGo.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = mainCam;
        c.sortingOrder = 20;

        pauseCanvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        pauseCanvasGo.AddComponent<GraphicRaycaster>();
        pauseCanvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();

        rt.sizeDelta = new Vector2(700, 520);
        rt.position = new Vector3(-0.06f, 1.48f, -3.4f);
        rt.rotation = Quaternion.Euler(0f, 180f, 0f);
        rt.localScale = new Vector3(0.002f, 0.002f, 0.002f);

        GameObject panel = CreatePanel("PauseMenuPanel", pauseCanvasGo.transform, new Color(0.05f, 0.08f, 0.14f, 0.95f));
        CreateBorderOutline(panel.transform, new Color(1f, 0.8f, 0.3f, 0.85f));

        CreateLabel("Title", panel.transform, font, "GAME PAUSED", 44, FontStyles.Bold, new Color(1f, 0.9f, 0.4f), new Vector2(0, 165), new Vector2(550, 65));
        CreateStyledButton("ResumeBtn", panel.transform, font, "RESUME MATCH", 30, new Vector2(0, 45), new Vector2(420, 72), new Color(0.14f, 0.68f, 0.38f, 1f));
        CreateStyledButton("PauseSettingsBtn", panel.transform, font, "SETTINGS", 28, new Vector2(0, -45), new Vector2(420, 68), new Color(0.18f, 0.45f, 0.78f, 1f));
        CreateStyledButton("MenuBtn", panel.transform, font, "MAIN MENU", 28, new Vector2(0, -135), new Vector2(420, 68), new Color(0.65f, 0.22f, 0.25f, 1f));

        pauseCanvasGo.SetActive(false);
        EditorUtility.SetDirty(pauseCanvasGo);
        return pauseCanvasGo;
    }

    private static void UpgradeScoreAndGameOverCanvas(
        GameObject scoreGo,
        Camera mainCam,
        TMP_FontAsset font,
        out TextMeshProUGUI vrScoreTxt,
        out TextMeshProUGUI vrEnemyScoreTxt,
        out TextMeshProUGUI vrGameScoreTxt,
        out GameObject victoryPanel,
        out GameObject losePanel)
    {
        vrScoreTxt = null;
        vrEnemyScoreTxt = null;
        vrGameScoreTxt = null;
        victoryPanel = null;
        losePanel = null;

        if (scoreGo == null) return;

        Canvas c = scoreGo.GetComponent<Canvas>();
        if (c != null)
        {
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = mainCam;
        }
        if (scoreGo.GetComponent<GraphicRaycaster>() == null) scoreGo.AddComponent<GraphicRaycaster>();
        if (scoreGo.GetComponent<TrackedDeviceGraphicRaycaster>() == null) scoreGo.AddComponent<TrackedDeviceGraphicRaycaster>();

        vrScoreTxt = scoreGo.transform.Find("Tennis_Scoreboard_Root/PlayerScore")?.GetComponent<TextMeshProUGUI>();
        vrEnemyScoreTxt = scoreGo.transform.Find("Tennis_Scoreboard_Root/EnemyScore")?.GetComponent<TextMeshProUGUI>();
        vrGameScoreTxt = scoreGo.transform.Find("Tennis_Scoreboard_Root/GameScore")?.GetComponent<TextMeshProUGUI>();

        Transform oldVic = scoreGo.transform.Find("VictoryPanel");
        if (oldVic != null) Object.DestroyImmediate(oldVic.gameObject);
        Transform oldLose = scoreGo.transform.Find("LosePanel");
        if (oldLose != null) Object.DestroyImmediate(oldLose.gameObject);

        victoryPanel = BuildGameOverPanel("VictoryPanel", scoreGo.transform, font, "VICTORY!", new Color(1f, 0.88f, 0.25f));
        losePanel = BuildGameOverPanel("LosePanel", scoreGo.transform, font, "DEFEAT...", new Color(1f, 0.35f, 0.35f));

        EditorUtility.SetDirty(scoreGo);
    }

    private static GameObject BuildGameOverPanel(string name, Transform parent, TMP_FontAsset font, string title, Color titleColor)
    {
        GameObject panel = CreateUIObject(name, parent);
        SetRect(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(650, 420));
        panel.AddComponent<Image>().color = new Color(0.05f, 0.08f, 0.14f, 0.95f);
        CreateBorderOutline(panel.transform, titleColor);

        CreateLabel("ResultText", panel.transform, font, title, 64, FontStyles.Bold, titleColor, new Vector2(0, 100), new Vector2(580, 90));
        CreateStyledButton("RematchBtn", panel.transform, font, "PLAY AGAIN", 30, new Vector2(0, -15), new Vector2(380, 70), new Color(0.14f, 0.68f, 0.38f, 1f));
        CreateStyledButton("GameOverMenuBtn", panel.transform, font, "MAIN MENU", 28, new Vector2(0, -105), new Vector2(380, 66), new Color(0.55f, 0.25f, 0.28f, 1f));

        panel.SetActive(false);
        return panel;
    }

    // ==================== UI Builder Helpers ====================

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static GameObject CreatePanel(string name, Transform parent, Color bgColor)
    {
        GameObject panel = CreateUIObject(name, parent);
        SetRectFull(panel, 0f);
        Image img = panel.AddComponent<Image>();
        img.color = bgColor;
        return panel;
    }

    private static void CreateBorderOutline(Transform parent, Color borderColor)
    {
        Outline outline = parent.gameObject.GetComponent<Outline>();
        if (outline == null) outline = parent.gameObject.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(3f, -3f);
    }

    private static void SetRectFull(GameObject go, float padding)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    private static void SetRect(GameObject go, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }

    private static TextMeshProUGUI CreateLabel(string name, Transform parent, TMP_FontAsset font, string text, float fontSize, FontStyles style, Color color, Vector2 pos, Vector2 size)
    {
        GameObject go = CreateUIObject(name, parent);
        SetRect(go, new Vector2(0.5f, 0.5f), pos, size);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateStyledButton(string name, Transform parent, TMP_FontAsset font, string label, float fontSize, Vector2 pos, Vector2 size, Color normalColor)
    {
        GameObject btnGo = CreateUIObject(name, parent);
        SetRect(btnGo, new Vector2(0.5f, 0.5f), pos, size);

        Image img = btnGo.AddComponent<Image>();
        img.color = normalColor;
        img.raycastTarget = true;

        Button btn = btnGo.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        cb.selectedColor = Color.white;
        btn.colors = cb;

        CreateBorderOutline(btnGo.transform, new Color(1f, 1f, 1f, 0.25f));

        GameObject txtGo = CreateUIObject("Text (TMP)", btnGo.transform);
        SetRectFull(txtGo, 4f);
        TextMeshProUGUI tmp = txtGo.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = label;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }
}
