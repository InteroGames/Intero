using Newtonsoft.Json;
using UnityEngine;

namespace Interrogation.Heist
{
    /// <summary>
    /// Phase 1 → Phase 2 로 넘어가는 유일한 통로.
    ///
    /// 씬을 넘길 때 메모리로 전달하고, 동시에 PlayerPrefs에도 적어둔다.
    /// 취조 씬만 따로 열어 밸런싱할 때(그리고 에디터에서 Play를 멈췄다 다시 켤 때)
    /// 직전 잠입 결과가 그대로 살아 있어야 반복 테스트가 가능하다.
    /// </summary>
    public static class PhaseBridge
    {
        const string Key = "interrogation.heist_result";

        static HeistResult cached;

        /// <summary>잠입이 끝났다. 결과를 봉인해서 넘긴다.</summary>
        public static void Publish(HeistResult r)
        {
            HeistOutcome.Seal(r);
            r.played = true;
            cached = r;

            try
            {
                PlayerPrefs.SetString(Key, JsonConvert.SerializeObject(r));
                PlayerPrefs.Save();
            }
            catch { /* 저장 실패가 게임을 멈추면 안 된다. 메모리 전달은 이미 끝났다. */ }

            TranscriptLog.Write($"[잠입] {r.Summary()}");
            foreach (var s in r.slots)
                TranscriptLog.Write($"  [슬롯] {s.id} exists={s.exists} q={s.quality} round={s.acquiredRound}");
        }

        /// <summary>
        /// 취조가 참조할 잠입 결과. 없으면 기본값을 만들어 준다.
        /// **null을 반환하지 않는다.** 취조 페이즈는 어떤 경우에도 시작될 수 있어야 한다.
        /// </summary>
        public static HeistResult Consume()
        {
            if (cached != null) return cached;

            try
            {
                string json = PlayerPrefs.GetString(Key, null);
                if (!string.IsNullOrEmpty(json))
                {
                    var r = JsonConvert.DeserializeObject<HeistResult>(json);
                    if (r != null && r.slots != null && r.slots.Count > 0)
                    {
                        cached = r;
                        return cached;
                    }
                }
            }
            catch { /* 형식이 깨졌으면 기본값으로 간다 */ }

            cached = HeistOutcome.Standalone();
            return cached;
        }

        /// <summary>새 게임. 다음 잠입 결과가 직전 판에 오염되지 않게 한다.</summary>
        public static void Clear()
        {
            cached = null;
            try { PlayerPrefs.DeleteKey(Key); } catch { }
        }
    }
}
