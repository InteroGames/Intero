using UnityEngine;

namespace Interrogation.Heist
{
    public enum InteractKind
    {
        Loot,     // 목표물. 3개를 다 가져가야 온전한 판이 된다.
        Conceal,  // 인멸. 시간을 먹고, 은폐 혐의의 근거를 만든다.
        Exit      // 탈출구. 어느 쪽으로 나가느냐가 남는 증거를 바꾼다.
    }

    /// <summary>
    /// 잠입 구간에서 플레이어가 건드릴 수 있는 것. 씬 생성기가 붙인다.
    /// 규칙은 여기 없다 - 전부 HeistDirector가 판정한다.
    /// </summary>
    public class HeistInteractable : MonoBehaviour
    {
        public InteractKind kind = InteractKind.Loot;

        [Tooltip("화면에 뜨는 이름. \"금시계\"")]
        public string label = "";

        [Tooltip("붙잡고 있어야 하는 시간. 0이면 즉시.")]
        public float holdSeconds = 0f;

        [Tooltip("Exit 전용. \"front\" 빠른 경로 / \"wall\" 우회 경로.")]
        public string routeId = "";

        [Tooltip("장갑을 벗어야만 열린다. 흔적이 진해진다.")]
        public bool bareHandsRequired = false;

        [Tooltip("경보가 울린 뒤에만 쓸 수 있다.")]
        public bool afterAlarmOnly = false;

        public bool Consumed { get; private set; }

        /// <summary>다 쓴 물건은 눈에 띄게 죽인다. 뭘 이미 했는지 헷갈리면 안 된다.</summary>
        public void Consume()
        {
            Consumed = true;

            if (kind == InteractKind.Loot)
            {
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
                return;
            }

            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var m = r.material;      // 인스턴스. 공유 머티리얼을 건드리면 에셋이 더러워진다.
                m.color = new Color(0.14f, 0.12f, 0.11f);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            }
        }
    }
}
