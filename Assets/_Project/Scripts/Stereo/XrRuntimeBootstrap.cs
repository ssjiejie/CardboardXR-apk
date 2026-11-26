using UnityEngine;
using UnityEngine.XR.Management;

namespace CardboardXR.Stereo
{
    /// <summary>
    /// Last-resort start for XR Plug-in Management.
    ///
    /// XRGeneralSettings.s_Instance is only assigned from Awake, and Awake only runs for objects in
    /// PlayerSettings.preloadedAssets. When that list is right, the package's own
    /// RuntimeInitializeOnLoadMethod starts the loader before any scene loads and this does nothing.
    /// When it is wrong, the package's hook returns silently on its first line: no loader, no
    /// session, no camera subsystem, and a screen that is black with no error anywhere. This runs
    /// slightly earlier than the package's hook and starts the loader itself if nobody else has, so
    /// a missing preload entry degrades to a slow start instead of a black screen.
    /// </summary>
    public static class XrRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var settings = XRGeneralSettings.Instance;

            // No settings object at all means the asset never got into the build; nothing this
            // method can do, but the on-screen status line will say so instead of showing black.
            if (settings == null || settings.Manager == null)
                return;

            var manager = settings.Manager;

            if (manager.activeLoader != null)
            {
                if (!manager.isInitializationComplete)
                    manager.StartSubsystems();
                return;
            }

            Debug.Log("[XrRuntimeBootstrap] no active loader at BeforeSceneLoad, initialising manually");
            manager.InitializeLoaderSync();

            if (manager.activeLoader == null)
            {
                Debug.LogError("[XrRuntimeBootstrap] InitializeLoaderSync produced no loader");
                return;
            }

            manager.StartSubsystems();
            Debug.Log($"[XrRuntimeBootstrap] started loader={manager.activeLoader.name}");
        }
    }
}
