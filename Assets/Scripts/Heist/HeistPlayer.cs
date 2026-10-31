using UnityEngine;

namespace Interrogation.Heist
{
    /// <summary>
    /// 잠입 구간의 1인칭 이동. 취조실의 SeatedLook과 달리 여기서는 걸어다닌다.
    /// 외부 입력 패키지를 쓰지 않는다 - 빌트인 Input 하나로 어디서 열어도 똑같이 돈다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HeistPlayer : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] float walkSpeed = 2.6f;
        [SerializeField] float runSpeed = 4.4f;
        [SerializeField] float sensitivity = 2.4f;
        [SerializeField] float pitchLimit = 82f;

        CharacterController cc;
        float pitch;
        float verticalVelocity;

        /// <summary>탈출이 끝났거나 잡혔을 때. 조작을 잠근다.</summary>
        public bool Frozen { get; set; }

        public Transform Head => head != null ? head : transform;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (head == null && Camera.main != null) head = Camera.main.transform;
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            if (Frozen) return;

            // 시연 중 창을 벗어났다 돌아오는 일이 흔하다. 클릭하면 다시 잠근다.
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            Look();
            Move();
        }

        void Look()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            float yaw = Input.GetAxis("Mouse X") * sensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, -pitchLimit, pitchLimit);

            transform.Rotate(0f, yaw, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move()
        {
            var wish = transform.right * Input.GetAxisRaw("Horizontal")
                     + transform.forward * Input.GetAxisRaw("Vertical");
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;

            // 계단이나 문턱에서 미끄러지지 않을 만큼의 최소 중력
            if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += Physics.gravity.y * Time.deltaTime;

            var motion = wish * speed;
            motion.y = verticalVelocity;
            cc.Move(motion * Time.deltaTime);
        }
    }
}
