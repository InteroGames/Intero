using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Interrogation.Heist;

namespace Interrogation.Inquiry
{
    /// <summary>한 턴이 끝나고 UI로 나가는 것. 수치는 여기서 더 바뀌지 않는다.</summary>
    public class InquiryTurn
    {
        public string line;          // 형사의 대사
        public string beat;          // 대사 앞의 무언 연출
        public string displayLine;   // 뭉갠 입력 대신 화면에 나갈 지문
        public TurnReport report;
        public EvidenceCard revealed;

        public int roundNumber;
        public Charge? frontCharge;
        public Stage stage;
        public bool roundEnded;

        public bool finished;
        public InquiryEnding ending;
        public Charge topCharge;
        public VictoryAxes axes;
        public string epilogue;
    }

    /// <summary>
    /// 취조 페이즈의 턴 루프.
    ///
    /// 한 라운드 = Account(진술) → Clarify(확인) → Challenge(대질) → Evaluation(정리).
    /// R1 자유진술(혐의 미공개) → R2 혐의A → R3 혐의B → R4 혐의C.
    ///
    /// 어떤 실패도 화면에 오류로 나가지 않는다. 전부 형사의 대사로 흡수된다.
    /// </summary>
    public class InquiryController : MonoBehaviour
    {
        [SerializeField] LLMClient llm;
        [SerializeField] TextAsset evidenceJson;
        [SerializeField] TextAsset linesJson;

        [Tooltip("끄면 형사 대사가 사전 작성 풀에서만 나온다. 네트워크 없이 시연할 때 확인용.")]
        [SerializeField] bool useLlmDialogue = true;

        public const int TURNS_PER_ROUND = 4;
        const int HISTORY_LINES = 8;

        public InquirySession Session { get; private set; }

        InquiryLLM brain;
        InquiryLines lines;
        readonly List<string> history = new List<string>();
        readonly HashSet<string> usedLines = new HashSet<string>();

        void Awake()
        {
            TranscriptLog.Begin();

            Session = new InquirySession { world = PhaseBridge.Consume() };
            Session.deck = evidenceJson != null
                ? EvidenceDeck.Load(evidenceJson.text, Session.world)
                : new EvidenceDeck();

            // 잠입 결과가 카드 수치와 형사의 출발 가설 양쪽에 반영된다.
            Session.board.ApplyWorld(Session.world);

            lines = InquiryLines.Load(linesJson != null ? linesJson.text : null);
            brain = new InquiryLLM(llm);

            TranscriptLog.Write($"=== 취조 시작 === (잠입: {Session.world.Summary()})");
            foreach (var c in Session.deck.cards)
                TranscriptLog.Write($"  [카드] {c.id} exists={c.Exists} rel={c.reliability} " +
                                    $"uniq={c.uniqueness} risk={c.proceduralRisk} altered={c.altered}");
        }

        // ---------------------------------------------------------------- 진행

        public string Opening() => Session.Round.opening;

        /// <summary>이번 턴에 형사가 서 있는 단계.</summary>
        public Stage StageOf(int turnInRound)
        {
            switch (Mathf.Clamp(turnInRound, 1, TURNS_PER_ROUND))
            {
                case 1: return Stage.Account;
                case 2: return Stage.Clarify;
                case 3: return Stage.Challenge;
                default: return Stage.Evaluation;
            }
        }

        /// <summary>판의 첫 질문. 증거는 아직 꺼내지 않는다.</summary>
        public string FirstQuestion()
            => lines.Pick("account", usedLines, "그날 밤 있었던 일을 자네 입으로 말해보게.");

        public IEnumerator Submit(string question, string input, Action<InquiryTurn> done)
        {
            Session.turnInRound++;
            Session.totalTurns++;

            var turn = new InquiryTurn
            {
                roundNumber = Session.RoundNumber,
                frontCharge = Session.FrontCharge,
                stage = StageOf(Session.turnInRound),
            };

            var report = new TurnReport();
            turn.report = report;

            // 0. 입력의 형태부터 본다. 자판을 뭉갠 것을 LLM에 보내면 형사가 그 글자를 따라 읽는다.
            var kind = InputClassifier.Classify(input);
            turn.displayLine = lines.Action(InputClassifier.PoolKey(kind));

            Utterance u;
            if (InputClassifier.SkipExtraction(kind) || kind == InputKind.Profanity)
            {
                u = new Utterance { raw = input ?? "" };
                u.tactics.Add(TacticTag.EMPTY);
                InquiryJudge.Resolve(Session, u, report);

                // Resolve가 situation을 통째로 쓰므로 형태에 대한 설명은 그 뒤에 앞세운다.
                report.situation = InputClassifier.Describe(kind) + " " + report.situation;
                turn.beat = lines.Pick("beat", usedLines, "형사가 자네를 본다.");
            }
            else
            {
                Utterance parsed = null;
                yield return brain.Read(question, input, HypothesisMenu(), r => parsed = r);
                u = parsed ?? new Utterance { raw = input };
                if (u.tactics.Count == 0) u.tactics.Add(TacticTag.EMPTY);

                InquiryJudge.Resolve(Session, u, report);
            }

            LogUtterance(u, report);

            // 1. 증거 공개. **항상 진술 잠금 이후다.** 약하고 모호한 것부터 나간다.
            if (Session.FrontCharge.HasValue && Session.openCard == null
                && InquiryJudge.ShouldRevealNow(Session, Session.turnInRound))
            {
                var card = Session.deck.NextFor(Session.FrontCharge.Value, Session.RoundNumber);
                if (card != null)
                {
                    InquiryJudge.Reveal(Session, card, report);
                    turn.revealed = card;
                }
            }

            // 2. 라운드 정산. 확정된 혐의가 다음 라운드 난이도가 된다.
            bool lastRound = Session.roundIndex >= Session.rounds.Count - 1;
            if (Session.turnInRound >= TURNS_PER_ROUND)
            {
                InquiryJudge.CloseRound(Session, report);
                turn.roundEnded = true;

                if (lastRound) Finish(turn);
                else { Session.roundIndex++; Session.turnInRound = 0; }
            }

            // 3. 코드가 정한 상황을 형사의 말투로 옮긴다. 여기서 수치는 바뀌지 않는다.
            turn.line = lines.Pick(FallbackKey(turn), usedLines, "…계속하게.");
            yield return Dress(turn, report);

            Remember(turn.displayLine ?? input, turn.line);
            LogTurn(turn, report);
            done(turn);
        }

        void Finish(InquiryTurn turn)
        {
            Session.Finished = true;
            var ending = EndingResolver.Resolve(Session, out Charge top, out VictoryAxes axes);
            Session.ending = ending;

            turn.finished = true;
            turn.ending = ending;
            turn.topCharge = top;
            turn.axes = axes;
            turn.epilogue = EndingResolver.Epilogue(Session, ending, top, axes);
        }

        // ---------------------------------------------------------------- 대사

        IEnumerator Dress(InquiryTurn turn, TurnReport report)
        {
            if (!useLlmDialogue || string.IsNullOrWhiteSpace(report.situation)) yield break;

            var sit = new StringBuilder(report.situation);

            if (turn.revealed != null)
            {
                sit.Append("\n\n이제 서류를 한 장 꺼낸다.\n")
                   .Append($"확인된 사실: {turn.revealed.Fact}\n")
                   .Append($"자네가 그것으로 내린 결론: {turn.revealed.Claim}\n")
                   .Append("이 두 줄만 말한다. 다른 증거는 언급하지 마라. 그리고 반박할 기회를 준다.");
                if (!string.IsNullOrEmpty(turn.revealed.assertion))
                    sit.Append($"\n이 말을 반드시 그대로 한 번 한다: \"{turn.revealed.assertion}\"");
            }

            if (turn.finished)
            {
                sit.Append("\n\n조사가 여기서 끝난다. ")
                   .Append(turn.ending == InquiryEnding.Charged
                       ? "형사는 서류에 한 줄을 적는다. 다음 질문을 하지 않는다."
                       : "형사가 서류를 덮는다. 다음 질문을 하지 않는다.");
            }
            else
            {
                var next = StageOf(Session.turnInRound + 1);
                sit.Append("\n\n[이번 발언에서 할 일]\n").Append(StageInfo.Direction(next));
                if (turn.roundEnded && Session.FrontCharge.HasValue)
                    sit.Append($"\n이제 화제를 옮긴다: {ChargeInfo.Name(Session.FrontCharge.Value)}. ")
                       .Append($"{Session.Round.opening} 정도의 말로 넘어간다.");
            }

            string generated = null;
            yield return brain.Speak(Session.detective.persona, KnowledgeBrief(), HypothesisBrief(),
                                     sit.ToString(), RecentHistory(), s => generated = s);

            if (!string.IsNullOrWhiteSpace(generated)) turn.line = generated;
        }

        string FallbackKey(InquiryTurn turn)
        {
            if (turn.finished) return turn.ending == InquiryEnding.Charged ? "closing_charged" : "closing_released";
            if (turn.revealed != null) return "challenge";
            switch (StageOf(Session.turnInRound + 1))
            {
                case Stage.Account: return "account";
                case Stage.Clarify: return "clarify";
                case Stage.Challenge: return "challenge";
                default: return "evaluation";
            }
        }

        // ---------------------------------------------------------------- 프롬프트 재료

        /// <summary>
        /// DETECTIVE_KNOWLEDGE. 형사가 실제로 확보한 것만 적는다.
        ///
        /// <b>아직 꺼내지 않은 증거의 내용은 여기 적지 않는다.</b>
        /// 적으면 형사가 미리 흘리고, 증거 선공개 금지 규칙이 깨진다.
        /// WORLD_STATE는 이 함수 안에서 단 한 번도 참조되지 않는다.
        /// </summary>
        string KnowledgeBrief()
        {
            var sb = new StringBuilder();
            sb.Append(Session.detective.caseTheory).Append('\n');

            var revealed = Session.deck.Revealed();
            if (revealed.Count > 0)
            {
                sb.Append("\n이미 꺼내 보인 서류:\n");
                foreach (var c in revealed)
                    sb.Append($"· {c.Fact}  → 자네의 결론: {c.Claim}")
                      .Append(c.attacksLanded > 0 ? "  (반박이 먹혀 무게가 줄었다)" : "")
                      .Append('\n');
            }

            int held = 0;
            foreach (var c in Session.deck.cards)
                if (c.Exists && !c.revealed && c.AcquiredRound <= Session.RoundNumber) held++;
            if (held > 0)
                sb.Append($"\n아직 꺼내지 않은 서류가 {held}장 있다. 무엇인지 여기 적지 않는다. ")
                  .Append("꺼낼 때가 되면 따로 알려준다. 그 전에는 있는 척도 없는 척도 하지 마라.\n");

            if (Session.claims.claims.Count > 0)
            {
                sb.Append("\n조서에 적힌 피의자의 진술:\n");
                int from = Mathf.Max(0, Session.claims.claims.Count - 6);
                for (int i = from; i < Session.claims.claims.Count; i++)
                {
                    var c = Session.claims.claims[i];
                    sb.Append($"· ({c.round}회) {c.value}\n");
                }
            }

            var pending = new List<InquiryLead>();
            foreach (var l in Session.knowledge.leads) if (!l.resolved) pending.Add(l);
            if (pending.Count > 0)
            {
                sb.Append("\n확인해보기로 한 것:\n");
                foreach (var l in pending) sb.Append($"· {l.text}\n");
            }

            sb.Append("\n여기 적히지 않은 것은 자네도 모른다. 아는 척하지 마라.\n")
              .Append(Session.detective.antiManipulation).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// DETECTIVE_HYPOTHESIS. 숫자는 절대 넣지 않는다 - 형사가 점수를 입에 담으면 게임이 끝난다.
        /// 대안 가설을 항상 함께 적는다. 그게 형사가 터널비전에 빠지지 않는 유일한 장치다.
        /// </summary>
        string HypothesisBrief()
        {
            var sb = new StringBuilder();
            foreach (Charge c in new[] { Charge.A, Charge.B, Charge.C })
            {
                bool front = Session.FrontCharge == c;
                sb.Append(front ? "▶ " : "  ")
                  .Append(ChargeInfo.Name(c)).Append(" — ")
                  .Append(ChargeInfo.BandWord(Session.board.BandOf(c))).Append('\n');

                if (!front) continue;
                foreach (var h in Session.board.Under(c))
                {
                    sb.Append($"   · {h.statement} ({ChargeInfo.BandWord(h.Band)})");
                    if (h.conceded) sb.Append(" — 피의자가 이건 인정했다");
                    sb.Append($"\n     같이 열어두고 있는 다른 설명: {h.counterHypothesis}\n");
                }
            }
            sb.Append("\n너는 언제나 다른 설명을 최소 하나 열어둔다. 하나로 정해놓고 몰아붙이지 않는다.\n");
            return sb.ToString();
        }

        /// <summary>CONCEDE 분류에 쓸 가설 목록. 플레이어에게는 절대 보이지 않는다.</summary>
        string HypothesisMenu()
        {
            var sb = new StringBuilder();
            foreach (var h in Session.board.all) sb.Append($"{h.id}: {h.statement}\n");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 기록

        void Remember(string input, string line)
        {
            if (!string.IsNullOrWhiteSpace(input)) history.Add($"자네: {input}");
            if (!string.IsNullOrWhiteSpace(line)) history.Add($"형사: {line}");
            while (history.Count > HISTORY_LINES) history.RemoveAt(0);
        }

        string RecentHistory() => history.Count == 0 ? null : string.Join("\n", history);

        void LogUtterance(Utterance u, TurnReport r)
        {
            var tags = new StringBuilder();
            foreach (var t in u.tactics) tags.Append(t).Append(' ');
            TranscriptLog.Write($"  [발화] tactics={tags}claims={u.claims.Count} " +
                                $"verif={u.verifiability.Count}/3 {(u.fromModel ? "" : "(폴백)")}");
        }

        void LogTurn(InquiryTurn t, TurnReport r)
        {
            var sb = new StringBuilder();
            sb.Append($"[R{t.roundNumber} T{Session.turnInRound} {StageInfo.Name(t.stage)}] ")
              .Append($"판정 {r.verdict} Δ{r.chargeDelta:+0.#;-0.#;0} :: ");
            foreach (var e in r.effects) sb.Append(e).Append(" / ");
            sb.Append($"\n  A {Session.board.ScoreOf(Charge.A):0} ")
              .Append($"B {Session.board.ScoreOf(Charge.B):0} ")
              .Append($"C {Session.board.ScoreOf(Charge.C):0} ")
              .Append($"| 신빙성 {Session.knowledge.credibility:0} ")
              .Append($"위험 {Session.knowledge.institutionalRisk:0} ")
              .Append($"확신 {Session.board.confidence:0}");
            if (t.revealed != null) sb.Append($"\n  ※ 증거 공개: {t.revealed.id}");
            if (t.finished) sb.Append($"\n  ※ 엔딩 {t.ending} / {t.axes.Describe()}");
            TranscriptLog.Write(sb.ToString());
        }
    }

    /// <summary>형사 대사 폴백 풀. LLM이 죽어도 형사는 말을 해야 한다.</summary>
    public class InquiryLines
    {
        readonly Dictionary<string, List<string>> pool = new Dictionary<string, List<string>>();
        readonly Dictionary<string, List<string>> actions = new Dictionary<string, List<string>>();

        public static InquiryLines Load(string json)
        {
            var l = new InquiryLines();
            if (string.IsNullOrWhiteSpace(json)) return l;

            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var lineRoot = root["lines"] as Newtonsoft.Json.Linq.JObject;
                if (lineRoot != null)
                    foreach (var kv in lineRoot)
                        l.pool[kv.Key] = kv.Value.ToObject<List<string>>();

                var actRoot = root["player_action"] as Newtonsoft.Json.Linq.JObject;
                if (actRoot != null)
                    foreach (var kv in actRoot)
                        l.actions[kv.Key] = kv.Value.ToObject<List<string>>();
            }
            catch (Exception e)
            {
                TranscriptLog.Write($"  [대사풀] 로드 실패: {e.Message}");
            }
            return l;
        }

        public string Pick(string key, HashSet<string> used, string fallback)
        {
            if (key == null || !pool.TryGetValue(key, out var list) || list.Count == 0) return fallback;

            var avail = new List<string>();
            foreach (var s in list) if (!used.Contains(s)) avail.Add(s);
            if (avail.Count == 0) { used.Clear(); avail.AddRange(list); }

            string pick = avail[UnityEngine.Random.Range(0, avail.Count)];
            used.Add(pick);
            return pick;
        }

        /// <summary>말이 아닌 입력을 대신할 지문. 없으면 null이고 원문을 그대로 쓴다.</summary>
        public string Action(string key)
        {
            if (key == null || !actions.TryGetValue(key, out var l) || l.Count == 0) return null;
            return l[UnityEngine.Random.Range(0, l.Count)];
        }
    }
}
