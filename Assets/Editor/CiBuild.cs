using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace ProjectBootstrap
{
    // Split in two because changing the scripting backend makes Unity recompile and reload scripts,
    // which aborts whatever the current -executeMethod call is doing. Configure runs alone, BuildApk
    // runs in a fresh session that just reads the settings back.
    public static class CiBuild
    {
        // ARCore needs arm64, which only the IL2CPP backend produces, so Mono is not an option.
        //
        // OpenGLES3 only, deliberately: the 32-bit Mono build of this project had a working picture
        // on GLES3 alone, the official 1.0.0 ships GLES3 only, and the all-black builds were the ones
        // that listed Vulkan first.
        //
        // Code generation stays OptimizeSize: OptimizeSpeed makes the IL2CPP link step write tens of
        // GB of debug symbols and the CI runner runs out of disk.
        //
        // Stripping stays off on top of that, for margin: the project has no link.xml, and engine
        // code is left alone too, so nothing ARCore reaches for can be cut.
        public static void Configure()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Disabled);
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Higher than the official 1.0.0 build (10000) so this installs over it instead of failing
            // with "app not installed".
            PlayerSettings.Android.bundleVersionCode = 10002;
            PlayerSettings.bundleVersion = "1.0.2";
            EnsureXrRuntimeSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[CiBuild] configured backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures} strip={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android)} " +
                      $"codegen={PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.Android)} " +
                      $"apis={string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}");
        }

        // XR Plug-in Management only starts if an XRGeneralSettings instance exists in the player.
        // Its static s_Instance is assigned in Awake, and Awake only runs for objects listed in
        // PlayerSettings.preloadedAssets. When that list is missing the entry, both runtime hooks
        // (AttemptInitializeXRSDKOnLoad, AttemptStartXRSDKOnBeforeSplashScreen) return on their first
        // line -- no loader, no session, no camera subsystem, and no error anywhere, because nothing
        // runs. On screen that reads exactly as "XR loader: none".
        //
        // This writes the Android XRGeneralSettings into preloadedAssets through the API (so the
        // serialized reference is one Unity produced itself), makes sure ARCore's loader is attached,
        // and logs the counts -- the build log then proves it instead of assuming it.
        static void EnsureXrRuntimeSettings()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey,
                    out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Debug.LogError("[CiBuild] XR settings asset missing: no loader can ever start");
                return;
            }

            var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            Debug.Log($"[CiBuild] XR Android settings: {(settings == null ? "NULL" : settings.name)}");
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = "Android Settings";
                AssetDatabase.AddObjectToAsset(settings, perTarget);
                perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, settings);
                EditorUtility.SetDirty(perTarget);
            }

            if (settings.Manager == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = "Android Providers";
                AssetDatabase.AddObjectToAsset(manager, perTarget);
                settings.Manager = manager;
            }

            settings.InitManagerOnStart = true;
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);

            var assigned = XRPackageMetadataStore.AssignLoader(settings.Manager,
                "UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android);
            Debug.Log($"[CiBuild] ARCore loader assigned={assigned} loaders={settings.Manager.activeLoaders.Count}");

            var preloaded = new List<UnityEngine.Object>(PlayerSettings.GetPreloadedAssets());
            if (!preloaded.Contains(settings))
            {
                preloaded.Add(settings);
                PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
                Debug.Log("[CiBuild] added XR settings to preloaded assets");
            }

            var after = PlayerSettings.GetPreloadedAssets();
            var contains = false;
            foreach (var asset in after)
                if (asset == settings) contains = true;
            Debug.Log($"[CiBuild] preloaded assets={after.Length} containsXrSettings={contains} " +
                      $"initOnStart={settings.InitManagerOnStart}");

            AssetDatabase.SaveAssets();
        }

        // Fallback for this machine: its 8 GB cap kills the IL2CPP linker, so the shippable APK is a
        // 32-bit Mono build. Mono has no IL2CPP link step, which is why it fits.
        public static void ConfigureMono32()
        {
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
            AssetDatabase.SaveAssets();
            Debug.Log($"[CiBuild] configured backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures}");
        }

        // ARCore's camera subsystem looks its two background shaders up with Shader.Find at runtime,
        // so at build time nothing references them and the stripper drops them. cameraMaterial then
        // comes back null and every camera view draws black. Pinning them in Always Included is the
        // only thing that keeps them in the build. ProjectSettings already lists the guids; this runs
        // anyway because a guid that stops resolving (package re-hashed, shader renamed) fails
        // silently, and a silent failure here looks exactly like the bug it is meant to fix.
        static void EnsureArBackgroundShaders()
        {
            var wanted = new[] { "Unlit/ARCoreBackground", "Unlit/ARCoreBackground/AfterOpaques" };
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[CiBuild] GraphicsSettings.asset could not be loaded");
                return;
            }

            var settings = new SerializedObject(assets[0]);
            var list = settings.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning("[CiBuild] m_AlwaysIncludedShaders not found in GraphicsSettings");
                return;
            }

            var present = new HashSet<string>();
            for (var i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is Shader s)
                    present.Add(s.name);

            var added = 0;
            foreach (var name in wanted)
            {
                if (present.Contains(name))
                {
                    Debug.Log($"[CiBuild] always-included shader already pinned: {name}");
                    continue;
                }

                Shader shader = null;
                foreach (var guid in AssetDatabase.FindAssets("ARCoreBackground t:Shader", new[] { "Assets", "Packages" }))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate != null && candidate.name == name)
                    {
                        shader = candidate;
                        break;
                    }
                }

                if (shader == null)
                {
                    Debug.LogError($"[CiBuild] {name} not found: the camera background will be black");
                    continue;
                }

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                added++;
                Debug.Log($"[CiBuild] pinned always-included shader: {name}");
            }

            if (added > 0)
            {
                settings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }

            // Read it back the way the build will: a pin that did not survive the save is the same
            // as no pin at all.
            settings.Update();
            var after = new HashSet<string>();
            for (var i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is Shader s)
                    after.Add(s.name);
            foreach (var name in wanted)
                Debug.Log(after.Contains(name)
                    ? $"[CiBuild] verified always-included: {name}"
                    : $"[CiBuild] MISSING always-included: {name}");
        }

        public static void BuildApk()
        {
            // Resources assets are always packed, and these materials reference the two ARCore
            // background shaders, so the shaders come with them. That is what makes
            // Shader.Find("Unlit/ARCoreBackground") work in the player; without it the camera
            // material is null and every camera view is black.
            ArBackgroundMaterials.Ensure();
            EnsureArBackgroundShaders();
            Debug.Log($"[CiBuild] building backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures}");
            StereoBuilder.BuildAndroid();
        }
    }
}
