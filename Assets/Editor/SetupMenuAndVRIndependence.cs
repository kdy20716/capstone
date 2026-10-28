using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

[InitializeOnLoad]
public class SetupMenuAndVRIndependence
{
    static SetupMenuAndVRIndependence()
    {
        EditorApplication.update -= CheckPlayTriggers;
        EditorApplication.update += CheckPlayTriggers;

        EditorApplication.delayCall += () =>
        {
            if (UnityMCPServer.Core.UnityMCPServer.Status == UnityMCPServer.Models.McpStatus.Error)
            {
                UnityMCPServer.Core.UnityMCPServer.Restart();
            }
        };
    }

    private static void CheckPlayTriggers()
    {
        var playFlag = GameObject.Find("TRIGGER_PLAY_MODE");
        if (playFlag != null)
        {
            Object.DestroyImmediate(playFlag);
            EditorApplication.isPlaying = true;
        }
        var stopFlag = GameObject.Find("TRIGGER_STOP_MODE");
        if (stopFlag != null)
        {
            Object.DestroyImmediate(stopFlag);
            EditorApplication.isPlaying = false;
        }
    }

    [MenuItem("Tools/Capstone/Execute and Play Match")]
    public static void ExecuteAndPlayMatch()
    {
        Execute();
        EditorApplication.isPlaying = true;
    }

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

        // 9. Configure GameManager and ViewPoints (45-degree diagonal overview viewpoint)
        GameObject overviewGo = allGos.FirstOrDefault(g => g != null && (g.name == "Overview_60Deg_ViewPoint" || g.name == "Overview_Diagonal_ViewPoint"));
        if (overviewGo == null) overviewGo = new GameObject("Overview_Diagonal_ViewPoint");
        overviewGo.name = "Overview_Diagonal_ViewPoint";
        overviewGo.transform.position = new Vector3(11.0f, 13.5f, -3.5f);
        overviewGo.transform.rotation = Quaternion.Euler(39.0f, 225.0f, 0f);
        EditorUtility.SetDirty(overviewGo);

        GameObject baselineGo = allGos.FirstOrDefault(g => g != null && g.name == "Player_Baseline_ViewPoint");
        if (baselineGo == null) baselineGo = new GameObject("Player_Baseline_ViewPoint");
        baselineGo.transform.position = new Vector3(-0.06f, 0.0f, -1.499f);
        baselineGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        EditorUtility.SetDirty(baselineGo);

        // Configure Ball Audio Clips
        Ball ball = allGos.Select(g => g != null ? g.GetComponent<Ball>() : null).FirstOrDefault(b => b != null);
        if (ball != null)
        {
            AudioClip hitClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_tang_hit.wav") ?? AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_racket_hit.wav");
            AudioClip powerClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_power_tang.wav");
            AudioClip bounceClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/tennis_court_bounce.wav");
            ball.hitSound = hitClip;
            ball.powerHitSound = powerClip;
            ball.bounceSound = bounceClip;
            AudioSource aSrc = ball.GetComponent<AudioSource>();
            if (aSrc == null) aSrc = ball.gameObject.AddComponent<AudioSource>();
            aSrc.playOnAwake = false;
            aSrc.spatialBlend = 0.5f;
            EditorUtility.SetDirty(ball);
            EditorUtility.SetDirty(aSrc);
        }

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

                gm.overviewViewPoint = overviewGo.transform;
                gm.playerViewPoint = baselineGo.transform;

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

        // 9.5 Apply scenario override if requested
        string scenarioFile = "C:/Users/kdy02/.gemini/antigravity/brain/8ee1e380-fc8f-435b-a2bc-2d9e376dce50/scratch/scenario_target.txt";
        if (!System.IO.File.Exists(scenarioFile))
        {
            scenarioFile = Application.dataPath + "/scenario_target.txt";
        }
        if (!System.IO.File.Exists(scenarioFile))
        {
            scenarioFile = System.IO.Path.Combine(Application.dataPath, "../scenario_target.txt");
        }
        if (System.IO.File.Exists(scenarioFile))
        {
            string target = System.IO.File.ReadAllText(scenarioFile).Trim();
            Debug.Log("<color=yellow>[VR Tennis Scenario]</color> Applying Scenario Target: " + target);
            ApplyScenarioTarget(target, scene, allGos, vrMenuCanvas, mainMenuPanel, selectionPanel, scoreGo, pauseGo, xrOriginGo);
        }

        // 10. Save Scene and Assets
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("<color=green>[VR Tennis Setup]</color> Unified VR scene setup complete! PC version removed.");
    }

    private static void ApplyScenarioTarget(
        string target,
        UnityEngine.SceneManagement.Scene scene,
        GameObject[] oldGos,
        GameObject vrMenuCanvas,
        GameObject mainMenuPanel,
        GameObject vrSelectionUI,
        GameObject scoreRoot,
        GameObject pauseObj,
        GameObject vrPlayerRig)
    {
        var allGos = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(g => g != null && g.scene == scene)
            .ToArray();

        var tennisBallRoot = allGos.FirstOrDefault(g => g != null && g.name == "tennis_ball");
        var ballObj = allGos.FirstOrDefault(g => g != null && g.name == "ball2");
        if (scoreRoot == null) scoreRoot = vrPlayerRig != null ? vrPlayerRig.transform.Find("Score")?.gameObject : allGos.FirstOrDefault(g => g != null && g.name == "Score");
        if (pauseObj == null) pauseObj = allGos.FirstOrDefault(g => g != null && (g.name == "pause" || g.name == "PauseMenuCanvas"));
        var blueRacket = allGos.FirstOrDefault(g => g != null && (g.name == "Tennis_Racket_Blue" || g.name == "tennis_racket_blue"));
        var rightCtrl = vrPlayerRig != null ? vrPlayerRig.transform.Find("Camera Offset/Right Controller") : null;
        var leftCtrl = vrPlayerRig != null ? vrPlayerRig.transform.Find("Camera Offset/Left Controller") : null;

        if (target != "1" && target != "2")
        {
            if (vrMenuCanvas != null) vrMenuCanvas.SetActive(false);
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (vrSelectionUI != null) vrSelectionUI.SetActive(false);

            if (vrPlayerRig != null)
            {
                vrPlayerRig.transform.position = new Vector3(-0.06f, 0f, -1.499f);
                vrPlayerRig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }

            if (rightCtrl != null)
            {
                rightCtrl.localPosition = new Vector3(0.25f, 1.05f, 0.45f);
                rightCtrl.localRotation = Quaternion.identity;
                EditorUtility.SetDirty(rightCtrl);
            }
            if (leftCtrl != null)
            {
                leftCtrl.localPosition = new Vector3(-0.25f, 1.05f, 0.45f);
                leftCtrl.localRotation = Quaternion.identity;
                EditorUtility.SetDirty(leftCtrl);
            }
        }

        switch (target)
        {
            case "2": // 45-degree Diagonal Overhead Selection View
                Debug.Log($"<color=cyan>[Case 2 Debug]</color> mainMenuPanel={(mainMenuPanel != null)} ({mainMenuPanel?.name}), vrSelectionUI={(vrSelectionUI != null)} ({vrSelectionUI?.name})");
                if (mainMenuPanel != null)
                {
                    mainMenuPanel.SetActive(false);
                    EditorUtility.SetDirty(mainMenuPanel);
                }
                if (vrSelectionUI != null)
                {
                    vrSelectionUI.SetActive(true);
                    EditorUtility.SetDirty(vrSelectionUI);
                }
                if (vrMenuCanvas != null)
                {
                    vrMenuCanvas.SetActive(true);
                    vrMenuCanvas.transform.position = new Vector3(9.02f, 11.23f, -5.48f);
                    vrMenuCanvas.transform.rotation = Quaternion.Euler(39.0f, 225.0f, 0f);
                    vrMenuCanvas.transform.localScale = new Vector3(0.0032f, 0.0032f, 0.0032f);
                    EditorUtility.SetDirty(vrMenuCanvas);
                }
                if (pauseObj != null) { pauseObj.SetActive(false); EditorUtility.SetDirty(pauseObj); }
                if (scoreRoot != null) { scoreRoot.SetActive(false); EditorUtility.SetDirty(scoreRoot); }
                if (blueRacket != null) { blueRacket.SetActive(false); EditorUtility.SetDirty(blueRacket); }
                if (tennisBallRoot != null) { tennisBallRoot.SetActive(false); EditorUtility.SetDirty(tennisBallRoot); }
                if (vrPlayerRig != null)
                {
                    vrPlayerRig.transform.position = new Vector3(11.0f, 13.5f, -3.5f);
                    vrPlayerRig.transform.rotation = Quaternion.Euler(39.0f, 225.0f, 0f);
                    EditorUtility.SetDirty(vrPlayerRig);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(vrPlayerRig.transform);
                }
                break;

            case "3": // Baseline Ground Match
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(true);
                    var vic = scoreRoot.transform.Find("VictoryPanel");
                    if (vic != null) vic.gameObject.SetActive(false);
                    var lose = scoreRoot.transform.Find("LosePanel");
                    if (lose != null) lose.gameObject.SetActive(false);
                }
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = Vector3.zero;
                    blueRacket.transform.localEulerAngles = new Vector3(-110.964f, 4.646f, 171.02f);
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(0.28f, 1.15f, -2.4f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                }
                break;

            case "4": // Serve Toss
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(true);
                    var vic = scoreRoot.transform.Find("VictoryPanel");
                    if (vic != null) vic.gameObject.SetActive(false);
                    var lose = scoreRoot.transform.Find("LosePanel");
                    if (lose != null) lose.gameObject.SetActive(false);
                }
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = Vector3.zero;
                    blueRacket.transform.localEulerAngles = new Vector3(-110.964f, 4.646f, 171.02f);
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(0.18f, 1.85f, -3.2f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                }
                break;

            case "5": // Player Hit Ball
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(true);
                    var vic = scoreRoot.transform.Find("VictoryPanel");
                    if (vic != null) vic.gameObject.SetActive(false);
                    var lose = scoreRoot.transform.Find("LosePanel");
                    if (lose != null) lose.gameObject.SetActive(false);
                }
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = new Vector3(0.05f, 0.05f, 0.15f);
                    blueRacket.transform.localEulerAngles = new Vector3(-80f, -15f, 155f);
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(0.02f, 1.35f, -5.5f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                }
                break;

            case "6": // AI Rally Return
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(true);
                    var vic = scoreRoot.transform.Find("VictoryPanel");
                    if (vic != null) vic.gameObject.SetActive(false);
                    var lose = scoreRoot.transform.Find("LosePanel");
                    if (lose != null) lose.gameObject.SetActive(false);
                }
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = Vector3.zero;
                    blueRacket.transform.localEulerAngles = new Vector3(-110.964f, 4.646f, 171.02f);
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(-0.2f, 1.45f, -8.5f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                }
                break;

            case "7": // Scoreboard Update (15 : 0)
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(true);
                    var vic = scoreRoot.transform.Find("VictoryPanel");
                    if (vic != null) vic.gameObject.SetActive(false);
                    var lose = scoreRoot.transform.Find("LosePanel");
                    if (lose != null) lose.gameObject.SetActive(false);

                    var ps = scoreRoot.transform.Find("Tennis_Scoreboard_Root/PlayerScore")?.GetComponent<TextMeshProUGUI>();
                    if (ps != null) ps.text = "PLAYER: 15";
                }
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = Vector3.zero;
                    blueRacket.transform.localEulerAngles = new Vector3(-110.964f, 4.646f, 171.02f);
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(-0.25f, 0.12f, -16.0f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                }
                break;

            case "8": // Pause Menu
                if (scoreRoot != null) scoreRoot.SetActive(false);
                if (blueRacket != null) blueRacket.SetActive(false);
                if (ballObj != null) ballObj.SetActive(false);
                if (pauseObj != null)
                {
                    pauseObj.SetActive(true);
                    pauseObj.transform.position = new Vector3(-0.06f, 1.48f, -3.2f);
                    pauseObj.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    pauseObj.transform.localScale = new Vector3(0.0018f, 0.0018f, 0.0018f);
                }
                break;

            case "9": // Victory Screen
                if (pauseObj != null) pauseObj.SetActive(false);
                if (blueRacket != null) blueRacket.SetActive(false);
                if (ballObj != null) ballObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(false);

                    var vicPanel = scoreRoot.transform.Find("VictoryPanel");
                    if (vicPanel != null)
                    {
                        vicPanel.gameObject.SetActive(true);
                        vicPanel.localPosition = new Vector3(0f, 0f, 0f);
                    }

                    var loseP = scoreRoot.transform.Find("LosePanel");
                    if (loseP != null) loseP.gameObject.SetActive(false);
                }
                break;

            case "10": // Defeat Screen
                if (pauseObj != null) pauseObj.SetActive(false);
                if (blueRacket != null) blueRacket.SetActive(false);
                if (ballObj != null) ballObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(false);

                    var vicPanel = scoreRoot.transform.Find("VictoryPanel");
                    if (vicPanel != null) vicPanel.gameObject.SetActive(false);

                    var loseP = scoreRoot.transform.Find("LosePanel");
                    if (loseP != null)
                    {
                        loseP.gameObject.SetActive(true);
                        loseP.localPosition = new Vector3(0f, 0f, 0f);
                    }
                }
                break;

            case "11": // Rally Gauge 100% Full & Electric Racket Spark
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(true);
                    var vic = scoreRoot.transform.Find("VictoryPanel");
                    if (vic != null) vic.gameObject.SetActive(false);
                    var lose = scoreRoot.transform.Find("LosePanel");
                    if (lose != null) lose.gameObject.SetActive(false);

                    var gRoot = scoreRoot.transform.Find("RallyGauge_Root");
                    if (gRoot != null)
                    {
                        gRoot.gameObject.SetActive(true);
                        var gLbl = gRoot.Find("GaugeLabel")?.GetComponent<TextMeshProUGUI>();
                        if (gLbl != null)
                        {
                            gLbl.text = "⚡ SMASH READY! ⚡";
                            gLbl.color = new Color(1f, 0.92f, 0.2f);
                        }
                        var bFill = gRoot.Find("BarSlot/BarFill")?.GetComponent<Image>();
                        if (bFill != null)
                        {
                            bFill.fillAmount = 1.0f;
                            bFill.color = new Color(1f, 0.88f, 0.15f);
                        }
                    }
                }
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = new Vector3(0.05f, 0.05f, 0.15f);
                    blueRacket.transform.localEulerAngles = new Vector3(-80f, -15f, 155f);

                    Transform stringsTf = blueRacket.transform.Find("Strings") ?? blueRacket.transform;
                    var spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    spark.name = "Editor_Racket_Spark";
                    Object.DestroyImmediate(spark.GetComponent<Collider>());
                    spark.transform.SetParent(stringsTf, false);
                    spark.transform.localPosition = Vector3.zero;
                    spark.transform.localScale = new Vector3(0.35f, 0.08f, 0.45f);
                    var ren = spark.GetComponent<Renderer>();
                    if (ren != null)
                    {
                        var s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                        if (s != null)
                        {
                            var mat = new Material(s);
                            mat.color = new Color(0.2f, 0.95f, 1f, 0.85f);
                            ren.material = mat;
                        }
                    }
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(0.25f, 1.25f, -2.8f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                }
                break;

            case "12": // Super Smash Bullet Shot with Trail
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null)
                {
                    scoreRoot.SetActive(true);
                    var sb = scoreRoot.transform.Find("Tennis_Scoreboard_Root");
                    if (sb != null) sb.gameObject.SetActive(true);
                    var vic = scoreRoot.transform.Find("VictoryPanel");
                    if (vic != null) vic.gameObject.SetActive(false);
                    var lose = scoreRoot.transform.Find("LosePanel");
                    if (lose != null) lose.gameObject.SetActive(false);
                }
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = new Vector3(0.12f, 0.05f, 0.25f);
                    blueRacket.transform.localEulerAngles = new Vector3(-65f, -30f, 140f);
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(-0.35f, 1.45f, -7.5f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                    var ballScript = ballObj.GetComponentInParent<Ball>();
                    if (ballScript != null) ballScript.ActivateSuperSmashTrail(true);
                }
                break;

            case "14": // Tang! Hit Impact Effect Showcase
                if (pauseObj != null) pauseObj.SetActive(false);
                if (scoreRoot != null) scoreRoot.SetActive(true);
                if (blueRacket != null)
                {
                    blueRacket.SetActive(true);
                    blueRacket.transform.localPosition = new Vector3(0.08f, 0.08f, 0.22f);
                    blueRacket.transform.localEulerAngles = new Vector3(-70f, -20f, 150f);
                }
                if (tennisBallRoot != null)
                {
                    tennisBallRoot.SetActive(true);
                    tennisBallRoot.transform.position = new Vector3(0.16f, 1.22f, -2.25f);
                }
                if (ballObj != null)
                {
                    ballObj.SetActive(true);
                    ballObj.transform.localPosition = Vector3.zero;
                    var bScript = ballObj.GetComponentInParent<Ball>();
                    if (bScript != null)
                    {
                        bScript.SpawnHitImpactEffect(tennisBallRoot.transform.position, false);
                    }
                }
                break;
        }
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
        uiModule.activeInputMode = XRUIInputModule.ActiveInputMode.Both;
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

        // Move tennis_ball out of the camera/menu sightline in Edit mode
        GameObject tennisBallRoot = allGos.FirstOrDefault(g => g.name == "tennis_ball");
        if (tennisBallRoot != null)
        {
            tennisBallRoot.transform.position = new Vector3(0f, 0.25f, -13.8f);
            Transform ballChild = tennisBallRoot.transform.Find("ball2");
            if (ballChild != null) ballChild.localPosition = Vector3.zero;
            EditorUtility.SetDirty(tennisBallRoot);
        }

        GameObject servePointGo = allGos.FirstOrDefault(g => g.name == "ServePoint");
        if (servePointGo != null)
        {
            servePointGo.transform.position = new Vector3(0.29f, 1.18f, -2.65f);
            EditorUtility.SetDirty(servePointGo);
        }

        // Disable default XRI Locomotion/Move so thumbstick doesn't walk away during UI selection
        if (xrOriginGo != null)
        {
            Transform locoMove = xrOriginGo.transform.Find("Locomotion/Move");
            if (locoMove != null)
            {
                locoMove.gameObject.SetActive(false);
                EditorUtility.SetDirty(locoMove.gameObject);
            }
        }

        GameObject courtGo = allGos.FirstOrDefault(g => g.name.Contains("Tennis_Court"));
        if (courtGo != null)
        {
            courtGo.tag = "Court";
            foreach (Transform t in courtGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "pCube4" || t.name == "pCube5")
                {
                    t.gameObject.tag = "Net";
                }
                else
                {
                    t.gameObject.tag = "Court";
                }
            }
            EditorUtility.SetDirty(courtGo);
        }

        VRPlayerController vrPlayer = allGos.Select(g => g.GetComponent<VRPlayerController>()).FirstOrDefault(c => c != null);
        if (vrPlayer != null && xrOriginGo != null)
        {
            vrPlayer.rigTransform = xrOriginGo.transform;
            vrPlayer.animator = vrPlayer.GetComponent<Animator>();

            var moveCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/move.controller");
            if (vrPlayer.animator != null && moveCtrl != null)
            {
                vrPlayer.animator.runtimeAnimatorController = moveCtrl;
                EditorUtility.SetDirty(vrPlayer.animator);
            }

            Transform leftCtrl = xrOriginGo.transform.Find("Camera Offset/Left Controller");
            Transform rightCtrl = xrOriginGo.transform.Find("Camera Offset/Right Controller");
            if (leftCtrl != null)
            {
                vrPlayer.vrLeftHand = leftCtrl;
                leftCtrl.localPosition = new Vector3(-0.25f, 1.05f, 0.45f);
                leftCtrl.localRotation = Quaternion.identity;
                EditorUtility.SetDirty(leftCtrl);
            }
            if (rightCtrl != null)
            {
                vrPlayer.vrRightHand = rightCtrl;
                rightCtrl.localPosition = new Vector3(0.25f, 1.05f, 0.45f);
                rightCtrl.localRotation = Quaternion.identity;
                EditorUtility.SetDirty(rightCtrl);
            }

            // Disable broken Default-Line LineVisuals and missing controller visual prefabs so they don't render giant triangles in view
            foreach (Transform t in xrOriginGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "LineVisual")
                {
                    var lr = t.GetComponent<LineRenderer>();
                    if (lr != null)
                    {
                        lr.startWidth = 0.004f;
                        lr.endWidth = 0.004f;
                        lr.widthCurve = AnimationCurve.Constant(0f, 1f, 0.004f);
                        EditorUtility.SetDirty(lr);
                    }
                    t.gameObject.SetActive(false);
                    EditorUtility.SetDirty(t.gameObject);
                }
                else if (t.name.Contains("Missing Prefab"))
                {
                    t.gameObject.SetActive(false);
                    EditorUtility.SetDirty(t.gameObject);
                }
            }

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
                    // Keep hidden in Edit Mode / Main Menu so it doesn't float in the sky; GameManager activates the selected racket on GameStart!
                    rGo.SetActive(false);
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

        // Position directly in front of VR Player at (-0.06, 1.48, -3.05) facing player (rotation 0, 180, 0)
        canvasRT.sizeDelta = new Vector2(1920, 1080);
        canvasRT.position = new Vector3(-0.06f, 1.48f, -3.05f);
        canvasRT.rotation = Quaternion.Euler(0f, 180f, 0f);
        canvasRT.localScale = new Vector3(0.0016f, 0.0016f, 0.0016f);

        // ==================== PANEL 1: VR MAIN MENU (FULL-SCREEN START TITLE) ====================
        mainMenuPanel = CreateUIObject("VR_MainMenuPanel", canvasObj.transform);
        SetRectFull(mainMenuPanel, 0f);
        Image mainBgImg = mainMenuPanel.AddComponent<Image>();
        if (titleSprite != null)
        {
            mainBgImg.sprite = titleSprite;
            mainBgImg.color = Color.white;
            mainBgImg.preserveAspect = false; // Fills the entire start screen with the title artwork
        }
        else
        {
            mainBgImg.color = new Color(0.05f, 0.08f, 0.14f, 1f);
        }

        // Action buttons positioned cleanly on the lower half of the title screen
        CreateStyledButton("StartGameBtn", mainMenuPanel.transform, font, "START GAME", 34, new Vector2(0, -180), new Vector2(500, 84), new Color(0.12f, 0.74f, 0.38f, 1f));
        CreateStyledButton("OpenSettingsBtn", mainMenuPanel.transform, font, "SETTINGS & CONTROLS", 28, new Vector2(0, -280), new Vector2(500, 74), new Color(0.18f, 0.46f, 0.80f, 1f));
        CreateStyledButton("QuitBtn", mainMenuPanel.transform, font, "EXIT GAME", 26, new Vector2(0, -375), new Vector2(500, 68), new Color(0.70f, 0.22f, 0.25f, 1f));

        // ==================== PANEL 2: VR SETTINGS & CONTROLS ====================
        settingsPanel = CreateUIObject("VR_SettingsPanel", canvasObj.transform);
        SetRect(settingsPanel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1060, 720));
        Image setBg = settingsPanel.AddComponent<Image>();
        setBg.color = new Color(0.05f, 0.08f, 0.14f, 0.96f);
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
        selectionPanel = CreateUIObject("vrSelectionUI", canvasObj.transform);
        SetRect(selectionPanel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 750));
        Image selBg = selectionPanel.AddComponent<Image>();
        selBg.color = new Color(0.05f, 0.08f, 0.14f, 0.96f);
        CreateBorderOutline(selectionPanel.transform, new Color(0.25f, 0.95f, 0.6f, 0.85f));
        selectionPanel.SetActive(false);

        CreateLabel("SelectionTitle", selectionPanel.transform, font, "SELECT OPPONENT & RACKET", 38, FontStyles.Bold, Color.white, new Vector2(0, 310), new Vector2(920, 50));
        CreateLabel("SelectionHint", selectionPanel.transform, font, "Aim with VR Controller Laser & Pull Trigger to Select", 20, FontStyles.Normal, new Color(0.65f, 0.9f, 1f), new Vector2(0, 265), new Vector2(920, 32));

        // Left Card: Opponent Character Selection
        GameObject charCard = CreateUIObject("CharacterCard", selectionPanel.transform);
        SetRect(charCard, new Vector2(0.5f, 0.5f), new Vector2(-255, 15), new Vector2(460, 430));
        charCard.AddComponent<Image>().color = new Color(0.09f, 0.14f, 0.22f, 0.95f);

        CreateLabel("CharLabel", charCard.transform, font, "OPPONENT CHARACTER", 24, FontStyles.Bold, new Color(0.4f, 1f, 0.75f), new Vector2(0, 175), new Vector2(400, 40));
        CreateStyledButton("CharPrevBtn", charCard.transform, font, "<", 38, new Vector2(-170, 20), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));
        CreateStyledButton("CharNextBtn", charCard.transform, font, ">", 38, new Vector2(170, 20), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));

        GameObject charBorder = CreateUIObject("CharImage_Border", charCard.transform);
        SetRect(charBorder, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(220, 220));
        charBorder.AddComponent<Image>().color = new Color(0.15f, 0.22f, 0.32f, 1f);

        GameObject charImgGo = CreateUIObject("CharImage", charBorder.transform);
        SetRectFull(charImgGo, 8f);
        charImg = charImgGo.AddComponent<Image>();
        if (charSprite != null) charImg.sprite = charSprite;
        charImg.preserveAspect = true;
        charImg.raycastTarget = false;

        charNameTxt = CreateLabel("CharNameText", charCard.transform, font, "EMERALD HEROINE\n<size=20><color=#80FFAA>[BALANCED] SPD **  PWR **</color></size>", 23, FontStyles.Bold, Color.white, new Vector2(0, -145), new Vector2(430, 75));

        // Right Card: Tennis Racket Selection
        GameObject racketCard = CreateUIObject("RacketCard", selectionPanel.transform);
        SetRect(racketCard, new Vector2(0.5f, 0.5f), new Vector2(255, 15), new Vector2(460, 430));
        racketCard.AddComponent<Image>().color = new Color(0.09f, 0.14f, 0.22f, 0.95f);

        CreateLabel("RacketLabel", racketCard.transform, font, "TENNIS RACKET", 24, FontStyles.Bold, new Color(1f, 0.85f, 0.4f), new Vector2(0, 175), new Vector2(400, 40));
        CreateStyledButton("RacketPrevBtn", racketCard.transform, font, "<", 38, new Vector2(-170, 20), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));
        CreateStyledButton("RacketNextBtn", racketCard.transform, font, ">", 38, new Vector2(170, 20), new Vector2(76, 130), new Color(0.18f, 0.38f, 0.62f, 1f));

        GameObject racketBorder = CreateUIObject("RacketImage_Border", racketCard.transform);
        SetRect(racketBorder, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(220, 220));
        racketBorder.AddComponent<Image>().color = new Color(0.15f, 0.22f, 0.32f, 1f);

        GameObject racketImgGo = CreateUIObject("RacketImage", racketBorder.transform);
        SetRectFull(racketImgGo, 8f);
        racketImg = racketImgGo.AddComponent<Image>();
        if (racketSprites != null && racketSprites.Length > 0) racketImg.sprite = racketSprites[0];
        racketImg.preserveAspect = true;
        racketImg.raycastTarget = false;

        racketNameTxt = CreateLabel("RacketNameText", racketCard.transform, font, "CLASSIC ORANGE\n<size=20><color=#FFD180>Standard Balanced Racket</color></size>", 23, FontStyles.Bold, Color.white, new Vector2(0, -145), new Vector2(430, 75));

        // Bottom Buttons: Back & Start Match
        CreateStyledButton("BackToMainBtn", selectionPanel.transform, font, "BACK TO MENU", 26, new Vector2(-210, -285), new Vector2(320, 72), new Color(0.48f, 0.22f, 0.25f, 1f));
        CreateStyledButton("VRPlayBtn", selectionPanel.transform, font, "START MATCH!", 32, new Vector2(210, -285), new Vector2(420, 72), new Color(0.12f, 0.72f, 0.38f, 1f));

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

        RectTransform scoreRT = scoreGo.GetComponent<RectTransform>();
        if (scoreRT != null)
        {
            scoreRT.sizeDelta = new Vector2(960, 640);
            scoreRT.localPosition = new Vector3(0f, 1.52f, 2.35f);
            scoreRT.localRotation = Quaternion.identity;
            scoreRT.localScale = new Vector3(0.0022f, 0.0022f, 0.0022f);
        }

        Canvas c = scoreGo.GetComponent<Canvas>();
        if (c != null)
        {
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = mainCam;
            c.sortingOrder = 15;
        }
        if (scoreGo.GetComponent<GraphicRaycaster>() == null) scoreGo.AddComponent<GraphicRaycaster>();
        if (scoreGo.GetComponent<TrackedDeviceGraphicRaycaster>() == null) scoreGo.AddComponent<TrackedDeviceGraphicRaycaster>();

        Transform oldScoreRoot = scoreGo.transform.Find("Tennis_Scoreboard_Root");
        if (oldScoreRoot != null) Object.DestroyImmediate(oldScoreRoot.gameObject);

        GameObject scoreRoot = CreateUIObject("Tennis_Scoreboard_Root", scoreGo.transform);
        SetRect(scoreRoot, new Vector2(0.5f, 0.5f), new Vector2(0, 265), new Vector2(860, 82));
        Image sbBg = scoreRoot.AddComponent<Image>();
        sbBg.color = new Color(0.04f, 0.08f, 0.14f, 0.88f);
        sbBg.raycastTarget = false;
        CreateBorderOutline(scoreRoot.transform, new Color(0.25f, 0.85f, 0.6f, 0.7f));

        vrScoreTxt = CreateLabel("PlayerScore", scoreRoot.transform, font, "PLAYER: 0", 30, FontStyles.Bold, new Color(0.45f, 1f, 0.75f), new Vector2(-265, 0), new Vector2(280, 65));
        vrGameScoreTxt = CreateLabel("GameScore", scoreRoot.transform, font, "SET  0 : 0", 32, FontStyles.Bold, Color.yellow, new Vector2(0, 0), new Vector2(240, 65));
        vrEnemyScoreTxt = CreateLabel("EnemyScore", scoreRoot.transform, font, "AI: 0", 30, FontStyles.Bold, new Color(1f, 0.55f, 0.55f), new Vector2(265, 0), new Vector2(280, 65));

        Transform oldVic = scoreGo.transform.Find("VictoryPanel");
        if (oldVic != null) Object.DestroyImmediate(oldVic.gameObject);
        Transform oldLose = scoreGo.transform.Find("LosePanel");
        if (oldLose != null) Object.DestroyImmediate(oldLose.gameObject);

        // Build In-Game Rally Gauge Bar (Bottom-Right HUD)
        Transform oldGauge = scoreGo.transform.Find("RallyGauge_Root");
        if (oldGauge != null) Object.DestroyImmediate(oldGauge.gameObject);

        GameObject gaugeRoot = CreateUIObject("RallyGauge_Root", scoreGo.transform);
        SetRect(gaugeRoot, new Vector2(0.5f, 0.5f), new Vector2(285, -235), new Vector2(340, 68));
        Image gBg = gaugeRoot.AddComponent<Image>();
        gBg.color = new Color(0.04f, 0.08f, 0.14f, 0.92f);
        gBg.raycastTarget = false;
        CreateBorderOutline(gaugeRoot.transform, new Color(0.2f, 0.9f, 1f, 0.85f));

        var gaugeLabel = CreateLabel("GaugeLabel", gaugeRoot.transform, font, "RALLY GAUGE  0%", 20, FontStyles.Bold, new Color(0.4f, 1f, 0.85f), new Vector2(0, 15), new Vector2(310, 28));

        GameObject barSlot = CreateUIObject("BarSlot", gaugeRoot.transform);
        SetRect(barSlot, new Vector2(0.5f, 0.5f), new Vector2(0, -14), new Vector2(300, 20));
        Image slotImg = barSlot.AddComponent<Image>();
        slotImg.color = new Color(0.08f, 0.14f, 0.22f, 1f);
        slotImg.raycastTarget = false;

        GameObject barFill = CreateUIObject("BarFill", barSlot.transform);
        SetRectFull(barFill, 2f);
        Image fillImg = barFill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.95f, 1f, 1f);
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 0f;
        fillImg.raycastTarget = false;

        VRPlayerController vrPlayer = Resources.FindObjectsOfTypeAll<VRPlayerController>().FirstOrDefault(p => p != null && p.gameObject.scene == scoreGo.scene);
        if (vrPlayer != null)
        {
            vrPlayer.rallyGaugeFill = fillImg;
            vrPlayer.rallyGaugeText = gaugeLabel;
            vrPlayer.rallyGaugeRoot = gaugeRoot.GetComponent<RectTransform>();
            EditorUtility.SetDirty(vrPlayer);
        }

        victoryPanel = BuildGameOverPanel("VictoryPanel", scoreGo.transform, font, "VICTORY!", new Color(1f, 0.88f, 0.25f));
        losePanel = BuildGameOverPanel("LosePanel", scoreGo.transform, font, "DEFEAT...", new Color(1f, 0.35f, 0.35f));

        scoreGo.SetActive(false);
        EditorUtility.SetDirty(scoreGo);
    }

    private static GameObject BuildGameOverPanel(string name, Transform parent, TMP_FontAsset font, string title, Color titleColor)
    {
        GameObject panel = CreateUIObject(name, parent);
        SetRect(panel, new Vector2(0.5f, 0.5f), new Vector2(0, -15), new Vector2(650, 420));
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

    // ==================== Gameplay Test Menu Items ====================
    [MenuItem("Tools/Capstone/Toggle Play Mode")]
    public static void TogglePlayMode()
    {
        EditorApplication.isPlaying = !EditorApplication.isPlaying;
    }

    [MenuItem("Tools/Capstone/Test/Serve Toss")]
    public static void TestServeToss()
    {
        var vr = Object.FindFirstObjectByType<VRPlayerController>();
        if (vr != null) vr.TossBall();
    }

    [MenuItem("Tools/Capstone/Test/Player Hit Ball")]
    public static void TestPlayerHitBall()
    {
        var vr = Object.FindFirstObjectByType<VRPlayerController>();
        if (vr != null) vr.ApplyHitVelocity();
    }

    [MenuItem("Tools/Capstone/Test/AI Hit Ball")]
    public static void TestAIHitBall()
    {
        var enemy = Object.FindFirstObjectByType<EnemyAI>();
        if (enemy != null)
        {
            var mi = typeof(EnemyAI).GetMethod("EnemyHit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (mi != null) mi.Invoke(enemy, null);
        }
    }

    [MenuItem("Tools/Capstone/Test/Score Point Player")]
    public static void TestScorePointPlayer()
    {
        if (GameManager.Instance != null && GameManager.Instance.enemyBackTrigger != null)
        {
            GameManager.Instance.NotifyBallOut(GameManager.Instance.enemyBackTrigger);
        }
    }

    [MenuItem("Tools/Capstone/Test/Score Point AI")]
    public static void TestScorePointAI()
    {
        if (GameManager.Instance != null && GameManager.Instance.playerBackTrigger != null)
        {
            GameManager.Instance.NotifyBallOut(GameManager.Instance.playerBackTrigger);
        }
    }

    [MenuItem("Tools/Capstone/Test/Show Victory Screen")]
    public static void TestVictoryScreen()
    {
        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.victoryPanel != null)
                GameManager.Instance.victoryPanel.SetActive(true);
            if (GameManager.Instance.vrCanvas != null)
                GameManager.Instance.vrCanvas.SetActive(true);
        }
    }

    [MenuItem("Tools/Capstone/Test/Show Defeat Screen")]
    public static void TestDefeatScreen()
    {
        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.losePanel != null)
                GameManager.Instance.losePanel.SetActive(true);
            if (GameManager.Instance.vrCanvas != null)
                GameManager.Instance.vrCanvas.SetActive(true);
        }
    }

    [MenuItem("Tools/Capstone/Test/Pause Game")]
    public static void TestPauseGame()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PauseGame();
        }
    }
}
