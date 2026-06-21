package yuanwow.foxr.starter;

import android.accounts.NetworkErrorException;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;

public class NetUtils {
    public static String post(String url, String body) throws Exception {
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setRequestMethod("POST");
            conn.setReadTimeout(0x1388);
            conn.setConnectTimeout(0x2710);
            conn.setDoOutput(true);
            OutputStream os = conn.getOutputStream();
            os.write(body.getBytes());
            os.flush();
            os.close();

            int code = conn.getResponseCode();
            if (code != 0xc8) {
                throw new NetworkErrorException("response status is" + code);
            }
            return getStringFromInputStream(conn.getInputStream());
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
    }

    public static String get(String url) throws Exception {
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setRequestMethod("GET");
            conn.setReadTimeout(0x1388);
            conn.setConnectTimeout(0x2710);

            int code = conn.getResponseCode();
            if (code != 0xc8) {
                throw new NetworkErrorException("response status is " + code);
            }
            return getStringFromInputStream(conn.getInputStream());
        } finally {
            if (conn != null) {
                conn.disconnect();
            }
        }
    }

    private static String getStringFromInputStream(InputStream is) {
        String result = null;
        ByteArrayOutputStream baos = new ByteArrayOutputStream();
        byte[] buffer = new byte[0x400];
        try {
            int len;
            while ((len = is.read(buffer)) != -1) {
                baos.write(buffer, 0, len);
            }
            is.close();
            result = baos.toString();
            baos.close();
        } catch (Exception e) {
            e.printStackTrace();
        }
        return result;
    }
}
