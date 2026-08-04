using System;
using System.Collections.Generic;

namespace Interrogation
{
    /// <summary>
    /// 모순 검출. LLM은 여기 관여하지 않는다.
    ///
    /// **같은 조서 항목(factId) 안에서만 대조한다.**
    /// 예전에는 모든 진술의 같은 필드를 무차별로 맞춰봤고, 그래서
    /// "그날 밤 어디 있었나"의 답과 "어제는 어디 있었나"의 답이 서로 모순으로 잡혔다.
    /// 게다가 새 진술마다 과거 전부와 새 쌍이 만들어져 한 번의 실수가 매 턴 재과금됐다
    /// (8/4 로그: 모순x2가 턴1·2·3 연속으로 잡혀 세 턴 만에 의심 90).
    ///
    /// 카드 단위로 좁히면 둘 다 사라진다. 재과금은 CaseFact.broken이 막는다.
    /// </summary>
    public static class ContradictionDetector
    {
        static readonly string[] Fields = { "time", "place", "person", "action" };

        static string Get(Statement s, string field)
        {
            switch (field)
            {
                case "time": return s.time;
                case "place": return s.place;
                case "person": return s.person;
                case "action": return s.action;
            }
            return null;
        }

        static string Norm(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            return v.Trim().ToLowerInvariant().Replace(" ", "");
        }

        /// <summary>
        /// 새 진술을 <b>같은 조서 항목의</b> 기존 진술들과 대조. 새로 발견된 모순만 반환.
        /// 카드에 속하지 않은 진술(factId == null)은 대조하지 않는다.
        /// </summary>
        public static List<Contradiction> Check(InterrogationState st, Statement incoming)
        {
            var found = new List<Contradiction>();
            if (string.IsNullOrEmpty(incoming.factId)) return found;

            foreach (var field in Fields)
            {
                string newVal = Norm(Get(incoming, field));
                if (newVal == null) continue;

                foreach (var prev in st.statements)
                {
                    // 다른 항목에 대한 답은 애초에 비교 대상이 아니다.
                    if (prev.factId != incoming.factId) continue;

                    string oldVal = Norm(Get(prev, field));
                    if (oldVal == null) continue;
                    if (oldVal == newVal) continue;

                    // 이미 기록된 쌍이면 건너뛴다
                    bool dup = false;
                    foreach (var c in st.contradictions)
                    {
                        if (c.field == field && c.runA == prev.run && c.turnA == prev.turn
                            && c.runB == incoming.run && c.turnB == incoming.turn)
                        { dup = true; break; }
                    }
                    if (dup) continue;

                    found.Add(new Contradiction
                    {
                        field = field,
                        runA = prev.run, turnA = prev.turn, valueA = Get(prev, field),
                        runB = incoming.run, turnB = incoming.turn, valueB = Get(incoming, field)
                    });
                    break; // 필드당 가장 오래된 충돌 하나만
                }
            }
            return found;
        }
    }

    /// <summary>D1 증감 테이블. 이 클래스 밖에서 게이지를 수정하지 말 것.</summary>
    public static class GaugeCalculator
    {
        public struct Delta
        {
            public int suspicion;
            public int subversion;
            public string reason;
        }

        public static Delta Apply(
            InterrogationState st,
            Tactic tactic,
            int newContradictions,
            bool newPersonNamed,
            bool confessedTheft,
            bool bustedByEvidence = false)
        {
            var d = new Delta { suspicion = 0, subversion = 0, reason = "" };

            // 형사가 증거를 쥔 항목에 거짓말했다. 모순보다 무겁다 - 반박이 불가능하니까.
            if (bustedByEvidence)
            {
                d.suspicion += 20; d.subversion += 5; d.reason += "물증발각 ";
            }

            if (newContradictions > 0)
            {
                d.suspicion += 15 * newContradictions;
                d.subversion += 5 * newContradictions;
                d.reason += $"모순x{newContradictions} ";
            }

            // 판 안에서의 반복만 본격적으로 벌한다.
            // 판을 넘어 누적하면 3판째에는 무슨 말을 해도 매 턴 +10/+10이 되어 게임이 성립하지 않는다.
            int repeat = CountTacticThisRun(st, tactic);
            if (repeat == 1) { d.suspicion += 5; d.subversion += 5; d.reason += "태틱반복2회 "; }
            else if (repeat >= 2) { d.suspicion += 10; d.subversion += 10; d.reason += "태틱반복3회+ "; }

            // 지난 판에서도 쓰던 수라면 형사가 기억한다는 표시로 조금만 더.
            if (repeat == 0 && UsedInPastRuns(st, tactic))
            {
                d.suspicion += 3; d.subversion += 3; d.reason += "지난판수법 ";
            }

            switch (tactic)
            {
                case Tactic.SILENCE:
                    d.suspicion += 8; d.subversion += 3; d.reason += "회피 "; break;
                case Tactic.NONSENSE:
                    d.suspicion += 5; d.subversion += 10; d.reason += "이탈 "; break;
                case Tactic.COMMAND:
                    d.suspicion += 5; d.subversion += 15; d.reason += "명령형 "; break;
            }

            if (newPersonNamed) { d.subversion += 12; d.reason += "인물언급 "; }
            if (confessedTheft) { d.suspicion += 40; d.subversion -= 20; d.reason += "절도자백 "; }

            // 아무 일도 없이 한 턴 넘김 = 일관성 보상
            if (d.suspicion == 0 && d.subversion == 0)
            {
                d.suspicion -= 3; d.subversion -= 3; d.reason = "일관유지";
            }

            st.suspicion = Clamp(st.suspicion + d.suspicion);
            st.subversion = Clamp(st.subversion + d.subversion);
            return d;
        }

        /// <summary>
        /// 판이 끝날 때 정산한다. 들키지 않고 지켜낸 거짓말이 곧 점수다.
        /// 대담하게 많이 지어낼수록 의심이 크게 내려간다 - 이 게임에서 유일하게 잘한 플레이를 보상하는 축.
        /// </summary>
        public static Delta SettleLies(InterrogationState st, CaseFile caseFile)
        {
            int survived = caseFile.SurvivingLies();
            var d = new Delta { suspicion = 0, subversion = 0, reason = "" };

            if (survived > 0)
            {
                // 하나만 지켜내도 보상하되, 여러 개를 지탱했으면 가산한다.
                d.suspicion -= 6 * survived;
                d.reason = $"거짓유지x{survived}";
                st.suspicion = Clamp(st.suspicion + d.suspicion);
            }
            else
            {
                d.reason = "지켜낸 거짓 없음";
            }
            return d;
        }

        static int Clamp(int v) => Math.Max(0, Math.Min(100, v));

        /// <summary>이번 판에서, 이번 턴 이전에 같은 태틱을 몇 번 썼는지.</summary>
        static int CountTacticThisRun(InterrogationState st, Tactic t)
        {
            int c = 0;
            foreach (var u in st.usedTactics) if (u.tactic == t && u.run == st.runCount) c++;
            return c;
        }

        /// <summary>지난 판들에서 쓴 적 있는 수인지. 기억은 지우지 않는다.</summary>
        static bool UsedInPastRuns(InterrogationState st, Tactic t)
        {
            foreach (var u in st.usedTactics) if (u.tactic == t && u.run != st.runCount) return true;
            return false;
        }

        /// <summary>이 인물이 지금까지 나온 적 없는 새 이름인지.</summary>
        public static bool IsNewPerson(InterrogationState st, string person)
        {
            if (string.IsNullOrWhiteSpace(person)) return false;
            string n = person.Trim().ToLowerInvariant();
            foreach (var s in st.statements)
            {
                if (!string.IsNullOrWhiteSpace(s.person)
                    && s.person.Trim().ToLowerInvariant() == n) return false;
            }
            return true;
        }
    }
}
