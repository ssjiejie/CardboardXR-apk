using System;
using UnityEditor;
using UnityEngine;

namespace ProjectBootstrap
{
    // The command line entry point the CI uses: stamp a version that installs over the earlier
    // builds, then run the repository's own release build exactly as Tools/release_android.sh does.
    public static class BuildCi
    {
        public static void BuildApk()
        {
            var version = Environment.GetEnvironmentVariable("CARDBOARDXR_VERSION");
            var code = Environment.GetEnvironmentVariable("CARDBOARDXR_VERSION_CODE");
            if (!string.IsNullOrWhiteSpace(version))
                PlayerSettings.bundleVersion = version.Trim();
            if (!string.IsNullOrWhiteSpace(code) && int.TryParse(code.Trim(), out var parsed))
                PlayerSettings.Android.bundleVersionCode = parsed;
            Debug.Log($"[CiBuild] version {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");
            StereoBuilder.BuildAndroid();
        }
    }
}
