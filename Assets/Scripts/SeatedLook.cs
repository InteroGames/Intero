using UnityEngine;

namespace Interrogation
{
    /// <summary>
    /// 앉아 있는 주인공의 시점. 의자에 묶여 있으므로 이동은 없다.
    /// 마우스 오른쪽 버튼을 누른 동안만 둘러본다 - 왼쪽 버튼은 UI 입력용으로 남긴다.
    /// </summary>
    public class SeatedLook : MonoBehaviour
    {
        [SerializeField] float sensitivity = 2.2f;
        [SerializeField] float yawLimit = 38f;
        [SerializeField] float pitchLimit = 22f;

        [Header("숨 쉬는 흔들림")]
        [SerializeField] float swayAmount = 0.35f;
        [SerializeField] float swaySpeed = 0.6f;

        Quaternion baseRotation;
        float yaw, pitch;

        void Start() { baseRotation = transform.localRotation; }

        void Update()
        {
            if (Input.GetMouseButton(1))
            {
                yaw = Mathf.Clamp(yaw + Input.GetAxis("Mouse X") * sensitivity, -yawLimit, yawLimit);
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, -pitchLimit, pitchLimit);
            }

            float t = Time.time * swaySpeed;
            float swayX = (Mathf.PerlinNoise(t, 0f) - 0.5f) * swayAmount;
            float swayY = (Mathf.PerlinNoise(0f, t) - 0.5f) * swayAmount;

            transform.localRotation = baseRotation * Quaternion.Euler(pitch + swayY, yaw + swayX, 0f);
        }
    }
}
