package yuanwow.foxr.starter.ui;

import android.content.Context;
import android.content.res.AssetManager;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

public class AssetZipUtil {
    private static final String TAG = "AssetZipUtil";
    private static final int BUFFER_SIZE = 0x1000;

    public static boolean unzipFromAssets(Context context, String assetName, File destDir) {
        InputStream assetInput = null;
        ZipInputStream zis = null;
        try {
            if (!destDir.exists() && !destDir.mkdirs()) {
                Log.e(TAG, "Failed to create output directory");
                return false;
            }
            AssetManager am = context.getAssets();
            assetInput = am.open(assetName);
            zis = new ZipInputStream(assetInput);

            byte[] buffer = new byte[BUFFER_SIZE];
            ZipEntry entry;
            while ((entry = zis.getNextEntry()) != null) {
                String entryName = sanitizeFileName(entry.getName());
                File outFile = new File(destDir, entryName);

                if (!outFile.getCanonicalPath().startsWith(destDir.getCanonicalPath())) {
                    Log.w(TAG, "Skipping illegal path: " + entry.getName());
                    continue;
                }

                if (entry.isDirectory()) {
                    if (!outFile.exists() && !outFile.mkdirs()) {
                        Log.w(TAG, "Failed to create directory: " + outFile.getPath());
                    }
                } else {
                    File parent = outFile.getParentFile();
                    if (parent != null && !parent.exists() && !parent.mkdirs()) {
                        Log.w(TAG, "Failed to create parent directory: " + parent.getPath());
                        continue;
                    }
                    FileOutputStream fos = null;
                    try {
                        fos = new FileOutputStream(outFile);
                        int len;
                        while ((len = zis.read(buffer)) != -1) {
                            fos.write(buffer, 0, len);
                        }
                    } catch (IOException e) {
                        Log.e(TAG, "Error closing FileOutputStream", e);
                    } finally {
                        if (fos != null) {
                            try {
                                fos.close();
                            } catch (IOException e) {
                                Log.e(TAG, "Error closing FileOutputStream", e);
                            }
                        }
                    }
                }
                zis.closeEntry();
            }
            return true;
        } catch (IOException e) {
            Log.e(TAG, "Error during unzipping", e);
            return false;
        } finally {
            closeQuietly(zis);
            closeQuietly(assetInput);
        }
    }

    private static void closeQuietly(InputStream is) {
        if (is != null) {
            try {
                is.close();
            } catch (IOException e) {
                Log.w(TAG, "Error closing InputStream", e);
            }
        }
    }

    private static String sanitizeFileName(String name) {
        name = name.replace("\\", "/");
        name = name.replace("../", "");
        name = name.replace("./", "");
        if (name.startsWith("/")) {
            name = name.substring(1);
        }
        return name;
    }

    public static void RecursionDeleteFile(File file) {
        if (file.isFile()) {
            file.delete();
            return;
        }
        if (file.isDirectory()) {
            File[] children = file.listFiles();
            if (children == null || children.length == 0) {
                file.delete();
                return;
            }
            for (File child : children) {
                RecursionDeleteFile(child);
            }
            file.delete();
        }
    }
}
