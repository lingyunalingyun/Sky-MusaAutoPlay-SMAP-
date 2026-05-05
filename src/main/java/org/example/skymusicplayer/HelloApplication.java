package org.example.skymusicplayer;

import javafx.application.Application;
import javafx.application.Platform;
import javafx.fxml.FXMLLoader;
import javafx.scene.Scene;
import javafx.scene.image.Image;
import javafx.stage.Stage;

import java.io.InputStream;
import java.io.IOException;

public class HelloApplication extends Application {
    @Override
    public void start(Stage stage) throws IOException {
        // 1. 加载 FXML 布局文件
        FXMLLoader fxmlLoader = new FXMLLoader(HelloApplication.class.getResource("hello-view.fxml"));

        // 2. 创建场景。注意：根据我们之前的界面设计，建议将尺寸改为 450x350 左右
        Scene scene = new Scene(fxmlLoader.load(), 900, 500);

        // 3. 设置窗口标题 + 图标
        stage.setTitle("Sky 光遇自动弹琴助手");
        try (InputStream icon = HelloApplication.class.getResourceAsStream("icon.png")) {
            if (icon != null) stage.getIcons().add(new Image(icon));
        } catch (IOException ignored) {}

        // 4. 设置窗口关闭时的行为
        // 这行代码非常重要！如果不加，当你关闭窗口时，后台的弹奏线程可能还在运行，导致键盘乱跳
        stage.setOnCloseRequest(event -> {
            try { com.github.kwhat.jnativehook.GlobalScreen.unregisterNativeHook(); }
            catch (Exception ignored) {}
            Platform.exit();
            System.exit(0);
        });

        stage.setScene(scene);
        stage.show();
    }

    public static void main(String[] args) {
        launch();
    }
}