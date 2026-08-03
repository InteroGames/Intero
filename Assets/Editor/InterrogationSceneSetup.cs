using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Interrogation.EditorTools
{
    /// <summary>
    /// 취조실 씬을 통째로 만든다. Tools > Interrogation > Setup Scene 한 번이면
    /// 방, 심문관, 주인공 시점, 말풍선, UI 배선까지 전부 생성된다.
    ///
    /// 전부 프리미티브로 짓는다. 외부 에셋 임포트가 없으므로 어디서 열어도 똑같이 나온다.
    /// 기존 씬을 덮어쓰므로 씬을 손으로 고친 뒤에는 다시 돌리지 말 것.
    /// API 키는 씬 밖(apikey.txt)에 있으므로 재생성해도 날아가지 않는다.
    /// </summary>
    public static class InterrogationSceneSetup
    {
        const string ScenePath = "Assets/Scenes/Interrogation.unity";
        const string FontSourcePath = "Assets/Fonts/malgun.ttf";
        const string FontAssetPath = "Assets/Fonts/Malgun SDF.asset";
        const string PoolPath = "Assets/Resources/dialogue_pool.json";
        const string MatFolder = "Assets/Materials";

        // 방 치수. 창은 없다.
        const float RoomW = 5.0f, RoomH = 3.0f, RoomD = 4.6f;
        const float TableTop = 0.75f;
        const float SeatZ = 1.05f;    // 심문관 쪽
        const float PlayerZ = -1.05f; // 주인공 쪽

        static TMP_FontAsset koreanFont;

        [MenuItem("Tools/Interrogation/Setup Scene")]
        public static void Setup()
        {
            ImportTMPEssentialsIfNeeded();
            koreanFont = GetOrCreateKoreanFont();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();
            BuildRoom();
            BuildTable();
            var detectiveHead = BuildDetective();
            BuildPlayerHands();

            var cam = BuildCamera();
            var bubble = BuildSpeechBubble(detectiveHead);

            var ui = BuildScreenUI(out TMP_Text logTMP, out TMP_InputField input,
                                   out Button submitButton, out Slider suspicionBar,
                                   out Slider subversionBar, out TMP_Text gaugeTMP,
                                   out TMP_Text dbgTMP);

            WireGame(bubble, logTMP, input, submitButton, suspicionBar, subversionBar, gaugeTMP, dbgTMP);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Interrogation",
                "취조실 생성 완료.\n\n" +
                "· 주인공 시점으로 앉아 있다\n" +
                "· 마우스 오른쪽 버튼을 누른 채 움직이면 둘러본다\n" +
                "· 심문관은 말풍선으로 말한다\n\n" +
                "API 키는 apikey.txt에서 자동으로 읽는다. Play만 누르면 된다.", "확인");
        }

        // ---------------------------------------------------------------- 환경

        static void BuildEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.09f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.03f, 0.03f, 0.04f);
            RenderSettings.fogDensity = 0.055f;
            RenderSettings.skybox = null;

            // 천장에 매달린 전구 하나. 이 방의 유일한 광원이다.
            var lampRoot = new GameObject("Lamp");
            lampRoot.transform.position = new Vector3(0, 0, 0.1f);

            var cable = Prim(PrimitiveType.Cylinder, "Cable", lampRoot.transform,
                             new Vector3(0, 2.62f, 0.1f), new Vector3(0.015f, 0.34f, 0.015f),
                             Mat("M_Metal", new Color(0.2f, 0.2f, 0.22f), 0.5f, 0.7f));

            var shade = Prim(PrimitiveType.Cylinder, "Shade", lampRoot.transform,
                             new Vector3(0, 2.26f, 0.1f), new Vector3(0.34f, 0.10f, 0.34f),
                             Mat("M_Metal", new Color(0.2f, 0.2f, 0.22f), 0.5f, 0.7f));

            var bulb = Prim(PrimitiveType.Sphere, "Bulb", lampRoot.transform,
                            new Vector3(0, 2.14f, 0.1f), Vector3.one * 0.10f,
                            Mat("M_Bulb", new Color(1f, 0.92f, 0.75f), 0.9f, 0f,
                                new Color(1f, 0.85f, 0.6f) * 2.2f));

            var lightGO = new GameObject("KeyLight");
            lightGO.transform.SetParent(lampRoot.transform, false);
            lightGO.transform.position = new Vector3(0, 2.08f, 0.1f);
            var l = lightGO.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.88f, 0.72f);
            l.intensity = 2.6f;
            l.range = 9f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.85f;

            // 주인공 손이 완전히 묻히지 않을 만큼의 최소 보조광
            var fillGO = new GameObject("FillLight");
            fillGO.transform.position = new Vector3(0, 1.6f, PlayerZ - 0.4f);
            var fill = fillGO.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.6f, 0.65f, 0.8f);
            fill.intensity = 0.35f;
            fill.range = 3.5f;
            fill.shadows = LightShadows.None;
        }

        // ---------------------------------------------------------------- 방

        static void BuildRoom()
        {
            var root = new GameObject("Room").transform;

            var wall = Mat("M_Wall", new Color(0.19f, 0.19f, 0.18f), 0.05f, 0f);
            var floor = Mat("M_Floor", new Color(0.11f, 0.11f, 0.12f), 0.15f, 0f);
            var ceil = Mat("M_Ceiling", new Color(0.14f, 0.14f, 0.14f), 0.05f, 0f);

            float t = 0.2f;
            Prim(PrimitiveType.Cube, "Floor", root, new Vector3(0, -t / 2, 0), new Vector3(RoomW, t, RoomD), floor);
            Prim(PrimitiveType.Cube, "Ceiling", root, new Vector3(0, RoomH + t / 2, 0), new Vector3(RoomW, t, RoomD), ceil);
            Prim(PrimitiveType.Cube, "Wall_Back", root, new Vector3(0, RoomH / 2, RoomD / 2 + t / 2), new Vector3(RoomW, RoomH, t), wall);
            Prim(PrimitiveType.Cube, "Wall_Front", root, new Vector3(0, RoomH / 2, -RoomD / 2 - t / 2), new Vector3(RoomW, RoomH, t), wall);
            Prim(PrimitiveType.Cube, "Wall_Left", root, new Vector3(-RoomW / 2 - t / 2, RoomH / 2, 0), new Vector3(t, RoomH, RoomD), wall);
            Prim(PrimitiveType.Cube, "Wall_Right", root, new Vector3(RoomW / 2 + t / 2, RoomH / 2, 0), new Vector3(t, RoomH, RoomD), wall);

            // 문. 열릴 일은 없다.
            var door = Mat("M_Door", new Color(0.16f, 0.13f, 0.11f), 0.1f, 0f);
            Prim(PrimitiveType.Cube, "Door", root, new Vector3(1.55f, 1.02f, RoomD / 2 - 0.04f),
                 new Vector3(0.9f, 2.04f, 0.06f), door);
            Prim(PrimitiveType.Sphere, "DoorKnob", root, new Vector3(1.16f, 1.0f, RoomD / 2 - 0.11f),
                 Vector3.one * 0.07f, Mat("M_Metal", new Color(0.35f, 0.35f, 0.38f), 0.6f, 0.8f));
        }

        static void BuildTable()
        {
            var root = new GameObject("Table").transform;
            var wood = Mat("M_Table", new Color(0.26f, 0.20f, 0.15f), 0.18f, 0f);
            var metal = Mat("M_Metal", new Color(0.35f, 0.35f, 0.38f), 0.6f, 0.8f);

            Prim(PrimitiveType.Cube, "Top", root, new Vector3(0, TableTop, 0), new Vector3(1.75f, 0.07f, 0.95f), wood);

            float lx = 0.78f, lz = 0.38f;
            foreach (var p in new[] { new Vector2(lx, lz), new Vector2(-lx, lz), new Vector2(lx, -lz), new Vector2(-lx, -lz) })
                Prim(PrimitiveType.Cube, "Leg", root, new Vector3(p.x, TableTop / 2, p.y),
                     new Vector3(0.06f, TableTop, 0.06f), metal);

            // 형사의 서류. 게이지가 오르면 여기 무언가 적힌다는 설정.
            Prim(PrimitiveType.Cube, "Papers", root, new Vector3(0.42f, TableTop + 0.045f, 0.24f),
                 new Vector3(0.30f, 0.012f, 0.40f), Mat("M_Paper", new Color(0.78f, 0.75f, 0.68f), 0.05f, 0f));
        }

        // ---------------------------------------------------------------- 인물

        /// <summary>심문관. 앉아 있고, 서두르지 않는다. 반환값은 말풍선을 매달 머리 트랜스폼.</summary>
        static Transform BuildDetective()
        {
            var root = new GameObject("Detective").transform;
            root.position = new Vector3(0, 0, SeatZ);

            var coat = Mat("M_Coat", new Color(0.15f, 0.16f, 0.19f), 0.08f, 0f);
            var skin = Mat("M_Skin", new Color(0.70f, 0.56f, 0.46f), 0.1f, 0f);
            var metal = Mat("M_Metal", new Color(0.35f, 0.35f, 0.38f), 0.6f, 0.8f);

            // 의자
            Prim(PrimitiveType.Cube, "Seat", root, new Vector3(0, 0.44f, SeatZ + 0.20f),
                 new Vector3(0.50f, 0.06f, 0.48f), metal);
            Prim(PrimitiveType.Cube, "SeatBack", root, new Vector3(0, 0.74f, SeatZ + 0.42f),
                 new Vector3(0.50f, 0.56f, 0.06f), metal);

            // 몸
            Prim(PrimitiveType.Capsule, "Torso", root, new Vector3(0, 0.92f, SeatZ + 0.04f),
                 new Vector3(0.46f, 0.34f, 0.30f), coat);
            var head = Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0, 1.42f, SeatZ + 0.01f),
                            new Vector3(0.25f, 0.28f, 0.25f), skin);

            // 팔은 탁자 위에 놓여 있다
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 0.31f : -0.31f;
                var arm = Prim(PrimitiveType.Capsule, "Forearm", root,
                               new Vector3(x, TableTop + 0.09f, 0.42f),
                               new Vector3(0.13f, 0.26f, 0.13f), coat);
                arm.localRotation = Quaternion.Euler(90f, 0f, 0f);

                Prim(PrimitiveType.Sphere, "Hand", root,
                     new Vector3(x, TableTop + 0.08f, 0.17f), Vector3.one * 0.11f, skin);
            }

            // 펜. 형사가 무언가 적을 때 쓰는 그것.
            var pen = Prim(PrimitiveType.Cylinder, "Pen", root,
                           new Vector3(0.30f, TableTop + 0.05f, 0.06f),
                           new Vector3(0.012f, 0.075f, 0.012f), metal);
            pen.localRotation = Quaternion.Euler(0f, 0f, 78f);

            return head;
        }

        /// <summary>주인공은 1인칭이라 탁자 위에 놓인 두 팔만 보인다.</summary>
        static void BuildPlayerHands()
        {
            var root = new GameObject("PlayerHands").transform;
            var sleeve = Mat("M_Sleeve", new Color(0.22f, 0.20f, 0.18f), 0.06f, 0f);
            var skin = Mat("M_Skin", new Color(0.70f, 0.56f, 0.46f), 0.1f, 0f);

            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 0.27f : -0.27f;
                var arm = Prim(PrimitiveType.Capsule, "Forearm", root,
                               new Vector3(x, TableTop + 0.08f, PlayerZ + 0.52f),
                               new Vector3(0.12f, 0.24f, 0.12f), sleeve);
                arm.localRotation = Quaternion.Euler(90f, 0f, 0f);

                Prim(PrimitiveType.Sphere, "Hand", root,
                     new Vector3(x, TableTop + 0.07f, PlayerZ + 0.80f), Vector3.one * 0.10f, skin);
            }
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("PlayerCamera");
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0, 1.19f, PlayerZ);
            go.transform.LookAt(new Vector3(0, 1.36f, SeatZ + 0.01f));

            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;

            go.AddComponent<AudioListener>();
            go.AddComponent<SeatedLook>();
            return cam;
        }

        // ---------------------------------------------------------------- 말풍선

        static SpeechBubble BuildSpeechBubble(Transform head)
        {
            var root = new GameObject("SpeechBubble",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(SpeechBubble));
            root.transform.position = head.position + new Vector3(0.62f, 0.42f, -0.10f);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(560, 240);
            rt.localScale = Vector3.one * 0.0022f;

            // 풍선 몸통 - 형사가 든 서류처럼 바랜 종이색
            var panel = NewUI("Panel", root.transform, typeof(Image));
            Stretch(panel, Vector2.zero, Vector2.zero);
            var img = panel.GetComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = new Color(0.90f, 0.88f, 0.82f, 0.96f);

            // 꼬리
            var tail = NewUI("Tail", root.transform, typeof(Image));
            var tailRT = (RectTransform)tail.transform;
            tailRT.anchorMin = tailRT.anchorMax = new Vector2(0f, 0.5f);
            tailRT.pivot = new Vector2(0.5f, 0.5f);
            tailRT.anchoredPosition = new Vector2(-10f, -70f);
            tailRT.sizeDelta = new Vector2(70, 70);
            tailRT.localRotation = Quaternion.Euler(0, 0, 45f);
            tail.GetComponent<Image>().color = new Color(0.90f, 0.88f, 0.82f, 0.96f);

            var textGO = NewUI("Text", root.transform, typeof(TextMeshProUGUI));
            Stretch(textGO, new Vector2(34, 28), new Vector2(-34, -28));
            var tmp = textGO.GetComponent<TextMeshProUGUI>();
            StyleText(tmp, 40, new Color(0.10f, 0.09f, 0.08f));
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 24;
            tmp.fontSizeMax = 44;
            tmp.text = "";

            var bubble = root.GetComponent<SpeechBubble>();
            var so = new SerializedObject(bubble);
            so.FindProperty("group").objectReferenceValue = root.GetComponent<CanvasGroup>();
            so.FindProperty("text").objectReferenceValue = tmp;
            so.ApplyModifiedPropertiesWithoutUndo();

            return bubble;
        }

        // ---------------------------------------------------------------- 화면 UI

        static GameObject BuildScreenUI(out TMP_Text logTMP, out TMP_InputField input,
                                        out Button submitButton, out Slider suspicionBar,
                                        out Slider subversionBar, out TMP_Text gaugeTMP,
                                        out TMP_Text dbgTMP)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasGO = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // 기록 - 3D가 주가 되었으므로 구석으로 물러난다
            var logScroll = NewUI("LogScroll", canvasGO.transform, typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            var logRoot = (RectTransform)logScroll.transform;
            logRoot.anchorMin = new Vector2(0, 0);
            logRoot.anchorMax = new Vector2(0, 0);
            logRoot.pivot = new Vector2(0, 0);
            logRoot.anchoredPosition = new Vector2(40, 150);
            logRoot.sizeDelta = new Vector2(620, 420);
            logScroll.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, 0.55f);

            var logText = NewUI("LogText", logScroll.transform, typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            var logRT = (RectTransform)logText.transform;
            logRT.anchorMin = new Vector2(0, 0);
            logRT.anchorMax = new Vector2(1, 0);
            logRT.pivot = new Vector2(0.5f, 0);
            logRT.offsetMin = new Vector2(18, 10);
            logRT.offsetMax = new Vector2(-18, 10);
            logTMP = logText.GetComponent<TextMeshProUGUI>();
            StyleText(logTMP, 21, new Color(0.72f, 0.72f, 0.70f));
            logTMP.alignment = TextAlignmentOptions.BottomLeft;
            logTMP.text = "";
            logText.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = logScroll.GetComponent<ScrollRect>();
            scroll.content = logRT;
            scroll.viewport = logRoot;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;

            suspicionBar = MakeGaugeBar(canvasGO.transform, "SuspicionBar", "의심",
                new Vector2(-40, -50), new Color(0.72f, 0.26f, 0.22f));
            subversionBar = MakeGaugeBar(canvasGO.transform, "SubversionBar", "불온",
                new Vector2(-40, -96), new Color(0.33f, 0.42f, 0.78f));

            var gaugeLabel = NewUI("GaugeLabel", canvasGO.transform, typeof(TextMeshProUGUI));
            PlaceTopRight(gaugeLabel, new Vector2(-40, -140), new Vector2(420, 36));
            gaugeTMP = gaugeLabel.GetComponent<TextMeshProUGUI>();
            StyleText(gaugeTMP, 26, new Color(0.65f, 0.65f, 0.65f));
            gaugeTMP.alignment = TextAlignmentOptions.MidlineRight;
            gaugeTMP.text = "의심 0   불온 0";

            var debugText = NewUI("DebugText", canvasGO.transform, typeof(TextMeshProUGUI));
            var dbgRT = (RectTransform)debugText.transform;
            dbgRT.anchorMin = dbgRT.anchorMax = new Vector2(0, 1);
            dbgRT.pivot = new Vector2(0, 1);
            dbgRT.anchoredPosition = new Vector2(40, -40);
            dbgRT.sizeDelta = new Vector2(700, 120);
            dbgTMP = debugText.GetComponent<TextMeshProUGUI>();
            StyleText(dbgTMP, 20, new Color(0.45f, 0.55f, 0.45f));
            dbgTMP.alignment = TextAlignmentOptions.TopLeft;
            dbgTMP.text = "";

            var tmpRes = GetTMPResources();

            var inputGO = TMP_DefaultControls.CreateInputField(tmpRes);
            inputGO.name = "InputField";
            inputGO.transform.SetParent(canvasGO.transform, false);
            var inRT = (RectTransform)inputGO.transform;
            inRT.anchorMin = new Vector2(0, 0);
            inRT.anchorMax = new Vector2(1, 0);
            inRT.pivot = new Vector2(0.5f, 0);
            inRT.offsetMin = new Vector2(40, 40);
            inRT.offsetMax = new Vector2(-320, 110);
            input = inputGO.GetComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            inputGO.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.92f);
            foreach (var t in inputGO.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                StyleText(t, 28, t.name == "Placeholder"
                    ? new Color(0.40f, 0.40f, 0.40f)
                    : new Color(0.90f, 0.90f, 0.88f));
                t.fontStyle = FontStyles.Normal;
            }
            if (input.placeholder is TextMeshProUGUI ph) ph.text = "진술한다…";

            var buttonGO = TMP_DefaultControls.CreateButton(tmpRes);
            buttonGO.name = "SubmitButton";
            buttonGO.transform.SetParent(canvasGO.transform, false);
            var btnRT = (RectTransform)buttonGO.transform;
            btnRT.anchorMin = btnRT.anchorMax = new Vector2(1, 0);
            btnRT.pivot = new Vector2(1, 0);
            btnRT.anchoredPosition = new Vector2(-40, 40);
            btnRT.sizeDelta = new Vector2(240, 70);
            buttonGO.GetComponent<Image>().color = new Color(0.20f, 0.18f, 0.16f, 0.95f);
            var btnLabel = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
            StyleText(btnLabel, 28, new Color(0.85f, 0.82f, 0.75f));
            btnLabel.text = "답한다";
            submitButton = buttonGO.GetComponent<Button>();

            return canvasGO;
        }

        static void WireGame(SpeechBubble bubble, TMP_Text logTMP, TMP_InputField input,
                             Button submitButton, Slider suspicionBar, Slider subversionBar,
                             TMP_Text gaugeTMP, TMP_Text dbgTMP)
        {
            var game = new GameObject("_Game",
                typeof(LLMClient), typeof(InterrogationController), typeof(InterrogationUI));

            var llm = game.GetComponent<LLMClient>();
            var controller = game.GetComponent<InterrogationController>();
            var ui = game.GetComponent<InterrogationUI>();

            var pool = AssetDatabase.LoadAssetAtPath<TextAsset>(PoolPath);
            if (pool == null)
                Debug.LogError($"[Setup] {PoolPath} 를 찾지 못했다. dialoguePoolJson을 수동으로 연결할 것.");

            var soC = new SerializedObject(controller);
            soC.FindProperty("llm").objectReferenceValue = llm;
            soC.FindProperty("dialoguePoolJson").objectReferenceValue = pool;
            soC.ApplyModifiedPropertiesWithoutUndo();

            var soU = new SerializedObject(ui);
            soU.FindProperty("controller").objectReferenceValue = controller;
            soU.FindProperty("bubble").objectReferenceValue = bubble;
            soU.FindProperty("logText").objectReferenceValue = logTMP;
            soU.FindProperty("inputField").objectReferenceValue = input;
            soU.FindProperty("submitButton").objectReferenceValue = submitButton;
            soU.FindProperty("suspicionBar").objectReferenceValue = suspicionBar;
            soU.FindProperty("subversionBar").objectReferenceValue = subversionBar;
            soU.FindProperty("gaugeLabel").objectReferenceValue = gaugeTMP;
            soU.FindProperty("debugText").objectReferenceValue = dbgTMP;
            soU.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = game;
        }

        // ---------------------------------------------------------------- 헬퍼

        static Transform Prim(PrimitiveType type, string name, Transform parent,
                              Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        static Material Mat(string name, Color color, float smoothness, float metallic,
                            Color? emission = null)
        {
            string path = $"{MatFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");

            var m = new Material(shader) { name = name };
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);

            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission.Value);
            }

            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Slider MakeGaugeBar(Transform parent, string name, string labelText,
                                   Vector2 pos, Color fillColor)
        {
            var label = NewUI(name + "Label", parent, typeof(TextMeshProUGUI));
            PlaceTopRight(label, new Vector2(pos.x - 440, pos.y + 6), new Vector2(80, 34));
            var tmp = label.GetComponent<TextMeshProUGUI>();
            StyleText(tmp, 24, new Color(0.65f, 0.65f, 0.65f));
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.text = labelText;

            var go = DefaultControls.CreateSlider(GetUIResources());
            go.name = name;
            go.transform.SetParent(parent, false);
            PlaceTopRight(go, pos, new Vector2(420, 26));

            var slider = go.GetComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            var handleArea = go.transform.Find("Handle Slide Area");
            if (handleArea != null) UnityEngine.Object.DestroyImmediate(handleArea.gameObject);
            slider.handleRect = null;
            slider.targetGraphic = null;

            go.transform.Find("Background").GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.13f, 0.85f);

            var fillArea = (RectTransform)go.transform.Find("Fill Area");
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(3, 3);
            fillArea.offsetMax = new Vector2(-3, -3);
            var fill = (RectTransform)fillArea.Find("Fill");
            fill.sizeDelta = Vector2.zero;
            fill.GetComponent<Image>().color = fillColor;
            slider.fillRect = fill;

            return slider;
        }

        static GameObject NewUI(string name, Transform parent, params Type[] comps)
        {
            var go = new GameObject(name, comps);
            go.transform.SetParent(parent, false);
            if (go.GetComponent<RectTransform>() == null) go.AddComponent<RectTransform>();
            return go;
        }

        static void Stretch(GameObject go, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        static void PlaceTopRight(GameObject go, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void StyleText(TMP_Text t, float size, Color color)
        {
            if (koreanFont != null) t.font = koreanFont;
            t.fontSize = size;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = false;
        }

        static TMP_FontAsset GetOrCreateKoreanFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (font == null)
            {
                Debug.LogWarning($"[Setup] {FontSourcePath} 없음. TMP 기본 폰트 사용 (한글 깨짐 주의).");
                return null;
            }

            var fa = TMP_FontAsset.CreateFontAsset(font, 60, 6, GlyphRenderMode.SDFAA,
                                                   1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = "Malgun SDF";
            AssetDatabase.CreateAsset(fa, FontAssetPath);
            if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
            {
                fa.atlasTextures[0].name = "Malgun Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            }
            if (fa.material != null)
            {
                fa.material.name = "Malgun Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        }

        static void ImportTMPEssentialsIfNeeded()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro")) return;
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.Name == "TMP_PackageResourceImporter");
                var m = type?.GetMethod("ImportResources",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                m?.Invoke(null, new object[] { true, false, false });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Setup] TMP 리소스 자동 임포트 실패: {e.Message}. " +
                                 "Window > TextMeshPro > Import TMP Essential Resources 를 수동 실행할 것.");
            }
        }

        static DefaultControls.Resources GetUIResources() => new DefaultControls.Resources
        {
            standard   = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark  = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown   = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };

        static TMP_DefaultControls.Resources GetTMPResources() => new TMP_DefaultControls.Resources
        {
            standard   = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark  = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown   = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };
    }
}
