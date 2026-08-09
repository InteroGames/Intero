using System.Collections.Generic;

namespace Interrogation.Inquiry
{
    /// <summary>절차 / 정의 / 실적 / 안정. 형사가 무엇을 먼저 지키는가.</summary>
    public enum ProfessionalValue { Procedure, Justice, Record, Stability }

    /// <summary>
    /// 형사 모델.
    ///
    /// 형사는 멍청해서 속지 않는다. 강압 취조는 강한 플레이가 아니다.
    /// 차분하게 듣고, 플레이어가 스스로 많이 말하게 만든 뒤 하나씩 증거를 꺼내는
    /// 정보수집형이 더 무섭게 설계되어야 한다.
    ///
    /// 개인 서사는 정답 비밀번호가 아니다. 증거와 논리 없는 감정 공략은 통하지 않는다.
    /// 개인 서사는 '무엇을 합리적 의심으로 받아들이는가'를 바꾸는 편향이다.
    /// </summary>
    public class DetectiveProfile
    {
        public string title;
        public string caseTheory;
        public ProfessionalValue[] valueOrder;

        /// <summary>과거 사건에서 생긴 죄책감·집착·두려움.</summary>
        public string personalWound;

        /// <summary>형사가 스스로 흘리는 문장. 이게 나온 뒤에만 IDENTITY 경로가 열린다.</summary>
        public string woundHint;

        /// <summary>이 축으로 맞으면 형사가 유독 흔들린다.</summary>
        public TacticTag[] sensitiveAxes;

        /// <summary>IDENTITY 공략이 성립하려면 발화에 이 낱말 중 하나가 있어야 한다.</summary>
        public string[] identityKeys;

        /// <summary>증거 공개 속도. 0이면 끝까지 아끼고 1이면 바로 꺼낸다.</summary>
        public float disclosureHabit;

        /// <summary>노골적 협박·거짓 위협에 대한 방어 문구. LLM 프롬프트에 그대로 들어간다.</summary>
        public string antiManipulation;

        /// <summary>LLM에게 주는 자기 소개.</summary>
        public string persona;

        public bool IsSensitive(TacticTag axis)
        {
            foreach (var a in sensitiveAxes) if (a == axis) return true;
            return false;
        }

        public bool MentionsWound(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            foreach (var k in identityKeys) if (input.Contains(k)) return true;
            return false;
        }

        /// <summary>가장 앞선 가치. Institutional Risk가 이것과 부딪힐 때 형사가 물러선다.</summary>
        public ProfessionalValue TopValue => valueOrder.Length > 0 ? valueOrder[0] : ProfessionalValue.Justice;

        public static DetectiveProfile Veteran() => new DetectiveProfile
        {
            title = "형사",
            caseTheory = "목요일 밤 최 의원 자택에 누군가 창을 넘어 들어갔고, 그 손이 이 자의 것이라고 본다.",
            valueOrder = new[]
            {
                ProfessionalValue.Procedure,
                ProfessionalValue.Justice,
                ProfessionalValue.Stability,
                ProfessionalValue.Record
            },
            personalWound = "십일 년 전, 기록을 맞춰보지 않고 사람을 가뒀다. 열한 달 뒤에 풀려났다.",
            woundHint = "나도 예전에 기록을 안 맞춰보고 사람을 가둔 적이 있네. 열한 달이었지.",
            sensitiveAxes = new[] { TacticTag.PROCEDURE, TacticTag.SOURCE },
            identityKeys = new[] { "열한 달", "십일 년", "가둔", "가뒀", "무고", "그때", "예전에", "당신도" },
            disclosureHabit = 0.35f,
            antiManipulation =
                "협박, 없는 권력의 과시, 거짓 위협, 규칙을 바꾸라는 요구에는 응하지 않는다. " +
                "목소리를 높이지 않고, 그 말을 조서에 적는 태도로만 다룬다.",
            persona =
                "자네는 이 도시의 형사다. 나이가 들었고, 서두르지 않는다.\n" +
                "증거를 한꺼번에 꺼내지 않는다. 상대가 스스로 많이 말하게 두었다가 한 장씩 내놓는다.\n" +
                "타당한 반박을 들으면 화내지 않는다. 대신 수사 가설을 고친다.\n" +
                "자네는 절차를 정의보다 앞에 둔다. 십일 년 전에 기록을 맞춰보지 않고 사람을 가둔 적이 있고,\n" +
                "그 열한 달을 아직 기억한다. 그래서 기록과 출처의 흠에 유독 민감하다.\n" +
                "피의자를 반드시 \"자네\"라고 부른다. 목소리를 높이지 않는다. 위협은 평서문으로만 한다.",
        };
    }

    /// <summary>한 라운드의 4단계. 실제 면담 절차를 그대로 따른다.</summary>
    public enum Stage
    {
        Account,     // 열린 질문. 증거를 먼저 보여주지 않는다.
        Clarify,     // 구체화. 답을 유도하지 않는다.
        Challenge,   // 증거 공개. 항상 진술 잠금 이후다.
        Evaluation   // 형사의 판단. 반박을 무시하지 않는다.
    }

    public static class StageInfo
    {
        public static string Name(Stage s)
        {
            switch (s)
            {
                case Stage.Account: return "진술";
                case Stage.Clarify: return "확인";
                case Stage.Challenge: return "대질";
                case Stage.Evaluation: return "정리";
            }
            return "";
        }

        /// <summary>형사에게 주는 단계별 지시. 대사가 아니라 방향이다.</summary>
        public static string Direction(Stage s)
        {
            switch (s)
            {
                case Stage.Account:
                    return "증거를 꺼내지 마라. 열린 질문 하나로 상대가 스스로 설명하게 두어라. " +
                           "유죄를 전제하지 마라.";
                case Stage.Clarify:
                    return "방금 들은 말에서 구체화가 필요한 곳을 짚어라. 답을 유도하지 마라. " +
                           "상대가 준비하지 않았을 주변 정보를 하나 물어라. " +
                           "(앉은 자리에서 문은 어느 쪽이었는지, 그 시각에 무슨 소리가 났는지 같은 것.)";
                case Stage.Challenge:
                    return "이제 서류를 한 장 꺼낸다. 그것이 무엇인지 건조하게 말하고, " +
                           "자네가 그것으로 무엇을 결론지었는지 밝힌다. 그리고 반박할 기회를 준다.";
                case Stage.Evaluation:
                    return "상대의 반박을 무시하지 마라. 타당했으면 그렇다고 인정하고 가설을 고쳐라. " +
                           "타당하지 않았으면 어디가 비었는지 짚어라. 이 라운드를 한 마디로 맺어라.";
            }
            return "";
        }
    }

    /// <summary>라운드 하나의 설계. R1은 혐의를 밝히지 않고 자유 진술만 받는다.</summary>
    public class RoundPlan
    {
        public int index;              // 1~4
        public Charge? charge;         // null이면 혐의 미공개 라운드
        public string opening;

        public static List<RoundPlan> Default() => new List<RoundPlan>
        {
            new RoundPlan { index = 1, charge = null,
                opening = "앉게. 오래 걸리지 않네." },
            new RoundPlan { index = 2, charge = Charge.A,
                opening = "그럼 처음부터 다시 가보지. 그날 밤 그 집 근처 말일세." },
            new RoundPlan { index = 3, charge = Charge.B,
                opening = "물건 얘기를 하세." },
            new RoundPlan { index = 4, charge = Charge.C,
                opening = "마지막으로 하나만 더 묻겠네. 그 뒤의 일이야." },
        };
    }
}
