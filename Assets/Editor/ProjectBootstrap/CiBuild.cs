using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ProjectBootstrap
{
    // The build entry for CI. It does one thing: the official release script's command
    // (Tools/release_android.sh), with a version stamped on.
    //
    //     -executeMethod ProjectBootstrap.CiBuild.BuildApk
    //     -> ProjectBootstrap.StereoBuilder.BuildAndroid()
    //
    // Deliberately nothing else here. Earlier CI builds set the graphics API, the managed stripping
    // level, the IL2CPP code generation mode and the XR preload list by hand, and every one of those
    // was a departure from the build that is known to show the camera: the official script sets none
    // of them, the repository's ProjectSettings already carry the values the release APK was built
    // with (Vulkan first, GLES3 fallback), and overriding them produced builds whose every camera and
    // video view was black. If a setting does need changing, it belongs in ProjectSettings, changed
    // once and reviewed, not silently rewritten at build time.
    public static class CiBuild
    {
        const string Version = "1.2.0";
        const int VersionCode = 10200;

        public static void BuildApk()
        {
            // Nothing is built until the configuration has been checked against the things that have
            // actually gone wrong here: package versions, the XR manager and its preload, the video
            // shader's texture property, the catalog's requirements, the scene's wiring. A build that
            // fails one of these would install and run, and show a black screen.
            if (!VerifySetup.RunChecks())
            {
                EditorApplication.Exit(1);
                return;
            }

            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = VersionCode;
            AssetDatabase.SaveAssets();

            Debug.Log($"[CiBuild] version {Version} ({VersionCode})");
            Debug.Log($"[CiBuild] apis={string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))} " +
                      $"automatic={PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android)}");
            Debug.Log($"[CiBuild] backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)} " +
                      $"arch={PlayerSettings.Android.targetArchitectures} " +
                      $"strip={PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android)} " +
                      $"stripEngine={PlayerSettings.stripEngineCode}");

            // Builds the scenes from scratch, then the APK, and exits with the build's result.
            StereoBuilder.BuildAndroid();
        }
    }
}
