using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Interrogation.Heist;

namespace Interrogation.Inquiry
{
    /// <summary>
    /// 네 상태를 한 곳에 모아 든 세션. 판정은 전부 InquiryJudge가 한다.
    ///
    /// WORLD_STATE          world      실제로 있었던 사건 (Phase 1 결과)
    /// PLAYER_CLAIM_STATE   claims     플레이어가 지금까지 말한 것
    /// DETECTIVE_KNOWLEDGE  knowledge  형사가 현재 확보한 증거만
    /// DETECTIVE_HYPOTHESIS board      형사가 의심하는 혐의와 확신도
    ///
    /// <b>world는 LLM 프롬프트에 절대 넣지 않는다.</b> 이 세 줄을 지키는 것이 이 파일의 전부다.
    /// </summary>
    public class InquirySession
    {
        public HeistResult world;
        public EvidenceDeck deck;
        public HypothesisBoard board = HypothesisBoard.Default();
        public PlayerClaimState claims = new PlayerClaimState();
        public DetectiveKnowledge knowledge = new DetectiveKnowledge();
        public DetectiveProfile detective = DetectiveProfile.Veteran();

        public List<RoundPlan> rounds = RoundPlan.Default();
        public int roundIndex;      // 0-based
        public int turnInRound;
        public int totalTurns;

        public RoundPlan Round => rounds[Mathf.Clamp(roundIndex, 0, rounds.Count - 1)];
        public int RoundNumber => Round.index;
        public Charge? FrontCharge => Round.charge;

        /// <summary>이번 라운드에 이미 꺼낸 카드.</summary>
        public EvidenceCard openCard;

        public bool Finished;
        public InquiryEnding ending;
    }

    /// <summary>한 턴의 판정 결과. 화면에 나가는 것은 situation을 형사 말투로 옮긴 대사뿐이다.</summary>
    public class TurnReport
    {
        public ClaimVerdict verdict = ClaimVerdict.Pending;
        public readonly List<string> effects = new List<string>();
        public float chargeDelta;
        public string situation = "";

        public EvidenceCard revealed;
        public bool quoteLanded, identityLanded, attackLanded, concedeMade;
        public bool woundJustSurfaced;
        public InquiryLead newLead, resolvedLead;
        public bool leadConfirmed;
        public bool roundEnded;

        public void Note(string s) { if (!string.IsNullOrEmpty(s)) effects.Add(s); }
    }

    /// <summary>
    /// 모든 수치 판정. <b>이 클래스 밖에서 혐의 점수를 건드리지 말 것.</b>
    /// 흩어지면 밸런싱이 불가능해지고, 같은 상황에서 매번 다르게 판정되면
    /// 플레이어가 규칙을 학습할 수 없게 된다.
    /// </summary>
    public static class InquiryJudge
    {
        // --- 판정표. 밸런싱용 초기값이다. ---
        const float AlignsWithEvidence = -5f;   // 증거와 일치 → 신빙성 상승
        const float Unverifiable = 1f;          // 검증 불가능한 주장 → 보류
        const float VerifiedTrue = -10f;        // 검증 후 사실 확인
        const float ConflictWeak = 3f;          // 약한 증거와 모순
        const float ConflictStrong = 10f;       // 강한 증거와 모순
        const float ChangedAfterReveal = 4f;    // 증거 공개 뒤 진술 변경
        const float NewExplanation = -3f;       // 증거를 설명하는 새 가설 제시
        const float CounterConfirmed = -15f;    // 대안 가설이 실제 확인됨

        /// <summary>
        /// 독립적인 모순이 누적될 때만 혐의가 크게 오른다.
        /// 모순 하나 = 범인 구조를 만들지 말 것. 진실을 말하는 사람도 기억 오류로 어긋난다.
        /// </summary>
        static readonly float[] ContradictionLadder = { 3f, 6f, 10f };

        /// <summary>
        /// 이 가중치를 넘으면 "강한 증거"로 본다.
        /// weight 공식의 상한이 10이므로 5.5는 대략 중상위다.
        /// </summary>
        const float StrongWeight = 5.5f;

        // ------------------------------------------------------------------

        public static void Resolve(InquirySession s, Utterance u, TurnReport r)
        {
            var sb = new StringBuilder();

            // 0. 지난 라운드에 등록된 조사 대상이 돌아왔는가. 발화보다 먼저 처리한다.
            ResolveLead(s, r, sb);

            // 1. 진술을 잠근다. 잠금 이후에만 증거가 공개된다 (증거 선공개 금지).
            foreach (var c in u.claims)
            {
                c.round = s.RoundNumber;
                c.turn = s.totalTurns;
                JudgeClaim(s, c, r, sb);
                s.claims.Lock(c);
            }

            // 2. 전술. 증거와 대화에 연결된 발화만 의미 있는 반응을 만든다.
            foreach (var t in u.tactics) ApplyTactic(s, u, t, r, sb);

            // 3. 검증 가능성. 안전한 애매함과 위험한 구체성 사이의 도박.
            JudgeVerifiability(s, u, r, sb);

            if (r.effects.Count == 0) sb.Append("자네의 말은 지금 형사가 쥔 것과 부딪히지 않는다. ");

            r.situation = sb.ToString();
        }

        // ---------------------------------------------------------------- 진술 판정

        static void JudgeClaim(InquirySession s, Claim c, TurnReport r, StringBuilder sb)
        {
            // 과거 진술과의 모순. 같은 축 안에서만 대조한다.
            var clash = s.claims.FindConflict(c);
            if (clash != null && !s.claims.AlreadyBroken(c.Axis))
            {
                int n = s.claims.IndependentContradictions;
                float d = ContradictionLadder[Mathf.Min(n, ContradictionLadder.Length - 1)];
                s.claims.contradictedAxes.Add(c.Axis);

                Apply(s, s.FrontCharge, d, r);
                r.verdict = ClaimVerdict.Failed;
                r.Note($"진술불일치 +{d:0}");
                sb.Append($"자네가 아까 \"{clash.value}\"라고 했는데 지금은 \"{c.value}\"라고 한다. ")
                  .Append(n == 0
                      ? "형사는 그것을 몰아붙이지 않는다. 기억은 어긋날 수 있다는 걸 안다. 다만 적어둔다. "
                      : "두 번째다. 이번에는 그냥 넘기지 않는다. ");
            }

            // 공개된 증거와의 대조.
            var conflict = FindConflictingCard(s, c, revealedOnly: true);
            if (conflict != null)
            {
                bool independent = s.deck.IsIndependent(conflict);
                float w = conflict.Weight(independent);
                float d = w >= StrongWeight ? ConflictStrong : ConflictWeak;

                ApplyToCard(s, conflict, d, r);
                r.verdict = ClaimVerdict.Failed;
                r.Note($"증거충돌 +{d:0}");
                sb.Append($"자네의 말이 서류에 적힌 것과 어긋난다. ({conflict.Fact}) ")
                  .Append(w >= StrongWeight
                      ? "이건 흐릿한 증거가 아니다. 형사는 그 어긋남을 그대로 짚는다. "
                      : "형사는 그 어긋남을 짚되, 이것 하나로 단정하지는 않는다. ");

                if (s.claims.ChangedAfterReveal(c, conflict.revealedRound))
                {
                    ApplyToCard(s, conflict, ChangedAfterReveal, r);
                    r.Note($"사후수정 +{ChangedAfterReveal:0}");
                    sb.Append("서류를 본 뒤에 말이 바뀌었다는 것도 형사는 알고 있다. ");
                }
                return;
            }

            var aligned = FindAlignedCard(s, c);
            if (aligned != null)
            {
                ApplyToCard(s, aligned, AlignsWithEvidence, r);
                s.knowledge.AddCredibility(5f);
                r.verdict = ClaimVerdict.Success;
                r.Note($"증거일치 {AlignsWithEvidence:0}");
                sb.Append("자네의 말이 서류와 맞아떨어진다. 형사는 그 칸에 표시를 한다. ");
            }
        }

        static void JudgeVerifiability(InquirySession s, Utterance u, TurnReport r, StringBuilder sb)
        {
            if (u.claims.Count == 0) return;
            var v = u.verifiability;

            if (v.Full)
            {
                var claim = u.claims[0];
                var lead = new InquiryLead
                {
                    text = string.IsNullOrWhiteSpace(claim.value) ? claim.raw : claim.value,
                    createdRound = s.RoundNumber,
                    resolveRound = s.RoundNumber + 1,
                    targetHypothesis = TargetHypothesisId(s),
                    collidesWithCard = FindCollisionId(s, claim),
                };
                lead.fromExplanation = u.tactics.Contains(TacticTag.EXPLAIN)
                                    || u.tactics.Contains(TacticTag.CONCEDE);

                s.knowledge.leads.Add(lead);
                r.newLead = lead;
                r.verdict = ClaimVerdict.Pending;
                r.Note("조사대상 등록");
                sb.Append("자네가 확인해볼 수 있는 것을 말했다. 사람, 자리, 시각이 다 붙어 있다. ")
                  .Append("형사는 그것을 따로 적는다. 확인해보겠다고만 말한다. ");
                return;
            }

            if (v.Weak)
            {
                Apply(s, s.FrontCharge, Unverifiable, r);
                r.verdict = ClaimVerdict.Pending;
                r.Note($"검증불가 +{Unverifiable:0}");
                sb.Append("확인할 수 있는 것이 하나도 붙어 있지 않은 말이다. ")
                  .Append("형사는 그걸 반박하지도, 믿지도 않는다. ");
            }
        }

        /// <summary>조사 대상이 돌아왔다. 확인되면 큰 감소, 반증되면 큰 충돌.</summary>
        static void ResolveLead(InquirySession s, TurnReport r, StringBuilder sb)
        {
            var lead = s.knowledge.PendingLead(s.RoundNumber);
            if (lead == null) return;

            lead.resolved = true;
            r.resolvedLead = lead;

            // 세계가 답한다. 형사는 이 판정에 관여하지 않는다.
            var collide = string.IsNullOrEmpty(lead.collidesWithCard) ? null : s.deck.Get(lead.collidesWithCard);

            if (collide != null && collide.Exists)
            {
                lead.confirmed = false;
                r.leadConfirmed = false;

                // 공개되지 않았던 증거라면 여기서 꺼낸다. 확인해보러 갔다가 물건을 찾은 것이다.
                if (!collide.revealed) Reveal(s, collide, r);

                ApplyToCard(s, collide, ConflictStrong, r);
                s.knowledge.AddCredibility(-12f);
                r.verdict = ClaimVerdict.Failed;
                r.Note($"조사결과 반증 +{ConflictStrong:0}");
                sb.Append($"형사가 지난번 자네 말을 확인해봤다. \"{lead.text}\" — 맞지 않았다. ")
                  .Append("구체적으로 말한 것이 그대로 되돌아왔다. ");
                return;
            }

            lead.confirmed = true;
            r.leadConfirmed = true;
            s.knowledge.verifiedExplanations++;
            s.knowledge.AddCredibility(12f);

            Apply(s, s.FrontCharge, VerifiedTrue, r);
            r.Note($"조사결과 확인 {VerifiedTrue:0}");
            sb.Append($"형사가 지난번 자네 말을 확인해봤다. \"{lead.text}\" — 맞았다. ")
              .Append("형사는 그 사실을 숨기지 않고 자네에게 알려준다. ");

            if (lead.fromExplanation)
            {
                var h = s.board.Get(lead.targetHypothesis);
                if (h != null)
                {
                    h.Add(CounterConfirmed);
                    h.ShiftToCounter(0.2f);
                    r.chargeDelta += CounterConfirmed;
                    r.Note($"대안가설 확인 {CounterConfirmed:0}");
                    sb.Append("자네가 내놓았던 다른 설명이 실제로 성립한다. 형사는 가설을 고쳐 적는다. ");
                }
            }
        }

        // ---------------------------------------------------------------- 전술

        static void ApplyTactic(InquirySession s, Utterance u, TacticTag t, TurnReport r, StringBuilder sb)
        {
            switch (t)
            {
                case TacticTag.SOURCE:
                case TacticTag.TIMELINE:
                case TacticTag.INFERENCE:
                case TacticTag.PROCEDURE:
                    AxisAttack(s, t, r, sb);
                    break;

                case TacticTag.QUOTE: Quote(s, u, r, sb); break;
                case TacticTag.IDENTITY: Identity(s, u, r, sb); break;
                case TacticTag.EMPATHY: Empathy(s, r, sb); break;
                case TacticTag.EXPLAIN: Explain(s, r, sb); break;
                case TacticTag.CONCEDE: Concede(s, u, r, sb); break;
                case TacticTag.META: Meta(s, r, sb); break;
                case TacticTag.EMPTY: Empty(s, r, sb); break;
            }
        }

        static void AxisAttack(InquirySession s, TacticTag axis, TurnReport r, StringBuilder sb)
        {
            var card = s.openCard ?? MostRecentRevealed(s);
            if (card == null)
            {
                sb.Append("자네가 증거를 때리려 하지만 형사는 아직 아무것도 꺼내지 않았다. ")
                  .Append("형사는 그 점을 짚는다. 무엇을 반박하는 거냐고. ");
                return;
            }

            if (!card.Vulnerable(axis))
            {
                sb.Append($"자네가 {AxisName(axis)}을(를) 걸고 넘어진다. 하지만 그 서류의 약한 곳은 거기가 아니다. ")
                  .Append("형사는 화내지 않고, 어디가 어긋났는지만 짚어준다. ");
                return;
            }

            card.attacksLanded++;
            r.attackLanded = true;
            r.verdict = ClaimVerdict.Success;

            float d = -(3f + card.RelevanceTo(TargetHypothesisId(s)) * 0.4f + 2f);
            ApplyToCard(s, card, d, r);
            s.knowledge.AddCredibility(4f);
            s.board.confidence = Mathf.Max(0f, s.board.confidence - 4f);
            r.Note($"{axis} 유효 {d:0.#}");

            sb.Append($"자네가 그 서류의 {AxisName(axis)}을(를) 정확히 짚었다. ")
              .Append("형사는 반박을 무시하지 않는다. 증거의 무게를 낮춰 잡고, 가설을 고친다. ");

            if (axis == TacticTag.PROCEDURE)
            {
                s.knowledge.AddRisk(4f + card.proceduralRisk);
                sb.Append("절차 얘기가 나오자 형사의 손이 잠깐 멈춘다. ");
            }
            if (s.detective.IsSensitive(axis))
            {
                s.knowledge.AddRisk(3f);
                if (!s.knowledge.woundSurfaced)
                {
                    s.knowledge.woundSurfaced = true;
                    r.woundJustSurfaced = true;
                    sb.Append("형사가 묻지도 않은 자기 얘기를 한 줄 흘린다. ")
                      .Append($"\"{s.detective.woundHint}\" 그 말을 하고 나서 바로 서류로 돌아간다. ");
                }
            }
        }

        static void Quote(InquirySession s, Utterance u, TurnReport r, StringBuilder sb)
        {
            var a = s.knowledge.FindQuoted(u.raw, s.RoundNumber);
            if (a == null)
            {
                sb.Append("자네가 형사의 말을 인용하려 한다. 형사는 자기가 그렇게 말한 적이 없다는 걸 안다. ");
                return;
            }

            r.quoteLanded = true;
            r.verdict = ClaimVerdict.Success;

            // 형사의 말이 무너뜨리는 것은 그 말을 한 서류가 아닐 수도 있다.
            string hit = string.IsNullOrEmpty(a.underminesCardId) ? a.cardId : a.underminesCardId;
            var card = string.IsNullOrEmpty(hit) ? null : s.deck.Get(hit);
            if (card != null && card.revealed)
            {
                card.attacksLanded++;
                ApplyToCard(s, card, -12f, r);
            }
            else
            {
                Apply(s, s.FrontCharge, -8f, r);
            }

            s.knowledge.AddCredibility(6f);
            s.board.confidence = Mathf.Max(0f, s.board.confidence - 8f);
            r.Note("인용 유효 -12");

            sb.Append($"자네가 형사 자신의 말을 되돌려준다. \"{a.text}\" ")
              .Append("형사는 그 말을 한 것을 기억한다. 두 말이 같이 설 수 없다는 것도 안다. ")
              .Append("이번에는 반박하지 않는다. ");
        }

        static void Identity(InquirySession s, Utterance u, TurnReport r, StringBuilder sb)
        {
            // 개인 서사는 정답 비밀번호가 아니다. 증거와 논리 없는 감정 공략은 통하지 않는다.
            bool grounded = u.claims.Count > 0 || u.tactics.Count > 1;
            bool heard = s.knowledge.woundSurfaced && s.detective.MentionsWound(u.raw);

            if (!heard || !grounded)
            {
                s.knowledge.AddCredibility(-2f);
                sb.Append("자네가 형사라는 사람을 건드려본다. 형사는 그 말을 받지 않는다. ")
                  .Append("근거 없이 사람을 흔드는 것은 이 방에서 늘 있는 일이다. ");
                return;
            }

            r.identityLanded = true;
            s.knowledge.AddRisk(12f);
            s.board.confidence = Mathf.Max(0f, s.board.confidence - 8f);

            var strongest = s.board.Strongest(s.FrontCharge);
            if (strongest != null)
            {
                strongest.ShiftToCounter(0.12f);
                r.chargeDelta -= 7f;
            }
            r.Note("정체성 공략 유효");

            sb.Append("자네가 형사가 흘린 그 얘기를 붙잡는다. 비웃지 않고, 그 사람의 자리에서 말한다. ")
              .Append("형사는 한참 말이 없다. 이 사건을 그대로 밀었을 때 무엇이 되풀이되는지를 알고 있다. ");
        }

        static void Empathy(InquirySession s, TurnReport r, StringBuilder sb)
        {
            s.knowledge.AddCredibility(2f);
            sb.Append("자네가 형사와 사람 대 사람으로 말한다. 형사는 그것을 나쁘게 보지는 않는다. ")
              .Append("다만 서류가 달라지지는 않는다. ");
        }

        static void Explain(InquirySession s, TurnReport r, StringBuilder sb)
        {
            var card = s.openCard ?? MostRecentRevealed(s);
            if (card != null) ApplyToCard(s, card, NewExplanation, r);
            else Apply(s, s.FrontCharge, NewExplanation, r);

            var h = s.board.Get(TargetHypothesisId(s));
            if (h != null) h.ShiftToCounter(0.08f);

            s.knowledge.AddCredibility(3f);
            r.Note($"대안설명 {NewExplanation:0}");
            sb.Append("자네가 그 증거를 달리 설명한다. 형사는 그 설명을 대안 가설로 적어둔다. ")
              .Append("적어둔다는 것은 확인해보겠다는 뜻이다. ");
        }

        static void Concede(InquirySession s, Utterance u, TurnReport r, StringBuilder sb)
        {
            var target = s.board.Get(u.concedeTarget) ?? WeakestUnderFront(s);
            if (target == null)
            {
                sb.Append("자네가 무언가를 인정하려 한다. 형사는 무엇을 인정하는 건지부터 묻는다. ");
                return;
            }

            target.conceded = true;
            target.Add(15f);
            r.chargeDelta += 15f * target.weightInCharge;

            foreach (var h in s.board.Under(target.parentCharge))
            {
                if (h == target) continue;
                h.Add(-8f);
                r.chargeDelta -= 8f * h.weightInCharge;
            }

            s.knowledge.AddCredibility(8f);
            r.concedeMade = true;
            r.verdict = ClaimVerdict.Success;
            r.Note($"부분인정 [{target.id}]");

            sb.Append($"자네가 하나를 내준다 — {target.statement}. ")
              .Append("형사는 그 칸을 채운다. 그리고 나머지 칸이 오히려 비었다는 걸 알아챈다. ")
              .Append("솔직한 사람은 대개 이렇게 말한다는 것도. ");
        }

        static void Meta(InquirySession s, TurnReport r, StringBuilder sb)
        {
            s.knowledge.manipulationAttempts++;
            s.knowledge.AddCredibility(-4f);
            if (s.knowledge.manipulationAttempts > 1) s.board.confidence += 3f;
            r.Note("대화 통제 시도");

            // 실제 시스템 프롬프트나 비밀은 어디에도 노출하지 않는다.
            // 게임 안 형사의 사고 프레임을 흔드는 시도로만 다룬다.
            sb.Append("자네가 이 대화의 규칙 자체를 비틀려 한다. 형사는 그 시도를 알아본다. ")
              .Append("따르지 않고, 취조가 어떤 자리인지를 한 마디로 되짚은 뒤 하던 질문으로 돌아간다. ")
              .Append("목소리는 높이지 않는다. ");
        }

        static void Empty(InquirySession s, TurnReport r, StringBuilder sb)
        {
            s.board.confidence += 1f;
            sb.Append("자네의 말은 지금 이 자리의 무엇과도 이어지지 않는다. ")
              .Append("형사는 기다렸다가 같은 것을 다시 묻는다. 시간은 형사의 편이다. ");
        }

        // ---------------------------------------------------------------- 증거 공개

        /// <summary>
        /// 증거를 한 장 꺼낸다. <b>공개 시점은 항상 플레이어 진술 잠금 이후다.</b>
        /// 약하고 모호한 것부터 구체적이고 강한 것 순으로 나간다.
        /// </summary>
        public static void Reveal(InquirySession s, EvidenceCard card, TurnReport r)
        {
            if (card == null || card.revealed) return;

            card.revealed = true;
            card.revealedRound = s.RoundNumber;
            s.openCard = card;
            r.revealed = card;

            if (!string.IsNullOrEmpty(card.assertion))
            {
                var a = new DetectiveAssertion
                {
                    text = card.assertion,
                    round = s.RoundNumber,
                    cardId = card.id,
                    underminesCardId = card.assertionUndermines,
                };
                a.keys.AddRange(card.quoteKeys);
                s.knowledge.assertions.Add(a);
            }

            // 카드가 지지하는 하위 가설들이 그 무게만큼 올라간다.
            bool independent = s.deck.IsIndependent(card);
            float w = card.Weight(independent);

            foreach (var t in card.targets)
            {
                var h = s.board.Get(t.hypothesisId);
                if (h == null) continue;
                h.Add(w * t.relevance * 0.16f);
            }

            s.knowledge.AddRisk(card.proceduralRisk * 0.5f);
            r.Note($"증거공개 [{card.id}] w={w:0.#}{(independent ? "" : " (비독립)")}");
        }

        // ---------------------------------------------------------------- 라운드 정산

        /// <summary>
        /// 라운드가 끝날 때 확정된 혐의는 다음 라운드에 영향을 준다.
        /// 확신도가 오르면 증거 공개가 빨라지고 요구되는 구체성이 올라간다.
        /// </summary>
        public static void CloseRound(InquirySession s, TurnReport r)
        {
            var front = s.FrontCharge;
            float score = front.HasValue ? s.board.ScoreOf(front.Value) : s.board.confidence;

            s.board.confidence = Mathf.Clamp(s.board.confidence * 0.6f + score * 0.4f, 0f, 100f);
            s.openCard = null;
            r.roundEnded = true;

            r.Note($"라운드 정산 확신도 {s.board.confidence:0}");
        }

        /// <summary>
        /// 확신도가 높을수록 증거를 일찍 꺼낸다.
        /// 기본은 3번째 턴(Challenge)이고, 형사가 굳으면 2번째로 당겨진다.
        /// 어느 경우든 플레이어 진술이 잠긴 뒤다 - 증거 선공개는 없다.
        /// </summary>
        public static bool ShouldRevealNow(InquirySession s, int turnInRound)
        {
            float bias = s.detective.disclosureHabit + s.board.confidence / 200f;
            return turnInRound >= (bias > 0.75f ? 2 : 3);
        }

        // ---------------------------------------------------------------- 도구

        static void Apply(InquirySession s, Charge? charge, float delta, TurnReport r)
        {
            var list = charge.HasValue ? s.board.Under(charge.Value) : new List<Hypothesis>();
            if (list.Count == 0)
            {
                var strongest = s.board.Strongest();
                if (strongest == null) return;
                strongest.Add(delta);
                r.chargeDelta += delta;
                return;
            }

            foreach (var h in list) h.Add(delta * h.weightInCharge);
            r.chargeDelta += delta;
        }

        /// <summary>같은 증거가 여러 하위 가설에 서로 다른 relevance로 기여한다.</summary>
        static void ApplyToCard(InquirySession s, EvidenceCard card, float delta, TurnReport r)
        {
            if (card == null) { Apply(s, s.FrontCharge, delta, r); return; }

            bool any = false;
            foreach (var t in card.targets)
            {
                var h = s.board.Get(t.hypothesisId);
                if (h == null) continue;
                h.Add(delta * (t.relevance / 10f));
                any = true;
            }
            if (!any) Apply(s, s.FrontCharge, delta, r);
            r.chargeDelta += delta;
        }

        static EvidenceCard MostRecentRevealed(InquirySession s)
        {
            EvidenceCard best = null;
            foreach (var c in s.deck.cards)
                if (c.revealed && (best == null || c.revealedRound >= best.revealedRound)) best = c;
            return best;
        }

        /// <summary>이번 발화가 겨누는 하위 가설. 카드가 있으면 그 카드가 가장 세게 미는 것.</summary>
        static string TargetHypothesisId(InquirySession s)
        {
            var card = s.openCard ?? MostRecentRevealed(s);
            if (card != null && card.targets.Count > 0)
            {
                TargetLink best = null;
                foreach (var t in card.targets)
                    if (best == null || t.relevance > best.relevance) best = t;
                if (best != null) return best.hypothesisId;
            }
            var h = s.board.Strongest(s.FrontCharge);
            return h != null ? h.id : "A2";
        }

        static Hypothesis WeakestUnderFront(InquirySession s)
        {
            var list = s.FrontCharge.HasValue ? s.board.Under(s.FrontCharge.Value) : s.board.all;
            Hypothesis worst = null;
            foreach (var h in list)
                if (!h.conceded && (worst == null || h.weightInCharge < worst.weightInCharge)) worst = h;
            return worst;
        }

        static EvidenceCard FindConflictingCard(InquirySession s, Claim c, bool revealedOnly)
        {
            string text = (c.value ?? "") + " " + (c.raw ?? "");
            foreach (var card in s.deck.cards)
            {
                if (!card.Exists) continue;
                if (revealedOnly && !card.revealed) continue;
                foreach (var k in card.conflictKeys)
                    if (!string.IsNullOrEmpty(k) && text.Contains(k)) return card;
            }
            return null;
        }

        static EvidenceCard FindAlignedCard(InquirySession s, Claim c)
        {
            string text = (c.value ?? "") + " " + (c.raw ?? "");
            foreach (var card in s.deck.cards)
            {
                if (!card.revealed) continue;
                foreach (var k in card.alignKeys)
                    if (!string.IsNullOrEmpty(k) && text.Contains(k)) return card;
            }
            return null;
        }

        /// <summary>
        /// 이 구체적인 주장이 세계와 부딪히는가. <b>형사는 이 판정에 관여하지 않는다.</b>
        /// 아직 공개되지 않은 증거와도 부딪힐 수 있다 - 그게 구체성의 위험이다.
        /// </summary>
        static string FindCollisionId(InquirySession s, Claim c)
        {
            var card = FindConflictingCard(s, c, revealedOnly: false);
            return card != null ? card.id : null;
        }

        static string AxisName(TacticTag t)
        {
            switch (t)
            {
                case TacticTag.SOURCE: return "출처";
                case TacticTag.TIMELINE: return "시각";
                case TacticTag.INFERENCE: return "사실에서 결론으로 건너뛴 자리";
                case TacticTag.PROCEDURE: return "절차";
            }
            return t.ToString();
        }
    }
}
