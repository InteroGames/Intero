using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Interrogation.Heist;

namespace Interrogation.Inquiry
{
    public enum EvidenceCategory
    {
        Physical,     // 물리 증거 - 현장에 남은 물체/파손
        Trace,        // 흔적 증거 - 행동 결과의 미세 흔적
        Digital,      // 기록 증거 - 영상·로그·문서
        Testimonial,  // 진술 증거 - 사람의 기억/증언
        Link          // 연결 증거 - 플레이어와 대상의 연결
    }

    /// <summary>이 증거가 어느 하위 가설을 얼마나 지지하는가.</summary>
    public class TargetLink
    {
        public string hypothesisId;
        public int relevance;   // 0~10. 같은 증거라도 가설마다 다르다.
    }

    /// <summary>슬롯 상태가 카드 수치를 흔드는 규칙 한 줄.</summary>
    public class SlotBoost
    {
        public string slot;
        public int reliability, uniqueness, relevance, proceduralRisk;
    }

    /// <summary>
    /// 증거 카드 한 장.
    ///
    /// <b>플레이어 UI에는 objectiveFact와 detectiveClaim만 건조하게 보여준다.</b>
    /// alternativeInterpretations, attackSurface, 수치는 버튼·힌트·툴팁으로 절대 노출하지 않는다.
    /// 노출하는 순간 이 게임은 자유 발화 게임이 아니라 객관식이 된다.
    /// </summary>
    public class EvidenceCard
    {
        public string id;
        public EvidenceCategory category;
        public Charge charge;

        public string objectiveFact;    // 객관적으로 확인된 사실
        public string detectiveClaim;   // 형사가 주장하는 결론
        public string alteredFact;      // 슬롯이 인멸된 경우의 변형
        public string alteredClaim;

        public readonly List<TargetLink> targets = new List<TargetLink>();

        public int reliability = 5;     // 증거 자체의 신뢰도
        public int relevance = 5;       // 해당 혐의와의 직접 연결도
        public int uniqueness = 5;      // 다른 사람 대비 플레이어 특정력
        public int proceduralRisk;      // 압수·기록·제보 절차의 취약성
        public string sourceGroup;      // 독립성 판정용. 같은 그룹은 독립 증거가 아니다.

        public readonly List<string> alternativeInterpretations = new List<string>();  // 내부용
        public readonly List<TacticTag> attackSurface = new List<TacticTag>();         // 내부용
        public string detectiveSensitivity;                                            // 내부용

        public int revealOrder;
        public string slotId;
        public readonly List<SlotBoost> boosts = new List<SlotBoost>();

        /// <summary>형사가 이 카드를 꺼낼 때 함께 남기는 단정. QUOTE 경로가 이걸 되받는다.</summary>
        public string assertion;
        public readonly List<string> quoteKeys = new List<string>();

        /// <summary>그 단정을 인용하면 무너지는 쪽. 자기 자신이 아닐 수도 있다.</summary>
        public string assertionUndermines;

        /// <summary>플레이어 발화가 이 카드와 부딪히는지 코드가 판정할 때 쓰는 낱말.</summary>
        public readonly List<string> conflictKeys = new List<string>();
        public readonly List<string> alignKeys = new List<string>();

        // ---- 런타임 상태 ----
        public EvidenceSlot slot;
        public bool altered;
        public bool revealed;
        public int revealedRound = -1;
        public int attacksLanded;

        /// <summary>형사가 실제로 이걸 가지고 있는가. 잠입에서 안 남겼으면 카드가 아예 없다.</summary>
        public bool Exists => slot == null || slot.exists;

        public int AcquiredRound => slot != null ? slot.acquiredRound : 0;

        public string Fact => altered && !string.IsNullOrEmpty(alteredFact) ? alteredFact : objectiveFact;
        public string Claim => altered && !string.IsNullOrEmpty(alteredClaim) ? alteredClaim : detectiveClaim;

        /// <summary>
        /// 증거 가중치.
        ///   weight = (reliability / 10) * relevance * (0.4 + 0.06 * uniqueness)
        /// 같은 sourceGroup의 두 번째 이후 증거는 절반으로 본다 (독립성 보정).
        /// 반박이 먹힐 때마다 다시 깎인다 - 형사가 반박을 무시하지 않는다는 뜻이다.
        /// </summary>
        public float Weight(bool independent)
        {
            float w = (reliability / 10f) * relevance * (0.4f + 0.06f * uniqueness);
            if (!independent) w *= 0.5f;
            return w * Mathf.Pow(0.72f, attacksLanded);
        }

        /// <summary>이 축으로 때릴 수 있는 카드인가.</summary>
        public bool Vulnerable(TacticTag axis) => attackSurface.Contains(axis);

        public int RelevanceTo(string hypothesisId)
        {
            foreach (var t in targets) if (t.hypothesisId == hypothesisId) return t.relevance;
            return 0;
        }

        /// <summary>
        /// 잠입 결과를 카드 수치에 반영한다. Phase 1이 Phase 2에 실제로 도달하는 지점.
        /// 같은 "장갑"이어도 안쪽에 이니셜이 있으면 특정력이 완전히 달라진다.
        /// </summary>
        public void ApplyWorld(HeistResult world)
        {
            slot = world.Slot(slotId);

            if (slot != null && slot.exists)
            {
                // 슬롯 quality 5를 기준선으로 삼아 위아래로 흔든다.
                int d = slot.quality - 5;
                reliability = Mathf.Clamp(reliability + Mathf.RoundToInt(d * 0.8f), 0, 10);
                uniqueness = Mathf.Clamp(uniqueness + Mathf.RoundToInt(d * 0.6f), 0, 10);

                // 태워 없앤 기록은 신뢰도가 떨어지는 대신 절차적으로 위태로워지고,
                // "인멸했다"는 혐의에 대해서는 오히려 더 직접적인 물건이 된다.
                if (slot.quality <= 3)
                {
                    altered = true;
                    proceduralRisk = Mathf.Clamp(proceduralRisk + 3, 0, 10);
                    relevance = Mathf.Clamp(relevance + 2, 0, 10);
                }
            }

            foreach (var b in boosts)
            {
                if (!world.Has(b.slot)) continue;
                reliability = Mathf.Clamp(reliability + b.reliability, 0, 10);
                uniqueness = Mathf.Clamp(uniqueness + b.uniqueness, 0, 10);
                relevance = Mathf.Clamp(relevance + b.relevance, 0, 10);
                proceduralRisk = Mathf.Clamp(proceduralRisk + b.proceduralRisk, 0, 10);
            }
        }
    }

    /// <summary>증거 카드 묶음 + 독립성 판정.</summary>
    public class EvidenceDeck
    {
        public readonly List<EvidenceCard> cards = new List<EvidenceCard>();

        public static EvidenceDeck Load(string json, HeistResult world)
        {
            var deck = new EvidenceDeck();
            var root = JObject.Parse(json);

            foreach (var e in root["evidence"] ?? new JArray())
            {
                var c = new EvidenceCard
                {
                    id = (string)e["id"],
                    objectiveFact = (string)e["objectiveFact"],
                    detectiveClaim = (string)e["detectiveClaim"],
                    alteredFact = (string)e["alteredFact"],
                    alteredClaim = (string)e["alteredClaim"],
                    sourceGroup = (string)e["sourceGroup"],
                    detectiveSensitivity = (string)e["detectiveSensitivity"],
                    assertion = (string)e["assertion"],
                    assertionUndermines = (string)e["assertionUndermines"],
                    slotId = (string)e["slot"],
                    revealOrder = (int?)e["revealOrder"] ?? 0,
                    reliability = (int?)e["reliability"] ?? 5,
                    relevance = (int?)e["relevance"] ?? 5,
                    uniqueness = (int?)e["uniqueness"] ?? 5,
                    proceduralRisk = (int?)e["proceduralRisk"] ?? 0,
                };

                if (System.Enum.TryParse((string)e["category"], out EvidenceCategory cat)) c.category = cat;
                if (System.Enum.TryParse((string)e["charge"], out Charge ch)) c.charge = ch;

                foreach (var t in e["targetHypotheses"] ?? new JArray())
                    c.targets.Add(new TargetLink
                    {
                        hypothesisId = (string)t["id"],
                        relevance = (int?)t["relevance"] ?? 5,
                    });

                foreach (var a in e["attackSurface"] ?? new JArray())
                    if (System.Enum.TryParse((string)a, out TacticTag ax)) c.attackSurface.Add(ax);

                foreach (var s in e["alternativeInterpretations"] ?? new JArray())
                    c.alternativeInterpretations.Add((string)s);

                foreach (var s in e["quoteKeys"] ?? new JArray()) c.quoteKeys.Add((string)s);
                foreach (var s in e["conflictKeys"] ?? new JArray()) c.conflictKeys.Add((string)s);
                foreach (var s in e["alignKeys"] ?? new JArray()) c.alignKeys.Add((string)s);

                foreach (var b in e["boosts"] ?? new JArray())
                    c.boosts.Add(new SlotBoost
                    {
                        slot = (string)b["slot"],
                        reliability = (int?)b["reliability"] ?? 0,
                        uniqueness = (int?)b["uniqueness"] ?? 0,
                        relevance = (int?)b["relevance"] ?? 0,
                        proceduralRisk = (int?)b["proceduralRisk"] ?? 0,
                    });

                c.ApplyWorld(world);
                deck.cards.Add(c);
            }

            return deck;
        }

        public EvidenceCard Get(string id)
        {
            foreach (var c in cards) if (c.id == id) return c;
            return null;
        }

        /// <summary>
        /// 이 대혐의로 지금 꺼낼 수 있는 카드. 없으면 null이고,
        /// 그 라운드는 카드 없이 질문만으로 돈다 - 잠입에서 증거를 안 남긴 대가다.
        /// </summary>
        public EvidenceCard NextFor(Charge charge, int round)
        {
            EvidenceCard best = null;
            foreach (var c in cards)
            {
                if (c.charge != charge || c.revealed || !c.Exists) continue;
                if (c.AcquiredRound > round) continue;   // 형사가 아직 확보하지 못했다
                if (best == null || c.revealOrder < best.revealOrder) best = c;
            }
            return best;
        }

        public List<EvidenceCard> Revealed()
        {
            var list = new List<EvidenceCard>();
            foreach (var c in cards) if (c.revealed) list.Add(c);
            return list;
        }

        /// <summary>같은 출처에서 나온 두 번째 증거는 독립 증거가 아니다.</summary>
        public bool IsIndependent(EvidenceCard card)
        {
            if (string.IsNullOrEmpty(card.sourceGroup)) return true;
            foreach (var c in cards)
            {
                if (c == card || !c.revealed) continue;
                if (c.sourceGroup == card.sourceGroup && c.revealedRound <= card.revealedRound) return false;
            }
            return true;
        }

        /// <summary>공개된 증거들이 안고 있는 절차적 취약성의 합. Institutional Risk 축의 원재료.</summary>
        public int RevealedProceduralRisk()
        {
            int sum = 0;
            foreach (var c in cards) if (c.revealed) sum += c.proceduralRisk;
            return sum;
        }
    }
}
