using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Interrogation.Inquiry
{
    /// <summary>
    /// 취조 페이즈 UI.
    ///
    /// 증거 카드는 <b>objectiveFact와 detectiveClaim만</b> 건조하게 보여준다.
    /// 대안 해석, 공격면, 수치는 버튼·힌트·툴팁 어디에도 노출하지 않는다.
    /// 노출하는 순간 이 게임은 자유 발화 게임이 아니라 객관식이 된다.
    /// </summary>
    public class InquiryUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] InquiryController controller;
        [SerializeField] SpeechBubble bubble;
        [SerializeField] TMP_Text logText;
        [SerializeField] TMP_InputField inputField;
        [SerializeField] Button submitButton;

        [Header("조서")]
        [SerializeField] Transform cardList;
        [SerializeField] GameObject cardTemplate;   // 자식 "Fact" / "Claim"
        [SerializeField] TMP_Text roundLabel;
        [SerializeField] TMP_Text chargeLabel;

        [Header("엔딩")]
        [SerializeField] CanvasGroup endOverlay;
        [SerializeField] TMP_Text endText;

        [Header("Tuning")]
        [SerializeField] float typeSpeed = 0.028f;
        [SerializeField] TMP_Text debugText;
        [SerializeField] bool showDebug = true;

        readonly StringBuilder log = new StringBuilder();
        string currentQuestion;
        bool busy;

        void Start()
        {
            submitButton.onClick.AddListener(OnSubmit);
            inputField.onSubmit.AddListener(_ => OnSubmit());
            if (cardTemplate != null) cardTemplate.SetActive(false);
            if (endOverlay != null) { endOverlay.alpha = 0f; endOverlay.blocksRaycasts = false; }
            if (debugText != null) debugText.gameObject.SetActive(showDebug);
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            RefreshHeader();
            AppendSystem($"— 제1차 조사 —");

            string opening = controller.Opening();
            if (!string.IsNullOrEmpty(opening))
            {
                yield return Say(opening);
                yield return new WaitForSeconds(0.5f);
            }

            currentQuestion = controller.FirstQuestion();
            yield return Say(currentQuestion);
            inputField.ActivateInputField();
        }

        void OnSubmit()
        {
            if (busy || controller.Session.Finished) return;
            string input = inputField.text;
            inputField.text = "";
            StartCoroutine(RunTurn(input));
        }

        IEnumerator RunTurn(string input)
        {
            busy = true;
            submitButton.interactable = false;

            AppendPlayer(input);

            var stall = StartCoroutine(Stall());

            InquiryTurn turn = null;
            yield return controller.Submit(currentQuestion, input, t => turn = t);

            StopCoroutine(stall);

            // 화면에 나갈 수 없는 입력이었으면 지문으로 바꿔 다시 적는다.
            if (turn.displayLine != null) ReplaceLastPlayerLine(turn.displayLine);

            if (turn.beat != null)
            {
                AppendDetective(turn.beat);
                yield return new WaitForSeconds(0.6f);
            }

            // 서류를 꺼내는 순간에는 뜸을 들인다. 즉답보다 무섭다.
            if (turn.revealed != null)
            {
                yield return new WaitForSeconds(0.8f);
                AddCard(turn.revealed);
            }

            yield return Say(turn.line);

            if (turn.roundEnded && !turn.finished)
            {
                yield return new WaitForSeconds(0.9f);
                AppendSystem($"— 제{controller.Session.RoundNumber}차 —");
            }

            RefreshHeader();

            if (showDebug && debugText != null)
            {
                var s = controller.Session;
                debugText.text =
                    $"A {s.board.ScoreOf(Charge.A):00}  B {s.board.ScoreOf(Charge.B):00}  C {s.board.ScoreOf(Charge.C):00}   " +
                    $"신빙성 {s.knowledge.credibility:00}  위험 {s.knowledge.institutionalRisk:00}  확신 {s.board.confidence:00}\n" +
                    $"{turn.report.verdict}  Δ{turn.report.chargeDelta:+0.#;-0.#;0}  " +
                    string.Join(" / ", turn.report.effects);
            }

            if (turn.finished) yield return ShowEnding(turn);

            currentQuestion = turn.line;
            busy = false;
            submitButton.interactable = !controller.Session.Finished;
            if (!controller.Session.Finished) inputField.ActivateInputField();
        }

        IEnumerator Stall()
        {
            yield return new WaitForSeconds(1.3f);
            AppendDetective("형사가 서류를 넘긴다.");
        }

        // ---------------------------------------------------------------- 표시

        void RefreshHeader()
        {
            var s = controller.Session;
            if (roundLabel != null)
                roundLabel.text = s.FrontCharge.HasValue
                    ? $"제{s.RoundNumber}차   {ChargeInfo.Name(s.FrontCharge.Value)}"
                    : $"제{s.RoundNumber}차";

            if (chargeLabel == null) return;

            // 숫자는 내보내지 않는다. 아직 전면에 나오지 않은 혐의는 가려둔다.
            var sb = new StringBuilder();
            foreach (Charge c in new[] { Charge.A, Charge.B, Charge.C })
            {
                bool seen = Seen(c);
                sb.Append(ChargeInfo.Name(c)).Append("   ")
                  .Append(seen ? ChargeInfo.BandWord(s.board.BandOf(c)) : "—")
                  .Append('\n');
            }
            chargeLabel.text = sb.ToString();
        }

        /// <summary>형사가 이 혐의를 전면에 꺼낸 적이 있는가. 그 전에는 알 길이 없다.</summary>
        bool Seen(Charge c)
        {
            var s = controller.Session;
            foreach (var r in s.rounds)
                if (r.charge == c && r.index <= s.RoundNumber) return true;
            return false;
        }

        void AddCard(EvidenceCard card)
        {
            if (cardList == null || cardTemplate == null) return;

            var row = Instantiate(cardTemplate, cardList);
            row.SetActive(true);

            var fact = row.transform.Find("Fact")?.GetComponent<TMP_Text>();
            var claim = row.transform.Find("Claim")?.GetComponent<TMP_Text>();

            // 여기 들어가는 것은 이 두 줄뿐이다. 수치도, 대안 해석도, 공격면도 아니다.
            if (fact != null) fact.text = card.Fact;
            if (claim != null) claim.text = $"— {card.Claim}";

            AppendSystem("(형사가 서류를 한 장 내려놓는다)");
        }

        IEnumerator ShowEnding(InquiryTurn turn)
        {
            yield return new WaitForSeconds(1.4f);
            if (endOverlay == null || endText == null) yield break;

            endText.text = turn.epilogue;
            endOverlay.blocksRaycasts = true;

            float t = 0f;
            while (t < 1.4f)
            {
                t += Time.deltaTime;
                endOverlay.alpha = Mathf.Clamp01(t / 1.4f);
                yield return null;
            }
            endOverlay.alpha = 1f;
        }

        // ---------------------------------------------------------------- 로그

        IEnumerator Say(string line)
        {
            if (string.IsNullOrEmpty(line)) yield break;

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

        string lastPlayerLine;

        void AppendPlayer(string s)
        {
            lastPlayerLine = $"\n> {s}\n";
            log.Append(lastPlayerLine);
            logText.text = log.ToString();
        }

        /// <summary>자판을 뭉갠 원문은 화면에 남기지 않는다. 지문으로 갈아끼운다.</summary>
        void ReplaceLastPlayerLine(string replacement)
        {
            if (lastPlayerLine == null) return;
            int at = log.ToString().LastIndexOf(lastPlayerLine, System.StringComparison.Ordinal);
            if (at < 0) return;

            log.Remove(at, lastPlayerLine.Length);
            log.Insert(at, $"\n> {replacement}\n");
            logText.text = log.ToString();
            lastPlayerLine = null;
        }

        void AppendDetective(string s)
        {
            if (bubble != null) bubble.ShowInstant(s);
            log.Append($"\n{s}\n");
            logText.text = log.ToString();
        }

        void AppendSystem(string s) { log.Append($"\n{s}\n"); logText.text = log.ToString(); }
    }
}
