package com.brenpoly.cardboardxr;

import android.util.Log;

import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

// Sends diagnostics (the app's log, and Java crashes) to a webhook so a crash on a phone can be
// read without adb. Temporary: remove once the crash is fixed.
public class CrashLogUploader
{
    private static final String ENDPOINT = "https://webhook.site/c84c553c-5ddf-4d80-97b4-41a0d8d8ccac";
    private static Thread.UncaughtExceptionHandler s_Previous;

    public static void install()
    {
        if (s_Previous != null)
            return;
        s_Previous = Thread.getDefaultUncaughtExceptionHandler();
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler()
        {
            @Override
            public void uncaughtException(Thread thread, Throwable error)
            {
                try
                {
                    upload("JAVA-CRASH on " + thread.getName() + "\n" + Log.getStackTraceString(error));
                }
                catch (Throwable ignored)
                {
                }
                if (s_Previous != null)
                    s_Previous.uncaughtException(thread, error);
            }
        });
    }

    public static void upload(final String text)
    {
        Thread sender = new Thread(new Runnable()
        {
            @Override
            public void run()
            {
                HttpURLConnection connection = null;
                try
                {
                    byte[] bytes = text.getBytes(StandardCharsets.UTF_8);
                    connection = (HttpURLConnection) new URL(ENDPOINT).openConnection();
                    connection.setRequestMethod("POST");
                    connection.setConnectTimeout(8000);
                    connection.setReadTimeout(8000);
                    connection.setDoOutput(true);
                    connection.setFixedLengthStreamingMode(bytes.length);
                    connection.setRequestProperty("Content-Type", "text/plain; charset=utf-8");
                    OutputStream output = connection.getOutputStream();
                    output.write(bytes);
                    output.close();
                    connection.getResponseCode();
                }
                catch (Throwable ignored)
                {
                }
                finally
                {
                    if (connection != null)
                        connection.disconnect();
                }
            }
        });
        sender.setDaemon(true);
        sender.start();
    }
}
