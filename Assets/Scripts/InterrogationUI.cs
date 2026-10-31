using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Interrogation
{
    /// <summary>
    /// D2용 최소 UI. 못생겨도 된다. 목표는 한 턴이 끝까지 도는 것.
    /// 연출(조명/카메라)은 D5에서 붙인다.
    /// </summary>
    public class InterrogationUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] InterrogationController controller;
        [SerializeField] SpeechBubble bubble;   // 없으면 로그창에 그대로 출력한다
        [SerializeField] TMP_Text logText;
        [SerializeField] TMP_InputField inputField;
        [SerializeField] Button submitButton;
        [SerializeField] Slider suspicionBar;
        [SerializeField] Slider subversionBar;
        [SerializeField] TMP_Text gaugeLabel;
        [SerializeField] TMP_Text debugText;   // D6 밸런싱용. 제출 빌드에서는 끈다.

        [Header("Tuning")]
        [SerializeField] float typeSpeed = 0.03f;
        [SerializeField] bool showDebug = true;

        [Header("안내")]
        [SerializeField] BriefingPanel briefing;
        [SerializeField] TurnFeedback feedback;   // 임시. 나중에 형사의 표정·행동이 대신한다.

        [Header("번복권")]
        [SerializeField] Button retractButton;
        [SerializeField] TMP_Text retractLabel;

        [Header("오프닝")]
        [SerializeField] CanvasGroup introOverlay;   // 암전 + 내레이션
        [SerializeField] TMP_Text introText;
        [SerializeField] float introLineHold = 2.0f;
        [SerializeField] bool playIntro = true;

        readonly StringBuilder log = new StringBuilder();
        string currentQuestion;
        bool busy;

        void Start()
        {
            TranscriptLog.Begin();
            submitButton.onClick.AddListener(OnSubmit);
            inputField.onSubmit.AddListener(_ => OnSubmit());
            if (retractButton != null) retractButton.onClick.AddListener(OnRetract);
            RefreshRetract();
            debugText.gameObject.SetActive(showDebug);
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            if (playIntro && introOverlay != null) yield return PlayIntro();
            else if (introOverlay != null) introOverlay.alpha = 0f;

            // 규칙을 한 번 짚어준다. 이후에는 Tab으로 다시 볼 수 있다.
            if (briefing != null)
            {
                briefing.Build(controller.Pool);
                yield return briefing.ShowAndWait();
            }

            // 미리 쓰는 조서 턴은 없앴다. 곧바로 취조실이고,
            // 형사가 처음 묻는 항목에 한 답이 그대로 조서에 적힌다.
            yield return OpenRun();
        }

        /// <summary>
        /// 암전에서 시작해 주인공의 독백을 한 줄씩 보여준다.
        /// 클릭이나 아무 키로 건너뛸 수 있다 - 시연 중 두 번째 플레이에서 기다리게 하면 안 된다.
        /// </summary>
        IEnumerator PlayIntro()
        {
            busy = true;
            submitButton.interactable = false;
            introOverlay.alpha = 1f;
            introOverlay.blocksRaycasts = true;

            for (int i = 0; i < controller.IntroLineCount; i++)
            {
                string line = controller.IntroLine(i);

                // 체포 근거는 매 판 다르고, 난이도에 따라 감춰진다.
                if (line == "{LEAD}")
                {
                    line = controller.LeadNarration()
                           ?? "무엇이 잘못됐는지는 모른다.\n다만 사흘 만에 그들이 찾아왔다.";
                }

                TranscriptLog.Write($"[intro] {line}");

                yield return FadeText(line, 0f, 1f, 0.5f);

                float hold = HoldFor(line);
                float t = 0f;
                while (t < hold && !SkipPressed()) { t += Time.deltaTime; yield return null; }

                yield return FadeText(line, 1f, 0f, 0.35f);
                if (SkipPressed()) break;
            }

            // 암전이 걷히며 취조실이 드러난다
            float f = 0f;
            while (f < 1f)
            {
                f += Time.deltaTime / 1.1f;
                introOverlay.alpha = 1f - f;
                yield return null;
            }
            introOverlay.alpha = 0f;
            introOverlay.blocksRaycasts = false;

            busy = false;
            submitButton.interactable = true;
        }

        /// <summary>여러 줄짜리 한 호흡은 읽는 데 더 걸린다. 줄 수에 맞춰 머무는 시간을 늘린다.</summary>
        float HoldFor(string line)
        {
            int lines = 1;
            foreach (char c in line) if (c == '\n') lines++;
            return introLineHold + (lines - 1) * 0.75f;
        }

        IEnumerator FadeText(string line, float from, float to, float dur)
        {
            introText.text = line;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                var c = introText.color;
                c.a = Mathf.Lerp(from, to, t / dur);
                introText.color = c;
                if (SkipPressed()) break;
                yield return null;
            }
            var end = introText.color;
            end.a = to;
            introText.color = end;
        }

        static bool SkipPressed() => Input.anyKeyDown || Input.GetMouseButtonDown(0);

        IEnumerator OpenRun()
        {
            log.Clear();
            TranscriptLog.Write($"\n=== 제{controller.State.runCount}차 조사 ===");
            AppendSystem($"— 제{controller.State.runCount}차 조사 —");

            // 2판째부터는 "또 불려왔다"는 감각을 먼저 준다
            if (controller.State.runCount > 1)
            {
                string opening = controller.RunOpening();
                if (!string.IsNullOrEmpty(opening))
                {
                    AppendSystem(opening);
                    yield return new WaitForSeconds(1.4f);
                }

                // 앉아 있는 사람이 바뀌었다는 것부터 알린다
                if (!string.IsNullOrEmpty(controller.InterrogatorArrival))
                {
                    AppendSystem(controller.InterrogatorArrival);
                    yield return new WaitForSeconds(1.6f);
                }

                // 이번엔 경찰이 다른 것을 들고 왔다
                string lead = controller.LeadNarration();
                if (!string.IsNullOrEmpty(lead))
                {
                    AppendSystem(lead);
                    yield return new WaitForSeconds(1.5f);
                }
            }

            currentQuestion = controller.NextQuestion();
            yield return Say(currentQuestion);
            RefreshGauges();
            inputField.ActivateInputField();
        }

        /// <summary>방금 한 말을 주워담는다. 모순으로 걸리기 전에 쓰는 마지막 수단.</summary>
        void OnRetract()
        {
            if (busy || !controller.CanRetract) return;

            string undone = controller.Retract();
            if (undone == null) return;

            AppendSystem($"(방금 한 말을 주워담는다 — \"{undone}\")");
            if (feedback != null)
                feedback.ReportSettlement("번복", 6);
            RefreshGauges();
            RefreshRetract();
        }

        void RefreshRetract()
        {
            if (retractButton == null) return;
            retractButton.interactable = !busy && controller.CanRetract;
            if (retractLabel != null)
                retractLabel.text = $"번복  {controller.RetractionsLeft}";
        }

        void OnSubmit()
        {
            if (busy) return;
            string input = inputField.text;
            inputField.text = "";
            StartCoroutine(RunTurn(input));
        }

        IEnumerator RunTurn(string input)
        {
            busy = true;
            submitButton.interactable = false;

            // 지금 형사가 캐고 있던 항목. 판정 후에는 다음 질문으로 넘어가므로 미리 잡아둔다.
            string probedLabel = controller.CurrentTopic?.label;
            int turnNumber = controller.State.turn + 1;

            // 자판을 뭉갠 입력은 그대로 띄우지 않는다. 지문으로 바꿔 보여준다.
            AppendPlayer(controller.DisplayFor(input) ?? input);

            // 응답 대기를 덮는 연출. LLM 호출과 병렬로 돈다.
            var stall = StartCoroutine(Stall());

            TurnResult result = null;
            yield return controller.Submit(currentQuestion, input, r => result = r);

            StopCoroutine(stall);

            if (result.beat != null)
            {
                AppendDetective(result.beat);
                yield return new WaitForSeconds(0.7f);   // 뜸. 즉답보다 무섭다.
            }

            // 판을 넘어 같은 수로 시작했을 때. 이 침묵이 대사의 무게를 만든다.
            if (result.openingRepeat)
                yield return new WaitForSeconds(1.1f);
            else if (result.quotedStatement != null || result.tacticRepeated)
                yield return new WaitForSeconds(0.4f);   // 인용 직전 침묵

            yield return Say(result.line);
            RefreshGauges();

            if (showDebug)
                debugText.text = $"{result.tactic}  의심{result.deltaSuspicion:+#;-#;0}  불온{result.deltaSubversion:+#;-#;0}  [{result.reason}]";

            if (feedback != null)
            {
                feedback.Report(turnNumber, result, probedLabel);
                if (result.settleReason != null)
                    feedback.ReportSettlement(result.settleReason, result.settleSuspicion);
            }

            if (result.settleReason != null)
                TranscriptLog.Write($"  [정산] {result.settleReason} 의심{result.settleSuspicion:+#;-#;0}");

            if (result.bustedFact != null)
                TranscriptLog.Write($"  ※ 물증 발각: {result.bustedFact.label} " +
                                    $"(진술 '{result.bustedFact.playerValue}' / 사실 '{result.bustedFact.truth}') → 확정 승격");

            var st = controller.State;
            TranscriptLog.Write(
                $"[턴{st.turn}] Q: {currentQuestion}\n" +
                $"  플레이어: {input}\n" +
                (result.beat != null ? $"  {result.beat}\n" : "") +
                $"  형사: {result.line}\n" +
                $"  → {result.tactic} 의심{result.deltaSuspicion:+#;-#;0} 불온{result.deltaSubversion:+#;-#;0} " +
                $"[{result.reason}] (의심 {st.suspicion} / 불온 {st.subversion}) {result.outcome}" +
                (result.lineIsQuestion ? " {LLM대사}" : " {풀대사}"));

            if (result.outcome != Outcome.ONGOING)
            {
                yield return EndRun(result);
            }
            else if (result.lineIsQuestion)
            {
                // 생성 대사가 반응과 질문을 겸한다. 풀 질문을 덧붙이면 형사가 두 번 말한다.
                currentQuestion = result.line;
            }
            else if (!result.repeatQuestion)
            {
                // 항목은 Submit 안에서 이미 넘어갔다. 여기서는 문장만 가져온다.
                currentQuestion = controller.QuestionText();
                yield return Say(currentQuestion);
            }

            busy = false;
            submitButton.interactable = true;
            RefreshRetract();
            inputField.ActivateInputField();
        }

        /// <summary>1.2초 넘게 걸리면 형사가 무언의 행동으로 시간을 끈다.</summary>
        IEnumerator Stall()
        {
            yield return new WaitForSeconds(1.2f);
            AppendDetective(controller.PickBeat());
        }

        IEnumerator EndRun(TurnResult r)
        {
            yield return new WaitForSeconds(0.8f);

            switch (r.outcome)
            {
                case Outcome.COLD_GAME:
                    AppendSystem("— 형사가 서류를 덮는다. 한 줄도 적히지 않았다 —"); break;
                case Outcome.RELEASED:
                    AppendSystem("— 문이 열린다 —"); break;
                case Outcome.ASYLUM:
                    AppendSystem("— 의사가 불려온다 —"); break;
                case Outcome.HELD:
                    AppendSystem("— 오늘은 여기까지 —"); break;
                case Outcome.CONVICTED_THEFT:
                    AppendSystem("— 절도 혐의 확정 —"); break;
                case Outcome.SEIZED_SUBVERSION:
                    AppendSystem("— 조직 수사로 이송 —"); break;
                case Outcome.TOTAL_LOSS:
                    AppendSystem("— 기록이 사라졌다 —"); break;
            }

            // 취조실을 나가는 경로들. 어느 쪽이든 다음 판은 없다.
            if (r.outcome == Outcome.RELEASED || r.outcome == Outcome.COLD_GAME
                || r.outcome == Outcome.ASYLUM)
            {
                yield return new WaitForSeconds(1.2f);

                switch (r.outcome)
                {
                    case Outcome.COLD_GAME:
                        AppendSystem("흠 하나 없었다. 그들은 자네에게 물을 것이 남지 않았다.\n" +
                                     "천라지망을 그대로 걸어 나왔다.");
                        break;
                    case Outcome.RELEASED:
                        AppendSystem($"제{controller.State.runCount}차 조사 끝에 자네는 취조실을 나왔다.");
                        break;
                    case Outcome.ASYLUM:
                        AppendSystem("그들은 더 이상 자네를 심문하지 않는다.\n" +
                                     "이제 자네를 진단할 뿐이다.");
                        break;
                }

                if (bubble != null) bubble.Hide();
                yield break;
            }

            if (r.hiddenEnding)
            {
                yield return new WaitForSeconds(1.5f);
                AppendSystem("");
                yield return Say(HiddenLine(r));
                yield break;   // 히든 엔딩에서는 다음 판으로 넘어가지 않는다
            }

            yield return new WaitForSeconds(1.5f);
            controller.NextRun();
            if (feedback != null) feedback.Clear();

            // 재조사도 카드를 다시 섞어 뽑은 채로 곧장 시작한다 (NextRun 안에서 Deal).
            yield return OpenRun();
        }

        /// <summary>
        /// 제4의 벽. 인용은 반드시 플레이어가 이 게임에 입력한 문장만 쓴다.
        /// D5에서 조명 프리셋 OFF와 함께 연출한다.
        /// </summary>
        string HiddenLine(TurnResult r)
        {
            string quote = "";
            var st = controller.State;
            for (int i = st.statements.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(st.statements[i].raw))
                { quote = st.statements[i].raw; break; }
            }
            return $"자네, {st.runCount}번 다 같은 방식이었어.\n\n" +
                   $"\"{quote}\"\n\n" +
                   "그건 배운 게 아니야. 원래 그런 거지.";
        }

        void RefreshGauges()
        {
            var st = controller.State;
            suspicionBar.value = st.suspicion / 100f;
            subversionBar.value = st.subversion / 100f;
            gaugeLabel.text = $"의심 {st.suspicion}   불온 {st.subversion}";
        }

        // --- 로그 출력 ---

        IEnumerator Say(string line)
        {
            if (bubble != null)
            {
                yield return bubble.Type(line, typeSpeed);
                log.Append($"\n형사: {line}\n");
                logText.text = log.ToString();
                yield break;
            }

            log.Append("\n형사: ");
            logText.text = log.ToString();
            foreach (char c in line)
            {
                log.Append(c);
                logText.text = log.ToString();
                yield return new WaitForSeconds(typeSpeed);
            }
            log.Append('\n');
            logText.text = log.ToString();
        }

        void AppendPlayer(string s) { log.Append($"\n> {s}\n"); logText.text = log.ToString(); }

        /// <summary>무언의 행동. 말풍선에도 그대로 띄운다.</summary>
        void AppendDetective(string s)
        {
            if (bubble != null) bubble.ShowInstant(s);
            log.Append($"\n{s}\n");
            logText.text = log.ToString();
        }

        void AppendSystem(string s) { log.Append($"\n{s}\n"); logText.text = log.ToString(); }
    }
}
