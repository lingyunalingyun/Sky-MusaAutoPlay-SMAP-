package org.example.skymusicplayer;

import javafx.application.Platform;
import javafx.beans.property.SimpleStringProperty;
import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.Scene;
import javafx.scene.control.*;
import javafx.scene.image.Image;
import javafx.scene.layout.*;
import javafx.stage.Stage;
import org.json.JSONArray;
import org.json.JSONObject;

import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

public class CloudSheetsWindow {

    private static final String BASE_URL = "http://musetreehouse.com";
    private static final int PER_PAGE = 20;
    private static final HttpClient HTTP = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .build();

    public static class SheetItem {
        public int id;
        public String title;
        public String artist;
        public String transcribedBy;
        public int bpm;
        public int difficulty;
        public String tags;
        public int noteCount;
        public int downloads;
        public int likes;
        public boolean recommended;
        public String uploader;
        public String description;
        public String downloadUrl;

        public final SimpleStringProperty pTitle  = new SimpleStringProperty();
        public final SimpleStringProperty pArtist = new SimpleStringProperty();
        public final SimpleStringProperty pTrans  = new SimpleStringProperty();
        public final SimpleStringProperty pBpm    = new SimpleStringProperty();
        public final SimpleStringProperty pDiff   = new SimpleStringProperty();
        public final SimpleStringProperty pDl     = new SimpleStringProperty();
        public final SimpleStringProperty pLikes  = new SimpleStringProperty();
        public final SimpleStringProperty pUp     = new SimpleStringProperty();

        void bindProps() {
            pTitle.set((recommended ? "★ " : "") + title);
            pArtist.set(artist == null ? "" : artist);
            pTrans.set(transcribedBy == null ? "" : transcribedBy);
            pBpm.set(bpm > 0 ? String.valueOf(bpm) : "");
            pDiff.set("★".repeat(Math.max(0, Math.min(5, difficulty))));
            pDl.set(String.valueOf(downloads));
            pLikes.set(String.valueOf(likes));
            pUp.set(uploader == null ? "" : uploader);
        }
    }

    public static void open(Stage owner, Path songsDir, Runnable onDownloaded) {
        Stage stage = new Stage();
        stage.setTitle("☁ 在线曲库 — 缪斯 MUSE");
        try (InputStream icon = CloudSheetsWindow.class.getResourceAsStream("icon.png")) {
            if (icon != null) stage.getIcons().add(new Image(icon));
        } catch (IOException ignored) {}
        if (owner != null) stage.initOwner(owner);

        // ── 顶部工具栏 ──
        TextField searchField = new TextField();
        searchField.setPromptText("🔍 搜索曲名 / 原唱 / 创谱人 / 标签");
        HBox.setHgrow(searchField, Priority.ALWAYS);

        ComboBox<String> sortCombo = new ComboBox<>(FXCollections.observableArrayList(
                "最新", "最热", "下载量"));
        sortCombo.getSelectionModel().select(0);

        ComboBox<String> diffCombo = new ComboBox<>(FXCollections.observableArrayList(
                "全部难度", "★ 1", "★★ 2", "★★★ 3", "★★★★ 4", "★★★★★ 5"));
        diffCombo.getSelectionModel().select(0);

        Button searchBtn  = new Button("筛选");
        Button refreshBtn = new Button("🔄");

        HBox toolbar = new HBox(8, searchField, sortCombo, diffCombo, searchBtn, refreshBtn);
        toolbar.setAlignment(Pos.CENTER_LEFT);
        toolbar.setPadding(new Insets(10));

        // ── 列表 ──
        TableView<SheetItem> table = new TableView<>();
        table.setPlaceholder(new Label("// 暂无曲谱 - 点击「🔄」刷新"));

        TableColumn<SheetItem, String> cTitle = new TableColumn<>("曲名");
        cTitle.setCellValueFactory(c -> c.getValue().pTitle);
        cTitle.setPrefWidth(220);

        TableColumn<SheetItem, String> cArtist = new TableColumn<>("原唱");
        cArtist.setCellValueFactory(c -> c.getValue().pArtist);
        cArtist.setPrefWidth(110);

        TableColumn<SheetItem, String> cTrans = new TableColumn<>("创谱人");
        cTrans.setCellValueFactory(c -> c.getValue().pTrans);
        cTrans.setPrefWidth(100);

        TableColumn<SheetItem, String> cBpm = new TableColumn<>("BPM");
        cBpm.setCellValueFactory(c -> c.getValue().pBpm);
        cBpm.setPrefWidth(60);

        TableColumn<SheetItem, String> cDiff = new TableColumn<>("难度");
        cDiff.setCellValueFactory(c -> c.getValue().pDiff);
        cDiff.setPrefWidth(80);

        TableColumn<SheetItem, String> cDl = new TableColumn<>("↓");
        cDl.setCellValueFactory(c -> c.getValue().pDl);
        cDl.setPrefWidth(55);

        TableColumn<SheetItem, String> cLikes = new TableColumn<>("♥");
        cLikes.setCellValueFactory(c -> c.getValue().pLikes);
        cLikes.setPrefWidth(50);

        TableColumn<SheetItem, String> cUp = new TableColumn<>("上传者");
        cUp.setCellValueFactory(c -> c.getValue().pUp);
        cUp.setPrefWidth(110);

        table.getColumns().addAll(cTitle, cArtist, cTrans, cBpm, cDiff, cDl, cLikes, cUp);
        VBox.setVgrow(table, Priority.ALWAYS);

        // ── 详情面板 ──
        Label detailTitle = new Label("// 选中一首曲谱查看详情");
        detailTitle.setStyle("-fx-font-weight: bold; -fx-font-size: 13px;");
        Label detailMeta = new Label("");
        detailMeta.setWrapText(true);
        detailMeta.setStyle("-fx-text-fill: gray; -fx-font-size: 11px;");
        Label detailDesc = new Label("");
        detailDesc.setWrapText(true);
        detailDesc.setStyle("-fx-font-size: 11px;");

        Button downloadBtn = new Button("↓ 下载到本地曲库");
        downloadBtn.setStyle("-fx-background-color: #2196F3; -fx-text-fill: white; -fx-font-weight: bold;");
        downloadBtn.setDisable(true);
        downloadBtn.setMaxWidth(Double.MAX_VALUE);

        Label statusLbl = new Label("");
        statusLbl.setStyle("-fx-text-fill: #666; -fx-font-size: 11px;");

        VBox detail = new VBox(6, detailTitle, detailMeta, detailDesc, downloadBtn, statusLbl);
        detail.setPadding(new Insets(10));
        detail.setStyle("-fx-border-color: #cccccc; -fx-border-width: 1 0 0 0;");
        detail.setPrefHeight(160);

        // ── 分页 ──
        Button prevBtn  = new Button("‹ 上一页");
        Button nextBtn  = new Button("下一页 ›");
        Label  pageLbl  = new Label("第 0/0 页");
        Label  totalLbl = new Label("总数: 0");
        prevBtn.setDisable(true);
        nextBtn.setDisable(true);

        HBox pager = new HBox(8, prevBtn, pageLbl, nextBtn, new Region(), totalLbl);
        HBox.setHgrow(pager.getChildren().get(3), Priority.ALWAYS);
        pager.setAlignment(Pos.CENTER_LEFT);
        pager.setPadding(new Insets(6, 10, 8, 10));

        // ── 状态机 ──
        AtomicInteger currentPage  = new AtomicInteger(1);
        AtomicInteger totalPages   = new AtomicInteger(1);
        AtomicInteger totalCount   = new AtomicInteger(0);

        Runnable updateDetail = () -> {
            SheetItem s = table.getSelectionModel().getSelectedItem();
            if (s == null) {
                detailTitle.setText("// 选中一首曲谱查看详情");
                detailMeta.setText("");
                detailDesc.setText("");
                downloadBtn.setDisable(true);
                return;
            }
            detailTitle.setText((s.recommended ? "★ " : "") + s.title);
            StringBuilder meta = new StringBuilder();
            if (s.artist != null && !s.artist.isEmpty())   meta.append("原唱: ").append(s.artist).append("  ");
            if (s.transcribedBy != null && !s.transcribedBy.isEmpty()) meta.append("谱: ").append(s.transcribedBy).append("  ");
            if (s.bpm > 0)        meta.append("BPM: ").append(s.bpm).append("  ");
            meta.append("音符: ").append(s.noteCount).append("  ");
            meta.append("难度: ").append("★".repeat(Math.max(0, Math.min(5, s.difficulty)))).append("  ");
            meta.append("↓ ").append(s.downloads).append("  ♥ ").append(s.likes);
            if (s.tags != null && !s.tags.isEmpty()) meta.append("\n标签: ").append(s.tags);
            if (s.uploader != null) meta.append("\n上传者: ").append(s.uploader);
            detailMeta.setText(meta.toString());
            detailDesc.setText(s.description == null ? "" : s.description);
            downloadBtn.setDisable(false);
        };
        table.getSelectionModel().selectedItemProperty().addListener((o, a, b) -> updateDetail.run());

        // 加载列表（异步）
        Runnable[] doFetch = new Runnable[1];
        doFetch[0] = () -> {
            statusLbl.setText("加载中...");
            String q          = searchField.getText().trim();
            int sortIdx       = sortCombo.getSelectionModel().getSelectedIndex();
            int diffIdx       = diffCombo.getSelectionModel().getSelectedIndex();
            String sortKey    = sortIdx == 1 ? "hot" : sortIdx == 2 ? "downloads" : "newest";
            int difficulty    = diffIdx;
            int page          = currentPage.get();

            String url = BASE_URL + "/api/sheets/list.php?per_page=" + PER_PAGE
                    + "&page=" + page
                    + "&sort=" + sortKey;
            if (!q.isEmpty()) url += "&q=" + URLEncoder.encode(q, StandardCharsets.UTF_8);
            if (difficulty >= 1 && difficulty <= 5) url += "&difficulty=" + difficulty;

            HttpRequest req = HttpRequest.newBuilder(URI.create(url))
                    .timeout(Duration.ofSeconds(15))
                    .GET().build();

            HTTP.sendAsync(req, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8))
                .thenAccept(resp -> Platform.runLater(() -> {
                    if (resp.statusCode() != 200) {
                        statusLbl.setText("加载失败 HTTP " + resp.statusCode());
                        return;
                    }
                    try {
                        JSONObject obj = new JSONObject(resp.body());
                        if (!"ok".equals(obj.optString("status"))) {
                            statusLbl.setText("服务端错误: " + obj.optString("msg", "未知"));
                            return;
                        }
                        totalCount.set(obj.optInt("total"));
                        totalPages.set(Math.max(1, obj.optInt("pages")));
                        JSONArray arr = obj.optJSONArray("items");
                        ObservableList<SheetItem> rows = FXCollections.observableArrayList();
                        if (arr != null) {
                            for (int i = 0; i < arr.length(); i++) {
                                JSONObject it = arr.getJSONObject(i);
                                SheetItem s = new SheetItem();
                                s.id            = it.optInt("id");
                                s.title         = it.optString("title");
                                s.artist        = it.optString("artist", "");
                                s.transcribedBy = it.optString("transcribed_by", "");
                                s.bpm           = it.optInt("bpm");
                                s.difficulty    = it.optInt("difficulty");
                                s.tags          = it.optString("tags", "");
                                s.noteCount     = it.optInt("note_count");
                                s.downloads     = it.optInt("downloads");
                                s.likes         = it.optInt("likes");
                                s.recommended   = it.optBoolean("is_recommended");
                                s.uploader      = it.optString("uploader", "");
                                s.description   = it.optString("description", "");
                                s.downloadUrl   = it.optString("download_url", "");
                                s.bindProps();
                                rows.add(s);
                            }
                        }
                        table.setItems(rows);
                        pageLbl.setText("第 " + currentPage.get() + "/" + totalPages.get() + " 页");
                        totalLbl.setText("总数: " + totalCount.get());
                        prevBtn.setDisable(currentPage.get() <= 1);
                        nextBtn.setDisable(currentPage.get() >= totalPages.get());
                        statusLbl.setText("已加载 " + rows.size() + " 首");
                    } catch (Exception ex) {
                        statusLbl.setText("解析失败: " + ex.getMessage());
                    }
                }))
                .exceptionally(ex -> {
                    Platform.runLater(() -> statusLbl.setText("网络错误: " + ex.getMessage()));
                    return null;
                });
        };

        searchBtn.setOnAction(e -> { currentPage.set(1); doFetch[0].run(); });
        refreshBtn.setOnAction(e -> doFetch[0].run());
        searchField.setOnAction(e -> { currentPage.set(1); doFetch[0].run(); });
        sortCombo.setOnAction(e -> { currentPage.set(1); doFetch[0].run(); });
        diffCombo.setOnAction(e -> { currentPage.set(1); doFetch[0].run(); });
        prevBtn.setOnAction(e -> {
            if (currentPage.get() > 1) { currentPage.decrementAndGet(); doFetch[0].run(); }
        });
        nextBtn.setOnAction(e -> {
            if (currentPage.get() < totalPages.get()) { currentPage.incrementAndGet(); doFetch[0].run(); }
        });

        // 下载逻辑
        downloadBtn.setOnAction(e -> {
            SheetItem s = table.getSelectionModel().getSelectedItem();
            if (s == null || s.downloadUrl == null || s.downloadUrl.isEmpty()) return;
            downloadBtn.setDisable(true);
            statusLbl.setText("下载中: " + s.title);
            HttpRequest req = HttpRequest.newBuilder(URI.create(s.downloadUrl))
                    .timeout(Duration.ofSeconds(30))
                    .GET().build();
            HTTP.sendAsync(req, HttpResponse.BodyHandlers.ofByteArray())
                .thenAccept(resp -> Platform.runLater(() -> {
                    try {
                        if (resp.statusCode() != 200) {
                            statusLbl.setText("下载失败 HTTP " + resp.statusCode());
                            downloadBtn.setDisable(false);
                            return;
                        }
                        if (!Files.isDirectory(songsDir)) Files.createDirectories(songsDir);
                        String safe = s.title.replaceAll("[\\\\/:*?\"<>|]", "_");
                        if (safe.isEmpty()) safe = "sheet_" + s.id;
                        Path target = songsDir.resolve(safe + ".txt");
                        // 同名追加 ID 防覆盖
                        if (Files.exists(target)) target = songsDir.resolve(safe + "_" + s.id + ".txt");
                        Files.write(target, resp.body(), java.nio.file.StandardOpenOption.CREATE,
                                java.nio.file.StandardOpenOption.TRUNCATE_EXISTING);
                        statusLbl.setText("✓ 已下载: " + target.getFileName());
                        downloadBtn.setDisable(false);
                        if (onDownloaded != null) onDownloaded.run();
                        // 增量更新本行 downloads 计数显示
                        s.downloads += 1;
                        s.bindProps();
                        table.refresh();
                    } catch (Exception ex) {
                        statusLbl.setText("保存失败: " + ex.getMessage());
                        downloadBtn.setDisable(false);
                    }
                }))
                .exceptionally(ex -> {
                    Platform.runLater(() -> {
                        statusLbl.setText("下载错误: " + ex.getMessage());
                        downloadBtn.setDisable(false);
                    });
                    return null;
                });
        });

        // ── 组装 ──
        VBox root = new VBox(toolbar, table, detail, pager);
        Scene scene = new Scene(root, 880, 620);

        // 跟随主窗口暗色主题
        if (owner != null && owner.getScene() != null) {
            java.net.URL darkUrl = CloudSheetsWindow.class.getResource("dark.css");
            if (darkUrl != null) {
                String css = darkUrl.toExternalForm();
                if (owner.getScene().getStylesheets().contains(css)) {
                    scene.getStylesheets().add(css);
                }
            }
        }

        stage.setScene(scene);
        stage.show();
        // 首次自动加载
        Platform.runLater(doFetch[0]);
    }
}
