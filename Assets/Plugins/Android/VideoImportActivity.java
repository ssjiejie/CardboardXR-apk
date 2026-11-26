package com.brenpoly.cardboardxr;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

// A tiny, invisible Activity that opens Android's document picker for videos and copies the chosen
// file into the app's own Videos folder, where VideoLibrary finds it. Living in its own Activity
// keeps the picker's result out of the game's activity, so nothing about how the app runs changes.
// VideoImporter starts it and gets the result through UnitySendMessage.
public class VideoImportActivity extends Activity
{
    private static final String TAG = "CardboardXR";
    private static final int REQUEST_PICK = 9001;

    private String m_GameObject;

    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);
        m_GameObject = getIntent().getStringExtra("unityGameObject");
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("video/*");
        try
        {
            startActivityForResult(intent, REQUEST_PICK);
        }
        catch (Exception e)
        {
            finishWith("ERROR", "Couldn't open the picker: " + e.getMessage());
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        if (requestCode != REQUEST_PICK)
        {
            super.onActivityResult(requestCode, resultCode, data);
            return;
        }
        if (resultCode != RESULT_OK || data == null || data.getData() == null)
        {
            finishWith("CANCELLED", "No video picked");
            return;
        }
        try
        {
            finishWith("OK", CopyToVideos(data.getData()));
        }
        catch (Exception e)
        {
            finishWith("ERROR", "Couldn't copy the video: " + e.getMessage());
        }
    }

    private String CopyToVideos(Uri uri) throws Exception
    {
        String name = Sanitize(DisplayName(uri));
        if (name == null || name.trim().isEmpty())
            name = "video_" + System.currentTimeMillis() + ".mp4";

        File base = getExternalFilesDir(null);
        if (base == null)
            base = getFilesDir();
        File folder = new File(base, "Videos");
        if (!folder.exists() && !folder.mkdirs())
            throw new Exception("Couldn't create " + folder);

        // Never overwrite one already there: add a number before the extension.
        File target = new File(folder, name);
        int dot = name.lastIndexOf('.');
        String stem = dot > 0 ? name.substring(0, dot) : name;
        String extension = dot > 0 ? name.substring(dot) : "";
        for (int copy = 1; target.exists(); copy++)
            target = new File(folder, stem + " (" + copy + ")" + extension);

        InputStream input = null;
        OutputStream output = null;
        try
        {
            input = getContentResolver().openInputStream(uri);
            if (input == null)
                throw new Exception("The picker handed over no file");
            output = new FileOutputStream(target);
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = input.read(buffer)) > 0)
                output.write(buffer, 0, read);
        }
        finally
        {
            if (input != null)
                try { input.close(); } catch (Exception ignored) { }
            if (output != null)
                try { output.close(); } catch (Exception ignored) { }
        }
        return target.getAbsolutePath();
    }

    private String DisplayName(Uri uri)
    {
        Cursor cursor = null;
        try
        {
            cursor = getContentResolver().query(uri, null, null, null, null);
            if (cursor != null && cursor.moveToFirst())
            {
                int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (index >= 0)
                    return cursor.getString(index);
            }
        }
        catch (Exception ignored)
        {
        }
        finally
        {
            if (cursor != null)
                try { cursor.close(); } catch (Exception ignored) { }
        }
        return uri.getLastPathSegment();
    }

    private static String Sanitize(String name)
    {
        return name == null ? null : name.replaceAll("[\\\\/:*?\"<>|]", "_");
    }

    private void finishWith(String result, String detail)
    {
        Log.i(TAG, "VideoImportActivity " + result + ": " + detail);
        if (m_GameObject != null)
        {
            String method = "OK".equals(result) ? "OnVideoImported" : "OnVideoImportFailed";
            try
            {
                UnityPlayer.UnitySendMessage(m_GameObject, method, detail == null ? "" : detail);
            }
            catch (Throwable t)
            {
                Log.w(TAG, "UnitySendMessage failed", t);
            }
        }
        finish();
    }
}
