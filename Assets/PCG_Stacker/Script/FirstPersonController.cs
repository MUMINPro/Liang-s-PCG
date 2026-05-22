using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 第一人称控制器（收工版）
// 功能：WASD移动、鼠标视角、二段跳、右键贴墙吸附、体力系统、左上角体力条
// 用法：挂在带 CharacterController 的胶囊体上，摄像机作为子物体
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("移动")]
    public float moveSpeed = 6f;        // 移动速度
    public float jumpHeight = 2f;       // 每段跳跃高度
    public float gravity = -20f;        // 重力（负数）

    [Header("跳跃")]
    public int maxJumps = 2;            // 最大跳跃段数（2 = 二段跳）

    [Header("视角")]
    public Transform cameraTransform;   // 摄像机（眼睛），拖入子物体 Camera
    public float mouseSensitivity = 2f; // 鼠标灵敏度

    [Header("贴墙吸附")]
    public float wallCheckDistance = 1f;   // 检测前方墙的距离
    public float maxStamina = 5f;          // 吸附体力（秒）
    public float wallJumpForce = 8f;       // 蹬墙跳的力度

    // ===== 内部状态 =====
    private CharacterController controller;
    private Vector3 velocity;          // 速度（主要管垂直方向）
    private float pitch = 0f;          // 摄像机俯仰角
    private int jumpsLeft;             // 剩余跳跃次数
    private bool isWallSticking;       // 是否正贴在墙上
    private float currentStamina;      // 当前体力
    private bool needReleaseForStick;  // 蹬墙跳后是否需要先松开右键才能再吸附

    void Start()
    {
        controller = GetComponent<CharacterController>();
        currentStamina = maxStamina;

        // 锁定并隐藏鼠标
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        LookAround();   // 鼠标视角（任何状态都能转）

        if (isWallSticking)
            WallStickUpdate();   // 贴墙状态
        else
            NormalUpdate();      // 正常移动状态

        TryStartWallStick();     // 检测是否要开始吸附
    }

    // ===== 鼠标转视角 =====
    void LookAround()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);   // 左右转身体

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -90f, 90f);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // ===== 正常移动状态 =====
    void NormalUpdate()
    {
        bool isGrounded = controller.isGrounded;

        // 落地：重置跳跃次数、贴地
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
            jumpsLeft = maxJumps;   // 落地恢复跳跃
        }

        // 水平移动（相对视角方向）
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * moveSpeed * Time.deltaTime);

        // 跳跃 / 二段跳
        if (Input.GetButtonDown("Jump") && jumpsLeft > 0)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpsLeft--;
        }

        // 重力
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    // ===== 贴墙状态 =====
    void WallStickUpdate()
    {
        // 消耗体力
        currentStamina -= Time.deltaTime;

        // 体力耗尽 或 松开右键 → 脱离墙面
        if (currentStamina <= 0 || !Input.GetMouseButton(1))
        {
            isWallSticking = false;
            return;
        }

        // 蹬墙跳：从当前位置直接向上跳
        if (Input.GetButtonDown("Jump"))
        {
            isWallSticking = false;
            // 纯向上的跳跃速度（用和普通跳一样的高度公式）
            velocity = Vector3.up * Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpsLeft = maxJumps - 1;  // 蹬墙算第1段，空中还剩1段
            needReleaseForStick = true; // 蹬墙跳后必须先松开右键才能再次吸附
        }
        // 贴墙时不移动，velocity 保持 0（不掉落）
        else
        {
            velocity = Vector3.zero;
        }
    }

    // ===== 检测是否开始吸附 =====
    void TryStartWallStick()
    {
        if (isWallSticking) return;

        // 蹬墙跳后，必须先松开右键，才允许再次吸附
        if (needReleaseForStick)
        {
            if (!Input.GetMouseButton(1))
                needReleaseForStick = false;  // 已松开，解除限制
            else
                return;                        // 还按着，不允许吸附
        }

        // 必须按住右键
        if (!Input.GetMouseButton(1)) return;

        // 从身体中心朝正前方发一条射线，看有没有墙
        Ray ray = new Ray(transform.position, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, wallCheckDistance))
        {
            if (hit.collider.CompareTag("Wall"))
            {
                isWallSticking = true;
                currentStamina = maxStamina;   // 吸附时刷新体力满
                jumpsLeft = maxJumps;          // 吸附时刷新跳跃次数
                velocity = Vector3.zero;
            }
        }
    }

    // ===== 左上角体力条 =====
    void OnGUI()
    {
        // 背景框
        float barWidth = 200f;
        float barHeight = 20f;
        float x = 20f;
        float y = 20f;

        GUI.color = Color.black;
        GUI.Box(new Rect(x - 2, y - 2, barWidth + 4, barHeight + 4), "");

        // 体力条（绿色，按比例缩短）
        float ratio = Mathf.Clamp01(currentStamina / maxStamina);
        GUI.color = Color.green;
        GUI.DrawTexture(new Rect(x, y, barWidth * ratio, barHeight), Texture2D.whiteTexture);

        // 文字
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 60, y, 100, barHeight), $"体力 {currentStamina:F1}s");
    }
}