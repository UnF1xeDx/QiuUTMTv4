package yuanwow;

import android.app.Application;
import android.content.Context;
import yuanwow.foxr.starter.gutmt.patcher;

public class YApplication extends Application {
    public YApplication() {
        super();
        patcher.ss(this);
    }
}
