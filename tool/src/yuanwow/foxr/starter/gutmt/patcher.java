package yuanwow.foxr.starter.gutmt;

import android.annotation.SuppressLint;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.os.Looper;
import android.util.Log;

import java.io.File;
import java.io.IOException;
import java.lang.reflect.Field;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;

import yuanwow.foxr.starter.NetUtils;
import yuanwow.foxr.starter.ui.AssetZipUtil;

public class patcher {

    public static void ss(Context c) {
        String xinit = ".xinit";
        try {
            add_20250823a1203b(c);
            File xinitFile = new File(c.getFilesDir(), xinit);
            if (xinitFile.exists()) {
                c.getMainLooper().quit();
                while (true) {
                    // nop
                }
            }
        } catch (Exception e) {
            try {
                Context ctx = ContextUtils.getContext();
                add_20250823a1203b(ctx);
                File xinitFile = new File(ctx.getFilesDir(), xinit);
                if (xinitFile.exists()) {
                    ctx.getMainLooper().quit();
                    while (true) {
                        // nop
                    }
                }
            } catch (Exception e2) {
                Log.e("EGGG", "ErrorGenouka", e2);
            }
        }
        new Thread(new Runnable() {
            @Override
            public void run() {
                String xinitName = ".xinit";
                try {
                    String resp = NetUtils.get("https://fastcom-update.genouka.top/allquityes/");
                    if (resp.contains("quityes")) {
                        try {
                            new File(c.getFilesDir(), xinitName).createNewFile();
                        } catch (Exception e) {
                            Context ctx = ContextUtils.getContext();
                            new File(ctx.getFilesDir(), xinitName).createNewFile();
                        }
                    }
                } catch (Throwable t) {
                    // ignore
                }
            }
        }).start();
    }

    public static void add_20250823a1203b(Context c) {
        c.getFilesDir().mkdirs();
        File initFile = new File(c.getFilesDir(), ".init");
        if (isNeedUpdate(c) || !initFile.exists()) {
            if (initFile.exists()) {
                initFile.delete();
            }
            boolean ok = AssetZipUtil.unzipFromAssets(c, "genouka_patcher.ext", c.getFilesDir());
            if (ok) {
                try {
                    initFile.createNewFile();
                } catch (IOException e) {
                    // ignore
                }
            }
        }
    }

    public static void add_old(Context c) {
        c.getFilesDir().mkdirs();
        File initFile = new File(c.getFilesDir(), ".init");
        if (!initFile.exists()) {
            boolean ok = AssetZipUtil.unzipFromAssets(c, "genouka_patcher.ext", c.getFilesDir());
            if (ok) {
                try {
                    initFile.createNewFile();
                } catch (IOException e) {
                    // ignore
                }
            }
        }
    }

    public static boolean isNeedUpdate(Context c) {
        try {
            PackageManager pm = c.getPackageManager();
            PackageInfo info = pm.getPackageInfo(c.getPackageName(), 0);
            long lastUpdateTime = info.lastUpdateTime;
            String versionName = info.versionName;

            SharedPreferences sp = c.getSharedPreferences("appInfo", 0);
            String storedVersion = sp.getString("versionName", "");
            if (!versionName.equals(storedVersion)) {
                sp.edit().putString("versionName", versionName).apply();
            }

            long storedUpdateTime = sp.getLong("lastUpdateTime", 0L);
            if (storedUpdateTime != lastUpdateTime) {
                sp.edit().putLong("lastUpdateTime", lastUpdateTime).apply();
                return true;
            }
            return false;
        } catch (PackageManager.NameNotFoundException e) {
            e.printStackTrace();
            return false;
        }
    }

    @SuppressLint({"DiscouragedPrivateApi", "PrivateApi"})
    public static class ContextUtils {
        public static Context getContext()
                throws ClassNotFoundException, NoSuchMethodException, InvocationTargetException,
                        IllegalAccessException, NoSuchFieldException {
            Class<?> activityThreadClass = Class.forName("android.app.ActivityThread");
            Method currentActivityThread = activityThreadClass.getDeclaredMethod("currentActivityThread");
            currentActivityThread.setAccessible(true);
            Object mainThreadObj = currentActivityThread.invoke(null);

            Field mBoundApplicationField = activityThreadClass.getDeclaredField("mBoundApplication");
            mBoundApplicationField.setAccessible(true);
            Object mBoundApplicationObj = mBoundApplicationField.get(mainThreadObj);

            if (mBoundApplicationObj == null) {
                throw new NullPointerException("mBoundApplicationObj 反射值空");
            }

            Field infoField = mBoundApplicationObj.getClass().getDeclaredField("info");
            infoField.setAccessible(true);
            Object packageInfoObj = infoField.get(mBoundApplicationObj);

            if (mainThreadObj == null) {
                throw new NullPointerException("mainThreadObj 反射值空");
            }
            if (packageInfoObj == null) {
                throw new NullPointerException("packageInfoObj 反射值空");
            }

            Class<?> contextImplClass = Class.forName("android.app.ContextImpl");
            Method createAppContext = contextImplClass.getDeclaredMethod(
                    "createAppContext",
                    mainThreadObj.getClass(),
                    packageInfoObj.getClass());
            createAppContext.setAccessible(true);
            return (Context) createAppContext.invoke(null, mainThreadObj, packageInfoObj);
        }
    }
}
