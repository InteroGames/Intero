using System;
using System.Collections.Generic;
using UnityEngine;

namespace Interrogation.Inquiry
{
    /// <summary>
    /// 대혐의. 형사는 "네가 범인이다"가 아니라 검증 가능한 수사 가설을 세운다.
    /// 라운드마다 하나씩 전면에 내세운다.
    /// </summary>
    public enum Charge
    {
        A,  // 현장 침입 - 근처 존재 / 내부 진입 / 개폐 방법 인지
        B,  // 절취 실행 - 직접 절취 / 사전 계획 / 공모
        C   // 사후 은폐 - 물건 이동 / 전달 / 증거 인멸
    }

    /// <summary>
    /// 혐의 점수 구간. <b>게임 밸런싱용 수치이지 법적 기준이 아니다.</b>
    /// 화면에 "유죄" 같은 말로 옮기지 말 것.
    /// </summary>
    public enum Band { None, Weak, Plausible, Strong, Decisive }

    public static class ChargeInfo
    {
        public static string Name(Charge c)
        {
            switch (c)
            {
                case Charge.A: return "현장 침입";
                case Charge.B: return "절취 실행";
                case Charge.C: return "사후 은폐";
            }
            return "";
        }

        public static Band BandOf(float score)
        {
            if (score < 20f) return Band.None;
            if (score < 40f) return Band.Weak;
            if (score < 60f) return Band.Plausible;
            if (score < 80f) return Band.Strong;
            return Band.Decisive;
        }

        /// <summary>형사가 스스로의 확신을 부르는 말. 수치는 절대 노출하지 않는다.</summary>
        public static string BandWord(Band b)
        {
            switch (b)
            {
                case Band.None: return "짚이는 것이 없다";
                case Band.Weak: return "미심쩍다";
                case Band.Plausible: return "가설이 선다";
                case Band.Strong: return "거의 굳었다";
                case Band.Decisive: return "서류를 덮어도 되겠다";
            }
            return "";
        }
    }

    /// <summary>
    /// 하위 가설 하나. 점수는 여기 붙고, 대혐의 점수는 이것들의 가중 합이다.
    ///
    /// counterHypothesis는 장식이 아니다. 형사는 <b>항상</b> 대안 설명을 최소 하나 유지한다.
    /// 이게 없으면 형사가 확증편향에 빠지고, 플레이어가 아무리 타당한 설명을 해도
    /// 반응이 없는 벽이 된다.
    /// </summary>
    [Serializable]
    public class Hypothesis
    {
        public string id;
        public Charge parentCharge;
        public string statement;          // "용의자가 현장 내부에 들어갔다"
        public string counterHypothesis;  // "다른 사람이 같은 창으로 들어갔다"

        public float score;               // 0~100
        public float weightInCharge = 1f; // 대혐의 가중합에서의 몫

        /// <summary>대안 쪽으로 옮겨간 확률 가중치. 0.15 아래로 내려가지 않는다.</summary>
        public float counterWeight = 0.15f;

        /// <summary>플레이어가 이 가설을 스스로 인정했는가 (CONCEDE).</summary>
        public bool conceded;

        public Band Band => ChargeInfo.BandOf(score);

        public void Add(float delta)
        {
            score = Mathf.Clamp(score + delta, 0f, 100f);
        }

        /// <summary>대안 가설 쪽으로 무게를 옮긴다. 점수도 그만큼 깎인다.</summary>
        public void ShiftToCounter(float amount)
        {
            counterWeight = Mathf.Clamp(counterWeight + amount, 0.15f, 0.9f);
            Add(-amount * 60f);
        }
    }

    /// <summary>
    /// DETECTIVE_HYPOTHESIS. 형사가 현재 의심하는 혐의와 확신도.
    /// LLM 프롬프트에 들어가도 되는 두 상태 중 하나다.
    /// </summary>
    public class HypothesisBoard
    {
        public readonly List<Hypothesis> all = new List<Hypothesis>();

        /// <summary>형사의 전반적 확신도. 라운드가 끝날 때 갱신되고 다음 라운드 난이도가 된다.</summary>
        public float confidence = 20f;

        public Hypothesis Get(string id)
        {
            foreach (var h in all) if (h.id == id) return h;
            return null;
        }

        public List<Hypothesis> Under(Charge c)
        {
            var list = new List<Hypothesis>();
            foreach (var h in all) if (h.parentCharge == c) list.Add(h);
            return list;
        }

        /// <summary>대혐의 점수 = 하위 점수의 가중 합.</summary>
        public float ScoreOf(Charge c)
        {
            float sum = 0f, w = 0f;
            foreach (var h in all)
            {
                if (h.parentCharge != c) continue;
                sum += h.score * h.weightInCharge;
                w += h.weightInCharge;
            }
            return w <= 0f ? 0f : sum / w;
        }

        public Band BandOf(Charge c) => ChargeInfo.BandOf(ScoreOf(c));

        /// <summary>지금 가장 강한 하위 가설. 형사의 질문 우선순위 1번이다.</summary>
        public Hypothesis Strongest(Charge? within = null)
        {
            Hypothesis best = null;
            foreach (var h in all)
            {
                if (within.HasValue && h.parentCharge != within.Value) continue;
                if (h.conceded) continue;   // 이미 내준 것을 또 파는 건 심문이 아니라 반복이다
                if (best == null || h.score > best.score) best = h;
            }
            return best;
        }

        /// <summary>
        /// 잠입 결과를 형사의 출발 가설에 반영한다.
        ///
        /// 카드를 거치지 않는 유일한 Phase1 → Phase2 경로다. 카드만으로는
        /// "증거를 지우면 취조가 쉬워지지만 은폐 혐의가 커진다"는 트레이드오프가 성립하지 않는다.
        /// 장부를 태우면 그 카드의 증거력은 떨어지지만, <b>태웠다는 사실 자체를 형사가 안다.</b>
        ///
        /// 여기 들어오는 것은 형사가 현장에서 확인할 수 있는 것뿐이다.
        /// 플레이어의 속마음이나 조작 이력은 절대 반영하지 않는다.
        /// </summary>
        public void ApplyWorld(Heist.HeistResult world)
        {
            if (world.concealed)
            {
                // 재가 남았다. 이건 해석의 여지가 좁다.
                Bump("C3", 24f);
                Bump("C1", 8f);
            }

            if (world.caught)
            {
                // 현행범이다. 형사가 물어볼 것이 훨씬 적다.
                Bump("A1", 18f); Bump("A2", 22f);
                Bump("B1", 20f); Bump("C1", 12f);
                confidence += 25f;
            }

            if (world.stolen == 0)
            {
                // 손에 쥔 것이 없으면 절취를 잇는 선 자체가 약해진다.
                Bump("B1", -18f);
                Bump("C1", -12f); Bump("C2", -12f);
            }

            // 담을 넘느라 버린 자루는 현장 유류품이다.
            if (world.Has(Heist.SlotId.Tool)) Bump("A3", 10f);

            // 맨손 흔적은 "안에 들어갔다"를 직접 민다.
            if (world.Has(Heist.SlotId.Glove)) Bump("A2", 10f);
        }

        void Bump(string id, float d)
        {
            var h = Get(id);
            if (h != null) h.Add(d);
        }

        /// <summary>대혐의 3개 중 40 미만인 것의 수. Case Doubt 축의 원재료.</summary>
        public int ChargesBelow(float threshold)
        {
            int n = 0;
            foreach (Charge c in new[] { Charge.A, Charge.B, Charge.C })
                if (ScoreOf(c) < threshold) n++;
            return n;
        }

        /// <summary>
        /// 기본 수사 가설판. 하위 9개, 대혐의당 3개.
        ///
        /// 초기 점수가 0이 아닌 이유: 형사는 이미 자네를 앉혀 놓았다.
        /// 0에서 시작하면 아무 말도 하지 않는 것이 최적 전략이 되고, 취조가 성립하지 않는다.
        /// A(현장 침입)가 가장 높고 C(사후 은폐)가 가장 낮다 - 형사가 아는 순서 그대로다.
        /// </summary>
        public static HypothesisBoard Default()
        {
            var b = new HypothesisBoard();

            b.all.Add(new Hypothesis
            {
                id = "A1", parentCharge = Charge.A, weightInCharge = 0.25f, score = 45f,
                statement = "용의자가 그날 밤 저택 근처에 있었다",
                counterHypothesis = "그 시각 그 골목을 지난 사람은 여럿이다",
            });
            b.all.Add(new Hypothesis
            {
                id = "A2", parentCharge = Charge.A, weightInCharge = 0.45f, score = 42f,
                statement = "용의자가 저택 내부에 들어갔다",
                counterHypothesis = "다른 사람이 같은 창으로 들어갔다",
            });
            b.all.Add(new Hypothesis
            {
                id = "A3", parentCharge = Charge.A, weightInCharge = 0.30f, score = 35f,
                statement = "용의자가 그 창의 개폐 방법을 미리 알고 있었다",
                counterHypothesis = "그 창은 원래 걸쇠가 헐거웠고 누구나 알 수 있었다",
            });

            b.all.Add(new Hypothesis
            {
                id = "B1", parentCharge = Charge.B, weightInCharge = 0.45f, score = 40f,
                statement = "용의자가 직접 물건을 가져갔다",
                counterHypothesis = "물건은 다른 손을 거쳐 용의자에게 왔다",
            });
            b.all.Add(new Hypothesis
            {
                id = "B2", parentCharge = Charge.B, weightInCharge = 0.30f, score = 34f,
                statement = "용의자가 사전에 대상을 정하고 계획했다",
                counterHypothesis = "지나가다 열린 창을 본 우발적 행동이다",
            });
            b.all.Add(new Hypothesis
            {
                id = "B3", parentCharge = Charge.B, weightInCharge = 0.25f, score = 22f,
                statement = "용의자에게 안에서 도운 공모자가 있다",
                counterHypothesis = "저택 사람 중 하나가 따로 벌인 일이다",
            });

            b.all.Add(new Hypothesis
            {
                id = "C1", parentCharge = Charge.C, weightInCharge = 0.35f, score = 30f,
                statement = "용의자가 물건을 현장 밖으로 옮겼다",
                counterHypothesis = "물건은 저택 안에서 이미 자리를 옮긴 뒤였다",
            });
            b.all.Add(new Hypothesis
            {
                id = "C2", parentCharge = Charge.C, weightInCharge = 0.30f, score = 26f,
                statement = "용의자가 물건을 제3자에게 넘겼다",
                counterHypothesis = "전당포가 출처를 잘못 적었다",
            });
            b.all.Add(new Hypothesis
            {
                id = "C3", parentCharge = Charge.C, weightInCharge = 0.35f, score = 18f,
                statement = "용의자가 기록을 없앴다",
                counterHypothesis = "장부는 저택 사람이 스스로 태웠다",
            });

            return b;
        }
    }
}
