using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectBootstrap
{
    // Split in two because changing the scripting backend makes Unity recompile and reload scripts,
    // which aborts whatever the current -executeMethod call is doing. Configure runs alone, BuildApk
    // runs in a fresh session that just reads the settings back.
    public static class CiBuild
    {
        // ARCore needs arm64, which only the IL2CPP backend produces, so Mono is not an option.
        // Vulkan comes first because that is what the ARCore release notes require; GLES3 stays as
        // the fallback for devices that have no Vulkan driver.
        //
        // Stripping is OFF, engine code included. The official 1.0.0 build leaves it at Unity's
        // default and its libil2cpp.so is 85 MB; ours came out at 42 MB with Low stripping and every
        // camera view drew black while the AR session still reported tracking. AR Foundation and
        // ARCore resolve subsystems, providers and shaders through reflection and generic
        // instantiation, none of which the stripper can see, so the safe setting for this project is
        // no stripping at all.
        public static void Configure()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Disabled);
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Higher than the official 1.0.0 build (10000) so this installs over it instead of failing
            // with "app not installed".
            PlayerSettings.Android.bundleVersionCode = 10001;
            PlayerSettings.bundleVersion = "1.0.1";
            AssetDatabase.SaveAssets();
            Debug.Log($"[CiBuild] configured backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures} strip={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android)} " +
                      $"apis={string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}");
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
