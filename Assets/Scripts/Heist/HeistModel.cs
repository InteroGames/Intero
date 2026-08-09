using System;
using System.Collections.Generic;
using UnityEngine;

namespace Interrogation.Heist
{
    /// <summary>
    /// 증거 슬롯 하나.
    ///
    /// **증거는 런타임에 자유 생성하지 않는다.** 슬롯을 사전에 고정하고,
    /// 잠입 결과가 각 슬롯의 상태만 결정한다. 완전 동적 생성이면 취조 콘텐츠가
    /// 조합 폭발해서 세로 슬라이스 단계에서 관리가 불가능해진다.
    /// </summary>
    [Serializable]
    public class EvidenceSlot
    {
        public string id;
        public bool exists;         // 이 증거가 형사에게 존재하는가
        public int acquiredRound;   // 형사가 언제 확보하는가 (0 = 취조 시작 전)
        public int quality;         // 0~10. 인멸/우회 성공 시 하향.

        public EvidenceSlot() { }

        public EvidenceSlot(string id, bool exists, int acquiredRound, int quality)
        {
            this.id = id;
            this.exists = exists;
            this.acquiredRound = acquiredRound;
            this.quality = Mathf.Clamp(quality, 0, 10);
        }
    }

    /// <summary>고정된 슬롯 이름. 증거 카드 JSON이 이 문자열로 슬롯을 찾는다.</summary>
    public static class SlotId
    {
        public const string Window = "physical_window";      // 창문 강제개방
        public const string Tool = "physical_tool";          // 유기된 공구
        public const string Glove = "trace_glove";           // 맨손 접촉 흔적
        public const string Footprint = "trace_footprint";   // 발자국
        public const string Ledger = "record_ledger";        // 저택 출입 장부
        public const string Pass = "record_pass";            // 통행 기록
        public const string Maid = "testimony_maid";         // 하녀 증언
        public const string Goods = "link_goods";            // 장물과의 연결

        public static readonly string[] All =
        { Window, Tool, Glove, Footprint, Ledger, Pass, Maid, Goods };
    }

    /// <summary>
    /// WORLD_STATE. 실제로 있었던 사건 그 자체다.
    ///
    /// **이 객체는 형사 LLM 프롬프트에 절대 들어가지 않는다.**
    /// 들어가는 순간 형사가 전지전능해지고 게임이 붕괴한다.
    /// 취조가 참조하는 것은 여기서 파생된 슬롯 상태뿐이고, 그마저도
    /// DetectiveKnowledge를 거쳐서만 형사에게 도달한다.
    /// </summary>
    [Serializable]
    public class HeistResult
    {
        /// <summary>Phase 1을 실제로 돌았는가. false면 취조 씬을 단독 실행한 것이다.</summary>
        public bool played;

        public int stolen;            // 0~3. 3 미만이면 패널티 엔딩 분기.
        public bool caught;           // 60초 안에 나가지 못했다
        public bool glovesOff;        // 금고 다이얼을 맨손으로 돌렸다
        public bool concealed;        // 장부를 태웠다 (은폐 혐의의 근거가 된다)
        public string route = "";     // "front" 빠른 경로 / "wall" 우회 경로
        public float escapeSeconds;   // 경보부터 탈출까지

        public List<EvidenceSlot> slots = new List<EvidenceSlot>();

        public EvidenceSlot Slot(string id)
        {
            foreach (var s in slots) if (s.id == id) return s;
            return null;
        }

        /// <summary>그 슬롯이 형사 손에 있는가. 없는 슬롯을 물으면 false.</summary>
        public bool Has(string id)
        {
            var s = Slot(id);
            return s != null && s.exists;
        }

        public int QualityOf(string id)
        {
            var s = Slot(id);
            return s != null && s.exists ? s.quality : 0;
        }

        /// <summary>목표 3개를 다 털고 잡히지 않았는가.</summary>
        public bool CleanJob => stolen >= 3 && !caught;

        public string Summary()
            => $"절취 {stolen}/3, {(caught ? "검거됨" : "탈출")}, 경로 {(string.IsNullOrEmpty(route) ? "-" : route)}, " +
               $"맨손 {(glovesOff ? "예" : "아니오")}, 인멸 {(concealed ? "성공" : "없음")}, {escapeSeconds:0.0}초";
    }

    /// <summary>
    /// 잠입 결과 → 증거 슬롯 변환. **이 클래스 밖에서 슬롯을 만들지 말 것.**
    ///
    /// 게이지를 GaugeCalculator 한 곳에 모아둔 것과 같은 이유다.
    /// 여기가 흩어지면 "증거를 지우면 취조가 쉬워지지만 은폐 혐의가 커진다"는
    /// 트레이드오프를 밸런싱할 수 없게 된다.
    /// </summary>
    public static class HeistOutcome
    {
        public static void Seal(HeistResult r)
        {
            r.slots.Clear();
            bool front = r.route == "front";
            bool wall = r.route == "wall";

            // 창을 넘어 들어갔다. 이건 어느 경로로 나가든 남는다.
            Add(r, SlotId.Window, true, 0, r.caught ? 8 : 6);

            // 담을 넘으려면 자루를 버려야 한다. 우회 경로의 대가.
            Add(r, SlotId.Tool, wall || r.caught, 0, 7);

            // 금고 다이얼은 장갑으로 돌아가지 않는다. 벗은 만큼 흔적이 또렷해진다.
            Add(r, SlotId.Glove, r.glovesOff, 0, 3 + (r.glovesOff ? 4 : 0));

            // 정문 쪽 화단은 젖어 있었다. 우회로에는 발자국이 남지 않는다.
            Add(r, SlotId.Footprint, front || r.caught, 0, 7);

            // 장부는 태워도 재가 남는다. 증거력은 떨어지지만 태운 사실 자체가 은폐 혐의다.
            Add(r, SlotId.Ledger, true, 0, r.concealed ? 3 : 8);

            // 도시의 통행 기록. 잠입과 무관하게 언제나 존재한다. 형사는 나중에 뗀다.
            Add(r, SlotId.Pass, true, 2, 5);

            // 하녀는 경보에 깼다. 정문으로 나가거나 꾸물거렸으면 얼굴을 봤다.
            Add(r, SlotId.Maid, front || r.caught || r.escapeSeconds > 40f, 1, 6);

            // 장물과 나를 잇는 선. 하나도 안 들고 나왔으면 이을 것이 없다.
            Add(r, SlotId.Goods, r.stolen > 0 || r.caught, 0,
                r.caught ? 10 : 3 + r.stolen * 2);
        }

        static void Add(HeistResult r, string id, bool exists, int round, int quality)
            => r.slots.Add(new EvidenceSlot(id, exists, round, exists ? quality : 0));

        /// <summary>
        /// 취조 씬을 단독으로 열었을 때 쓰는 기본값. 시연 중 Phase 1을 건너뛰어도
        /// Phase 2가 멈추지 않아야 한다. 중간 난이도로 맞춰 둔다.
        /// </summary>
        public static HeistResult Standalone()
        {
            var r = new HeistResult
            {
                played = false,
                stolen = 3,
                caught = false,
                glovesOff = true,
                concealed = false,
                route = "front",
                escapeSeconds = 38f,
            };
            Seal(r);
            return r;
        }
    }
}
