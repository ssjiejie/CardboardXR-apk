using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace ProjectBootstrap
{
    // Declares the permissions the video library reads the phone's own folders with: the videos in
    // DCIM, Movies and Downloads are listed so one can be watched without copying it in over adb.
    // Android 13 split the storage permission by media type, so both names are declared and the
    // request picks the one the phone knows (see VideoGallery). Follows
    // HighSamplingRatePermission, which declares its permission the same way.
    public class MediaPermissions : IPostGenerateGradleAndroidProject
    {
        const string k_AndroidNamespace = "http://schemas.android.com/apk/res/android";
        static readonly string[] k_Permissions =
        {
            "android.permission.READ_MEDIA_VIDEO",
            "android.permission.READ_EXTERNAL_STORAGE",
        };

        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[Build] No manifest at {manifestPath}; videos from the gallery can't be listed.");
                return;
            }

            var document = new XmlDocument();
            document.Load(manifestPath);
            var manifest = document.DocumentElement;
            foreach (var name in k_Permissions)
            {
                var declared = false;
                foreach (XmlElement permission in manifest.GetElementsByTagName("uses-permission"))
                {
                    if (permission.GetAttribute("name", k_AndroidNamespace) == name)
                    {
                        declared = true;
                        break;
                    }
                }
                if (declared)
                    continue;
                var element = document.CreateElement("uses-permission");
                element.SetAttribute("name", k_AndroidNamespace, name);
                manifest.PrependChild(element);
                Debug.Log($"[Build] Declared {name}");
            }
            document.Save(manifestPath);
        }
    }
}
