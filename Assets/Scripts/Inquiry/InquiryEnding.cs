using UnityEngine;

namespace Interrogation.Inquiry
{
    public enum InquiryEnding
    {
        None,
        Released,      // 석방
        HeldPending,   // 보류 - 나가긴 하지만 사건은 닫히지 않는다
        Charged        // 기소
    }

    /// <summary>
    /// 승리는 단순 호감도나 의심도 0이 아니다.
    /// 세 축 중 <b>둘 이상</b>을 달성하면 석방 또는 보류 승리다.
    /// </summary>
    public class VictoryAxes
    {
        /// <summary>증거만으로 기소하기 어렵다. (대혐의 3개 중 2개 이상이 40 미만)</summary>
        public bool caseDoubt;

        /// <summary>형사가 설명을 인간적으로 신뢰한다. (신빙성 + EXPLAIN/CONCEDE 경로의 검증 성공)</summary>
        public bool humanTrust;

        /// <summary>이 사건을 밀면 형사 자신 또는 조직이 위험하다.</summary>
        public bool institutionalRisk;

        public int Count => (caseDoubt ? 1 : 0) + (humanTrust ? 1 : 0) + (institutionalRisk ? 1 : 0);

        public const float DoubtThreshold = 40f;
        public const float TrustCredibility = 65f;
        public const float RiskThreshold = 45f;

        public static VictoryAxes Evaluate(InquirySession s)
        {
            var v = new VictoryAxes
            {
                caseDoubt = s.board.ChargesBelow(DoubtThreshold) >= 2,
                humanTrust = s.knowledge.credibility >= TrustCredibility
                          && s.knowledge.verifiedExplanations >= 1,
            };

            // 절차를 정의보다 앞에 두는 형사에게는 절차적 위험이 더 무겁게 걸린다.
            float risk = s.knowledge.institutionalRisk
                       * (s.detective.TopValue == ProfessionalValue.Procedure ? 1.15f : 1f);
            v.institutionalRisk = risk >= RiskThreshold;

            return v;
        }

        public string Describe()
        {
            return $"기소 곤란 {(caseDoubt ? "○" : "×")}   " +
                   $"인간적 신뢰 {(humanTrust ? "○" : "×")}   " +
                   $"수사 위험 {(institutionalRisk ? "○" : "×")}";
        }
    }

    public static class EndingResolver
    {
        public static InquiryEnding Resolve(InquirySession s, out Charge topCharge, out VictoryAxes axes)
        {
            axes = VictoryAxes.Evaluate(s);

            topCharge = Charge.A;
            float top = -1f;
            foreach (Charge c in new[] { Charge.A, Charge.B, Charge.C })
            {
                float sc = s.board.ScoreOf(c);
                if (sc > top) { top = sc; topCharge = c; }
            }

            if (axes.Count >= 2) return top < 60f ? InquiryEnding.Released : InquiryEnding.HeldPending;
            return top >= 70f ? InquiryEnding.Charged : InquiryEnding.HeldPending;
        }

        /// <summary>
        /// Phase 1 성과와 Phase 2 성과가 조합되어 엔딩이 갈린다.
        /// 증거를 많이 지운 플레이는 취조가 쉬워지지만 은폐 혐의가 커진다 -
        /// 그 트레이드오프가 마지막 화면에서 명확히 보여야 한다.
        /// </summary>
        public static string Epilogue(InquirySession s, InquiryEnding ending, Charge top, VictoryAxes axes)
        {
            var w = s.world;
            string head;

            switch (ending)
            {
                case InquiryEnding.Released:
                    head = "형사가 서류를 덮는다.\n문이 열린다.";
                    break;
                case InquiryEnding.HeldPending:
                    head = "형사가 서류를 덮지 않는다.\n오늘은 여기까지라고만 한다.";
                    break;
                default:
                    head = $"형사가 서류 맨 위에 한 줄을 적는다.\n{ChargeInfo.Name(top)}.";
                    break;
            }

            string body = "";

            if (w.caught)
                body += "\n\n애초에 그 집에서 걸어 나오지 못했다. 형사는 그것을 한 번도 입에 담지 않았다.";
            else if (w.stolen < HeistDirectorConstants.TargetCount)
                body += $"\n\n손에 쥔 것은 {w.stolen}개뿐이었다. 그날 밤 무엇을 두고 나왔는지는 자네만 안다.";

            if (w.concealed)
                body += ending == InquiryEnding.Charged
                    ? "\n\n장부를 태운 것이 결국 무게를 더했다. 지운 자리는 지운 티가 난다."
                    : "\n\n장부를 태운 덕에 물어올 것이 적었다. 대신 태웠다는 사실 하나가 끝까지 남았다.";

            if (w.route == "wall")
                body += "\n\n담을 넘느라 버린 자루는 아직 그 집 뒷마당에 있을 것이다.";

            body += $"\n\n{axes.Describe()}";
            return head + body;
        }
    }

    /// <summary>Heist 어셈블리를 참조하지 않고 목표 개수만 쓰기 위한 상수.</summary>
    public static class HeistDirectorConstants
    {
        public const int TargetCount = 3;
    }
}
