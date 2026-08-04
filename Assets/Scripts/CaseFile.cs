using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Interrogation
{
    /// <summary>
    /// 조서 한 항목.
    ///
    /// 3단 구조:
    ///   확정(locked)  - 경찰이 쥔 물증. 플레이어가 손댈 수 없다.
    ///   빈칸          - 이번 판에 뽑혀 나온 카드. 플레이어가 지어낸다. 여기가 게임이다.
    ///   지뢰(isMine)  - 뽑힌 카드 중 형사가 답을 이미 아는 것. 화면에 표시하지 않는다.
    ///
    /// 걸린 항목은 exposed가 되어 다음 판부터 확정으로 승격된다. 판이 갈수록 올가미가 조여든다.
    /// </summary>
    /// <summary>체포 근거를 플레이어가 얼마나 아는가. 형사는 언제나 전부 안다.</summary>
    public enum Disclosure { Full, Partial, Hidden }

    public class CaseFact
    {
        public string id;
        public string label;        // "당시 소재"
        public string matchField;   // 추출 필드명: time / place / person / action
        public string truth;        // 실제로 일어난 일. 사건 카드에 표시된다.
        public string question;     // 조사 턴에서 서기가 묻는 말. 확정 항목은 null.
        public string note;         // 확정 항목의 근거 ("현장 검증 완료")

        // --- 체포 근거(lead)에만 쓰이는 것들 ---
        public string narration;    // 다 알 때 오프닝에 나가는 줄
        public string partial;      // 어렴풋이만 알 때
        public bool isLead;
        public Disclosure disclosure = Disclosure.Full;

        /// <summary>지금 공개 수준에서 플레이어에게 보여줄 문장. 감춰졌으면 null.</summary>
        public string LeadNarration()
        {
            switch (disclosure)
            {
                case Disclosure.Full: return narration;
                case Disclosure.Partial: return partial;
            }
            return null;
        }

        public bool locked;         // 처음부터 확정 (JSON에 명시)
        public bool exposed;        // 걸려서 확정으로 승격됨 (판을 넘어 유지)

        /// <summary>이번 판에 뽑혔는가.</summary>
        public bool dealt;

        /// <summary>이번 판의 지뢰인가. 매 판 다시 뽑는다. 절대 화면에 노출하지 말 것.</summary>
        public bool isMine;

        /// <summary>
        /// 이번 판에 이 항목의 진술이 무너졌는가. 한 판에 한 번만 과금한다.
        ///
        /// 모순은 이벤트가 아니라 조서의 상태다. "당시 소재 진술이 흔들렸다"는
        /// 한 번 벌어지면 벌어진 것이지, 이후 턴마다 새로 벌어지지 않는다.
        /// 매 판 다시 진술하므로 Deal()에서 리셋된다.
        /// </summary>
        public bool broken;

        /// <summary>조사 턴에서 플레이어가 쓴 원문.</summary>
        public string playerAnswer;

        /// <summary>플레이어 답변에서 추출된 값. 거짓 판정의 기준.</summary>
        public string playerValue;

        /// <summary>형사가 이 항목의 답을 아는가.</summary>
        public bool DetectiveKnows => locked || exposed || isMine;

        /// <summary>플레이어가 손댈 수 있는 칸인가.</summary>
        public bool IsBlank => !locked && !exposed;

        /// <summary>조사 턴에 쓴 답이 사실과 다른가. 답을 안 썼으면 거짓이 아니다.</summary>
        public bool AnsweredFalsely()
        {
            if (string.IsNullOrWhiteSpace(playerValue)) return false;
            return !ValueMatches(playerValue, truth);
        }

        /// <summary>
        /// 값 비교. 추출값은 짧은 명사나 정규화된 시각이므로 한쪽이 다른 쪽을 품으면 같은 것으로 본다.
        /// ("서재" 와 "최 의원 자택 2층 서재")
        /// </summary>
        public static bool ValueMatches(string a, string b)
        {
            string na = Norm(a), nb = Norm(b);
            if (na == null || nb == null) return false;
            return na == nb || na.Contains(nb) || nb.Contains(na);
        }

        static string Norm(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            return v.Trim().ToLowerInvariant().Replace(" ", "");
        }
    }

    /// <summary>
    /// 사건 카드 겸 조서. 정답표이자 플레이어가 지어낸 이야기의 장부다.
    /// 이게 있어야 "거짓말"이 코드로 판정 가능해진다.
    ///
    /// 매 판 카드를 섞어 일부만 내놓는다. 같은 사건이라도 캐묻는 곳이 달라진다.
    /// </summary>
    public class CaseFile
    {
        public string summary;
        public readonly List<CaseFact> facts = new List<CaseFact>();

        /// <summary>체포 근거 후보. 매 판 하나씩 뽑혀 확정 사실이 된다.</summary>
        public readonly List<CaseFact> leads = new List<CaseFact>();

        /// <summary>이번 판에 새로 드러난 체포 근거.</summary>
        public CaseFact CurrentLead { get; private set; }

        public int blanksPerRun = 4;
        public int minesPerRun = 1;
        public int revealBatch = 2;   // 조사 턴에서 한 번에 내놓는 카드 수

        /// <summary>뽑을 카드가 이 수보다 적으면 더 방어할 수 없다. 판을 끝낸다.</summary>
        public const int MIN_BLANKS = 2;

        public static CaseFile Load(string json)
        {
            var cf = new CaseFile();
            var root = JObject.Parse(json);
            cf.summary = (string)root["summary"];

            var deal = root["deal"];
            if (deal != null)
            {
                cf.blanksPerRun = (int?)deal["blanksPerRun"] ?? cf.blanksPerRun;
                cf.minesPerRun = (int?)deal["minesPerRun"] ?? cf.minesPerRun;
                cf.revealBatch = (int?)deal["revealBatch"] ?? cf.revealBatch;
            }

            foreach (var f in root["facts"] ?? new JArray())
            {
                cf.facts.Add(new CaseFact
                {
                    id = (string)f["id"],
                    label = (string)f["label"],
                    matchField = (string)f["matchField"],
                    truth = (string)f["truth"],
                    question = (string)f["question"],
                    note = (string)f["note"],
                    locked = (bool?)f["locked"] ?? false,
                });
            }

            foreach (var f in root["leads"] ?? new JArray())
            {
                cf.leads.Add(new CaseFact
                {
                    id = (string)f["id"],
                    label = (string)f["label"],
                    matchField = (string)f["matchField"],
                    truth = (string)f["truth"],
                    note = (string)f["note"],
                    narration = (string)f["narration"],
                    partial = (string)f["partial"],
                    isLead = true,
                    locked = true,   // 경찰이 쥔 것이므로 부인할 수 없다
                });
            }
            return cf;
        }

        /// <summary>
        /// 이번 판의 체포 근거를 뽑는다. 판마다 경찰이 다른 것을 들고 온다.
        /// 뽑힌 것은 확정 사실 목록에 들어가고 다음 판에도 남는다 - 증거는 사라지지 않는다.
        ///
        /// disclosure는 난이도다. 형사는 언제나 전부 알고, 감춰지는 건 플레이어 쪽뿐이다.
        /// </summary>
        public CaseFact DrawLead(Disclosure disclosure)
        {
            var unused = new List<CaseFact>();
            foreach (var l in leads) if (!facts.Contains(l)) unused.Add(l);

            if (unused.Count == 0) { CurrentLead = null; return null; }

            var pick = unused[Random.Range(0, unused.Count)];
            pick.disclosure = disclosure;
            facts.Add(pick);
            CurrentLead = pick;
            return pick;
        }

        /// <summary>
        /// 이번 판에 쓸 카드를 섞어서 뽑고, 그중 일부를 지뢰로 지정한다.
        /// 확정·발각된 항목은 뽑기에서 빠진다 - 이미 부인할 수 없는 것들이므로.
        /// </summary>
        public void Deal()
        {
            var pool = new List<CaseFact>();
            foreach (var f in facts)
            {
                f.dealt = false;
                f.isMine = false;
                f.broken = false;
                f.playerAnswer = null;
                f.playerValue = null;

                if (f.IsBlank && !string.IsNullOrEmpty(f.question)) pool.Add(f);
            }

            Shuffle(pool);

            int take = Mathf.Min(blanksPerRun, pool.Count);
            for (int i = 0; i < take; i++) pool[i].dealt = true;

            // 뽑힌 것 중에서만 지뢰를 고른다. 매 판 다시 고르므로 같은 항목이 늘 위험하지는 않다.
            var dealtList = pool.GetRange(0, take);
            Shuffle(dealtList);
            int mines = Mathf.Min(minesPerRun, dealtList.Count);
            for (int i = 0; i < mines; i++) dealtList[i].isMine = true;
        }

        static void Shuffle(List<CaseFact> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>이번 판에 뽑힌 카드들. 조사 턴에서 이 순서대로 공개된다.</summary>
        public List<CaseFact> Dealt()
        {
            var list = new List<CaseFact>();
            foreach (var f in facts) if (f.dealt) list.Add(f);
            return list;
        }

        /// <summary>확정 사실들. 조서 상단에 늘 붙어 있다.</summary>
        public List<CaseFact> Established()
        {
            var list = new List<CaseFact>();
            foreach (var f in facts) if (f.locked || f.exposed) list.Add(f);
            return list;
        }

        /// <summary>아직 뽑을 수 있는 카드가 남았는가.</summary>
        public int RemainingPool()
        {
            int c = 0;
            foreach (var f in facts)
                if (f.IsBlank && !string.IsNullOrEmpty(f.question)) c++;
            return c;
        }

        /// <summary>더 지어낼 칸이 남지 않았다. 이 판으로 끝내야 한다.</summary>
        public bool OutOfRoom => RemainingPool() < MIN_BLANKS;

        /// <summary>
        /// 거짓말이 발각됐다. 이 항목은 다음 판부터 확정 사실이 된다.
        /// 플레이어는 이걸로 형사가 무엇을 아는지 배운다 - 유일한 학습 경로다.
        /// </summary>
        public void Expose(CaseFact fact)
        {
            if (fact == null || fact.exposed) return;
            fact.exposed = true;
        }

        /// <summary>
        /// 이 판에서 아직 들키지 않은 거짓말의 수. 그대로 득점이 된다.
        /// 진술이 무너진 항목(broken)은 지켜낸 것이 아니므로 세지 않는다.
        /// </summary>
        public int SurvivingLies()
        {
            int c = 0;
            foreach (var f in facts)
                if (f.dealt && !f.exposed && !f.broken && f.AnsweredFalsely()) c++;
            return c;
        }
    }
}
