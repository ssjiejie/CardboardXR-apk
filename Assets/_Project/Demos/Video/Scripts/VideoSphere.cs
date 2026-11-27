using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardboardXR.Demos.Video
{
    // The surface a 360 or 180 video is drawn on: a large sphere, or its front half, around the
    // viewer, textured from the inside. A 3D video gets one per eye, each on a layer only that eye's
    // camera draws (StereoRig.LeftEyeOnlyLayer and RightEyeOnlyLayer) and showing that eye's part
    // of the frame. It stays centered on the head, turning with nothing, so moving the phone never
    // shows the video as a ball around you: only turning looks around. While a video shows, the
    // whole sphere is clickable (Clicked), for toggling the controls with a tap anywhere.
    public class VideoSphere : MonoBehaviour
    {
        const int k_Rings = 48;
        const int k_Segments = 96;

        [Tooltip("An unlit material whose main texture the video is drawn into. Copied for each eye.")]
        [SerializeField] Material m_Material;
        [Tooltip("Meters. Beyond anything else drawn in the demo, such as its panels, and within the gaze's reach " +
                 "(GazePointer's max distance, 20 m) so a tap on it registers.")]
        [SerializeField] float m_Radius = 15f;

        // The green-screen surface, in Resources so neither the scene nor a prefab has to hold it.
        // Loaded as the material rather than the shader: a shader only reached through
        // Resources.Load<Shader> had its variants stripped from the build, which turned the sphere
        // magenta the moment the key was switched on. The material asset keeps them.
        const string k_ChromaAssetName = "ChromaKey";
        // The key is a switch at 1; how much green it takes is _Tolerance, set from the panel's
        // slider rather than fixed here.
        const float k_ChromaStrength = 1f;
        static readonly int k_ChromaKeyId = Shader.PropertyToID("_ChromaKey");
        static readonly int k_ToleranceId = Shader.PropertyToID("_Tolerance");

        // How much of the screen's green is cut: 0 takes only a saturated screen, 1 takes any green
        // cast with it. 0.6 is a lit screen, and still leaves skin and khaki alone.
        public const float DefaultTolerance = 0.6f;
        float m_Tolerance = DefaultTolerance;

        readonly MeshRenderer[] m_Eyes = new MeshRenderer[2];
        readonly Material[] m_Materials = new Material[2];
        Mesh m_Full;
        Mesh m_Half;
        GameObject m_ClickSurface;
        Shader m_ChromaShader;
        bool m_Chroma;
        // What Show last drew, so switching the shader can put the frame back on the new material.
        Texture m_Texture;
        Vector2 m_TextureScale = Vector2.one;
        Vector2 m_TextureOffset;
        // 1 shows the whole frame the eye is meant to see; more takes a smaller part of it, so the
        // picture fills more of the view.
        float m_Zoom = 1f;

        // The most the picture can be enlarged and shrunk, so it can't be lost by zooming. Below 1
        // the frame is sampled outside itself and its edge stretches; above 1 the picture is cropped.
        public const float MinZoom = 0.75f;
        public const float MaxZoom = 4f;

        public float Zoom => m_Zoom;

        // A tap or pinch with the gaze on the video, rather than on a panel.
        public event Action Clicked;

        void Awake()
        {
            var chromaSource = Resources.Load<Material>(k_ChromaAssetName);
            m_ChromaShader = chromaSource != null ? chromaSource.shader : Resources.Load<Shader>(k_ChromaAssetName);
            if (m_ChromaShader == null)
                Debug.LogWarning($"[Video] Resources/{k_ChromaAssetName} is missing; the green screen can't be cut out.");
            else if (!m_ChromaShader.isSupported)
            {
                Debug.LogWarning($"[Video] Shader {m_ChromaShader.name} isn't supported on this device ({Application.platform}); " +
                                 "the green screen can't be cut out.");
                m_ChromaShader = null;
            }
            m_Full = BuildMesh(Projection.Full360, m_Radius);
            m_Half = BuildMesh(Projection.Half180, m_Radius);
            for (var eye = 0; eye < 2; eye++)
            {
                var eyeObject = new GameObject(eye == 0 ? "Left Eye" : "Right Eye");
                eyeObject.transform.SetParent(transform, false);
                eyeObject.AddComponent<MeshFilter>();
                m_Eyes[eye] = eyeObject.AddComponent<MeshRenderer>();
                m_Eyes[eye].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_Eyes[eye].receiveShadows = false;
                m_Materials[eye] = new Material(m_Material) { name = $"Video {eyeObject.name}" };
                m_Eyes[eye].sharedMaterial = m_Materials[eye];
                eyeObject.SetActive(false);
            }

            // Always the whole sphere, so a tap anywhere counts, even outside a 180 video. It faces
            // inward, which is the side the gaze's ray meets.
            m_ClickSurface = new GameObject("Click Surface");
            m_ClickSurface.transform.SetParent(transform, false);
            m_ClickSurface.AddComponent<MeshCollider>().sharedMesh = m_Full;
            m_ClickSurface.AddComponent<ClickRelay>().Clicked += () => Clicked?.Invoke();
            m_ClickSurface.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var material in m_Materials)
                Destroy(material);
            Destroy(m_Full);
            Destroy(m_Half);
        }

        void LateUpdate()
        {
            var core = CoreServices.Instance;
            if (core != null)
                transform.SetPositionAndRotation(core.Head.position, Quaternion.identity);
        }

        // Shows the texture in the format, or hides the sphere for null.
        public void Show(Texture texture, VideoFormat format)
        {
            m_Texture = texture;
            m_ClickSurface.SetActive(texture != null);
            for (var eye = 0; eye < 2; eye++)
            {
                var shown = texture != null && (eye == 0 || format.IsStereo);
                m_Eyes[eye].gameObject.SetActive(shown);
                if (!shown)
                    continue;
                // A 2D video is on the ordinary layer, for both eyes; a 3D one gives each eye its own.
                m_Eyes[eye].gameObject.layer = !format.IsStereo ? 0
                    : eye == 0 ? Stereo.StereoRig.LeftEyeOnlyLayer : Stereo.StereoRig.RightEyeOnlyLayer;
                m_Eyes[eye].GetComponent<MeshFilter>().sharedMesh = format.Projection == Projection.Full360 ? m_Full : m_Half;
                var rect = format.EyeRect(eye);
                m_TextureScale = rect.size;
                m_TextureOffset = rect.position;
                PutTextureOn(m_Materials[eye]);
            }
        }

        // Cuts the green out of the video, or puts it back. Safe to call before a video plays, and
        // while one does: the frame already on screen is moved onto the other shader. Returns false
        // when the key was asked for and this build has no working shader for it.
        public bool SetChromaKey(bool on)
        {
            if (m_Chroma == on)
                return true;
            if (on && m_ChromaShader == null)
                return false;
            m_Chroma = on;
            foreach (var material in m_Materials)
            {
                material.shader = on ? m_ChromaShader : m_Material.shader;
                if (on)
                {
                    material.SetFloat(k_ChromaKeyId, k_ChromaStrength);
                    material.SetFloat(k_ToleranceId, m_Tolerance);
                }
                PutTextureOn(material);
            }
            Debug.Log($"[Video] Green screen {(on ? "on" : "off")}");
            return true;
        }

        // How much green the key takes, 0 to 1. Safe while a video plays: the frame already on
        // screen is drawn by the same shader with the new value on the next one.
        public void SetTolerance(float tolerance)
        {
            m_Tolerance = Mathf.Clamp01(tolerance);
            foreach (var material in m_Materials)
                material.SetFloat(k_ToleranceId, m_Tolerance);
            Debug.Log($"[Video] Green tolerance {m_Tolerance:P0}");
        }

        public float Tolerance => m_Tolerance;

        // Enlarges the picture (>1) or shrinks it (<1). The sphere is drawn around the head, so
        // moving it closer changes nothing on screen: every direction the eye looks maps to the same
        // texture coordinate however far the surface is. Zooming means sampling a smaller part of the
        // frame instead, which is why this rewrites the texture transform rather than scaling the
        // transform, which is what it used to do and why the buttons did nothing.
        public void SetZoom(float zoom)
        {
            m_Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            foreach (var material in m_Materials)
                PutTextureOn(material);
            Debug.Log($"[Video] Zoom {m_Zoom:F2}x");
        }

        void PutTextureOn(Material material)
        {
            if (m_Texture == null)
                return;
            // mainTexture is the shader's [MainTexture]: _BaseMap on both URP/Unlit and ChromaKey.
            material.mainTexture = m_Texture;
            // The part of the frame this eye shows: the whole picture, or half of a stereo one.
            var eye = m_TextureScale;
            var size = eye / m_Zoom;
            var center = m_TextureOffset + eye * 0.5f;
            material.mainTextureScale = size;
            material.mainTextureOffset = center - size * 0.5f;
        }

        // The texture coordinate for a direction given as longitude (0 ahead, positive to the right)
        // and latitude (positive up), in radians. Equirectangular: across is longitude and up is
        // latitude, with straight ahead in the middle of the frame. A 180 video spans only the
        // front half.
        public static Vector2 Uv(float longitude, float latitude, Projection projection)
        {
            var span = projection == Projection.Full360 ? 2f * Mathf.PI : Mathf.PI;
            return new Vector2(0.5f + longitude / span, 0.5f + latitude / Mathf.PI);
        }

        public static Vector3 Direction(float longitude, float latitude) =>
            new(Mathf.Cos(latitude) * Mathf.Sin(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Cos(longitude));

        // A sphere or its front half, facing inward. The seam repeats its column of vertices so the
        // texture doesn't wrap backwards across one strip.
        public static Mesh BuildMesh(Projection projection, float radius)
        {
            var half = projection == Projection.Half180;
            var segments = half ? k_Segments / 2 : k_Segments;
            var span = half ? Mathf.PI : 2f * Mathf.PI;
            var vertices = new Vector3[(k_Rings + 1) * (segments + 1)];
            var uvs = new Vector2[vertices.Length];
            for (var ring = 0; ring <= k_Rings; ring++)
            {
                var latitude = -Mathf.PI / 2f + Mathf.PI * ring / k_Rings;
                for (var segment = 0; segment <= segments; segment++)
                {
                    var longitude = -span / 2f + span * segment / segments;
                    var index = ring * (segments + 1) + segment;
                    vertices[index] = Direction(longitude, latitude) * radius;
                    uvs[index] = Uv(longitude, latitude, projection);
                }
            }

            var triangles = new int[k_Rings * segments * 6];
            var t = 0;
            for (var ring = 0; ring < k_Rings; ring++)
            {
                for (var segment = 0; segment < segments; segment++)
                {
                    var a = ring * (segments + 1) + segment;
                    var b = a + segments + 1;
                    // Clockwise as seen from the center, which Unity draws as the front.
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = a + 1;
                    triangles[t++] = a + 1;
                    triangles[t++] = b;
                    triangles[t++] = b + 1;
                }
            }

            var mesh = new Mesh
            {
                name = half ? "Video Hemisphere" : "Video Sphere",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = vertices,
                uv = uvs,
                triangles = triangles,
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // Passes a gaze click on this collider to whoever listens.
    public class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public event Action Clicked;

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
