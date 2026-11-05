using UnityEngine;

namespace ECS.Samples.QuickStart
{
    /// <summary>Attach to an empty scene object. The default World is already updated by WorldManager.</summary>
    public sealed class QuickStartDemo : MonoBehaviour
    {
        [SerializeField] private float speed = 1;
        private QuickStartSession session;
        private GameObject cube;

        private void Start()
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ECS QuickStart Cube";
            session = new QuickStartSession(speed);
        }

        private void LateUpdate()
        {
            // Only present the ECS result here. Do not tick the default World a second time.
            if (session != null && cube != null)
                cube.transform.position = new Vector3(session.Position, 0, 0);
        }

        private void OnGUI()
        {
            if (session == null) return;
            GUILayout.BeginArea(new Rect(12, 12, 300, 160));
            GUILayout.Label("ECS QuickStart | X = " + session.Position.ToString("F2"));
            GUILayout.Label("Speed = " + speed.ToString("F2") + " units/second");
            if (GUILayout.Button(session.IsPaused ? "Resume" : "Pause"))
            {
                if (session.IsPaused) session.Resume();
                else session.Pause();
            }
            if (GUILayout.Button("Restart"))
            {
                session.Dispose();
                session = new QuickStartSession(speed);
            }
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (session != null) session.Dispose();
            session = null;
            if (cube != null) Destroy(cube);
            cube = null;
        }
    }
}
