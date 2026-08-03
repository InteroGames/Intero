using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

namespace Interrogation
{
    /// <summary>
    /// 심문관 머리 옆에 뜨는 월드 공간 말풍선.
    /// 항상 카메라를 향하고, 대사가 없을 때는 서서히 사라진다.
    /// </summary>
    public class SpeechBubble : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text text;
        [SerializeField] float fadeSpeed = 5f;

        Transform cam;
        float targetAlpha;

        void Awake()
        {
            if (Camera.main != null) cam = Camera.main.transform;
            if (group != null) group.alpha = 0f;
        }

        void LateUpdate()
        {
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.position, Vector3.up);

            if (group != null)
                group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        }

        /// <summary>한 글자씩 출력. 형사는 서두르지 않는다.</summary>
        public IEnumerator Type(string line, float speed)
        {
            text.text = "";
            targetAlpha = 1f;

            var sb = new StringBuilder();
            foreach (char c in line)
            {
                sb.Append(c);
                text.text = sb.ToString();
                yield return new WaitForSeconds(speed);
            }
        }

        /// <summary>지문(무언의 행동)처럼 즉시 띄우는 짧은 표시.</summary>
        public void ShowInstant(string line)
        {
            text.text = line;
            targetAlpha = 1f;
        }

        public void Hide() => targetAlpha = 0f;
    }
}
