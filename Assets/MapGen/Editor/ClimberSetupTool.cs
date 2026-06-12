using UnityEditor;
using UnityEngine;

namespace MapGen.Editor
{
    public static class ClimberSetupTool
    {
        [MenuItem("Tools/MapGen/创建测试攀爬角色")]
        static void CreateClimber()
        {
            // 角色根：胶囊
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "TestClimber";
            // 移除自带碰撞，换 CharacterController
            Object.DestroyImmediate(player.GetComponent<Collider>());
            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f; cc.radius = 0.4f; cc.center = new Vector3(0, 0, 0);
            var ctrl = player.AddComponent<ClimberController>();

            // 相机挂在头部位置
            var cam = new GameObject("ClimberCamera");
            cam.transform.SetParent(player.transform, false);
            cam.transform.localPosition = new Vector3(0, 0.7f, 0);
            var camComp = cam.AddComponent<Camera>();
            cam.tag = "MainCamera";
            ctrl.cameraTransform = cam.transform;

            // 放到场景视图当前视角附近
            if (SceneView.lastActiveSceneView != null)
                player.transform.position = SceneView.lastActiveSceneView.pivot;
            else
                player.transform.position = new Vector3(0, 2, 0);

            Selection.activeGameObject = player;
            EditorGUIUtility.PingObject(player);
            Debug.Log("[攀爬角色] 已创建 TestClimber。\n" +
                "把它拖到起点平台上方，按 Play 开始爬。\n" +
                "WASD 移动 / 空格跳 / 贴墙按 W 攀爬 / 鼠标转视角 / Esc 解锁鼠标。\n" +
                "提示：把体力参数填成和你的 MapDesignConfig 一致。");
        }
    }
}
