using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Interrogation.Heist;
using Interrogation.Inquiry;

namespace Interrogation.EditorTools
{
    /// <summary>
    /// 생성된 씬이 실제로 굴러가는 상태인지 확인한다.
    ///
    /// 씬 생성기가 조용히 실패하면(SerializedObject 이름 오타 하나면 충분하다)
    /// Play를 누르기 전까지 아무도 모른다. 시연 30분 전에 알게 되는 게 최악이다.
    /// 머지 전에 한 번 돌려볼 것.
    /// </summary>
    public static class PhaseSceneValidator
    {
        [MenuItem("Tools/Interrogation/Validate Phases")]
        public static void Validate()
        {
            var problems = new List<string>();

            EditorSceneManager.OpenScene("Assets/Scenes/Heist.unity", OpenSceneMode.Single);
            CheckSerialized<HeistDirector>(problems, "Heist", "player", "hud", "alarmLights");
            CheckSerialized<HeistHUD>(problems, "Heist",
                "objectiveText", "timerText", "promptText", "noticeText", "holdFill", "fade");
            CheckSerialized<HeistPlayer>(problems, "Heist", "head");

            int loot = 0, conceal = 0, exits = 0;
            foreach (var it in Object.FindObjectsByType<HeistInteractable>(FindObjectsSortMode.None))
            {
                if (it.kind == InteractKind.Loot) loot++;
                else if (it.kind == InteractKind.Conceal) conceal++;
                else exits++;
            }
            if (loot != 3) problems.Add($"Heist: 목표물이 {loot}개다. 3개여야 한다.");
            if (conceal < 1) problems.Add("Heist: 인멸 지점이 없다.");
            if (exits < 2) problems.Add($"Heist: 탈출구가 {exits}개다. 2개여야 한다.");

            EditorSceneManager.OpenScene("Assets/Scenes/Inquiry.unity", OpenSceneMode.Single);
            CheckSerialized<InquiryController>(problems, "Inquiry", "llm", "evidenceJson", "linesJson");
            CheckSerialized<InquiryUI>(problems, "Inquiry",
                "controller", "bubble", "logText", "inputField", "submitButton",
                "cardList", "cardTemplate", "roundLabel", "chargeLabel", "endOverlay", "endText");

            // 증거 카드가 실제로 읽히는가. 잠입 결과가 카드 수치를 흔드는가.
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/evidence_cards.json");
            if (json == null) problems.Add("Inquiry: evidence_cards.json 이 없다.");
            else
            {
                var world = HeistOutcome.Standalone();
                var deck = EvidenceDeck.Load(json.text, world);
                if (deck.cards.Count != 3)
                    problems.Add($"Inquiry: 증거 카드가 {deck.cards.Count}장이다. 3장이어야 한다.");

                var board = HypothesisBoard.Default();
                foreach (var c in deck.cards)
                {
                    if (c.targets.Count == 0) problems.Add($"카드 {c.id}: 겨누는 가설이 없다.");
                    foreach (var t in c.targets)
                        if (board.Get(t.hypothesisId) == null)
                            problems.Add($"카드 {c.id}: 없는 가설 '{t.hypothesisId}'을(를) 가리킨다.");

                    if (c.attackSurface.Count < 2)
                        problems.Add($"카드 {c.id}: 공격 축이 {c.attackSurface.Count}개다. 최소 2개여야 한다.");
                    if (c.alternativeInterpretations.Count < 3)
                        problems.Add($"카드 {c.id}: 반박 경로가 {c.alternativeInterpretations.Count}개다. 최소 3개여야 한다.");
                    if (!string.IsNullOrEmpty(c.assertionUndermines) && deck.Get(c.assertionUndermines) == null)
                        problems.Add($"카드 {c.id}: assertionUndermines 가 없는 카드를 가리킨다.");
                }

                ReportTradeoffs(json.text, problems);
            }

            if (problems.Count == 0)
                Debug.Log("[Validate] 두 씬 모두 정상. 배선 누락 없음.");
            else
                foreach (var p in problems) Debug.LogError($"[Validate] {p}");
        }

        /// <summary>
        /// 기획서의 마지막 합격 조건: <b>Phase 1 결과가 카드 3장에 실제로 반영되는가.</b>
        /// 잠입 결과별로 카드 무게와 대혐의 출발 점수를 찍어 눈으로 확인한다.
        /// </summary>
        static void ReportTradeoffs(string json, List<string> problems)
        {
            var cases = new (string name, HeistResult w)[]
            {
                ("깨끗 (담/장갑착용/2개)", Heist(stolen: 2, gloves: false, conceal: false, route: "wall", secs: 22f)),
                ("완주 (정문/맨손/3개)",   Heist(stolen: 3, gloves: true,  conceal: false, route: "front", secs: 30f)),
                ("인멸 (담/맨손/3개)",     Heist(stolen: 3, gloves: true,  conceal: true,  route: "wall", secs: 44f)),
                ("검거",                   Heist(stolen: 3, gloves: true,  conceal: false, route: "",     secs: 60f, caught: true)),
            };

            float concealC = 0f, plainC = 0f;

            foreach (var (name, w) in cases)
            {
                var deck = EvidenceDeck.Load(json, w);
                var board = HypothesisBoard.Default();
                board.ApplyWorld(w);

                var line = new System.Text.StringBuilder();
                line.Append($"[Validate] {name,-22} ")
                    .Append($"A {board.ScoreOf(Charge.A),2:0} / B {board.ScoreOf(Charge.B),2:0} / C {board.ScoreOf(Charge.C),2:0}   |  ");
                foreach (var c in deck.cards)
                    line.Append($"{c.id} {(c.Exists ? $"w{c.Weight(true):0.0}" : "없음")}  ");
                Debug.Log(line.ToString());

                if (w.concealed) concealC = board.ScoreOf(Charge.C);
                if (name.StartsWith("완주")) plainC = board.ScoreOf(Charge.C);
            }

            // 증거를 지운 플레이는 취조가 쉬워지되 은폐 혐의는 커져야 한다.
            if (concealC <= plainC)
                problems.Add($"인멸 트레이드오프가 성립하지 않는다. 은폐 혐의 C가 " +
                             $"인멸 {concealC:0} vs 미인멸 {plainC:0}. 인멸 쪽이 더 높아야 한다.");
        }

        static HeistResult Heist(int stolen, bool gloves, bool conceal, string route, float secs,
                                 bool caught = false)
        {
            var r = new HeistResult
            {
                played = true, stolen = stolen, glovesOff = gloves,
                concealed = conceal, route = route, escapeSeconds = secs, caught = caught,
            };
            HeistOutcome.Seal(r);
            return r;
        }

        /// <summary>씬 안의 컴포넌트를 찾아 지정한 직렬화 필드가 비어 있지 않은지 본다.</summary>
        static void CheckSerialized<T>(List<string> problems, string scene, params string[] fields)
            where T : Component
        {
            var comp = Object.FindFirstObjectByType<T>();
            if (comp == null)
            {
                problems.Add($"{scene}: {typeof(T).Name} 가 씬에 없다.");
                return;
            }

            var so = new SerializedObject(comp);
            foreach (var f in fields)
            {
                var p = so.FindProperty(f);
                if (p == null) { problems.Add($"{scene}: {typeof(T).Name}.{f} 필드를 찾지 못했다."); continue; }

                bool empty = p.propertyType == SerializedPropertyType.ObjectReference
                    ? p.objectReferenceValue == null
                    : p.isArray && p.arraySize == 0;

                if (empty) problems.Add($"{scene}: {typeof(T).Name}.{f} 가 비어 있다.");
            }
        }
    }
}
