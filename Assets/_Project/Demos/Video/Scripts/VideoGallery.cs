using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CardboardXR.Demos.Video
{
    // The videos already on the phone, found in the folders Android keeps media in, so one can be
    // watched without copying it into the app's own folder over adb first.
    //   Android 13 and later ask for READ_MEDIA_VIDEO, earlier versions for READ_EXTERNAL_STORAGE.
    //   Once granted, reading those files by path is allowed (Android 11+); the app only ever reads
    //   them, and never moves or edits anything the player owns.
    // The list is what changed most recently, capped, rather than everything: a phone with years of
    // recordings should not take a minute to open a panel.
    public static class VideoGallery
    {
        static readonly string[] k_Extensions = { ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".3gp" };

        // Where cameras, downloads and chat apps leave videos. Relative to the shared storage root.
        static readonly string[] k_Folders =
        {
            "DCIM", "Movies", "Download", "Downloads", "Pictures", "Video",
            "DCIM/Camera", "Movies/WeiXin", "Pictures/WeiXin", "Movies/WhatsApp",
        };

        // Deep enough for DCIM/Camera/IMG_0001.mp4, shallow enough to skip app caches.
        const int k_MaxDepth = 3;
        const int k_MaxFiles = 400;

        public static bool Supported => Application.platform == RuntimePlatform.Android;

        static string PermissionName
        {
            get
            {
                // Android 13 split the storage permission up by media type.
                try
                {
                    using var version = new AndroidJavaClass("android.os.Build$VERSION");
                    return version.GetStatic<int>("SDK_INT") >= 33
                        ? "android.permission.READ_MEDIA_VIDEO"
                        : "android.permission.READ_EXTERNAL_STORAGE";
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Video] Couldn't read the Android version: {e.Message}");
                    return "android.permission.READ_EXTERNAL_STORAGE";
                }
            }
        }

        public static bool HasAccess
        {
            get
            {
                if (!Supported)
                    return false;
                try
                {
                    return UnityEngine.Android.Permission.HasUserAuthorizedPermission(PermissionName);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Video] Couldn't check the media permission: {e.Message}");
                    return false;
                }
            }
        }

        public static void RequestAccess()
        {
            if (!Supported)
                return;
            try
            {
                UnityEngine.Android.Permission.RequestUserPermission(PermissionName);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Video] Couldn't ask for the media permission: {e.Message}");
            }
        }

        // The most recently changed videos on the phone, newest first, at most max of them.
        public static List<VideoEntry> FindRecent(int max)
        {
            var entries = new List<VideoEntry>();
            if (!HasAccess)
                return entries;

            var found = new List<(string path, DateTime time)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in k_Folders)
            {
                Collect(Path.Combine(Root, folder), 0, seen, found);
                if (found.Count >= k_MaxFiles)
                    break;
            }

            found.Sort((a, b) => b.time.CompareTo(a.time));
            for (var i = 0; i < found.Count && entries.Count < max; i++)
            {
                var path = found[i].path;
                entries.Add(new VideoEntry
                {
                    Title = Path.GetFileNameWithoutExtension(path),
                    Format = VideoFormat.FromFileName(path),
                    Url = path,
                });
            }
            return entries;
        }

        // The shared storage root, as the system sees it: /storage/emulated/0 on a phone.
        static string Root
        {
            get
            {
                try
                {
                    using var environment = new AndroidJavaClass("android.os.Environment");
                    using var directory = environment.CallStatic<AndroidJavaObject>("getExternalStorageDirectory");
                    return directory.Call<string>("getAbsolutePath");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Video] Couldn't find the storage root: {e.Message}");
                    return "/storage/emulated/0";
                }
            }
        }

        static void Collect(string folder, int depth, HashSet<string> seen, List<(string path, DateTime time)> found)
        {
            if (depth > k_MaxDepth || found.Count >= k_MaxFiles)
                return;
            try
            {
                if (!Directory.Exists(folder))
                    return;
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!IsVideo(file) || !seen.Add(file))
                        continue;
                    found.Add((file, File.GetLastWriteTimeUtc(file)));
                    if (found.Count >= k_MaxFiles)
                        return;
                }
                foreach (var child in Directory.EnumerateDirectories(folder))
                    Collect(child, depth + 1, seen, found);
            }
            // A folder the phone keeps private, or one that vanished mid-scan, is simply skipped.
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        static bool IsVideo(string path) => Array.IndexOf(k_Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
    }
}
