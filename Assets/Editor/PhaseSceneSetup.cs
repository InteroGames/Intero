using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Interrogation.Heist;
using Interrogation.Inquiry;

namespace Interrogation.EditorTools
{
    /// <summary>
    /// 2페이즈 씬을 통째로 만든다. Tools > Interrogation > Setup Phases 하나면
    /// 잠입 저택과 취조실이 둘 다 생성되고 빌드 순서까지 잡힌다.
    ///
    /// 전부 프리미티브로 짓는다. 외부 에셋 임포트가 없으므로 어디서 열어도 똑같이 나온다.
    /// <b>기존 씬을 덮어쓰므로 손으로 고친 뒤에는 다시 돌리지 말 것.</b>
    /// API 키는 씬 밖(apikey.txt)에 있으므로 재생성해도 날아가지 않는다.
    /// </summary>
    public static class PhaseSceneSetup
    {
        const string HeistPath = "Assets/Scenes/Heist.unity";
        const string InquiryPath = "Assets/Scenes/Inquiry.unity";
        const string FontSourcePath = "Assets/Fonts/malgun.ttf";
        const string FontAssetPath = "Assets/Fonts/Malgun SDF.asset";
        const string MatFolder = "Assets/Materials";

        static TMP_FontAsset font;

        [MenuItem("Tools/Interrogation/Setup Phases (Heist + Inquiry)")]
        public static void SetupBoth()
        {
            font = GetOrCreateKoreanFont();

            BuildHeistScene();
            BuildInquiryScene();

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(HeistPath, true),
                new EditorBuildSettingsScene(InquiryPath, true),
            };
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Interrogation",
                "2페이즈 씬 생성 완료.\n\n" +
                "· Heist  — WASD 이동 / Shift 달리기 / E 상호작용\n" +
                "  목표물 3개를 챙기면 경보가 울리고 60초가 시작된다.\n" +
                "  정문은 빠르지만 흔적이 남고, 담은 멀지만 적게 남는다.\n" +
                "  경비실 장부는 경보 뒤에만 태울 수 있다. 5초를 먹는다.\n\n" +
                "· Inquiry — 잠입 결과가 증거 카드의 상태를 결정한다.\n" +
                "  Heist 씬을 먼저 Play할 것. 단독 실행하면 기본값으로 돈다.\n\n" +
                "API 키는 apikey.txt에서 자동으로 읽는다.", "확인");
        }

        [MenuItem("Tools/Interrogation/Setup Heist Scene Only")]
        public static void SetupHeistOnly()
        {
            font = GetOrCreateKoreanFont();
            BuildHeistScene();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Interrogation/Setup Inquiry Scene Only")]
        public static void SetupInquiryOnly()
        {
            font = GetOrCreateKoreanFont();
            BuildInquiryScene();
            AssetDatabase.SaveAssets();
        }

        // ================================================================ 잠입 씬

        // 저택 치수. 복도 하나에 방 넷이 붙은 단층이다.
        const float HallZ0 = -2.2f, HallZ1 = 2.2f;
        const float HallX0 = -16f, HallX1 = 16f;
        const float RoomZ1 = 8.2f;     // 북쪽 방들의 바깥벽
        const float GuardZ0 = -8.2f;   // 경비실 바깥벽
        const float WallH = 3.2f;
        const float DoorGap = 1.7f;

        static void BuildHeistScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            NightEnvironment(new Color(0.05f, 0.055f, 0.07f), 0.022f);

            var wallMat = Mat("M_HouseWall", new Color(0.23f, 0.21f, 0.19f), 0.05f, 0f);
            var floorMat = Mat("M_HouseFloor", new Color(0.13f, 0.12f, 0.11f), 0.2f, 0f);
            var woodMat = Mat("M_HouseWood", new Color(0.28f, 0.21f, 0.15f), 0.18f, 0f);
            var metalMat = Mat("M_HouseMetal", new Color(0.34f, 0.34f, 0.37f), 0.6f, 0.8f);
            var grassMat = Mat("M_Garden", new Color(0.09f, 0.12f, 0.09f), 0.1f, 0f);

            var root = new GameObject("Estate").transform;

            // 바닥과 정원
            Box(root, "Floor", new Vector3(0, -0.1f, 0), new Vector3(46f, 0.2f, 34f), floorMat, true);
            Box(root, "Garden", new Vector3(20f, -0.08f, -6f), new Vector3(16f, 0.2f, 34f), grassMat, true);
            Box(root, "Ceiling", new Vector3(0, WallH + 0.1f, 0), new Vector3(34f, 0.2f, 18f), wallMat, false);

            // 복도 북쪽 벽 - 방 셋으로 들어가는 문
            WallX(root, HallX0, -8f - DoorGap / 2f, HallZ1, wallMat);
            WallX(root, -8f + DoorGap / 2f, -DoorGap / 2f, HallZ1, wallMat);
            WallX(root, DoorGap / 2f, 8f - DoorGap / 2f, HallZ1, wallMat);
            WallX(root, 8f + DoorGap / 2f, HallX1, HallZ1, wallMat);

            // 복도 남쪽 벽 - 경비실 문 하나
            WallX(root, HallX0, -DoorGap / 2f, HallZ0, wallMat);
            WallX(root, DoorGap / 2f, HallX1, HallZ0, wallMat);

            // 복도 양 끝. 서쪽은 정문, 동쪽은 뒷문.
            WallZ(root, HallZ0, -DoorGap / 2f, HallX0, wallMat);
            WallZ(root, DoorGap / 2f, HallZ1, HallX0, wallMat);
            WallZ(root, HallZ0, -DoorGap / 2f, HallX1, wallMat);
            WallZ(root, DoorGap / 2f, HallZ1, HallX1, wallMat);

            // 북쪽 방 셋
            RoomShell(root, -11.5f, -4.5f, HallZ1, RoomZ1, wallMat);
            RoomShell(root, -3.5f, 3.5f, HallZ1, RoomZ1, wallMat);
            RoomShell(root, 4.5f, 11.5f, HallZ1, RoomZ1, wallMat);

            // 경비실
            RoomShell(root, -3.5f, 3.5f, GuardZ0, HallZ0, wallMat);

            // 저택 바깥 담. 이게 없으면 복도 문으로 한 발만 나가도 허허벌판이 되고,
            // 경로 선택이 통째로 무의미해진다. 넘을 수 있는 자리는 뒷담 한 곳뿐이다.
            WallZ(root, -22f, 10f, -20f, wallMat);   // 서쪽
            WallX(root, -20f, 24f, 10f, wallMat);    // 북쪽
            WallX(root, -20f, 24f, -22f, wallMat);   // 남쪽
            WallZ(root, -22f, -21f, 24f, wallMat);   // 동쪽 - 담 자리만 비운다
            WallZ(root, -19f, 10f, 24f, wallMat);

            // 들어온 자리. 응접실 북쪽 창이 열려 있다.
            Box(root, "BrokenWindow", new Vector3(8f, 1.5f, RoomZ1 - 0.14f),
                new Vector3(1.2f, 1.4f, 0.08f), metalMat, false);

            BuildHeistProps(root, woodMat, metalMat);

            var alarmLights = BuildHeistLights();
            var player = BuildHeistPlayer();
            var hud = BuildHeistHUD();

            var game = new GameObject("_Game", typeof(HeistDirector));
            var dir = game.GetComponent<HeistDirector>();
            var so = new SerializedObject(dir);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("hud").objectReferenceValue = hud;
            so.FindProperty("inquiryScene").stringValue = "Inquiry";

            var arr = so.FindProperty("alarmLights");
            arr.arraySize = alarmLights.Count;
            for (int i = 0; i < alarmLights.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = alarmLights[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, HeistPath);
        }

        /// <summary>세 면이 막히고 복도 쪽만 열린 방.</summary>
        static void RoomShell(Transform root, float x0, float x1, float z0, float z1, Material m)
        {
            bool north = z1 > 0f;
            WallX(root, x0, x1, north ? z1 : z0, m);   // 바깥벽
            WallZ(root, z0, z1, x0, m);
            WallZ(root, z0, z1, x1, m);
        }

        static void BuildHeistProps(Transform root, Material wood, Material metal)
        {
            // --- 서재: 금고. 다이얼은 장갑으로 돌아가지 않는다. ---
            var studyDesk = Box(root, "StudyDesk", new Vector3(-8f, 0.38f, 5.4f),
                                new Vector3(2.2f, 0.76f, 1.0f), wood, true);
            var safe = Box(root, "Safe", new Vector3(-8f, 0.55f, 7.3f),
                           new Vector3(1.0f, 1.1f, 0.7f), metal, true);
            Interactable(safe, InteractKind.Loot, "금시계", 3.0f, bareHands: true);
            Glow(root, new Vector3(-8f, 2.6f, 6.0f), new Color(0.95f, 0.85f, 0.6f), 1.5f, 7f);

            // --- 침실: 보석함 ---
            var bed = Box(root, "Bed", new Vector3(-1.4f, 0.28f, 6.6f),
                          new Vector3(1.6f, 0.56f, 2.4f), wood, true);
            var chest = Box(root, "JewelBox", new Vector3(1.8f, 0.85f, 6.6f),
                            new Vector3(0.6f, 0.35f, 0.45f), metal, true);
            Interactable(chest, InteractKind.Loot, "보석함", 1.2f);
            Glow(root, new Vector3(0f, 2.6f, 6.0f), new Color(0.9f, 0.82f, 0.65f), 1.2f, 7f);

            // --- 응접실: 서류 봉투. 창으로 들어오면 바로 눈앞이다. ---
            var table = Box(root, "ParlourTable", new Vector3(8f, 0.36f, 5.2f),
                            new Vector3(1.8f, 0.72f, 1.0f), wood, true);
            var envelope = Box(root, "Envelope", new Vector3(8f, 0.78f, 5.2f),
                               new Vector3(0.42f, 0.04f, 0.3f),
                               Mat("M_Envelope", new Color(0.72f, 0.68f, 0.58f), 0.05f, 0f), true);
            Interactable(envelope, InteractKind.Loot, "서류 봉투", 0.8f);
            Glow(root, new Vector3(8f, 2.6f, 6.0f), new Color(0.9f, 0.84f, 0.7f), 1.2f, 7f);

            // --- 경비실: 출입 장부. 태우면 5초를 먹고, 태웠다는 사실이 남는다. ---
            var deskG = Box(root, "GuardDesk", new Vector3(0f, 0.38f, -5.6f),
                            new Vector3(2.0f, 0.76f, 0.9f), wood, true);
            var ledger = Box(root, "Ledger", new Vector3(0f, 0.82f, -5.6f),
                             new Vector3(0.5f, 0.09f, 0.36f),
                             Mat("M_Ledger", new Color(0.55f, 0.44f, 0.30f), 0.05f, 0f), true);
            Interactable(ledger, InteractKind.Conceal, "출입 장부", 5.0f, afterAlarm: true);
            Glow(root, new Vector3(0f, 2.6f, -5.4f), new Color(0.8f, 0.8f, 0.85f), 1.0f, 6f);

            // --- 탈출구 둘 ---
            // 정문은 복도 서쪽 문간을 그대로 막고 있다. 걸어서 통과할 수 없고 열어야 나간다.
            var frontDoor = Box(root, "FrontDoor", new Vector3(HallX0, 1.05f, 0f),
                                new Vector3(0.3f, 2.1f, DoorGap + 0.1f), wood, true);
            Interactable(frontDoor, InteractKind.Exit, "정문", 0.4f, route: "front");
            Glow(root, new Vector3(HallX0 + 1.6f, 2.2f, 0f), new Color(0.85f, 0.8f, 0.7f), 1.0f, 6f);

            var backWall = Box(root, "GardenWall", new Vector3(23.4f, 1.2f, -20f),
                               new Vector3(0.6f, 2.4f, 2.2f),
                               Mat("M_Brick", new Color(0.22f, 0.19f, 0.17f), 0.08f, 0f), true);
            Interactable(backWall, InteractKind.Exit, "뒷담", 1.5f, route: "wall");
            Glow(root, new Vector3(21.5f, 2.4f, -19f), new Color(0.55f, 0.6f, 0.75f), 0.7f, 8f);
        }

        static void Interactable(Transform t, InteractKind kind, string label, float hold,
                                 bool bareHands = false, bool afterAlarm = false, string route = "")
        {
            var it = t.gameObject.AddComponent<HeistInteractable>();
            it.kind = kind;
            it.label = label;
            it.holdSeconds = hold;
            it.bareHandsRequired = bareHands;
            it.afterAlarmOnly = afterAlarm;
            it.routeId = route;
        }

        static List<Light> BuildHeistLights()
        {
            var moon = new GameObject("Moon");
            var l = moon.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.42f, 0.48f, 0.66f);
            l.intensity = 0.32f;
            l.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(38f, 200f, 0f);

            // 경보등. 처음에는 꺼져 있다.
            var alarms = new List<Light>();
            foreach (var p in new[]
            {
                new Vector3(-12f, 2.9f, 0f), new Vector3(0f, 2.9f, 0f),
                new Vector3(12f, 2.9f, 0f), new Vector3(0f, 2.9f, -5f),
            })
            {
                var go = new GameObject("AlarmLight");
                go.transform.position = p;
                var al = go.AddComponent<Light>();
                al.type = LightType.Point;
                al.color = new Color(0.95f, 0.25f, 0.18f);
                al.intensity = 3.2f;
                al.range = 14f;
                al.shadows = LightShadows.None;
                al.enabled = false;
                alarms.Add(al);
            }
            return alarms;
        }

        static void Glow(Transform root, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.Soft;
        }

        static HeistPlayer BuildHeistPlayer()
        {
            // 창으로 막 넘어 들어온 자리. 응접실이다.
            var go = new GameObject("Player", typeof(CharacterController), typeof(HeistPlayer));
            go.transform.position = new Vector3(8f, 1.1f, 7.2f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            var cc = go.GetComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.32f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;

            var camGO = new GameObject("Head");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(go.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.62f, 0f);

            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.035f);
            cam.fieldOfView = 68f;
            cam.nearClipPlane = 0.04f;
            cam.farClipPlane = 120f;
            camGO.AddComponent<AudioListener>();

            var p = go.GetComponent<HeistPlayer>();
            var so = new SerializedObject(p);
            so.FindProperty("head").objectReferenceValue = camGO.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return p;
        }

        static HeistHUD BuildHeistHUD()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvas = NewCanvas("HUD");

            var objective = Label(canvas.transform, "Objective", 30, new Color(0.85f, 0.83f, 0.76f),
                                  TextAlignmentOptions.TopLeft);
            Anchor(objective, new Vector2(0, 1), new Vector2(40, -40), new Vector2(520, 140));

            var timer = Label(canvas.transform, "Timer", 74, new Color(0.82f, 0.80f, 0.74f),
                              TextAlignmentOptions.Top);
            Anchor(timer, new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(400, 110));

            var notice = Label(canvas.transform, "Notice", 44, new Color(0.90f, 0.87f, 0.80f),
                               TextAlignmentOptions.Center);
            Anchor(notice, new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(1200, 130));

            var prompt = Label(canvas.transform, "Prompt", 28, new Color(0.88f, 0.86f, 0.80f),
                               TextAlignmentOptions.Center);
            Anchor(prompt, new Vector2(0.5f, 0.5f), new Vector2(0, -140), new Vector2(1200, 70));

            // 붙잡고 있는 진행도
            var holdRoot = NewUI("HoldBar", canvas.transform, typeof(Image));
            Anchor(holdRoot, new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(360, 12));
            holdRoot.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f, 0.85f);

            var holdFill = NewUI("Fill", holdRoot.transform, typeof(Image));
            Stretch(holdFill, new Vector2(2, 2), new Vector2(-2, -2));
            var fillImg = holdFill.GetComponent<Image>();
            fillImg.color = new Color(0.82f, 0.74f, 0.52f);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 0f;
            holdRoot.SetActive(false);

            // 조준점
            var dot = NewUI("Reticle", canvas.transform, typeof(Image));
            Anchor(dot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 5));
            dot.GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f, 0.5f);

            var fade = NewUI("Fade", canvas.transform, typeof(Image), typeof(CanvasGroup));
            Stretch(fade, Vector2.zero, Vector2.zero);
            fade.GetComponent<Image>().color = Color.black;
            var fadeGroup = fade.GetComponent<CanvasGroup>();
            fadeGroup.alpha = 0f;
            fade.transform.SetAsLastSibling();

            var hud = canvas.gameObject.AddComponent<HeistHUD>();
            var so = new SerializedObject(hud);
            so.FindProperty("objectiveText").objectReferenceValue = objective.GetComponent<TMP_Text>();
            so.FindProperty("timerText").objectReferenceValue = timer.GetComponent<TMP_Text>();
            so.FindProperty("promptText").objectReferenceValue = prompt.GetComponent<TMP_Text>();
            so.FindProperty("noticeText").objectReferenceValue = notice.GetComponent<TMP_Text>();
            so.FindProperty("holdFill").objectReferenceValue = fillImg;
            so.FindProperty("fade").objectReferenceValue = fadeGroup;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        // ================================================================ 취조 씬

        const float RoomW = 5.0f, RoomH = 3.0f, RoomD = 4.6f;
        const float TableTop = 0.75f;
        const float SeatZ = 1.05f, PlayerZ = -1.05f;

        static void BuildInquiryScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            NightEnvironment(new Color(0.07f, 0.07f, 0.09f), 0.055f);

            var wall = Mat("M_Wall", new Color(0.19f, 0.19f, 0.18f), 0.05f, 0f);
            var floor = Mat("M_Floor", new Color(0.11f, 0.11f, 0.12f), 0.15f, 0f);
            var metal = Mat("M_Metal", new Color(0.35f, 0.35f, 0.38f), 0.6f, 0.8f);
            var wood = Mat("M_Table", new Color(0.26f, 0.20f, 0.15f), 0.18f, 0f);
            var skin = Mat("M_Skin", new Color(0.70f, 0.56f, 0.46f), 0.1f, 0f);
            var coat = Mat("M_Coat", new Color(0.15f, 0.16f, 0.19f), 0.08f, 0f);

            var room = new GameObject("Room").transform;
            float t = 0.2f;
            Box(room, "Floor", new Vector3(0, -t / 2, 0), new Vector3(RoomW, t, RoomD), floor, false);
            Box(room, "Ceiling", new Vector3(0, RoomH + t / 2, 0), new Vector3(RoomW, t, RoomD), wall, false);
            Box(room, "Wall_Back", new Vector3(0, RoomH / 2, RoomD / 2), new Vector3(RoomW, RoomH, t), wall, false);
            Box(room, "Wall_Front", new Vector3(0, RoomH / 2, -RoomD / 2), new Vector3(RoomW, RoomH, t), wall, false);
            Box(room, "Wall_Left", new Vector3(-RoomW / 2, RoomH / 2, 0), new Vector3(t, RoomH, RoomD), wall, false);
            Box(room, "Wall_Right", new Vector3(RoomW / 2, RoomH / 2, 0), new Vector3(t, RoomH, RoomD), wall, false);
            Box(room, "Door", new Vector3(1.55f, 1.02f, RoomD / 2 - 0.14f), new Vector3(0.9f, 2.04f, 0.06f), wood, false);

            var table = new GameObject("Table").transform;
            Box(table, "Top", new Vector3(0, TableTop, 0), new Vector3(1.75f, 0.07f, 0.95f), wood, false);
            foreach (var p in new[] { new Vector2(0.78f, 0.38f), new Vector2(-0.78f, 0.38f),
                                      new Vector2(0.78f, -0.38f), new Vector2(-0.78f, -0.38f) })
                Box(table, "Leg", new Vector3(p.x, TableTop / 2, p.y), new Vector3(0.06f, TableTop, 0.06f), metal, false);
            Box(table, "Papers", new Vector3(0.42f, TableTop + 0.045f, 0.24f), new Vector3(0.30f, 0.012f, 0.40f),
                Mat("M_Paper", new Color(0.78f, 0.75f, 0.68f), 0.05f, 0f), false);

            // 전구 하나. 이 방의 유일한 광원이다.
            var lampRoot = new GameObject("Lamp").transform;
            Box(lampRoot, "Shade", new Vector3(0, 2.26f, 0.1f), new Vector3(0.5f, 0.16f, 0.5f), metal, false);
            var bulb = new GameObject("KeyLight");
            bulb.transform.position = new Vector3(0, 2.08f, 0.1f);
            var kl = bulb.AddComponent<Light>();
            kl.type = LightType.Point;
            kl.color = new Color(1f, 0.88f, 0.72f);
            kl.intensity = 2.6f; kl.range = 9f;
            kl.shadows = LightShadows.Soft; kl.shadowStrength = 0.85f;

            // 형사
            var det = new GameObject("Detective").transform;
            Box(det, "Seat", new Vector3(0, 0.44f, SeatZ + 0.20f), new Vector3(0.50f, 0.06f, 0.48f), metal, false);
            Box(det, "SeatBack", new Vector3(0, 0.74f, SeatZ + 0.42f), new Vector3(0.50f, 0.56f, 0.06f), metal, false);
            Sphere(det, "Torso", new Vector3(0, 0.95f, SeatZ + 0.04f), new Vector3(0.46f, 0.42f, 0.30f), coat);
            var head = Sphere(det, "Head", new Vector3(0, 1.42f, SeatZ + 0.01f), new Vector3(0.25f, 0.28f, 0.25f), skin);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 0.31f : -0.31f;
                Sphere(det, "Hand", new Vector3(x, TableTop + 0.08f, 0.17f), Vector3.one * 0.11f, skin);
            }

            // 주인공은 1인칭. 탁자 위의 두 팔만 보인다.
            var hands = new GameObject("PlayerHands").transform;
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? 0.27f : -0.27f;
                Sphere(hands, "Hand", new Vector3(x, TableTop + 0.07f, PlayerZ + 0.80f), Vector3.one * 0.10f, skin);
            }

            var camGO = new GameObject("PlayerCamera");
            camGO.tag = "MainCamera";
            camGO.transform.position = new Vector3(0, 1.19f, PlayerZ);
            camGO.transform.LookAt(new Vector3(0, 1.36f, SeatZ + 0.01f));
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            camGO.AddComponent<AudioListener>();
            camGO.AddComponent<SeatedLook>();

            var bubble = BuildBubble(head);
            var ui = BuildInquiryHUD(out var refs);

            var game = new GameObject("_Game", typeof(LLMClient), typeof(InquiryController), typeof(InquiryUI));
            var llm = game.GetComponent<LLMClient>();
            var controller = game.GetComponent<InquiryController>();
            var inquiryUI = game.GetComponent<InquiryUI>();

            var cards = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/evidence_cards.json");
            var linesAsset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/inquiry_lines.json");
            if (cards == null) Debug.LogError("[Setup] evidence_cards.json 을 찾지 못했다.");
            if (linesAsset == null) Debug.LogError("[Setup] inquiry_lines.json 을 찾지 못했다.");

            var soC = new SerializedObject(controller);
            soC.FindProperty("llm").objectReferenceValue = llm;
            soC.FindProperty("evidenceJson").objectReferenceValue = cards;
            soC.FindProperty("linesJson").objectReferenceValue = linesAsset;
            soC.ApplyModifiedPropertiesWithoutUndo();

            var soU = new SerializedObject(inquiryUI);
            soU.FindProperty("controller").objectReferenceValue = controller;
            soU.FindProperty("bubble").objectReferenceValue = bubble;
            soU.FindProperty("logText").objectReferenceValue = refs.log;
            soU.FindProperty("inputField").objectReferenceValue = refs.input;
            soU.FindProperty("submitButton").objectReferenceValue = refs.submit;
            soU.FindProperty("cardList").objectReferenceValue = refs.cardList;
            soU.FindProperty("cardTemplate").objectReferenceValue = refs.cardTemplate;
            soU.FindProperty("roundLabel").objectReferenceValue = refs.round;
            soU.FindProperty("chargeLabel").objectReferenceValue = refs.charges;
            soU.FindProperty("endOverlay").objectReferenceValue = refs.endGroup;
            soU.FindProperty("endText").objectReferenceValue = refs.endText;
            soU.FindProperty("debugText").objectReferenceValue = refs.debug;
            soU.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, InquiryPath);
            Selection.activeGameObject = game;
        }

        class InquiryRefs
        {
            public TMP_Text log, round, charges, endText, debug;
            public TMP_InputField input;
            public Button submit;
            public Transform cardList;
            public GameObject cardTemplate;
            public CanvasGroup endGroup;
        }

        static GameObject BuildInquiryHUD(out InquiryRefs refs)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvas = NewCanvas("HUD");
            refs = new InquiryRefs();

            // 기록
            var logPanel = NewUI("LogPanel", canvas.transform, typeof(Image), typeof(RectMask2D));
            Anchor(logPanel, new Vector2(0, 0), new Vector2(40, 150), new Vector2(600, 400));
            var lrt = (RectTransform)logPanel.transform;
            lrt.pivot = new Vector2(0, 0);
            logPanel.GetComponent<Image>().color = new Color(0.02f, 0.02f, 0.03f, 0.55f);

            var logGO = Label(logPanel.transform, "LogText", 20, new Color(0.72f, 0.72f, 0.70f),
                              TextAlignmentOptions.BottomLeft);
            var lrt2 = (RectTransform)logGO.transform;
            lrt2.anchorMin = new Vector2(0, 0); lrt2.anchorMax = new Vector2(1, 1);
            lrt2.offsetMin = new Vector2(18, 12); lrt2.offsetMax = new Vector2(-18, -12);
            refs.log = logGO.GetComponent<TMP_Text>();

            // 조서 - 형사가 내려놓은 서류가 여기 쌓인다
            var sheet = NewUI("Sheet", canvas.transform, typeof(Image));
            Anchor(sheet, new Vector2(1, 1), new Vector2(-40, -40), new Vector2(560, 640));
            var srt = (RectTransform)sheet.transform;
            srt.pivot = new Vector2(1, 1);
            sheet.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.06f, 0.72f);

            var roundGO = Label(sheet.transform, "Round", 26, new Color(0.80f, 0.77f, 0.70f),
                                TextAlignmentOptions.TopLeft);
            var rrt = (RectTransform)roundGO.transform;
            rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1);
            rrt.pivot = new Vector2(0.5f, 1);
            rrt.offsetMin = new Vector2(24, 0); rrt.offsetMax = new Vector2(-24, 0);
            rrt.anchoredPosition = new Vector2(0, -20);
            rrt.sizeDelta = new Vector2(rrt.sizeDelta.x, 34);
            refs.round = roundGO.GetComponent<TMP_Text>();

            var chargeGO = Label(sheet.transform, "Charges", 19, new Color(0.62f, 0.62f, 0.60f),
                                 TextAlignmentOptions.TopLeft);
            var crt = (RectTransform)chargeGO.transform;
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = new Vector2(24, 0); crt.offsetMax = new Vector2(-24, 0);
            crt.anchoredPosition = new Vector2(0, -62);
            crt.sizeDelta = new Vector2(crt.sizeDelta.x, 96);
            refs.charges = chargeGO.GetComponent<TMP_Text>();

            var listGO = NewUI("CardList", sheet.transform, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var virt = (RectTransform)listGO.transform;
            virt.anchorMin = new Vector2(0, 1); virt.anchorMax = new Vector2(1, 1);
            virt.pivot = new Vector2(0.5f, 1);
            virt.offsetMin = new Vector2(20, 0); virt.offsetMax = new Vector2(-20, 0);
            virt.anchoredPosition = new Vector2(0, -172);
            virt.sizeDelta = new Vector2(virt.sizeDelta.x, 440);
            var vlg = listGO.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 12;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            listGO.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            refs.cardList = listGO.transform;

            // 카드 한 장의 틀. 자식 이름은 InquiryUI가 찾으므로 바꾸지 말 것.
            var card = NewUI("CardTemplate", sheet.transform, typeof(Image));
            var cardRT = (RectTransform)card.transform;
            cardRT.sizeDelta = new Vector2(500, 128);
            card.GetComponent<Image>().color = new Color(0.13f, 0.12f, 0.11f, 0.95f);

            var factGO = Label(card.transform, "Fact", 18, new Color(0.84f, 0.82f, 0.76f),
                               TextAlignmentOptions.TopLeft);
            var frt = (RectTransform)factGO.transform;
            frt.anchorMin = new Vector2(0, 1); frt.anchorMax = new Vector2(1, 1);
            frt.pivot = new Vector2(0.5f, 1);
            frt.offsetMin = new Vector2(16, 0); frt.offsetMax = new Vector2(-16, 0);
            frt.anchoredPosition = new Vector2(0, -12);
            frt.sizeDelta = new Vector2(frt.sizeDelta.x, 72);

            var claimGO = Label(card.transform, "Claim", 18, new Color(0.72f, 0.55f, 0.48f),
                                TextAlignmentOptions.TopLeft);
            var qrt = (RectTransform)claimGO.transform;
            qrt.anchorMin = new Vector2(0, 1); qrt.anchorMax = new Vector2(1, 1);
            qrt.pivot = new Vector2(0.5f, 1);
            qrt.offsetMin = new Vector2(16, 0); qrt.offsetMax = new Vector2(-16, 0);
            qrt.anchoredPosition = new Vector2(0, -86);
            qrt.sizeDelta = new Vector2(qrt.sizeDelta.x, 34);

            card.SetActive(false);
            refs.cardTemplate = card;

            // 디버그. 제출 빌드에서는 showDebug를 끈다.
            var dbg = Label(canvas.transform, "Debug", 18, new Color(0.45f, 0.55f, 0.45f),
                            TextAlignmentOptions.TopLeft);
            Anchor(dbg, new Vector2(0, 1), new Vector2(40, -40), new Vector2(820, 90));
            refs.debug = dbg.GetComponent<TMP_Text>();

            // 입력
            var tmpRes = TMPResources();

            var inputGO = TMP_DefaultControls.CreateInputField(tmpRes);
            inputGO.name = "InputField";
            inputGO.transform.SetParent(canvas.transform, false);
            var irt = (RectTransform)inputGO.transform;
            irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(1, 0);
            irt.pivot = new Vector2(0.5f, 0);
            irt.offsetMin = new Vector2(40, 40); irt.offsetMax = new Vector2(-320, 110);
            var input = inputGO.GetComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            inputGO.GetComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 0.92f);
            foreach (var tx in inputGO.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                Style(tx, 26, tx.name == "Placeholder"
                    ? new Color(0.40f, 0.40f, 0.40f) : new Color(0.90f, 0.90f, 0.88f));
                tx.fontStyle = FontStyles.Normal;
            }
            if (input.placeholder is TextMeshProUGUI ph) ph.text = "말한다…";
            refs.input = input;

            var btnGO = TMP_DefaultControls.CreateButton(tmpRes);
            btnGO.name = "SubmitButton";
            btnGO.transform.SetParent(canvas.transform, false);
            var brt = (RectTransform)btnGO.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(1, 0);
            brt.pivot = new Vector2(1, 0);
            brt.anchoredPosition = new Vector2(-40, 40);
            brt.sizeDelta = new Vector2(240, 70);
            btnGO.GetComponent<Image>().color = new Color(0.20f, 0.18f, 0.16f, 0.95f);
            var blab = btnGO.GetComponentInChildren<TextMeshProUGUI>();
            Style(blab, 26, new Color(0.85f, 0.82f, 0.75f));
            blab.text = "말한다";
            refs.submit = btnGO.GetComponent<Button>();

            // 엔딩
            var end = NewUI("EndOverlay", canvas.transform, typeof(Image), typeof(CanvasGroup));
            Stretch(end, Vector2.zero, Vector2.zero);
            end.GetComponent<Image>().color = new Color(0.01f, 0.01f, 0.02f, 0.97f);
            refs.endGroup = end.GetComponent<CanvasGroup>();
            refs.endGroup.alpha = 0f;
            refs.endGroup.blocksRaycasts = false;
            end.transform.SetAsLastSibling();

            var endTextGO = Label(end.transform, "EndText", 30, new Color(0.84f, 0.81f, 0.75f),
                                  TextAlignmentOptions.Center);
            Anchor(endTextGO, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300, 620));
            refs.endText = endTextGO.GetComponent<TMP_Text>();

            return canvas.gameObject;
        }

        static SpeechBubble BuildBubble(Transform head)
        {
            var root = new GameObject("SpeechBubble",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(SpeechBubble));
            root.transform.position = head.position + new Vector3(0.62f, 0.42f, -0.10f);
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(560, 240);
            rt.localScale = Vector3.one * 0.0022f;

            var panel = NewUI("Panel", root.transform, typeof(Image));
            Stretch(panel, Vector2.zero, Vector2.zero);
            var img = panel.GetComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = new Color(0.90f, 0.88f, 0.82f, 0.96f);

            var textGO = Label(root.transform, "Text", 38, new Color(0.10f, 0.09f, 0.08f),
                               TextAlignmentOptions.TopLeft);
            Stretch(textGO, new Vector2(34, 28), new Vector2(-34, -28));
            var tmp = textGO.GetComponent<TextMeshProUGUI>();
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 22; tmp.fontSizeMax = 42;
            tmp.text = "";

            var bubble = root.GetComponent<SpeechBubble>();
            var so = new SerializedObject(bubble);
            so.FindProperty("group").objectReferenceValue = root.GetComponent<CanvasGroup>();
            so.FindProperty("text").objectReferenceValue = tmp;
            so.ApplyModifiedPropertiesWithoutUndo();
            return bubble;
        }

        // ================================================================ 헬퍼

        static void NightEnvironment(Color ambient, float fogDensity)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.05f);
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.skybox = null;
        }

        const float WallT = 0.25f;

        /// <summary>X축을 따라 뻗는 벽 한 장.</summary>
        static void WallX(Transform p, float x0, float x1, float z, Material m)
        {
            if (x1 - x0 <= 0.01f) return;
            Box(p, "Wall", new Vector3((x0 + x1) / 2f, WallH / 2f, z),
                new Vector3(x1 - x0, WallH, WallT), m, true);
        }

        /// <summary>Z축을 따라 뻗는 벽 한 장.</summary>
        static void WallZ(Transform p, float z0, float z1, float x, Material m)
        {
            if (z1 - z0 <= 0.01f) return;
            Box(p, "Wall", new Vector3(x, WallH / 2f, (z0 + z1) / 2f),
                new Vector3(WallT, WallH, z1 - z0), m, true);
        }

        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 scale,
                             Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        static Transform Sphere(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        static Material Mat(string name, Color color, float smoothness, float metallic)
        {
            string path = $"{MatFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader) { name = name, color = color };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);

            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Canvas NewCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return c;
        }

        static GameObject NewUI(string name, Transform parent, params Type[] comps)
        {
            var go = new GameObject(name, comps);
            go.transform.SetParent(parent, false);
            if (go.GetComponent<RectTransform>() == null) go.AddComponent<RectTransform>();
            return go;
        }

        static GameObject Label(Transform parent, string name, float size, Color color,
                                TextAlignmentOptions align)
        {
            var go = NewUI(name, parent, typeof(TextMeshProUGUI));
            var t = go.GetComponent<TextMeshProUGUI>();
            Style(t, size, color);
            t.alignment = align;
            t.text = "";
            return go;
        }

        static void Style(TMP_Text t, float size, Color color)
        {
            if (font != null) t.font = font;
            t.fontSize = size;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = false;
        }

        static void Anchor(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Stretch(GameObject go, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        static TMP_DefaultControls.Resources TMPResources() => new TMP_DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };

        /// <summary>한글 폰트. TMP 기본 폰트는 한글이 전부 □로 나온다.</summary>
        static TMP_FontAsset GetOrCreateKoreanFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null) return existing;

            var src = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (src == null)
            {
                Debug.LogWarning($"[Setup] {FontSourcePath} 없음. TMP 기본 폰트 사용 (한글 깨짐 주의).");
                return null;
            }

            var fa = TMP_FontAsset.CreateFontAsset(src, 60, 6, GlyphRenderMode.SDFAA,
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
    }
}
