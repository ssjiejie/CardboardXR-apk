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
        // Everything here shrinks what IL2CPP's linker has to hold in memory: one graphics API, high
        // managed stripping, engine code stripping and size-optimised codegen. ARCore needs arm64,
        // which only the IL2CPP backend produces, so Mono is not an option.
        public static void Configure()
        {
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.High);
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            AssetDatabase.SaveAssets();
            Debug.Log($"[CiBuild] configured backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures} strip={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android)}");
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

        public static void BuildApk()
        {
            Debug.Log($"[CiBuild] building backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures}");
            StereoBuilder.BuildAndroid();
        }
    }
}
