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
        //
        // OpenGLES3 only, deliberately. The 32-bit Mono build of this same project had a working
        // picture and used GLES3 alone; the 64-bit builds that came out all-black were the ones
        // listing Vulkan first. Video is the tell: the video player, the AR camera texture and the
        // passthrough quad all draw black, and none of them share code with each other -- they share
        // the graphics API. On this device Vulkan comes up but hands back no usable surface for
        // external/OES textures, so everything that samples a camera or a video frame renders black
        // while the rest of the 3D scene still draws. GLES3 is what the official 1.0.0 build ships
        // and what every Android video/AR path here is proven against.
        //
        // Code generation stays OptimizeSize. OptimizeSpeed was tried and the IL2CPP link step ran
        // the runner out of disk (libil2cpp.dbg.so is tens of GB of intermediate), so it is not an
        // option on this CI. It is also not needed: the black frame is a graphics-API problem, not a
        // missing-generic problem -- video playback takes no generics at all and it was black too.
        //
        // The size fingerprint is what matters here. The official 1.0.0 build ships a 85-87 MB
        // libil2cpp.so; ours came out at 49 MB even with stripping disabled, and every camera view
        // drew black while ARCore still reported tracking. The gap was not stripping: it is IL2CPP
        // code generation. OptimizeSize (Unity's non-default "smallest" mode) shares generic code
        // aggressively, and AR Foundation resolves its subsystem providers, delegate targets and
        // shader material through generic instantiation and reflection, which that mode can fold
        // away. OptimizeSpeed is Unity's default and what the official build uses, so matching it is
        // both the fix and the explanation for the size difference.
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
            PlayerSettings.Android.bundleVersionCode = 10001;
            PlayerSettings.bundleVersion = "1.0.1";
            AssetDatabase.SaveAssets();
            Debug.Log($"[CiBuild] configured backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures} strip={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android)} " +
                      $"codegen={PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.Android)} " +
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
