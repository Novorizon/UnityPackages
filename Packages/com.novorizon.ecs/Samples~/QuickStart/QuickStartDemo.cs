using UnityEngine;

namespace ECS.Samples.QuickStart
{
    /// <summary>Attach to an empty scene object. The default World is already updated by WorldManager.</summary>
    /// <remarks>快速入门对应的示例。演示该模块的典型调用方式。</remarks>
    public sealed class QuickStartDemo : MonoBehaviour
    {
        [SerializeField]
        private float speed = 1;
        private QuickStartSession session;
        private GameObject cube;
        /// <summary>启动快速入门示例。</summary>
        private void Start()
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ECS QuickStart Cube";
            session = new QuickStartSession(speed);
        }

        /// <summary>在常规帧更新以后执行本组件的处理。</summary>
        private void LateUpdate()
        {
            // Only present the ECS result here. Do not tick the default World a second time.
            if (session != null && cube != null)
            {
                cube.transform.position = new Vector3(session.Position, 0, 0);
            }
        }

        /// <summary>绘制当前界面并响应对应的 GUI 事件。</summary>
        private void OnGUI()
        {
            if (session == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 300, 160));
            GUILayout.Label("ECS QuickStart | X = " + session.Position.ToString("F2"));
            GUILayout.Label("Speed = " + speed.ToString("F2") + " units/second");
            if (GUILayout.Button(session.IsPaused ? "Resume" : "Pause"))
            {
                if (session.IsPaused)
                {
                    session.Resume();
                }
                else
                {
                    session.Pause();
                }
            }

            if (GUILayout.Button("Restart"))
            {
                session.Dispose();
                session = new QuickStartSession(speed);
            }

            GUILayout.EndArea();
        }

        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        private void OnDestroy()
        {
            if (session != null)
            {
                session.Dispose();
            }

            session = null;
            if (cube != null)
            {
                Destroy(cube);
            }

            cube = null;
        }
    }
}
