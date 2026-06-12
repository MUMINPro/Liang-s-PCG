using UnityEngine;

// ============================================================
// 攀爬角色控制器 v0.1（糙但能爬）
// 用于验证整座山的攀爬节奏，不是最终手感。
//
// 能力：
//   - 地面跑动（WASD）+ 跳跃（空格）
//   - 贴近可攀爬面时自动进入攀爬态，WASD 沿墙面移动
//   - 体力槽：攀爬消耗、地面恢复，耗尽则从墙上滑落
//
// 用法：
//   1. 建一个胶囊体或空物体，挂 CharacterController + 本脚本
//   2. 把相机设为子物体或拖到 cameraTransform
//   3. 运行，从起点平台开始爬
//
// 体力参数请填成和 MapDesignConfig 一致，这样爬的感受才对得上设计。
// ============================================================

namespace MapGen
{
    [RequireComponent(typeof(CharacterController))]
    public class ClimberController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("跟随相机。留空则自动找主相机")]
        public Transform cameraTransform;

        [Header("移动")]
        public float walkSpeed = 5f;
        public float jumpHeight = 1.5f;
        public float gravity = -20f;

        [Header("攀爬")]
        [Tooltip("检测可攀爬面的距离")]
        public float climbCheckDistance = 0.8f;
        [Tooltip("攀爬移动速度")]
        public float climbSpeed = 3f;
        [Tooltip("可攀爬的层。默认 Everything，正式开发时设成专门的层")]
        public LayerMask climbableMask = ~0;

        [Header("体力（建议与 MapDesignConfig 一致）")]
        public float maxStamina = 100f;
        [Tooltip("攀爬消耗：体力/秒")]
        public float climbCostPerSecond = 15f;
        [Tooltip("地面恢复：体力/秒")]
        public float recoverPerSecond = 25f;
        public float currentStamina;

        [Header("调试显示")]
        public bool showStaminaBar = true;

        CharacterController cc;
        Vector3 velocity;
        bool isClimbing;
        float yaw, pitch;

        void Start()
        {
            cc = GetComponent<CharacterController>();
            currentStamina = maxStamina;
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
            Cursor.lockState = CursorLockMode.Locked;
        }

        void Update()
        {
            HandleMouseLook();
            if (isClimbing) ClimbMove();
            else GroundMove();
            UpdateStamina();
        }

        // ---------------- 视角 ----------------
        void HandleMouseLook()
        {
            if (cameraTransform == null) return;
            yaw += Input.GetAxis("Mouse X") * 2.5f;
            pitch -= Input.GetAxis("Mouse Y") * 2.5f;
            pitch = Mathf.Clamp(pitch, -80f, 80f);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        // ---------------- 地面移动 ----------------
        void GroundMove()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 move = (transform.right * h + transform.forward * v).normalized * walkSpeed;

            if (cc.isGrounded && velocity.y < 0) velocity.y = -2f;
            if (cc.isGrounded && Input.GetButtonDown("Jump"))
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            velocity.y += gravity * Time.deltaTime;

            cc.Move((move + Vector3.up * velocity.y) * Time.deltaTime);

            // 贴墙 + 还有体力 + 按住前进 → 进入攀爬
            if (!cc.isGrounded && currentStamina > 1f && v > 0.1f && WallAhead(out _))
                isClimbing = true;
        }

        // ---------------- 攀爬移动 ----------------
        void ClimbMove()
        {
            if (!WallAhead(out Vector3 normal) || currentStamina <= 0f)
            {
                isClimbing = false;
                return;
            }
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            // 墙面坐标系：沿墙的上方向和右方向移动
            Vector3 wallUp = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            Vector3 wallRight = Vector3.Cross(wallUp, normal).normalized;
            Vector3 climbDir = (wallUp * v + wallRight * h).normalized;

            cc.Move(climbDir * climbSpeed * Time.deltaTime);
            // 轻微贴墙，避免飘离
            cc.Move(-normal * 0.5f * Time.deltaTime);

            // 攀爬中跳跃 = 蹬墙跃出
            if (Input.GetButtonDown("Jump"))
            {
                isClimbing = false;
                velocity = (normal + Vector3.up * 1.2f).normalized *
                           Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            // 爬到顶部（前方无墙、上方是地面）自动翻上去
            if (v > 0.1f && !WallAhead(out _))
            {
                isClimbing = false;
                cc.Move((wallUp + transform.forward).normalized * 1.5f);
            }
        }

        bool WallAhead(out Vector3 normal)
        {
            normal = Vector3.zero;
            Vector3 origin = transform.position;
            if (Physics.Raycast(origin, transform.forward, out RaycastHit hit,
                climbCheckDistance, climbableMask))
            {
                // 只认接近垂直的面为可攀爬墙
                if (Vector3.Angle(hit.normal, Vector3.up) > 50f)
                {
                    normal = hit.normal;
                    return true;
                }
            }
            return false;
        }

        // ---------------- 体力 ----------------
        void UpdateStamina()
        {
            if (isClimbing)
                currentStamina -= climbCostPerSecond * Time.deltaTime;
            else if (cc.isGrounded)
                currentStamina += recoverPerSecond * Time.deltaTime;
            currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);

            // 体力耗尽：从墙上滑落
            if (currentStamina <= 0f && isClimbing) isClimbing = false;
        }

        // ---------------- 屏幕体力条 ----------------
        void OnGUI()
        {
            if (!showStaminaBar) return;
            float w = 200f, h = 22f;
            float x = 20f, y = Screen.height - 50f;
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(Color.red, Color.green, currentStamina / maxStamina);
            GUI.DrawTexture(new Rect(x, y, w * (currentStamina / maxStamina), h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, w, h),
                $"  体力 {currentStamina:F0}/{maxStamina:F0}" + (isClimbing ? "  [攀爬中]" : ""));
        }
    }
}
