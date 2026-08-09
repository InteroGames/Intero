using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Interrogation.Heist
{
    /// <summary>잠입 구간 HUD. 규칙은 없고 표시만 한다.</summary>
    public class HeistHUD : MonoBehaviour
    {
        [SerializeField] TMP_Text objectiveText;
        [SerializeField] TMP_Text timerText;
        [SerializeField] TMP_Text promptText;
        [SerializeField] TMP_Text noticeText;
        [SerializeField] Image holdFill;
        [SerializeField] CanvasGroup fade;

        static readonly Color Calm = new Color(0.78f, 0.76f, 0.70f);
        static readonly Color Urgent = new Color(0.86f, 0.32f, 0.26f);

        void Awake()
        {
            SetPrompt(null);
            SetHold(0f);
            if (timerText != null) timerText.text = "";
            if (noticeText != null) noticeText.text = "";
            if (fade != null) fade.alpha = 0f;
        }

        public void SetObjective(string s)
        {
            if (objectiveText != null) objectiveText.text = s;
        }

        public void SetTimer(string s, bool urgent)
        {
            if (timerText == null) return;
            timerText.text = s;
            timerText.color = urgent ? Urgent : Calm;
        }

        public void SetPrompt(string s)
        {
            if (promptText != null) promptText.text = string.IsNullOrEmpty(s) ? "" : s;
        }

        /// <summary>붙잡고 있는 진행도. 0이면 게이지를 감춘다.</summary>
        public void SetHold(float t01)
        {
            if (holdFill == null) return;
            holdFill.transform.parent.gameObject.SetActive(t01 > 0.001f);
            holdFill.fillAmount = Mathf.Clamp01(t01);
        }

        /// <summary>화면 가운데 한 줄. 경보, 검거, 탈출 알림에 쓴다.</summary>
        public IEnumerator Notice(string s, float hold)
        {
            if (noticeText == null) yield break;
            noticeText.text = s;
            yield return new WaitForSeconds(hold);
            noticeText.text = "";
        }

        public void NoticeInstant(string s)
        {
            if (noticeText != null) noticeText.text = s;
        }

        public IEnumerator FadeOut(float dur)
        {
            if (fade == null) yield break;
            fade.blocksRaycasts = true;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                fade.alpha = Mathf.Clamp01(t / dur);
                yield return null;
            }
            fade.alpha = 1f;
        }
    }
}
