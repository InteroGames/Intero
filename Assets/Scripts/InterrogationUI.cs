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

        [Header("오프닝")]
        [SerializeField] CanvasGroup introOverlay;   // 암전 + 내레이션
        [SerializeField] TMP_Text introText;
        [SerializeField] float introLineHold = 2.4f;
        [SerializeField] bool playIntro = true;

        readonly StringBuilder log = new StringBuilder();
        string currentQuestion;
        bool busy;

        void Start()
        {
            TranscriptLog.Begin();
            submitButton.onClick.AddListener(OnSubmit);
            inputField.onSubmit.AddListener(_ => OnSubmit());
            debugText.gameObject.SetActive(showDebug);
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            if (playIntro && introOverlay != null) yield return PlayIntro();
            else if (introOverlay != null) introOverlay.alpha = 0f;
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
                TranscriptLog.Write($"[intro] {line}");

                yield return FadeText(line, 0f, 1f, 0.5f);

                float t = 0f;
                while (t < introLineHold && !SkipPressed()) { t += Time.deltaTime; yield return null; }

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
            }

            currentQuestion = controller.NextQuestion();
            yield return Say(currentQuestion);
            RefreshGauges();
            inputField.ActivateInputField();
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

            AppendPlayer(input);

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
            else if (!result.repeatQuestion)   // 폴백이면 같은 질문에 대한 답을 기다린다
            {
                currentQuestion = controller.NextQuestion();
                yield return Say(currentQuestion);
            }

            busy = false;
            submitButton.interactable = true;
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
                case Outcome.RELEASED:
                    AppendSystem("— 문이 열린다 —"); break;
                case Outcome.HELD:
                    AppendSystem("— 오늘은 여기까지 —"); break;
                case Outcome.CONVICTED_THEFT:
                    AppendSystem("— 절도 혐의 확정 —"); break;
                case Outcome.SEIZED_SUBVERSION:
                    AppendSystem("— 조직 수사로 이송 —"); break;
                case Outcome.TOTAL_LOSS:
                    AppendSystem("— 기록이 사라졌다 —"); break;
            }

            // 석방은 이 게임의 유일한 승리다. 다음 판은 없다.
            if (r.outcome == Outcome.RELEASED)
            {
                yield return new WaitForSeconds(1.2f);
                AppendSystem($"제{controller.State.runCount}차 조사 끝에 자네는 취조실을 나왔다.");
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
