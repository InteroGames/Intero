using System;
using System.Collections.Generic;

namespace Interrogation
{
    public enum Tactic
    {
        DENY, ALIBI, SYMPATHY, DEFLECT, PARTIAL,
        COUNTER, SILENCE, FLATTER, NONSENSE, COMMAND
    }

    public enum Mood { COLD, PROBING, PRESSING, CLOSING }

    /// <summary>
    /// HELD는 D1 이후 추가됐다 (규칙 4의 예외).
    /// 턴 상한에 걸렸지만 게이지가 패배선(80) 아래일 때, 승리도 패배도 아닌 상태가 필요했다.
    /// 이게 없으면 "판이 끝나도 형사는 기억한다"는 재조사 루프를 표현할 수 없다.
    /// </summary>
    /// <summary>
    /// HELD는 D1 이후 추가됐다 (규칙 4의 예외).
    /// 턴 상한에 걸렸지만 게이지가 패배선(80) 아래일 때, 승리도 패배도 아닌 상태가 필요했다.
    /// 이게 없으면 "판이 끝나도 형사는 기억한다"는 재조사 루프를 표현할 수 없다.
    ///
    /// COLD_GAME / ASYLUM은 8/4 기획서에서 추가됐다.
    /// COLD_GAME - 흠 하나 없이 통과. 이 게임의 만점이다.
    /// ASYLUM    - 헛소리를 반복하면 취조가 아니라 진단이 된다.
    /// </summary>
    public enum Outcome
    {
        ONGOING, RELEASED, COLD_GAME, HELD,
        CONVICTED_THEFT, SEIZED_SUBVERSION, TOTAL_LOSS, ASYLUM
    }

    /// <summary>LLM이 채우는 유일한 구조. 판정은 하지 않는다.</summary>
    [Serializable]
    public class Extraction
    {
        public string time;    // "HH:mm" 정규화 or null
        public string place;   // 짧은 명사 or null
        public string person;  // 짧은 명사 or null
        public string action;     // 짧은 명사 or null
        public string tactic;     // Tactic 열거명
        public bool confession;   // 절도 사실을 인정했는가 (부인문과 구별)

        public bool IsValid(out Tactic parsed)
        {
            return Enum.TryParse(tactic, false, out parsed);
        }
    }

    [Serializable]
    public class Statement
    {
        public int run;
        public int turn;
        public string time;
        public string place;
        public string person;
        public string action;
        public string raw;

        /// <summary>
        /// 이 진술이 답한 조서 항목. 형사가 물은 카드에서 온다 (LLM 관여 없음).
        ///
        /// 모순은 같은 factId 안에서만 대조한다. 이게 없으면
        /// "그날 밤 어디 있었나"와 "어제는 어디 있었나"의 답이 서로 모순으로 잡힌다.
        /// null이면 카드에 속하지 않은 잡담이므로 대조에 참여하지 않는다.
        /// </summary>
        public string factId;
    }

    [Serializable]
    public class Contradiction
    {
        public string field;
        public int runA, turnA, runB, turnB;
        public string valueA, valueB;
    }

    [Serializable]
    public class TacticUse
    {
        public Tactic tactic;
        public int run;
        public int turn;
    }

    [Serializable]
    public class Flags
    {
        public bool injectionAttempted;
        public int silenceCount;
        public int parseFailCount;
    }

    /// <summary>
    /// 판을 넘어 유지되는 전체 상태. 게이지는 코드만 수정한다.
    /// </summary>
    [Serializable]
    public class InterrogationState
    {
        public string sessionId = Guid.NewGuid().ToString("N");
        public int runCount = 0;
        public int turn = 0;

        public int suspicion = 0;   // 의심도 - 절도
        public int subversion = 0;  // 불온도 - 조직 연루

        public List<Statement> statements = new List<Statement>();
        public List<Contradiction> contradictions = new List<Contradiction>();
        public List<TacticUse> usedTactics = new List<TacticUse>();
        public Flags flags = new Flags();

        // --- 임시 밸런싱 값. D6에서 재조정. ---
        public const int LOSE_THRESHOLD = 80;
        public const int WIN_THRESHOLD = 50;
        public const int TURN_LIMIT = 6;      // 한 판의 길이. D6에서 재조정.
        public const int COLD_GAME_MAX = 20;  // 이 아래로 유지한 채 통과하면 콜드게임
        public const int ASYLUM_NONSENSE = 6; // 헛소리가 이만큼 쌓이면 진단으로 넘어간다
        public const int HIDDEN_MIN_RUNS = 3;
        public const int HIDDEN_MIN_REPEAT = 3;

        /// <summary>이번 턴이 이 판의 마지막인가. 형사의 마무리 대사에 쓴다.</summary>
        public bool IsLastTurn => turn >= TURN_LIMIT;

        public Mood CurrentMood()
        {
            int hi = Math.Max(suspicion, subversion);
            if (hi >= LOSE_THRESHOLD) return Mood.CLOSING;
            if (hi >= 60) return Mood.PRESSING;
            if (hi >= 30) return Mood.PROBING;
            return Mood.COLD;
        }

        public Outcome Evaluate()
        {
            // 헛소리가 쌓이면 혐의가 아니라 정신 상태가 문제가 된다. 취조실을 나가긴 하지만 승리는 아니다.
            if (CountTactic(Tactic.NONSENSE) >= ASYLUM_NONSENSE) return Outcome.ASYLUM;

            bool s = suspicion >= LOSE_THRESHOLD;
            bool v = subversion >= LOSE_THRESHOLD;
            if (s && v) return Outcome.TOTAL_LOSS;
            if (s) return Outcome.CONVICTED_THEFT;
            if (v) return Outcome.SEIZED_SUBVERSION;

            // 턴 상한에 도달하면 형사가 서류를 덮는다.
            // 둘 다 50 미만이어야 나갈 수 있다 (D1 승리 조건). 아니면 다시 부른다.
            if (turn >= TURN_LIMIT)
            {
                if (!CanReleaseNow()) return Outcome.HELD;

                // 흠 하나 없이 빠져나갔는가. 천라지망을 통과한 것이다.
                return suspicion < COLD_GAME_MAX && subversion < COLD_GAME_MAX && !everExposed
                    ? Outcome.COLD_GAME
                    : Outcome.RELEASED;
            }

            return Outcome.ONGOING;
        }

        /// <summary>이 판에서 한 번이라도 물증에 걸렸는가. 콜드게임 자격을 잃는다.</summary>
        public bool everExposed;

        int CountTactic(Tactic t)
        {
            int c = 0;
            foreach (var u in usedTactics) if (u.tactic == t) c++;
            return c;
        }

        public bool CanReleaseNow()
        {
            return suspicion < WIN_THRESHOLD && subversion < WIN_THRESHOLD;
        }

        /// <summary>새 판 시작. 게이지는 리셋, 기억은 유지.</summary>
        public void StartNewRun()
        {
            runCount++;
            turn = 0;
            suspicion = 0;
            subversion = 0;
            flags.silenceCount = 0;
            flags.parseFailCount = 0;
            everExposed = false;
            // statements / contradictions / usedTactics 는 절대 지우지 않는다.
        }

        /// <summary>
        /// 이 판의 첫 수가 지난 판들의 첫 수와 같은가.
        /// "자네는 늘 그렇게 시작하더군" — D4 게이트이자 이 게임이 팔려는 순간이다.
        /// 판 안의 반복이 아니라 판을 넘는 반복만 본다.
        /// </summary>
        public bool OpensTheSameWay(Tactic current, out int timesBefore)
        {
            timesBefore = 0;
            foreach (var u in usedTactics)
                if (u.turn == 1 && u.run != runCount && u.tactic == current) timesBefore++;
            return timesBefore > 0;
        }

        /// <summary>제4의 벽 엔딩 발동 조건.</summary>
        public bool HiddenEndingReady(out Tactic dominant)
        {
            dominant = Tactic.DENY;
            if (runCount < HIDDEN_MIN_RUNS) return false;

            var counts = new Dictionary<Tactic, int>();
            foreach (var t in usedTactics)
            {
                counts.TryGetValue(t.tactic, out int c);
                counts[t.tactic] = c + 1;
            }
            foreach (var kv in counts)
            {
                if (kv.Value >= HIDDEN_MIN_REPEAT)
                {
                    dominant = kv.Key;
                    return true;
                }
            }
            return false;
        }
    }
}
