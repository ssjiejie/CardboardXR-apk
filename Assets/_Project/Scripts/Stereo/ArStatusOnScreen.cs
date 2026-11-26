using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace CardboardXR.Stereo
{
    /// <summary>
    /// Draws why the camera is black, on the screen itself.
    ///
    /// Every camera-dependent feature in this project (passthrough, QR scanning, the AR debug view)
    /// needs three things at runtime: XR Plug-in Management must have started the ARCore loader,
    /// ARCore must have created the camera subsystem, and the subsystem must have built its camera
    /// material. When one of them is missing nothing throws — the screen just stays black — and the
    /// only trace is a line in logcat that nobody reads on a phone. This puts that line where the
    /// person holding the phone can see it, and hides itself when everything is fine.
    /// </summary>
    public class ArStatusOnScreen : MonoBehaviour
    {
        static ArStatusOnScreen s_Instance;

        ARSession m_Session;
        ARCameraManager m_Camera;
        string m_Text = "";
        float m_NextPoll;
        bool m_Healthy;
        GUIStyle m_Style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (s_Instance != null)
                return;
            var go = new GameObject("AR Status");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<ArStatusOnScreen>();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextPoll)
                return;
            m_NextPoll = Time.unscaledTime + 0.5f;

            if (m_Session == null)
                m_Session = FindFirstObjectByType<ARSession>();
            if (m_Camera == null)
                m_Camera = FindFirstObjectByType<ARCameraManager>();

            var manager = XRGeneralSettings.Instance == null ? null : XRGeneralSettings.Instance.Manager;
            var loader = manager == null || manager.activeLoader == null ? "none" : manager.activeLoader.name;
            var session = m_Session == null ? "not in scene" : ARSession.state.ToString();
            var subsystem = m_Session != null && m_Session.subsystem != null ? "ok" : "none";
            var material = m_Camera != null && m_Camera.cameraMaterial != null ? "ok" : "NULL";
            var permission = Application.HasUserAuthorization(UserAuthorization.WebCam) ? "granted" : "denied";

            m_Healthy = loader != "none" && subsystem == "ok" && material == "ok" && permission == "granted";

            var text = new StringBuilder();
            text.AppendLine($"=== build v{Application.version} ===");
            text.AppendLine($"XR loader: {loader}");
            text.AppendLine($"AR session: {session}");
            text.AppendLine($"camera subsystem: {subsystem}");
            text.AppendLine($"camera material: {material}");
            text.AppendLine($"camera permission: {permission}");
            m_Text = text.ToString();
        }

        void OnGUI()
        {
            // Everything is running: stay out of the way after the first few seconds.
            if (m_Healthy && Time.unscaledTime > 15f)
                return;

            if (m_Style == null)
                m_Style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(22, Screen.height / 28),
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                };

            var w = Screen.width * 0.9f;
            var h = m_Style.fontSize * 7.5f;
            var rect = new Rect(Screen.width * 0.05f, Screen.height * 0.04f, w, h);
            // Unhealthy: red wash so it cannot be mistaken for part of the app's own UI.
            GUI.color = m_Healthy ? new Color(0f, 0f, 0f, 0.55f) : new Color(0.5f, 0f, 0f, 0.75f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            m_Style.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x + 16, rect.y + 10, rect.width - 32, rect.height - 20), m_Text, m_Style);
        }
    }
}
