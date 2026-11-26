using System.Collections;
using UnityEngine;
using UnityEngine.Android;

namespace CardboardXR.Demos.Video
{
    // The phone's camera, drawn behind the video sphere: where the green screen is cut out of the
    // video, the room the camera sees shows through, so the people in it stand in the user's own
    // room. One flat quad, far behind the sphere (which is at most 30 m out when zoomed all the way
    // in), turned with the head, because the camera turns with it too: the picture stays a window
    // ahead whatever way the user looks, rather than sliding away.
    // The frame is a 2D image, so both eyes see the same one; at 60 m the eyes' separation is far
    // below a pixel. Off until asked for: the camera is the phone's, and its battery and heat with it.
    // Logs [Camera].
    public class CameraBackdrop : MonoBehaviour
    {
        // Far enough behind the sphere at its widest (15 m radius / 0.5 zoom = 30 m).
        const float k_Distance = 60f;
        // Half the height the quad covers, in degrees. Wide enough for a Cardboard eye's view, so
        // the picture reaches the edges of each eye; a smaller figure shrinks it into a window.
        const float k_HalfHeightDegrees = 55f;
        const int k_RequestWidth = 1280;
        const int k_RequestHeight = 720;
        const int k_RequestFps = 30;

        static readonly int k_RotationId = Shader.PropertyToID("_Rotation");
        static readonly int k_MirrorId = Shader.PropertyToID("_MirrorX");

        Material m_Material;
        MeshRenderer m_Renderer;
        WebCamTexture m_Camera;
        Coroutine m_Opening;
        bool m_On;
        bool m_Front;

        // Whether the camera is on, or on its way on. False also while the permission is pending.
        public bool IsOn => m_On;

        // Which camera to open: false is the one on the back, facing the room; true the one on the
        // front, facing the user. Takes effect the next time it's shown.
        public bool Front
        {
            get => m_Front;
            set
            {
                if (m_Front == value)
                    return;
                m_Front = value;
                if (m_On)
                {
                    Stop();
                    m_On = true;
                    m_Opening = StartCoroutine(Open());
                }
            }
        }

        void Awake()
        {
            var source = Resources.Load<Material>("ChromaKey");
            var shader = source != null ? source.shader : Resources.Load<Shader>("ChromaKey");
            if (shader == null)
            {
                Debug.LogWarning("[Camera] Resources/ChromaKey is missing; there's no camera backdrop.");
                return;
            }
            m_Material = new Material(shader) { name = "Camera Backdrop" };
            m_Material.SetFloat("_ChromaKey", 0f);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Camera Backdrop";
            var collider = quad.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            quad.transform.SetParent(transform, false);
            m_Renderer = quad.GetComponent<MeshRenderer>();
            m_Renderer.sharedMaterial = m_Material;
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.enabled = false;
        }

        void OnDestroy() => Stop();

        // Turns the camera on or off. Turning it on asks for the camera permission the first time;
        // until it's granted nothing is shown, and IsOn stays false.
        public void Show(bool on)
        {
            if (on == m_On)
                return;
            m_On = on;
            if (on)
            {
                if (m_Material == null)
                {
                    m_On = false;
                    return;
                }
                m_Opening = StartCoroutine(Open());
            }
            else
            {
                Stop();
            }
        }

        void Stop()
        {
            if (m_Opening != null)
            {
                StopCoroutine(m_Opening);
                m_Opening = null;
            }
            if (m_Renderer != null)
                m_Renderer.enabled = false;
            if (m_Camera != null)
            {
                if (m_Camera.isPlaying)
                    m_Camera.Stop();
                Destroy(m_Camera);
                m_Camera = null;
            }
            if (m_Material != null)
                m_Material.mainTexture = null;
            Debug.Log("[Camera] Off");
        }

        IEnumerator Open()
        {
            if (!HasCameraPermission)
            {
                Permission.RequestUserPermission(Permission.Camera);
                // The dialog is asynchronous; Android answers within a frame or two of the tap.
                for (var waited = 0f; waited < 20f && !HasCameraPermission; waited += 0.25f)
                    yield return new WaitForSecondsRealtime(0.25f);
                if (!HasCameraPermission)
                {
                    Debug.LogWarning("[Camera] Camera permission not granted");
                    m_On = false;
                    m_Opening = null;
                    yield break;
                }
            }

            var device = FindDevice(m_Front);
            if (device == null)
            {
                Debug.LogWarning("[Camera] No camera on this device");
                m_On = false;
                m_Opening = null;
                yield break;
            }

            m_Camera = new WebCamTexture(device, k_RequestWidth, k_RequestHeight, k_RequestFps);
            m_Camera.Play();
            // The size and rotation are only known once the camera started.
            for (var waited = 0f; waited < 5f && m_Camera.width <= 16; waited += 0.1f)
                yield return new WaitForSecondsRealtime(0.1f);

            m_Material.mainTexture = m_Camera;
            ApplyOrientation();
            m_Renderer.enabled = true;
            m_Opening = null;
            Debug.Log($"[Camera] {device} {m_Camera.width}x{m_Camera.height} at {m_Camera.requestedFPS} fps, " +
                      $"rotation {m_Camera.videoRotationAngle}, mirrored {m_Camera.videoVerticallyMirrored}");
        }

        // The size the quad was last laid out for, so it's only rebuilt when the camera reports
        // something new rather than every frame.
        int m_LaidOutWidth;
        int m_LaidOutHeight;
        int m_LaidOutRotation = int.MinValue;

        void ApplyOrientation()
        {
            if (m_Camera == null)
                return;
            if (m_Camera.width == m_LaidOutWidth && m_Camera.height == m_LaidOutHeight &&
                m_Camera.videoRotationAngle == m_LaidOutRotation)
                return;
            m_LaidOutWidth = m_Camera.width;
            m_LaidOutHeight = m_Camera.height;
            m_LaidOutRotation = m_Camera.videoRotationAngle;

            var quarter = Mathf.RoundToInt(m_Camera.videoRotationAngle / 90f) % 4;
            m_Material.SetFloat(k_RotationId, quarter);
            m_Material.SetFloat(k_MirrorId, m_Camera.videoVerticallyMirrored ? 1f : 0f);

            // The camera's own shape, turned the way the frame is: a portrait frame makes a tall quad.
            var aspect = m_Camera.height > 0 ? (float)m_Camera.width / m_Camera.height : 16f / 9f;
            if (quarter == 1 || quarter == 3)
                aspect = 1f / aspect;
            var height = 2f * k_Distance * Mathf.Tan(k_HalfHeightDegrees * Mathf.Deg2Rad);
            transform.localScale = new Vector3(height * aspect, height, 1f);
        }

        // Android is the only platform that asks; elsewhere the camera either needs no permission or
        // its own runtime request, which this demo doesn't carry.
        static bool HasCameraPermission =>
            Application.platform != RuntimePlatform.Android || Permission.HasUserAuthorizedPermission(Permission.Camera);

        static string FindDevice(bool front)
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
                return null;
            foreach (var device in devices)
            {
                var name = device.name.ToLowerInvariant();
                var isFront = device.isFrontFacing || name.Contains("front") || name.Contains("自拍");
                if (isFront == front)
                    return device.name;
            }
            return devices[0].name;
        }

        void LateUpdate()
        {
            if (!m_On)
                return;
            var core = CoreServices.Instance;
            if (core == null)
                return;
            var head = core.Head;
            transform.SetPositionAndRotation(head.position + head.forward * k_Distance, head.rotation);
            if (m_Camera != null && m_Camera.width > 16)
                ApplyOrientation();
        }
    }
}
