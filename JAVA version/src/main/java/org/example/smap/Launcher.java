package org.example.smap;

import javafx.application.Application;

public class Launcher {
    public static void main(String[] args) {
        // 修复 JavaFX 多显示器/不同 DPI 缩放间切换导致窗口内容坍缩 (JDK-8146920):
        // 固定缩放为主屏的 150%, 不再按 per-monitor DPI 重渲染。必须在 Application.launch 之前设置。
        System.setProperty("glass.win.uiScale", "1.5");
        Application.launch(HelloApplication.class, args);
    }
}