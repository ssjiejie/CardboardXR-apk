using System;
using UnityEngine;

namespace CardboardXR.Demos.Video
{
    // Brings a video from the phone's gallery into the app: opens Android's document picker, and the
    // picker's own Activity (Plugins/Android/VideoImportActivity.java) copies the chosen file into the
    // app's Videos folder and calls back here, so the library finds it. On the phone no storage
    // permission is needed, since the picker hands the file over.
    public class VideoImporter : MonoBehaviour
    {
        // The imported video's path, or null when the picker was closed or failed.
        public event Action<string> Imported;

        public static bool IsSupported =>
            Application.platform == RuntimePlatform.Android && !Application.isEditor;

        public void Pick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var pickerClass = new AndroidJavaClass("com.brenpoly.cardboardxr.VideoImportActivity");
                using var intent = new AndroidJavaObject("android.content.Intent", activity, pickerClass);
                intent.Call<AndroidJavaObject>("putExtra", "unityGameObject", gameObject.name);
                activity.Call("startActivity", intent);
                Debug.Log("[Video] Opening the gallery to add a video");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Video] Couldn't open the gallery: {e.Message}");
                Imported?.Invoke(null);
            }
#else
            Debug.LogWarning($"[Video] Adding from the gallery is Android only; copy videos into {VideoLibrary.VideoFolder} instead");
            Imported?.Invoke(null);
#endif
        }

        // Called by the picker Activity through UnitySendMessage once the file is in the Videos folder.
        public void OnVideoImported(string path)
        {
            Debug.Log($"[Video] Added from the gallery: {path}");
            Imported?.Invoke(string.IsNullOrEmpty(path) ? null : path);
        }

        // Called by the picker Activity when it was closed without a video, or couldn't copy one.
        public void OnVideoImportFailed(string reason)
        {
            Debug.LogWarning($"[Video] Adding from the gallery failed: {reason}");
            Imported?.Invoke(null);
        }
    }
}
