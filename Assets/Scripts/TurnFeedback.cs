using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Interrogation
{
    /// <summary>
    /// 화면 오른쪽에 턴별 판정을 짧게 띄운다.
    ///
    /// **임시 장치다.** 최종적으로는 형사의 표정과 행동이 이 역할을 대신한다
    /// (서류철을 툭툭 치면 의심, 뒤쪽 거울을 흘낏 보면 불온).
    /// 그때까지 플레이어가 인과를 배울 수 있게 해주는 보조 바퀴.
    /// </summary>
    public class TurnFeedback : MonoBehaviour
    {
        [SerializeField] RectTransform listRoot;
        [SerializeField] GameObject rowTemplate;
        [SerializeField] int maxRows = 6;
        [SerializeField] float fadeInTime = 0.25f;
        [SerializeField] float lifetime = 14f;

        static readonly Color Pass = new Color(0.52f, 0.74f, 0.52f);
        static readonly Color Warn = new Color(0.82f, 0.72f, 0.40f);
        static readonly Color Fail = new Color(0.84f, 0.42f, 0.36f);

        readonly List<GameObject> rows = new List<GameObject>();

        /// <summary>턴 결과를 한 줄로 요약해 띄운다.</summary>
        public void Report(int turn, TurnResult r, string topicLabel)
        {
            string verdict;
            Color tint;

            if (r.bustedFact != null) { verdict = "발각"; tint = Fail; }
            else if (r.quotedStatement != null) { verdict = "모순"; tint = Fail; }
            else if (r.openingRepeat) { verdict = "간파"; tint = Fail; }
            else if (r.tactic == Tactic.SILENCE) { verdict = "회피"; tint = Warn; }
            else if (r.tactic == Tactic.COMMAND || r.tactic == Tactic.NONSENSE) { verdict = "심기불편"; tint = Warn; }
            else if (r.deltaSuspicion <= 0 && r.deltaSubversion <= 0) { verdict = "통과"; tint = Pass; }
            else { verdict = "압박"; tint = Warn; }

            string label = string.IsNullOrEmpty(topicLabel) ? "" : topicLabel;
            string delta = Delta(r.deltaSuspicion, r.deltaSubversion);

            Add($"{turn}신문  {label}", $"{verdict}   {delta}", tint);
        }

        /// <summary>판 정산처럼 턴에 속하지 않는 결과.</summary>
        public void ReportSettlement(string reason, int suspicionDelta)
        {
            Add("정산", $"{reason}   {Delta(suspicionDelta, 0)}",
                suspicionDelta < 0 ? Pass : Warn);
        }

        static string Delta(int sus, int sub)
        {
            if (sus == 0 && sub == 0) return "변화 없음";
            var parts = new List<string>();
            if (sus != 0) parts.Add($"의심 {sus:+#;-#;0}");
            if (sub != 0) parts.Add($"불온 {sub:+#;-#;0}");
            return string.Join("  ", parts);
        }

        void Add(string title, string detail, Color tint)
        {
            var go = Instantiate(rowTemplate, listRoot);
            go.SetActive(true);
            go.transform.SetAsFirstSibling();   // 최신이 위로

            var titleTMP = go.transform.Find("Title").GetComponent<TMP_Text>();
            var detailTMP = go.transform.Find("Detail").GetComponent<TMP_Text>();
            titleTMP.text = title;
            detailTMP.text = detail;
            detailTMP.color = tint;

            var group = go.GetComponent<CanvasGroup>();
            rows.Add(go);

            while (rows.Count > maxRows)
            {
                var oldest = rows[0];
                rows.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }

            StartCoroutine(Live(go, group));
        }

        IEnumerator Live(GameObject go, CanvasGroup group)
        {
            float t = 0f;
            while (t < fadeInTime && group != null)
            {
                t += Time.deltaTime;
                group.alpha = Mathf.Clamp01(t / fadeInTime);
                yield return null;
            }
            if (group != null) group.alpha = 1f;

            yield return new WaitForSeconds(lifetime);

            // 오래된 줄은 흐려지되 사라지지는 않는다. 판 전체를 돌아볼 수 있어야 한다.
            float f = 0f;
            while (f < 1f && group != null)
            {
                f += Time.deltaTime / 1.2f;
                group.alpha = Mathf.Lerp(1f, 0.35f, f);
                yield return null;
            }
        }

        public void Clear()
        {
            foreach (var r in rows) if (r != null) Destroy(r);
            rows.Clear();
        }
    }
}
