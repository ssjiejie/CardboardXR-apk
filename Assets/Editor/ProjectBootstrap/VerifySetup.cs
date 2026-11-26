using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CardboardXR.Demos.Video;
using CardboardXR.Stereo;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace ProjectBootstrap
{
    // Checks the finished configuration before a build, so a package that cannot work is never
    // produced. Every rule here is one that has actually gone wrong in this project:
    //
    //  - AR Foundation and ARCore drifted to 6.7.1 once and the published build is 6.6.2. With the
    //    newer pair the ARCore subsystem registration never made it into the player: XR loader came
    //    up none, the camera subsystem did not exist, the app never asked for the camera, and every
    //    camera view drew black while nothing logged an error.
    //  - XR Plug-in Management's startup hooks can be stripped away, which produces the same silence.
    //  - The video sphere's material is URP/Unlit, so the chroma-key shader has to take its texture
    //    in _BaseMap; a shader reading _MainTex would come up blank after the swap.
    //  - The video demo only turns passthrough on for itself if its catalog requirements ask for it.
    public static class VerifySetup
    {
        static int s_Failures;

        // For the build entry point: returns false and prints why, without exiting the editor.
        public static bool RunChecks()
        {
            s_Failures = 0;
            CheckPackages();
            CheckXr();
            CheckPlayer();
            CheckCatalog();
            CheckScene();
            CheckShaders();
            CheckScripts();
            if (s_Failures == 0)
            {
                Debug.Log("[Verify] all checks passed");
                return true;
            }
            Debug.LogError($"[Verify] {s_Failures} check(s) failed; not building");
            return false;
        }

        // For the command line on its own.
        public static void Run()
        {
            EditorApplication.Exit(RunChecks() ? 0 : 1);
        }

        static void Ok(string what) => Debug.Log($"[Verify] PASS {what}");

        static void Bad(string what)
        {
            s_Failures++;
            Debug.LogError($"[Verify] FAIL {what}");
        }

        static void Expect(bool condition, string what)
        {
            if (condition) Ok(what);
            else Bad(what);
        }

        // The packages the published 1.0.0 was built with. A newer AR Foundation silently produces a
        // player with no ARCore subsystems in it.
        static void CheckPackages()
        {
            const string lockPath = "Packages/packages-lock.json";
            if (!File.Exists(lockPath))
            {
                Bad("Packages/packages-lock.json missing: versions could resolve to anything");
                return;
            }
            var json = File.ReadAllText(lockPath);
            foreach (var (package, expected) in new[]
                     {
                         ("com.unity.xr.arfoundation", "6.6.2"),
                         ("com.unity.xr.arcore", "6.6.2"),
                     })
            {
                var actual = Version(json, package);
                Expect(actual == expected, $"{package} is {actual ?? "missing"}, expected {expected}");
            }
        }

        static string Version(string json, string package)
        {
            var match = Regex.Match(json,
                "\"" + Regex.Escape(package) + "\"\\s*:\\s*\\{[^}]*\"version\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        // Android has to have an XR general settings with a manager holding the ARCore loader, and it
        // has to be preloaded: XRGeneralSettings only exists at runtime if that asset is loaded.
        static void CheckXr()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Bad("no XRGeneralSettingsPerBuildTarget registered in EditorBuildSettings");
                return;
            }
            var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings == null)
            {
                Bad("no XR general settings for Android");
                return;
            }
            Expect(settings.InitManagerOnStart, "XR initialises on start");
            if (settings.Manager == null)
            {
                Bad("Android XR manager is null");
                return;
            }

            // activeLoaders is read by reflection so a rename in a newer XR Management is a failed
            // check here rather than a compile error that stops the whole build.
            var property = settings.Manager.GetType().GetProperty("activeLoaders");
            var loaders = property?.GetValue(settings.Manager) as IList;
            var names = loaders?.Cast<object>().Where(l => l != null).Select(l => l.GetType().Name).ToList();
            Expect(names != null && names.Any(n => n.Contains("ARCore")),
                $"Android XR loaders include ARCore (found: {(names == null ? "none" : string.Join(",", names))})");

            var preloaded = PlayerSettings.GetPreloadedAssets().Where(a => a != null).ToList();
            Expect(preloaded.Any(a => a is XRGeneralSettings),
                $"XR general settings is a preloaded asset (preloaded: {preloaded.Count})");
        }

        static void CheckPlayer()
        {
            Expect(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP,
                "Android scripting backend is IL2CPP");
            Expect((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0,
                "Android target architectures include ARM64");

            // The published build strips at Minimal. Disabled and Low were both tried here and both
            // produced a player whose ARCore subsystems were gone, so this is the known-good value.
            var stripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android);
            Expect(stripping == ManagedStrippingLevel.Minimal,
                $"Android managed stripping is {stripping}, expected Minimal");

            // Empty means "what the platform prefers", which is what the published build used. A
            // forced Vulkan-first list is what blacked out video, camera and passthrough at once.
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            Expect(apis == null || apis.Length == 0,
                $"Android graphics APIs are left to the platform (found: {(apis == null || apis.Length == 0 ? "automatic" : string.Join(",", apis))})");
        }

        // The video demo has to ask for 6DoF and passthrough, or the core never turns them on for it.
        static void CheckCatalog()
        {
            const string path = "Assets/_Project/Demos/DemoCatalog.asset";
            if (!File.Exists(path))
            {
                Bad("DemoCatalog.asset missing");
                return;
            }
            var text = File.ReadAllText(path);
            var video = Regex.Match(text, @"Id:\s*video(?<body>[\s\S]*?)(?:-\s*Id:|\z)");
            if (!video.Success)
            {
                Bad("video entry not found in DemoCatalog");
                return;
            }
            var body = video.Groups["body"].Value;
            Expect(Regex.IsMatch(body, @"SixDof:\s*2"), "video demo requires 6DoF");
            Expect(Regex.IsMatch(body, @"Passthrough:\s*2"), "video demo requires passthrough");
            Expect(body.Contains("Assets/_Project/Demos/Video/Video.unity"), "video demo points at Video.unity");
        }

        // The scene has to still hold a wired video sphere and a passthrough controller; without them
        // the buttons work but nothing happens.
        static void CheckScene()
        {
            const string path = "Assets/_Project/Demos/Video/Video.unity";
            if (!File.Exists(path))
            {
                Bad("Video.unity missing");
                return;
            }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var spheres = Object.FindObjectsByType<VideoSphere>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (spheres.Length == 0)
            {
                Bad("Video.unity has no VideoSphere");
            }
            else
            {
                Ok($"Video.unity has {spheres.Length} VideoSphere");
                var so = new SerializedObject(spheres[0]);
                var material = so.FindProperty("m_Material");
                Expect(material != null && material.objectReferenceValue != null,
                    "VideoSphere has its material wired");
                if (material?.objectReferenceValue is Material mat && mat.shader != null)
                    Ok($"VideoSphere material uses {mat.shader.name}");
            }
            Expect(Object.FindObjectsByType<PassthroughController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length > 0, "Video.unity has a PassthroughController");
            var demo = Object.FindObjectsByType<VideoDemo>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).FirstOrDefault();
            Expect(demo != null, "Video.unity has a VideoDemo");
            if (demo != null)
            {
                // The three buttons write through this reference; null here is a NullReferenceException
                // the moment one of them is tapped.
                var so = new SerializedObject(demo);
                var sphere = so.FindProperty("m_Sphere");
                Expect(sphere != null && sphere.objectReferenceValue != null,
                    "VideoDemo has its VideoSphere wired");
            }
            Debug.Log($"[Verify] scene roots: {string.Join(", ", scene.GetRootGameObjects().Select(g => g.name))}");
            EditorSceneManager.CloseScene(scene, true);
        }

        // The chroma-key shader has to load from Resources (nothing references it from the scene) and
        // has to take its texture in _BaseMap, which is the property URP/Unlit put the video in.
        static void CheckShaders()
        {
            var shader = Resources.Load<Shader>("ChromaKey");
            if (shader == null)
            {
                Bad("Resources/ChromaKey.shader does not load");
                return;
            }
            Ok($"ChromaKey shader loads ({shader.name})");
            // isSupported is meaningless in -nographics: it reports the host's GPU, not the phone's.
            Debug.Log($"[Verify]     (isSupported on this host: {shader.isSupported}; ignored in batch mode)");
            Expect(shader.HasProperty("_BaseMap"), "ChromaKey takes its texture in _BaseMap (matches URP/Unlit)");

            // Read from the asset, not from the scene: the scene is closed by now, and what matters is
            // which property the material the sphere copies actually keeps its texture in.
            const string materialPath = "Assets/_Project/Demos/Video/Video Sphere.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Bad("Video Sphere.mat missing");
                return;
            }
            var main = material.HasProperty("_BaseMap") ? "_BaseMap"
                : material.HasProperty("_MainTex") ? "_MainTex" : null;
            Expect(main == "_BaseMap",
                $"video material's main texture property is {main ?? "none"} (must match the shader)");
        }

        // The three features are methods and properties; if any was renamed away the buttons would
        // throw at runtime instead of failing the build.
        static void CheckScripts()
        {
            Expect(typeof(VideoSphere).GetProperty("ChromaKey") != null, "VideoSphere.ChromaKey exists");
            Expect(typeof(PassthroughController).GetProperty("StereoEyes") != null,
                "PassthroughController.StereoEyes exists");
            Expect(typeof(VideoDemo).GetMethod("ToggleGreenScreen", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                "VideoDemo.ToggleGreenScreen exists");
            Expect(typeof(VideoDemo).GetMethod("ToggleStereoEyes", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                "VideoDemo.ToggleStereoEyes exists");
            Expect(typeof(VideoDemo).GetMethod("TogglePassthrough", BindingFlags.Instance | BindingFlags.NonPublic) != null,
                "VideoDemo.TogglePassthrough exists");
        }
    }
}
