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

        // Where cameras, downloads and chat apps leave videos. The scan starts at the top of shared
        // storage and walks every folder under it, because videos end up wherever the app that
        // saved them put them; a shortlist of camera folders missed most of a phone's videos.
        // Android's own tree is skipped: what's inside can't be read, and walking it is slow.
        static readonly string[] k_SkipNames = { "Android", "LOST.DIR", "obb", "cache", "Cache", "code_cache" };

        // Deep enough for a folder a few levels into an app's own tree, shallow enough to skip the
        // caches inside them.
        const int k_MaxDepth = 6;
        const int k_MaxFiles = 1500;

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
            if (!HasAccess)
                return new List<VideoEntry>();
            return FindRecentUnchecked(max, Root);
        }

        // Finds the same list from any thread, for a scan that mustn't hold up the frame. Android's
        // permission API is only asked on the main thread, so the caller checks HasAccess first.
        // Walks every folder it knows, so on a phone with years of recordings it can take seconds.
        static List<VideoEntry> FindRecentUnchecked(int max, string root)
        {
            var entries = new List<VideoEntry>();

            var found = new List<(string path, DateTime time)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in Folders(root))
                Collect(folder, 0, seen, found);

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

        // The main thread's queue of work the scanner sends back.
        static readonly Queue<Action> s_MainThread = new();

        // Runs whatever the background scan finished, on the main thread. Call it once a frame.
        public static void Pump()
        {
            while (true)
            {
                Action action;
                lock (s_MainThread)
                {
                    if (s_MainThread.Count == 0)
                        return;
                    action = s_MainThread.Dequeue();
                }
                action();
            }
        }

        // The same list as FindRecent, found off the main thread: the folders hold thousands of
        // files, and walking them in the frame froze the app for as long as it took. done runs on
        // the main thread.
        public static void FindRecentAsync(int max, Action<List<VideoEntry>> done)
        {
            if (!HasAccess)
            {
                done(new List<VideoEntry>());
                return;
            }
            // Looked up here, on the main thread: asking Android for it is a JNI call.
            var root = Root;
            System.Threading.Tasks.Task.Run(() =>
            {
                List<VideoEntry> list;
                try
                {
                    list = FindRecentUnchecked(max, root);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Video] Scanning the phone for videos failed: {e.Message}");
                    list = new List<VideoEntry>();
                }
                lock (s_MainThread)
                    s_MainThread.Enqueue(() => done(list));
            });
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

        // Every place worth looking in: the top of shared storage, plus any SD card mounted beside
        // it. Collect walks each one, skipping the folders listed above as it goes.
        static List<string> Folders(string root)
        {
            var folders = new List<string> { root };
            try
            {
                foreach (var volume in Directory.GetDirectories("/storage"))
                {
                    var name = Path.GetFileName(volume);
                    // emulated is the internal storage already listed; self is how the app sees it.
                    if (name.Equals("emulated", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("self", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(volume, root, StringComparison.OrdinalIgnoreCase))
                        continue;
                    folders.Add(volume);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
            return folders;
        }

        static bool ShouldSkip(string name) =>
            name.Length == 0 || name[0] == '.' || Array.IndexOf(k_SkipNames, name) >= 0;

        static void Collect(string folder, int depth, HashSet<string> seen, List<(string path, DateTime time)> found)
        {
            if (depth > k_MaxDepth || found.Count >= k_MaxFiles)
                return;

            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return;
            }
            foreach (var file in files)
            {
                if (found.Count >= k_MaxFiles)
                    return;
                if (!IsVideo(file) || !seen.Add(file))
                    continue;
                // A file that vanished under the scan is skipped, not fatal to the folder.
                try
                {
                    found.Add((file, File.GetLastWriteTimeUtc(file)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return;
            }
            foreach (var child in children)
            {
                if (found.Count >= k_MaxFiles)
                    return;
                if (!ShouldSkip(Path.GetFileName(child)))
                    Collect(child, depth + 1, seen, found);
            }
        }

        static bool IsVideo(string path) => Array.IndexOf(k_Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
    }
}
