using CardboardXR.Demos;
using CardboardXR.Demos.Video;
using UnityEditor;
using UnityEngine;

namespace ProjectBootstrap
{
    // 360 Video: a sphere to draw on, the player and its panel (built at runtime).
    class VideoSetup : DemoSetup
    {
        const string ChromaKeyShaderName = "CardboardXR/ChromaKey";

        public override string Name => "Video";
        public override int Order => 20;

        public override DemoCatalog.Entry Entry => new()
        {
            Id = "video",
            Title = "360 Video",
            Description = "Watch 360 and VR180 videos, 2D or 3D, all around you. Tap the video for the controls. Plays videos copied to the phone or linked from Google Drive.",
            Tagline = "360 and VR180 videos all around you",
            Icon = "360",
            Tint = new Color(0.7f, 0.25f, 0.29f),
            // 6DoF and passthrough: passthrough is what a green-screen video's cut shows through, and
            // it needs ARCore running. Hands stay off, the largest part of the heat, since the video
            // decoder and ARCore already are.
            Requirements = new DemoRequirements { SixDof = Need.On, Passthrough = Need.On, Hands = Need.Off },
        };

        public override void BuildScene(string folder)
        {
            // Double-sided in case the sphere is ever seen from outside, and white so the video's own
            // colors show.
            var material = StereoBuilder.CreateMaterial("Video Sphere", DemoAssets.Unlit, Color.white, null, 1f, folder);
            // The green-screen cut, drawn over the passthrough image (see ChromaKey.shader). Falls back
            // to the opaque material, so a missing shader costs the feature rather than the build.
            var chromaShader = Shader.Find(ChromaKeyShaderName);
            if (chromaShader == null)
                Debug.LogWarning($"[Build] {ChromaKeyShaderName} not found: the video sphere will have no green-screen cut");
            var chroma = chromaShader != null
                ? StereoBuilder.CreateMaterial("Video Sphere Chroma", chromaShader, Color.white, null, 1f, folder)
                : material;

            var sphere = new GameObject("Video Sphere").AddComponent<VideoSphere>();
            var serializedSphere = new SerializedObject(sphere);
            serializedSphere.FindProperty("m_Material").objectReferenceValue = material;
            serializedSphere.FindProperty("m_ChromaMaterial").objectReferenceValue = chroma;
            serializedSphere.ApplyModifiedPropertiesWithoutUndo();

            var demo = new GameObject("Video Demo").AddComponent<VideoDemo>();
            var serializedDemo = new SerializedObject(demo);
            serializedDemo.FindProperty("m_Sphere").objectReferenceValue = sphere;
            serializedDemo.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
