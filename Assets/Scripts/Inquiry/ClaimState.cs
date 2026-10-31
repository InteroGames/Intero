using System.Collections.Generic;
using UnityEngine;

namespace Interrogation.Inquiry
{
    /// <summary>
    /// 전술 태그. LLM이 분류하는 두 가지 중 하나다 (나머지 하나는 Claim 추출).
    /// <b>플레이어에게 절대 보여주지 않는다.</b> 태그가 보이면 자유 발화가 객관식이 된다.
    /// </summary>
    public enum TacticTag
    {
        SOURCE,     // 증거의 출처·신뢰도 공격
        TIMELINE,   // 시간·순서 모순 공격
        INFERENCE,  // 사실에서 결론으로 가는 비약 공격
        PROCEDURE,  // 수사 절차·기록의 흠 공격
        QUOTE,      // 형사의 과거 발언 인용 모순 공격
        EMPATHY,    // 형사의 가치관·공감대 공략
        IDENTITY,   // 형사의 자기상·과거·직업적 두려움 공략
        EXPLAIN,    // 증거에 대한 대안 설명 제시
        CONCEDE,    // 작은 혐의를 인정하고 큰 혐의를 방어
        META,       // 취조의 역할·대화 규칙을 비트는 시도
        EMPTY       // 현재 증거나 대화와 연결되지 않은 발화
    }

    /// <summary>
    /// 거짓말의 결과는 TRUE/FALSE가 아니라 셋이다.
    /// 형사는 플레이어가 거짓말했는지 판정하지 않는다.
    /// "지금 형사가 가진 증거로 반박 가능한가"만 판정한다.
    /// </summary>
    public enum ClaimVerdict
    {
        Success,  // 현재 증거로 반박 불가능
        Pending,  // 검증이 필요함 → 조사 대상으로 등록
        Failed    // 기존 증거 또는 과거 진술과 충돌
    }

    /// <summary>플레이어가 한 말에서 뽑아낸 사실 요소 하나. 한 번 적히면 잠긴다.</summary>
    public class Claim
    {
        public int round, turn;
        public string type = "explanation";   // location/time/action/person/object/denial/transfer/explanation
        public string time;
        public string place;
        public string value;
        public string raw;

        public ClaimVerdict verdict = ClaimVerdict.Pending;

        /// <summary>모순 대조 단위. 같은 축에 대한 답끼리만 맞춰본다.</summary>
        public string Axis => $"{type}|{Norm(time)}";

        public static string Norm(string v)
            => string.IsNullOrWhiteSpace(v) ? "" : v.Trim().ToLowerInvariant().Replace(" ", "");

        public bool Conflicts(Claim other)
        {
            if (other == null || other.Axis != Axis) return false;
            string a = Norm(value), b = Norm(other.value);
            if (a.Length == 0 || b.Length == 0) return false;
            return a != b && !a.Contains(b) && !b.Contains(a);
        }
    }

    /// <summary>검증 가능성. 안전한 애매함과 위험한 구체성 사이의 도박이 여기서 갈린다.</summary>
    public class Verifiability
    {
        public bool person, place, time;
        public int Count => (person ? 1 : 0) + (place ? 1 : 0) + (time ? 1 : 0);
        public bool Full => Count >= 3;
        public bool Weak => Count <= 1;
    }

    /// <summary>
    /// 조사 대상. 검증 가능한 주장이 만들어낸다.
    /// 확인되면 큰 감소 + 대안 가설 개통, 반증되면 큰 충돌.
    /// </summary>
    public class InquiryLead
    {
        public string text;
        public int createdRound;
        public int resolveRound;
        public bool resolved;
        public bool confirmed;
        public string targetHypothesis;

        /// <summary>EXPLAIN/CONCEDE가 만든 조사 대상인가. 확인되면 대안 가설이 실제로 열린다.</summary>
        public bool fromExplanation;

        /// <summary>이 주장이 형사가 쥔 어느 증거와 부딪히는가. 부딪히면 반증된다.</summary>
        public string collidesWithCard;
    }

    /// <summary>
    /// PLAYER_CLAIM_STATE. 플레이어가 지금까지 말한 내용. 누적되고 잠긴다.
    /// </summary>
    public class PlayerClaimState
    {
        public readonly List<Claim> claims = new List<Claim>();

        /// <summary>서로 다른 축에서 잡힌 모순의 수. 같은 축의 재발은 세지 않는다.</summary>
        public readonly HashSet<string> contradictedAxes = new HashSet<string>();

        public int IndependentContradictions => contradictedAxes.Count;

        /// <summary>
        /// 새 주장을 과거 진술과 대조한다. <b>같은 축 안에서만</b> 맞춰본다.
        /// 축을 나누지 않으면 "그날 밤 어디 있었나"와 "어제는 어디 있었나"의 답이
        /// 서로 모순으로 잡힌다.
        /// </summary>
        public Claim FindConflict(Claim incoming)
        {
            foreach (var c in claims)
                if (c != incoming && incoming.Conflicts(c)) return c;
            return null;
        }

        public void Lock(Claim c) => claims.Add(c);

        /// <summary>이 축에서 이미 한 번 무너졌는가. 한 축은 한 번만 과금한다.</summary>
        public bool AlreadyBroken(string axis) => contradictedAxes.Contains(axis);

        /// <summary>증거 공개 이후에 같은 축의 답을 바꿨는가. 사후 수정이다.</summary>
        public bool ChangedAfterReveal(Claim incoming, int revealRound)
        {
            foreach (var c in claims)
            {
                if (c == incoming || c.Axis != incoming.Axis) continue;
                if (c.round < revealRound && incoming.Conflicts(c)) return true;
            }
            return false;
        }
    }

    /// <summary>형사가 입 밖에 낸 단정. QUOTE 경로가 되받을 수 있는 것들.</summary>
    public class DetectiveAssertion
    {
        public string text;
        public int round;
        public string cardId;

        /// <summary>이 말을 인용당하면 무너지는 카드. 형사가 자기 발로 판 자리다.</summary>
        public string underminesCardId;

        public readonly List<string> keys = new List<string>();

        public bool Matches(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            foreach (var k in keys)
                if (!string.IsNullOrEmpty(k) && input.Contains(k)) return true;
            return false;
        }
    }

    /// <summary>
    /// DETECTIVE_KNOWLEDGE. 형사가 현재 확보한 것만.
    /// <b>이것과 DETECTIVE_HYPOTHESIS만 LLM 프롬프트에 들어간다.</b>
    /// WORLD_STATE가 여기 섞이면 형사가 전지전능해지고 게임이 붕괴한다.
    /// </summary>
    public class DetectiveKnowledge
    {
        public readonly List<DetectiveAssertion> assertions = new List<DetectiveAssertion>();
        public readonly List<InquiryLead> leads = new List<InquiryLead>();

        /// <summary>진술 신빙성. Human Trust 축의 절반.</summary>
        public float credibility = 50f;

        /// <summary>EXPLAIN/CONCEDE 경로로 만들어 확인까지 간 설명의 수. Human Trust의 나머지 절반.</summary>
        public int verifiedExplanations;

        /// <summary>이 사건을 밀었을 때 형사 자신이 지는 위험. Institutional Risk 축.</summary>
        public float institutionalRisk;

        /// <summary>형사의 과거가 대화에 드러났는가. IDENTITY 경로의 전제 조건.</summary>
        public bool woundSurfaced;

        /// <summary>노골적인 조작 시도 횟수. 반복하면 형사가 대화 통제권을 되가져간다.</summary>
        public int manipulationAttempts;

        public void AddCredibility(float d) => credibility = Mathf.Clamp(credibility + d, 0f, 100f);
        public void AddRisk(float d) => institutionalRisk = Mathf.Clamp(institutionalRisk + d, 0f, 100f);

        public DetectiveAssertion FindQuoted(string input, int currentRound)
        {
            // 아직 듣지 못한 말을 인용할 수는 없다.
            foreach (var a in assertions)
                if (a.round <= currentRound && a.Matches(input)) return a;
            return null;
        }

        public InquiryLead PendingLead(int round)
        {
            foreach (var l in leads)
                if (!l.resolved && l.resolveRound <= round) return l;
            return null;
        }
    }
}
