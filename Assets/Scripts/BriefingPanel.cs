using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Interrogation
{
    /// <summary>
    /// 게임 방식 설명. 주인공이 취조실에 들어가기 전 스스로 정리하는 형식이라
    /// 규칙을 알려주면서도 몰입을 깨지 않는다.
    ///
    /// 처음 한 번은 자동으로 뜨고, 이후에는 Tab으로 언제든 다시 볼 수 있다.
    /// </summary>
    public class BriefingPanel : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text bodyText;
        [SerializeField] TMP_Text footerText;
        [SerializeField] Button closeButton;
        [SerializeField] float fadeTime = 0.3f;

        bool open;
        bool built;

        /// <summary>
        /// 지금 돌고 있는 페이드. 새 페이드를 걸기 전에 반드시 멈춘다.
        ///
        /// 이게 없으면 열기와 닫기가 겹쳤을 때 두 코루틴이 매 프레임 alpha를
        /// 반대로 밀어서 패널이 깜빡인다. Tab을 연타하거나 닫히는 중에 다시 열면 걸린다.
        /// </summary>
        Coroutine fade;

        void Awake()
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (open) Close(); else StartCoroutine(Show());
            }
            else if (open && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        public void Build(DialoguePool pool)
        {
            if (built || pool == null) return;

            titleText.text = string.IsNullOrEmpty(pool.BriefingTitle) ? "취조실에 들어가기 전에" : pool.BriefingTitle;
            footerText.text = pool.BriefingFooter ?? "";

            var sb = new StringBuilder();
            foreach (var line in pool.BriefingLines)
            {
                // **강조** 를 TMP 볼드로. 규칙 첫머리만 굵게 해서 훑어읽을 수 있게 한다.
                sb.Append("· ").Append(Bold(line)).Append("\n\n");
            }
            bodyText.text = sb.ToString().TrimEnd();
            built = true;
        }

        static string Bold(string s)
        {
            var sb = new StringBuilder();
            bool inBold = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
                {
                    sb.Append(inBold ? "</b>" : "<b>");
                    inBold = !inBold;
                    i++;
                    continue;
                }
                sb.Append(s[i]);
            }
            if (inBold) sb.Append("</b>");
            return sb.ToString();
        }

        public IEnumerator Show()
        {
            if (open) yield break;

            open = true;
            group.blocksRaycasts = true;

            // 닫히는 중이었다면 그 페이드를 먼저 끊는다. 지금 알파에서 이어받는다.
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(FadeTo(1f));

            // 페이드 핸들을 yield하지 않는다. 도중에 Close가 끊으면 영영 안 돌아온다.
            yield return new WaitForSeconds(fadeTime);
        }

        /// <summary>플레이어가 닫을 때까지 기다린다. 첫 진입에서 쓴다.</summary>
        public IEnumerator ShowAndWait()
        {
            yield return Show();
            while (open) yield return null;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            group.blocksRaycasts = false;

            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(FadeTo(0f));
        }

        /// <summary>지금 알파에서 목표까지. 중간에 끊겨도 다음 페이드가 그 자리에서 이어받는다.</summary>
        IEnumerator FadeTo(float target)
        {
            float from = group.alpha;
            float dur = fadeTime * Mathf.Abs(target - from);   // 반쯤 열린 상태면 남은 만큼만
            float t = 0f;

            while (t < dur)
            {
                t += Time.deltaTime;
                group.alpha = Mathf.Lerp(from, target, t / dur);
                yield return null;
            }

            group.alpha = target;
            fade = null;
        }
    }
}
