using UnityEngine;

namespace Interrogation
{
    public enum InterrogatorType { Rookie, Veteran, Profiler }

    /// <summary>
    /// 취조관은 판마다 바뀐다. 조서가 두꺼워지는 것과 함께 상대가 강해진다.
    ///
    /// 1차 - 초보 형사     사건만 묻는다. 지난 조서는 뒤지지 않는다.
    /// 2차 - 베테랑 형사   D1 §4의 그 형사. 조서를 대조하고 이름을 미끼로 쓴다.
    /// 3차 - 프로파일러    사건이 아니라 사람을 본다. 말버릇까지 읽는다.
    ///
    /// 능력이 말투로만 다르면 의미가 없다. 아래 플래그가 실제로 무엇을 할 수 있는지 정한다.
    /// </summary>
    public class InterrogatorProfile
    {
        public InterrogatorType type;
        public string title;
        public string persona;

        /// <summary>지난 판의 진술과 대조하는가. 초보는 못 한다.</summary>
        public bool crossReferencesRuns;

        /// <summary>같은 수법을 반복하는 것을 알아채는가.</summary>
        public bool noticesTacticRepeat;

        /// <summary>판을 넘어 같은 말로 시작하는 것을 알아채는가. D4 게이트.</summary>
        public bool noticesOpeningPattern;

        /// <summary>쓸 수 있는 행동.</summary>
        public bool canPin, canBait, canDeal;

        /// <summary>사람과 동기를 캐는 만큼 불온도를 더 밀어올린다.</summary>
        public int subversionBias;

        /// <summary>
        /// 이번 판의 체포 근거를 플레이어가 얼마나 아는가.
        /// 상대가 강해질수록 내가 왜 앉아 있는지도 모르게 된다.
        /// </summary>
        public Disclosure leadDisclosure;

        public static InterrogatorProfile For(int runCount)
        {
            switch (Mathf.Clamp(runCount, 1, 3))
            {
                case 1: return Rookie();
                case 2: return Veteran();
                default: return Profiler();
            }
        }

        // 초보. 매뉴얼대로 묻고, 조급하다. 시간이 자기 편인 줄 모른다.
        static InterrogatorProfile Rookie() => new InterrogatorProfile
        {
            type = InterrogatorType.Rookie,
            title = "형사",
            crossReferencesRuns = false,
            noticesTacticRepeat = false,
            noticesOpeningPattern = false,
            canPin = false, canBait = false, canDeal = false,
            subversionBias = 0,
            leadDisclosure = Disclosure.Full,   // 왜 잡혀왔는지 안다
        };

        // D1 §4의 그 형사. 목소리를 높이지 않고, 시간은 자기 편이다.
        static InterrogatorProfile Veteran() => new InterrogatorProfile
        {
            type = InterrogatorType.Veteran,
            title = "형사",
            crossReferencesRuns = true,
            noticesTacticRepeat = true,
            noticesOpeningPattern = false,
            canPin = true, canBait = true, canDeal = false,
            subversionBias = 0,
            leadDisclosure = Disclosure.Partial,   // 뭔가 있다는 것만 안다
        };

        // 사건이 아니라 사람을 본다. 무엇을 했는지보다 어떤 사람인지를 묻는다.
        static InterrogatorProfile Profiler() => new InterrogatorProfile
        {
            type = InterrogatorType.Profiler,
            title = "조사관",
            crossReferencesRuns = true,
            noticesTacticRepeat = true,
            noticesOpeningPattern = true,
            canPin = true, canBait = true, canDeal = true,
            subversionBias = 3,
            leadDisclosure = Disclosure.Hidden,   // 왜 불려왔는지조차 모른다
        };
    }
}
