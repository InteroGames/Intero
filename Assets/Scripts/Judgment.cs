using System;
using System.Collections.Generic;

namespace Interrogation
{
    /// <summary>
    /// 모순 검출. LLM은 여기 관여하지 않는다.
    /// 같은 (run 무관) 질문 맥락에서 같은 필드에 다른 값이 오면 모순.
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

        /// <summary>새 진술을 기존 진술들과 대조. 새로 발견된 모순만 반환.</summary>
        public static List<Contradiction> Check(InterrogationState st, Statement incoming)
        {
            var found = new List<Contradiction>();

            foreach (var field in Fields)
            {
                string newVal = Norm(Get(incoming, field));
                if (newVal == null) continue;

                foreach (var prev in st.statements)
                {
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
            bool confessedTheft)
        {
            var d = new Delta { suspicion = 0, subversion = 0, reason = "" };

            if (newContradictions > 0)
            {
                d.suspicion += 15 * newContradictions;
                d.subversion += 5 * newContradictions;
                d.reason += $"모순x{newContradictions} ";
            }

            int repeat = CountTactic(st, tactic);
            if (repeat == 1) { d.suspicion += 5; d.subversion += 5; d.reason += "태틱반복2회 "; }
            else if (repeat >= 2) { d.suspicion += 10; d.subversion += 10; d.reason += "태틱반복3회+ "; }

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

        static int Clamp(int v) => Math.Max(0, Math.Min(100, v));

        /// <summary>이번 턴 이전에 같은 태틱을 몇 번 썼는지.</summary>
        static int CountTactic(InterrogationState st, Tactic t)
        {
            int c = 0;
            foreach (var u in st.usedTactics) if (u.tactic == t) c++;
            return c;
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
