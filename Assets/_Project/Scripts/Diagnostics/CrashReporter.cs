using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace CardboardXR.Diagnostics
{
    // Temporary crash diagnostics: mirrors the player log to a local file and, on Android, posts
    // batches to a webhook so a crash can be diagnosed without adb. Remove once the crash is fixed.
    public static class CrashReporter
    {
        const string EndpointClass = "com.brenpoly.cardboardxr.CrashLogUploader";
        const double UploadIntervalSeconds = 3.0;
        const int MaxBatchChars = 120000;

        static readonly object s_Lock = new object();
        static readonly StringBuilder s_Pending = new StringBuilder();
        static string s_LogPath;
        static DateTime s_LastUpload = DateTime.MinValue;
        static bool s_Ready;
        static bool s_InUpload;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            try
            {
                s_LogPath = Path.Combine(Application.persistentDataPath, "diag.log");
                Application.logMessageReceivedThreaded += OnLog;
#if UNITY_ANDROID && !UNITY_EDITOR
                CallJava("install");
#endif
                SendPreviousRun();
                s_Ready = true;
                Append("=== start " + DateTime.UtcNow.ToString("O") + " ===\n"
                       + SystemInfo.deviceModel + " | " + SystemInfo.operatingSystem
                       + " | " + Application.version + "\n");
            }
            catch (Exception error)
            {
                Debug.LogWarning("CrashReporter init failed: " + error.Message);
            }
        }

        static void SendPreviousRun()
        {
            try
            {
                if (!File.Exists(s_LogPath))
                    return;
                string text = File.ReadAllText(s_LogPath);
                File.Delete(s_LogPath);
                if (!string.IsNullOrEmpty(text))
                    Send("PREVIOUS-RUN\n" + text);
            }
            catch
            {
            }
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!s_Ready)
                return;
            if (condition != null && condition.Contains("CrashLogUploader"))
                return;
            bool isError = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            Append("[" + type + "] " + condition + "\n" + (isError ? stackTrace + "\n" : string.Empty));
            if (type == LogType.Exception)
                Flush(true);
        }

        static void Append(string text)
        {
            lock (s_Lock)
                s_Pending.Append(text);
            try
            {
                File.AppendAllText(s_LogPath, text);
            }
            catch
            {
            }
            Flush(false);
        }

        static void Flush(bool force)
        {
            if (s_InUpload)
                return;
            if (!force && (DateTime.UtcNow - s_LastUpload).TotalSeconds < UploadIntervalSeconds)
                return;
            string batch;
            lock (s_Lock)
            {
                if (s_Pending.Length == 0)
                    return;
                int take = Math.Min(s_Pending.Length, MaxBatchChars);
                batch = s_Pending.ToString(0, take);
                s_Pending.Remove(0, take);
            }
            s_LastUpload = DateTime.UtcNow;
            Send(batch);
        }

        static void Send(string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            s_InUpload = true;
            try
            {
                CallJava("upload", text);
            }
            finally
            {
                s_InUpload = false;
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void CallJava(string method, params object[] args)
        {
            try
            {
                using (var bridge = new AndroidJavaClass(EndpointClass))
                    bridge.CallStatic(method, args);
            }
            catch
            {
            }
        }
#endif
    }
}
