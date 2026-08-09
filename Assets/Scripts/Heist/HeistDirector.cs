using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Interrogation.Heist
{
    /// <summary>
    /// 잠입 구간의 규칙 전부.
    ///
    /// 이 구간이 하는 일은 재미있는 액션이 아니라 <b>취조 난이도를 플레이어가 직접 정하게 하는 것</b>이다.
    /// 60초 탈출 구간은 절취 구간이 아니라 "증거 최소화 경로 선택" 구간이다.
    /// </summary>
    public class HeistDirector : MonoBehaviour
    {
        [SerializeField] HeistPlayer player;
        [SerializeField] HeistHUD hud;

        [Header("배선")]
        [SerializeField] Light[] alarmLights;
        [SerializeField] string inquiryScene = "Inquiry";

        [Header("밸런싱")]
        [SerializeField] float escapeLimit = 60f;
        [SerializeField] float interactRange = 2.8f;

        public const int TARGET_COUNT = 3;

        readonly HeistResult run = new HeistResult();

        bool alarmOn;
        bool finished;
        float timeLeft;
        float holdTimer;
        HeistInteractable holding;

        void Awake()
        {
            timeLeft = escapeLimit;
            SetAlarmLights(false);
        }

        void Start()
        {
            TranscriptLog.Begin();
            TranscriptLog.Write("=== 잠입 시작 ===");
            RefreshObjective();
            if (hud != null) StartCoroutine(hud.Notice("창을 넘었다.", 2.4f));
        }

        void Update()
        {
            if (finished) return;

            if (alarmOn)
            {
                timeLeft -= Time.deltaTime;
                if (hud != null)
                    hud.SetTimer($"{Mathf.Max(0f, timeLeft):00.0}", timeLeft <= 15f);

                if (timeLeft <= 0f) { StartCoroutine(Caught()); return; }
            }

            Aim();
        }

        // ---------------------------------------------------------------- 조준과 상호작용

        void Aim()
        {
            var target = RaycastTarget();

            if (target == null)
            {
                CancelHold();
                if (hud != null) hud.SetPrompt(null);
                return;
            }

            if (!Usable(target, out string reason))
            {
                CancelHold();
                if (hud != null) hud.SetPrompt(reason);
                return;
            }

            if (hud != null) hud.SetPrompt(PromptFor(target));

            if (!Input.GetKey(KeyCode.E)) { CancelHold(); return; }

            if (holding != target) { holding = target; holdTimer = 0f; }

            holdTimer += Time.deltaTime;
            if (hud != null && target.holdSeconds > 0f)
                hud.SetHold(holdTimer / target.holdSeconds);

            if (holdTimer >= target.holdSeconds) Perform(target);
        }

        HeistInteractable RaycastTarget()
        {
            var head = player != null ? player.Head : Camera.main?.transform;
            if (head == null) return null;

            if (!Physics.Raycast(head.position, head.forward, out RaycastHit hit, interactRange))
                return null;

            var it = hit.collider.GetComponentInParent<HeistInteractable>();
            return it != null && !it.Consumed ? it : null;
        }

        void CancelHold()
        {
            holding = null;
            holdTimer = 0f;
            if (hud != null) hud.SetHold(0f);
        }

        /// <summary>지금 쓸 수 있는가. 못 쓰면 왜 못 쓰는지를 준다.</summary>
        bool Usable(HeistInteractable it, out string reason)
        {
            reason = null;
            if (it.afterAlarmOnly && !alarmOn)
            {
                reason = $"{it.label} — 경비가 자리를 지키고 있다";
                return false;
            }
            return true;
        }

        string PromptFor(HeistInteractable it)
        {
            switch (it.kind)
            {
                case InteractKind.Loot:
                    return it.bareHandsRequired
                        ? $"[E] {it.label} — 장갑을 벗고 다이얼을 돌린다"
                        : $"[E] {it.label} 을(를) 챙긴다";
                case InteractKind.Conceal:
                    return $"[E] {it.label} 을(를) 태운다  (붙잡고 있어야 한다)";
                case InteractKind.Exit:
                    return it.routeId == "front"
                        ? $"[E] {it.label} — 빠르다. 흔적이 남는다."
                        : $"[E] {it.label} — 멀다. 자루를 버려야 넘는다.";
            }
            return null;
        }

        void Perform(HeistInteractable it)
        {
            CancelHold();

            switch (it.kind)
            {
                case InteractKind.Loot:
                    it.Consume();
                    run.stolen++;
                    if (it.bareHandsRequired) run.glovesOff = true;
                    TranscriptLog.Write($"  [절취] {it.label}" + (it.bareHandsRequired ? " (맨손)" : ""));
                    RefreshObjective();
                    if (run.stolen >= TARGET_COUNT) StartCoroutine(RaiseAlarm());
                    else if (hud != null) StartCoroutine(hud.Notice($"{it.label}", 1.2f));
                    break;

                case InteractKind.Conceal:
                    it.Consume();
                    run.concealed = true;
                    TranscriptLog.Write("  [인멸] 출입 장부를 태웠다");
                    RefreshObjective();
                    if (hud != null) StartCoroutine(hud.Notice("장부가 탄다. 재는 남는다.", 1.8f));
                    break;

                case InteractKind.Exit:
                    StartCoroutine(Escape(it));
                    break;
            }
        }

        // ---------------------------------------------------------------- 국면 전환

        IEnumerator RaiseAlarm()
        {
            alarmOn = true;
            timeLeft = escapeLimit;
            SetAlarmLights(true);
            TranscriptLog.Write("  [경보] 마지막 목표물. 종이 울린다.");
            RefreshObjective();
            if (hud != null) yield return hud.Notice("종이 울린다.", 2.0f);
        }

        IEnumerator Escape(HeistInteractable exit)
        {
            finished = true;
            run.route = exit.routeId;
            run.escapeSeconds = alarmOn ? escapeLimit - timeLeft : 0f;
            run.caught = false;

            if (player != null) player.Frozen = true;
            if (hud != null)
            {
                hud.SetPrompt(null);
                hud.NoticeInstant(exit.routeId == "front" ? "정문으로 나왔다." : "담을 넘었다.");
            }

            TranscriptLog.Write($"  [탈출] {exit.routeId} / {run.escapeSeconds:0.0}초");
            yield return HandOff(1.6f);
        }

        IEnumerator Caught()
        {
            finished = true;
            run.caught = true;
            run.route = "";
            run.escapeSeconds = escapeLimit;

            if (player != null) player.Frozen = true;
            if (hud != null)
            {
                hud.SetPrompt(null);
                hud.SetTimer("00.0", true);
                hud.NoticeInstant("불이 켜진다.");
            }

            TranscriptLog.Write("  [검거] 60초 안에 나가지 못했다");
            yield return HandOff(2.0f);
        }

        /// <summary>결과를 봉인해서 취조로 넘긴다. 여기가 두 페이즈의 유일한 접합부다.</summary>
        IEnumerator HandOff(float hold)
        {
            yield return new WaitForSeconds(hold);
            PhaseBridge.Publish(run);

            if (hud != null) yield return hud.FadeOut(1.2f);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (Application.CanStreamedLevelBeLoaded(inquiryScene)) SceneManager.LoadScene(inquiryScene);
            else TranscriptLog.Write($"  [경고] '{inquiryScene}' 씬이 빌드 세팅에 없다. 취조로 넘어가지 못했다.");
        }

        // ---------------------------------------------------------------- 표시

        void RefreshObjective()
        {
            if (hud == null) return;

            if (!alarmOn)
            {
                hud.SetObjective($"물건  {run.stolen} / {TARGET_COUNT}" +
                                 (run.concealed ? "\n장부  소각" : ""));
                return;
            }

            hud.SetObjective("나가라." + (run.concealed ? "\n장부  소각" : ""));
        }

        void SetAlarmLights(bool on)
        {
            if (alarmLights == null) return;
            foreach (var l in alarmLights)
            {
                if (l == null) continue;
                l.enabled = on;
            }
        }
    }
}
