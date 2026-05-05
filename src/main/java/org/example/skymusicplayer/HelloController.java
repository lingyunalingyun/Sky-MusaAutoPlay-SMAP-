package org.example.skymusicplayer;

import javafx.animation.PauseTransition;
import javafx.application.Platform;
import javafx.beans.property.SimpleLongProperty;
import javafx.beans.property.SimpleStringProperty;
import javafx.collections.FXCollections;
import javafx.collections.ObservableList;
import javafx.event.ActionEvent;
import javafx.event.EventHandler;
import javafx.fxml.FXML;
import javafx.geometry.Insets;
import javafx.geometry.Orientation;
import javafx.geometry.Pos;
import javafx.scene.Node;
import javafx.scene.Scene;
import javafx.scene.canvas.Canvas;
import javafx.scene.canvas.GraphicsContext;
import javafx.scene.control.*;
import javafx.scene.control.cell.ChoiceBoxTableCell;
import javafx.scene.control.cell.TextFieldTableCell;
import javafx.scene.input.KeyCode;
import javafx.scene.input.KeyEvent;
import javafx.scene.layout.Background;
import javafx.scene.layout.BackgroundFill;
import javafx.scene.layout.GridPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Pane;
import javafx.scene.layout.Priority;
import javafx.scene.layout.Region;
import javafx.scene.layout.VBox;
import javafx.scene.paint.Color;
import javafx.scene.shape.Line;
import javafx.scene.shape.Rectangle;
import javafx.stage.FileChooser;
import javafx.stage.Stage;
import javafx.stage.Window;
import javafx.util.Duration;
import javafx.util.converter.LongStringConverter;
import org.json.JSONArray;
import org.json.JSONObject;

import java.awt.Robot;
import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.TreeMap;
import java.util.TreeSet;

public class HelloController {

    @FXML private TextField filePathField;
    @FXML private Label statusLabel;
    @FXML private ListView<String> songListView;
    @FXML private TextField searchField;
    @FXML private ComboBox<String> filterCombo;
    @FXML private Slider progressSlider;
    private volatile double seekFraction = 0;
    @FXML private Slider speedSlider;
    @FXML private Label speedLabel;
    @FXML private Spinner<Integer> countdownSpinner;
    @FXML private ToggleButton themeToggle;
    @FXML private ToggleButton audioModeToggle;

    private volatile boolean isPreviewing = false;
    private Thread previewThread = null;

    // 15 个虚拟琴键按钮
    @FXML private Button pianoKey0, pianoKey1, pianoKey2, pianoKey3, pianoKey4;
    @FXML private Button pianoKey5, pianoKey6, pianoKey7, pianoKey8, pianoKey9;
    @FXML private Button pianoKey10, pianoKey11, pianoKey12, pianoKey13, pianoKey14;
    private Button[] pianoKeys;

    // 重映射状态
    private int remappingIndex = -1;
    private EventHandler<KeyEvent> remapHandler;

    // 琴键样式
    private static final String KEY_DEFAULT_STYLE =
            "-fx-background-color: white; -fx-text-fill: #222; "
            + "-fx-font-size: 16px; -fx-font-weight: bold; "
            + "-fx-border-color: #888; -fx-border-radius: 6; -fx-background-radius: 6;";
    private static final String KEY_HIGHLIGHT_STYLE =
            "-fx-background-color: #FFEB3B; -fx-text-fill: #333; "
            + "-fx-font-size: 16px; -fx-font-weight: bold; "
            + "-fx-border-color: #FF9800; -fx-border-width: 2; "
            + "-fx-border-radius: 6; -fx-background-radius: 6;";
    private static final String KEY_REMAP_STYLE =
            "-fx-background-color: #2196F3; -fx-text-fill: white; "
            + "-fx-font-size: 16px; -fx-font-weight: bold; "
            + "-fx-border-color: #0D47A1; -fx-border-width: 2; "
            + "-fx-border-radius: 6; -fx-background-radius: 6;";

    private final Map<String, Integer> keyMap = new HashMap<>();
    /**
     * 数据根目录: jpackage 打包时跟 .exe 同目录, 开发时为 cwd.
     * 所有 *.json 配置和 songs/ 都基于此.
     */
    static final java.nio.file.Path DATA_DIR = resolveDataDir();
    static final String SONGS_DIR = DATA_DIR.resolve("songs").toString();
    private final String CONFIG_PATH = DATA_DIR.resolve("key_config.json").toString();
    private static final String FAVORITES_PATH = DATA_DIR.resolve("favorites.json").toString();
    private static final String CATEGORIES_PATH = DATA_DIR.resolve("categories.json").toString();
    private static final String SETTINGS_PATH = DATA_DIR.resolve("settings.json").toString();

    /**
     * 首次启动: 若 DATA_DIR/songs 不存在或为空, 解压 classpath 下的 songs.zip 到该目录.
     * 打包成 exe 后让用户开箱即用; 开发环境若已有 songs/ 则跳过.
     */
    private static void ensureSongsExtracted() {
        java.nio.file.Path songsDir = DATA_DIR.resolve("songs");
        if (java.nio.file.Files.isDirectory(songsDir)) {
            try (var stream = java.nio.file.Files.list(songsDir)) {
                if (stream.findAny().isPresent()) return;
            } catch (IOException e) { return; }
        }
        try (java.io.InputStream zin = HelloController.class.getResourceAsStream("songs.zip")) {
            if (zin == null) return;
            java.nio.file.Files.createDirectories(songsDir);
            try (java.util.zip.ZipInputStream zip = new java.util.zip.ZipInputStream(zin)) {
                java.util.zip.ZipEntry e;
                int count = 0;
                while ((e = zip.getNextEntry()) != null) {
                    if (e.isDirectory()) continue;
                    String name = e.getName().replace('\\', '/');
                    if (name.startsWith("songs/")) name = name.substring(6);
                    if (name.isEmpty() || name.contains("..")) continue;
                    java.nio.file.Path out = songsDir.resolve(name);
                    java.nio.file.Path parent = out.getParent();
                    if (parent != null) java.nio.file.Files.createDirectories(parent);
                    java.nio.file.Files.copy(zip, out, StandardCopyOption.REPLACE_EXISTING);
                    count++;
                }
                System.out.println("[Init] 解压 " + count + " 首曲谱到 " + songsDir);
            }
        } catch (IOException e) {
            System.err.println("[Init] 曲谱解压失败: " + e);
        }
    }

    private static java.nio.file.Path resolveDataDir() {
        // jpackage 给打包后的 launcher 设这个属性, 指向 .exe 的绝对路径
        String appPath = System.getProperty("jpackage.app-path");
        if (appPath != null && !appPath.isEmpty()) {
            try {
                java.nio.file.Path p = java.nio.file.Paths.get(appPath).toAbsolutePath().getParent();
                if (p != null) return p;
            } catch (Exception ignored) {}
        }
        return java.nio.file.Paths.get("").toAbsolutePath();
    }
    private boolean loadingSettings = false;
    private int defaultBpm = 120;
    private int defaultSubdiv = 4;

    // 收藏 + 分类
    private final Set<String> favorites = new HashSet<>();
    private final Map<String, Set<String>> tagsByFile = new HashMap<>();
    private final ObservableList<String> filterOptions = FXCollections.observableArrayList();

    private volatile boolean isPlaying = false;
    private volatile boolean isPaused = false;
    private volatile boolean skipCountdown = false;
    private final List<MusicNote> playlist = new ArrayList<>();
    private File currentFile = null;
    private String currentSongName = "";
    private String currentArtist = "";
    private String currentTranscriber = "";
    private int currentSongBpm = 120;
    private int currentSongSubdiv = 4;

    // 全量曲目库
    private final List<File> allSongFiles = new ArrayList<>();
    private final List<String> allSongNames = new ArrayList<>();
    // 当前显示的过滤结果
    private final List<File> songFiles = new ArrayList<>();
    private final ObservableList<String> songNames = FXCollections.observableArrayList();

    @FXML
    public void initialize() {
        ensureSongsExtracted();
        loadKeyConfig();
        loadFavorites();
        loadCategories();

        pianoKeys = new Button[]{
                pianoKey0, pianoKey1, pianoKey2, pianoKey3, pianoKey4,
                pianoKey5, pianoKey6, pianoKey7, pianoKey8, pianoKey9,
                pianoKey10, pianoKey11, pianoKey12, pianoKey13, pianoKey14
        };
        for (Button b : pianoKeys) if (b != null) b.setStyle(KEY_DEFAULT_STYLE);
        updatePianoKeyLabels();

        songListView.setItems(songNames);
        songListView.setCellFactory(lv -> new SongListCell());
        songListView.setContextMenu(buildSongContextMenu());
        songListView.getSelectionModel().selectedItemProperty().addListener((obs, oldVal, newVal) -> {
            int index = songListView.getSelectionModel().getSelectedIndex();
            if (index >= 0 && index < songFiles.size()) {
                stopPreview();
                parseJsonMusic(songFiles.get(index));
                if (audioModeToggle != null && audioModeToggle.isSelected() && !playlist.isEmpty()) {
                    startPreview();
                }
            }
        });

        filterCombo.setItems(filterOptions);
        updateFilterOptions();
        filterCombo.setValue("全部");
        filterCombo.setOnAction(e -> applyFilter());

        searchField.textProperty().addListener((obs, old, val) -> applyFilter());

        speedSlider.valueProperty().addListener((obs, old, val) ->
                speedLabel.setText(String.format("%.1fx", val.doubleValue())));

        // 进度条拖动 → 保存 seekFraction, 下次播放从此处开始
        progressSlider.valueChangingProperty().addListener((obs, wasChanging, isChanging) -> {
            if (wasChanging && !isChanging) seekFraction = progressSlider.getValue();
        });
        progressSlider.setOnMouseReleased(e -> seekFraction = progressSlider.getValue());

        // 倒计时 Spinner
        countdownSpinner.setValueFactory(new SpinnerValueFactory.IntegerSpinnerValueFactory(1, 10, 5));
        countdownSpinner.valueProperty().addListener((obs, old, val) -> saveSettings());

        // 主题切换
        themeToggle.selectedProperty().addListener((obs, old, val) -> {
            applyTheme(val);
            saveSettings();
        });

        // 试听模式: 关 → 立即停掉预览; 开 → 若已有曲目则立刻试听
        audioModeToggle.selectedProperty().addListener((obs, old, val) -> {
            if (!val) {
                stopPreview();
            } else if (!playlist.isEmpty()) {
                startPreview();
            }
        });

        loadSettings();
        refreshLibrary();
        registerGlobalHotkeys();
    }

    /**
     * 切换 dark.css 到 / 从 Scene 的 stylesheets
     */
    private void applyTheme(boolean dark) {
        Scene sc = themeToggle.getScene();
        if (sc == null) return; // 还未加入场景, loadSettings 会延迟应用
        java.net.URL url = HelloController.class.getResource("dark.css");
        if (url == null) return;
        String css = url.toExternalForm();
        sc.getStylesheets().remove(css);
        if (dark) sc.getStylesheets().add(css);
        themeToggle.setText(dark ? "☀" : "🌙");
    }

    private void loadSettings() {
        File f = new File(SETTINGS_PATH);
        if (!f.exists()) return;
        loadingSettings = true;
        try {
            JSONObject obj = new JSONObject(Files.readString(f.toPath()));
            if (obj.has("countdownSeconds")) {
                countdownSpinner.getValueFactory().setValue(obj.getInt("countdownSeconds"));
            }
            if (obj.has("darkTheme") && obj.getBoolean("darkTheme")) {
                Platform.runLater(() -> themeToggle.setSelected(true));
            }
            if (obj.has("instrument")) {
                ToneGenerator.setInstrument(obj.getString("instrument"));
            }
            if (obj.has("bpm")) defaultBpm = Math.max(30, Math.min(400, obj.getInt("bpm")));
            if (obj.has("subdiv")) defaultSubdiv = Math.max(1, Math.min(32, obj.getInt("subdiv")));
        } catch (Exception ignored) {
        } finally {
            loadingSettings = false;
        }
    }

    private void saveSettings() {
        if (loadingSettings) return;
        try {
            JSONObject obj = new JSONObject();
            obj.put("countdownSeconds", countdownSpinner.getValue());
            obj.put("darkTheme", themeToggle.isSelected());
            obj.put("instrument", ToneGenerator.getInstrument());
            obj.put("bpm", defaultBpm);
            obj.put("subdiv", defaultSubdiv);
            Files.writeString(Paths.get(SETTINGS_PATH), obj.toString());
        } catch (IOException ignored) {}
    }

    /**
     * 综合过滤: 搜索文本 + 收藏/标签下拉
     */
    private void applyFilter() {
        songFiles.clear();
        songNames.clear();
        String searchText = searchField != null ? searchField.getText() : "";
        String lower = searchText == null ? "" : searchText.trim().toLowerCase();
        String filterChoice = filterCombo != null ? filterCombo.getValue() : null;

        for (int i = 0; i < allSongNames.size(); i++) {
            String name = allSongNames.get(i);
            File f = allSongFiles.get(i);
            String fname = f.getName();

            if (!lower.isEmpty() && !name.toLowerCase().contains(lower)) continue;

            if ("⭐ 仅收藏".equals(filterChoice)) {
                if (!favorites.contains(fname)) continue;
            } else if (filterChoice != null && filterChoice.startsWith("🏷 ")) {
                String tag = filterChoice.substring(2).trim();
                Set<String> ts = tagsByFile.get(fname);
                if (ts == null || !ts.contains(tag)) continue;
            }

            songFiles.add(f);
            songNames.add(name);
        }
    }

    // ========== 收藏 / 分类持久化 ==========

    private void loadFavorites() {
        File f = new File(FAVORITES_PATH);
        if (!f.exists()) return;
        try {
            JSONArray arr = new JSONArray(Files.readString(f.toPath()));
            favorites.clear();
            for (int i = 0; i < arr.length(); i++) favorites.add(arr.getString(i));
        } catch (Exception ignored) {}
    }

    private void saveFavorites() {
        try {
            JSONArray arr = new JSONArray();
            for (String s : favorites) arr.put(s);
            Files.writeString(Paths.get(FAVORITES_PATH), arr.toString());
        } catch (IOException ignored) {}
    }

    private void loadCategories() {
        File f = new File(CATEGORIES_PATH);
        if (!f.exists()) return;
        try {
            JSONObject obj = new JSONObject(Files.readString(f.toPath()));
            tagsByFile.clear();
            for (String key : obj.keySet()) {
                JSONArray arr = obj.getJSONArray(key);
                Set<String> tags = new TreeSet<>();
                for (int i = 0; i < arr.length(); i++) tags.add(arr.getString(i));
                tagsByFile.put(key, tags);
            }
        } catch (Exception ignored) {}
    }

    private void saveCategories() {
        try {
            JSONObject obj = new JSONObject();
            for (Map.Entry<String, Set<String>> e : tagsByFile.entrySet()) {
                JSONArray arr = new JSONArray();
                for (String t : e.getValue()) arr.put(t);
                obj.put(e.getKey(), arr);
            }
            Files.writeString(Paths.get(CATEGORIES_PATH), obj.toString());
        } catch (IOException ignored) {}
    }

    /**
     * 重建筛选下拉的可选项: 全部 / 收藏 / 各标签
     */
    private void updateFilterOptions() {
        String current = filterCombo != null ? filterCombo.getValue() : null;
        Set<String> uniqueTags = new TreeSet<>();
        for (Set<String> ts : tagsByFile.values()) uniqueTags.addAll(ts);

        filterOptions.clear();
        filterOptions.add("全部");
        filterOptions.add("⭐ 仅收藏");
        for (String t : uniqueTags) filterOptions.add("🏷 " + t);

        if (filterCombo != null) {
            if (current != null && filterOptions.contains(current)) {
                filterCombo.setValue(current);
            } else {
                filterCombo.setValue("全部");
            }
        }
    }

    /**
     * 列表行右键菜单: 加标签 / 移除标签
     */
    private ContextMenu buildSongContextMenu() {
        ContextMenu cm = new ContextMenu();
        MenuItem addTag = new MenuItem("➕ 添加标签...");
        addTag.setOnAction(e -> {
            int idx = songListView.getSelectionModel().getSelectedIndex();
            if (idx < 0 || idx >= songFiles.size()) return;
            TextInputDialog d = new TextInputDialog();
            d.setTitle("添加标签");
            d.setHeaderText(songNames.get(idx));
            d.setContentText("标签名:");
            Optional<String> r = d.showAndWait();
            if (r.isPresent() && !r.get().trim().isEmpty()) {
                String tag = r.get().trim();
                String fname = songFiles.get(idx).getName();
                tagsByFile.computeIfAbsent(fname, k -> new TreeSet<>()).add(tag);
                saveCategories();
                updateFilterOptions();
                songListView.refresh();
                updateStatus("状态: 已为「" + songNames.get(idx) + "」添加标签 " + tag);
            }
        });
        MenuItem removeTag = new MenuItem("➖ 移除标签...");
        removeTag.setOnAction(e -> {
            int idx = songListView.getSelectionModel().getSelectedIndex();
            if (idx < 0 || idx >= songFiles.size()) return;
            String fname = songFiles.get(idx).getName();
            Set<String> tags = tagsByFile.getOrDefault(fname, Collections.emptySet());
            if (tags.isEmpty()) {
                new Alert(Alert.AlertType.INFORMATION, "此曲目暂无标签", ButtonType.OK).showAndWait();
                return;
            }
            ChoiceDialog<String> cd = new ChoiceDialog<>(tags.iterator().next(), tags);
            cd.setTitle("移除标签");
            cd.setHeaderText(songNames.get(idx));
            cd.setContentText("选择要移除的标签:");
            cd.showAndWait().ifPresent(t -> {
                tags.remove(t);
                if (tags.isEmpty()) tagsByFile.remove(fname);
                saveCategories();
                updateFilterOptions();
                songListView.refresh();
                updateStatus("状态: 已移除标签 " + t);
            });
        });
        cm.getItems().addAll(addTag, removeTag);
        return cm;
    }

    /**
     * 自定义曲目行: ★ 切换收藏 + 曲名 + 标签提示
     */
    private class SongListCell extends ListCell<String> {
        private final Button starBtn = new Button("☆");
        private final Label nameLbl = new Label();
        private final HBox box = new HBox(5, starBtn, nameLbl);

        SongListCell() {
            box.setAlignment(Pos.CENTER_LEFT);
            starBtn.setFocusTraversable(false);
            starBtn.setOnAction(e -> {
                int idx = getIndex();
                if (idx < 0 || idx >= songFiles.size()) return;
                String fname = songFiles.get(idx).getName();
                if (favorites.contains(fname)) favorites.remove(fname);
                else favorites.add(fname);
                saveFavorites();
                songListView.refresh();
            });
        }

        @Override
        protected void updateItem(String item, boolean empty) {
            super.updateItem(item, empty);
            if (empty || item == null) {
                setGraphic(null);
                setText(null);
                return;
            }
            int idx = getIndex();
            boolean isFav = false;
            String tooltip = item;
            if (idx >= 0 && idx < songFiles.size()) {
                String fname = songFiles.get(idx).getName();
                isFav = favorites.contains(fname);
                Set<String> ts = tagsByFile.get(fname);
                if (ts != null && !ts.isEmpty()) tooltip += " [" + String.join(", ", ts) + "]";
            }
            starBtn.setText(isFav ? "★" : "☆");
            starBtn.setStyle(isFav
                    ? "-fx-background-color: transparent; -fx-text-fill: #FFC107; -fx-font-size: 14px; -fx-padding: 0 4 0 0;"
                    : "-fx-background-color: transparent; -fx-text-fill: #aaa; -fx-font-size: 14px; -fx-padding: 0 4 0 0;");
            nameLbl.setText(item);
            setTooltip(new Tooltip(tooltip));
            setGraphic(box);
            setText(null);
        }
    }

    /**
     * 把 keyMap 中的键码转为可读字母显示在虚拟琴键按钮上
     */
    private void updatePianoKeyLabels() {
        if (pianoKeys == null) return;
        for (int i = 0; i < pianoKeys.length; i++) {
            if (pianoKeys[i] == null) continue;
            Integer code = keyMap.get("1Key" + i);
            pianoKeys[i].setText(code != null ? prettyKeyName(code) : "?");
        }
    }

    /**
     * 点击虚拟琴键 → 进入"等待键盘按键"重映射模式
     */
    @FXML
    protected void onPianoKeyClick(ActionEvent event) {
        if (isPlaying) {
            updateStatus("状态: 演奏中, 无法重映射");
            return;
        }
        Button btn = (Button) event.getSource();
        String id = btn.getId(); // pianoKeyN
        int idx;
        try { idx = Integer.parseInt(id.replaceAll("[^0-9]", "")); }
        catch (Exception e) { return; }
        if (idx < 0 || idx >= pianoKeys.length) return;

        // 取消上一个未完成的重映射
        if (remappingIndex >= 0 && remappingIndex < pianoKeys.length) {
            pianoKeys[remappingIndex].setStyle(KEY_DEFAULT_STYLE);
        }
        remappingIndex = idx;
        btn.setStyle(KEY_REMAP_STYLE);
        updateStatus("状态: 请按键盘上想绑定到此琴键的按键 (Esc 取消)");

        if (remapHandler == null) {
            remapHandler = ev -> {
                if (remappingIndex < 0) return;
                int slot = remappingIndex;
                if (ev.getCode() == javafx.scene.input.KeyCode.ESCAPE) {
                    pianoKeys[slot].setStyle(KEY_DEFAULT_STYLE);
                    remappingIndex = -1;
                    updateStatus("状态: 已取消重映射");
                    ev.consume();
                    return;
                }
                int code = ev.getCode().getCode();
                keyMap.put("1Key" + slot, code);
                pianoKeys[slot].setText(ev.getCode().toString());
                pianoKeys[slot].setStyle(KEY_DEFAULT_STYLE);
                remappingIndex = -1;
                updateStatus("状态: 已映射 1Key" + slot + " → " + ev.getCode());
                ev.consume();
            };
            Scene sc = btn.getScene();
            if (sc != null) sc.addEventFilter(KeyEvent.KEY_PRESSED, remapHandler);
        }
    }

    /**
     * 闪烁高亮指定琴键 (用于演奏/录制时的视觉反馈)
     */
    private void flashKey(String keyName) {
        if (pianoKeys == null || keyName == null || !keyName.startsWith("1Key")) return;
        int idx;
        try { idx = Integer.parseInt(keyName.substring(4)); }
        catch (Exception e) { return; }
        if (idx < 0 || idx >= pianoKeys.length || pianoKeys[idx] == null) return;
        Button btn = pianoKeys[idx];
        Platform.runLater(() -> {
            // 重映射中的键不被闪烁覆盖
            if (remappingIndex == idx) return;
            btn.setStyle(KEY_HIGHLIGHT_STYLE);
            PauseTransition pause = new PauseTransition(Duration.millis(130));
            pause.setOnFinished(e -> {
                if (remappingIndex != idx) btn.setStyle(KEY_DEFAULT_STYLE);
            });
            pause.play();
        });
    }

    @FXML
    protected void saveConfig() {
        try {
            JSONObject json = new JSONObject(keyMap);
            Files.writeString(Paths.get(CONFIG_PATH), json.toString());
            updateStatus("状态: 按键配置已永久保存！");
        } catch (IOException e) {
            updateStatus("状态: 保存失败");
        }
    }

    private void loadKeyConfig() {
        try {
            File file = new File(CONFIG_PATH);
            if (file.exists()) {
                JSONObject json = new JSONObject(Files.readString(file.toPath()));
                for (String key : json.keySet()) keyMap.put(key, json.getInt(key));
            } else {
                String[] keys = {"1Key0", "1Key1", "1Key2", "1Key3", "1Key4",
                        "1Key5", "1Key6", "1Key7", "1Key8", "1Key9",
                        "1Key10", "1Key11", "1Key12", "1Key13", "1Key14"};

                int[] values = {
                        89, 85, 73, 79, 80, // Y, U, I, O, P
                        72, 74, 75, 76, 59, // H, J, K, L, ;
                        78, 77, 44, 46, 47  // N, M, ,, ., /
                };

                for (int i = 0; i < keys.length; i++) keyMap.put(keys[i], values[i]);
                saveConfig();
            }
        } catch (Exception e) { e.printStackTrace(); }
    }

    /**
     * 写曲谱 JSON 到文件 (SkyStudio 格式)
     */
    private boolean writeSongToFile(List<MusicNote> notes, File target, String name,
                                    String author, String transcribedBy, int bpm, int subdiv) {
        JSONObject song = new JSONObject();
        song.put("name", name);
        song.put("author", author == null ? "" : author);
        song.put("transcribedBy", transcribedBy == null || transcribedBy.isEmpty() ? "SkyMusicPlayer" : transcribedBy);
        song.put("isComposed", true);
        song.put("bpm", bpm);
        song.put("subdiv", subdiv);
        song.put("bitsPerPage", 16);
        song.put("pitchLevel", 0);
        JSONArray arr = new JSONArray();
        for (MusicNote n : notes) {
            JSONObject o = new JSONObject();
            o.put("time", n.getAbsoluteTime());
            o.put("key", n.getKey());
            arr.put(o);
        }
        song.put("songNotes", arr);
        JSONArray top = new JSONArray();
        top.put(song);
        try {
            File parent = target.getParentFile();
            if (parent != null && !parent.exists()) parent.mkdirs();
            Files.writeString(target.toPath(), top.toString());
            return true;
        } catch (IOException e) {
            updateStatus("状态: 保存失败 " + e.getMessage());
            return false;
        }
    }

    /**
     * 弹三栏对话框: 曲名 / 歌手 / 创谱人.
     * 返回 String[3] = {name, artist, transcriber}, 取消返回 null.
     */
    private String[] showSongMetadataDialog(String title, String defaultName,
                                            String defaultArtist, String defaultTranscriber) {
        Dialog<String[]> dlg = new Dialog<>();
        dlg.setTitle(title);
        dlg.setHeaderText(null);

        TextField nameField = new TextField(defaultName);
        TextField artistField = new TextField(defaultArtist == null ? "" : defaultArtist);
        TextField transcriberField = new TextField(defaultTranscriber == null ? "" : defaultTranscriber);
        nameField.setPrefColumnCount(28);
        artistField.setPrefColumnCount(28);
        transcriberField.setPrefColumnCount(28);

        javafx.scene.layout.GridPane grid = new javafx.scene.layout.GridPane();
        grid.setHgap(10);
        grid.setVgap(10);
        grid.setPadding(new Insets(16, 18, 8, 18));
        grid.add(new Label("曲名 *:"), 0, 0); grid.add(nameField, 1, 0);
        grid.add(new Label("歌手:"), 0, 1); grid.add(artistField, 1, 1);
        grid.add(new Label("创谱人:"), 0, 2); grid.add(transcriberField, 1, 2);
        dlg.getDialogPane().setContent(grid);

        ButtonType ok = new ButtonType("保存", ButtonBar.ButtonData.OK_DONE);
        dlg.getDialogPane().getButtonTypes().addAll(ok, ButtonType.CANCEL);

        // 曲名空时禁用 OK
        Node okBtn = dlg.getDialogPane().lookupButton(ok);
        okBtn.setDisable(defaultName == null || defaultName.trim().isEmpty());
        nameField.textProperty().addListener((obs, old, val) ->
                okBtn.setDisable(val == null || val.trim().isEmpty()));

        Platform.runLater(nameField::requestFocus);

        dlg.setResultConverter(b -> b == ok ? new String[]{
                nameField.getText().trim(),
                artistField.getText().trim(),
                transcriberField.getText().trim()
        } : null);

        Optional<String[]> r = dlg.showAndWait();
        return r.orElse(null);
    }

    private File newSongFile(String songName) {
        File folder = new File(SONGS_DIR);
        if (!folder.exists()) folder.mkdir();
        String safe = songName.replaceAll("[\\\\/:*?\"<>|]", "_");
        return new File(folder, safe + ".json");
    }

    // ============ 编辑器窗口 ============

    @FXML
    protected void onEditClick() {
        if (playlist.isEmpty()) {
            updateStatus("状态: 请先选择曲目再编辑");
            return;
        }
        if (isPlaying) {
            updateStatus("状态: 演奏中, 无法编辑");
            return;
        }
        openEditorWindow(playlist, currentSongName, currentFile);
    }

    @FXML
    protected void onCreateClick() {
        if (isPlaying) {
            updateStatus("状态: 演奏中, 无法创建");
            return;
        }
        // 新建: 元数据空, BPM/subdiv 用全局默认
        currentArtist = "";
        currentTranscriber = "";
        currentSongBpm = defaultBpm;
        currentSongSubdiv = defaultSubdiv;
        openEditorWindow(Collections.emptyList(), "新歌曲_" + System.currentTimeMillis(), null);
    }

    private void openEditorWindow(List<MusicNote> sourceNotes, String songName, File sourceFile) {
        try {
            buildEditorWindow(sourceNotes, songName, sourceFile);
        } catch (Throwable t) {
            t.printStackTrace();
            updateStatus("状态: 编辑器打开失败 " + t.getClass().getSimpleName() + ": " + t.getMessage());
        }
    }

    private void buildEditorWindow(List<MusicNote> sourceNotes, String songName, File sourceFile) {
        boolean isNew = (sourceFile == null);
        Stage stage = new Stage();
        stage.setTitle((isNew ? "➕ 新建歌曲: " : "🎼 钢琴卷帘编辑器: ") + songName);

        // 工作副本 (确保不影响主窗口 playlist)
        ObservableList<MusicNote> notes = FXCollections.observableArrayList();
        for (MusicNote n : sourceNotes) notes.add(new MusicNote(n.getKey(), n.getAbsoluteTime()));

        // 底部按键映射按钮数组 (后面创建 mapPanel 时填充)
        final Button[] mapButtons = new Button[15];
        final String MAP_DEFAULT_STYLE = "-fx-background-color: #2c2c2c; -fx-text-fill: #e0e0e0; "
                + "-fx-font-size: 10px; -fx-background-radius: 4; "
                + "-fx-border-color: #1a1a1a; -fx-border-radius: 4; -fx-border-width: 1;";
        final String MAP_FLASH_STYLE = "-fx-background-color: #FFEB3B; -fx-text-fill: #333; "
                + "-fx-font-size: 10px; -fx-font-weight: bold; -fx-background-radius: 4; "
                + "-fx-border-color: #FF9800; -fx-border-radius: 4; -fx-border-width: 2;";
        java.util.function.IntConsumer flashMapKey = idx -> {
            if (idx < 0 || idx >= mapButtons.length || mapButtons[idx] == null) return;
            Button b = mapButtons[idx];
            Platform.runLater(() -> {
                b.setStyle(MAP_FLASH_STYLE);
                PauseTransition p = new PauseTransition(Duration.millis(130));
                p.setOnFinished(ev -> b.setStyle(MAP_DEFAULT_STYLE));
                p.play();
            });
        };

        // 撤销/重做栈 (限 50 步)
        final int MAX_UNDO = 50;
        final java.util.Deque<List<MusicNote>> undoStack = new java.util.ArrayDeque<>();
        final java.util.Deque<List<MusicNote>> redoStack = new java.util.ArrayDeque<>();
        java.util.function.Supplier<List<MusicNote>> snapshot = () -> {
            List<MusicNote> s = new ArrayList<>(notes.size());
            for (MusicNote n : notes) s.add(new MusicNote(n.getKey(), n.getAbsoluteTime()));
            return s;
        };
        Runnable pushUndo = () -> {
            undoStack.push(snapshot.get());
            while (undoStack.size() > MAX_UNDO) undoStack.pollLast();
            redoStack.clear();
        };

        final int KEYS = 15;
        final double ROW_H = 24;       // 每键行高
        final double RULER_H = 24;     // 顶部时间标尺
        final double KEY_W = 92;       // 左侧键盘宽度
        final double GRID_H = ROW_H * KEYS;
        final double TOTAL_H = RULER_H + GRID_H;
        final double TILE_W = 4000.0;  // 单 Canvas 宽度上限, 远低于 GPU 8192 限制

        // FL 风格节拍网格: cellMs = 60000/bpm/subdiv (BPM=拍/分, subdiv=每拍细分数)
        // 编辑现有曲目用其元数据 BPM, 新建用全局默认
        final int[] bpm = {currentSongBpm};
        final int[] subdiv = {currentSongSubdiv};
        // 元数据 (歌手/创谱人) 跟随编辑会话, 保存对话框可改
        final String[] meta = { currentArtist == null ? "" : currentArtist,
                                currentTranscriber == null ? "" : currentTranscriber };
        final int BEATS_PER_BAR = 4;   // 固定 4/4
        java.util.function.LongSupplier cellMsSup = () -> Math.max(5L, Math.round(60000.0 / bpm[0] / subdiv[0]));
        java.util.function.LongSupplier beatMsSup = () -> Math.max(20L, Math.round(60000.0 / bpm[0]));
        java.util.function.LongSupplier barMsSup = () -> beatMsSup.getAsLong() * BEATS_PER_BAR;
        final int MAX_TILES = 60;      // 防止极端放大爆 Node 数

        final long[] playheadRef = {0L};
        final boolean[] isEditorPlaying = {false};

        // 初始缩放: 默认 0.12 px/ms (1s ≈ 120px); 长歌仍按 0.12 起步, 多 tile 拼接绕过单纹理上限
        long initMax = 0;
        for (MusicNote n : notes) if (n.getAbsoluteTime() > initMax) initMax = n.getAbsoluteTime();
        long initialLen = Math.max(initMax + 5000, 8000L);
        final double basePxPerMs = 0.12;
        final double[] pxPerMs = {basePxPerMs}; // 由 zoom 滑块改

        java.util.function.Supplier<Long> songLen = () -> {
            long max = 0;
            for (MusicNote n : notes) if (n.getAbsoluteTime() > max) max = n.getAbsoluteTime();
            return Math.max(max + 5000, initialLen);
        };

        // Pane 容纳多个 tile Canvas, 横向拼接成超长时间轴
        Pane gridPane = new Pane();
        gridPane.setStyle("-fx-background-color: #1e1e1e;");
        java.util.List<Canvas> tiles = new ArrayList<>();

        Runnable redraw = () -> {
            long len = songLen.get();
            double pxMs = pxPerMs[0];
            double totalW = Math.max(800, len * pxMs);
            int needTiles = Math.min(MAX_TILES, Math.max(1, (int) Math.ceil(totalW / TILE_W)));
            // 调整 tile 数
            while (tiles.size() > needTiles) {
                Canvas removed = tiles.remove(tiles.size() - 1);
                gridPane.getChildren().remove(removed);
            }
            while (tiles.size() < needTiles) {
                Canvas c = new Canvas(TILE_W, TOTAL_H);
                c.setLayoutX(tiles.size() * TILE_W);
                gridPane.getChildren().add(c);
                tiles.add(c);
            }
            for (int i = 0; i < tiles.size(); i++) {
                double w = Math.min(TILE_W, totalW - i * TILE_W);
                tiles.get(i).setWidth(Math.max(1, w));
            }
            gridPane.setPrefSize(totalW, TOTAL_H);
            gridPane.setMinSize(totalW, TOTAL_H);
            gridPane.setMaxSize(totalW, TOTAL_H);

            // 节拍尺寸 (snapshot 一次, 整次 redraw 用同一组值)
            long cellMs = cellMsSup.getAsLong();
            long beatMs = beatMsSup.getAsLong();
            long barMs = barMsSup.getAsLong();
            // 音符方块宽度严格 = 1 cell × pxMs → 与节拍网格 1:1 对齐
            double noteWidth = cellMs * pxMs;

            for (int i = 0; i < tiles.size(); i++) {
                Canvas tile = tiles.get(i);
                double tileX0 = i * TILE_W;
                double w = tile.getWidth();
                GraphicsContext g = tile.getGraphicsContext2D();
                // 整体背景
                g.setFill(Color.web("#181818"));
                g.fillRect(0, 0, w, TOTAL_H);
                // 交替行: 5 键一组用更深背景突出分组 (K0-4 / K5-9 / K10-14)
                for (int row = 0; row < KEYS; row++) {
                    double y = RULER_H + row * ROW_H;
                    int keyIdx = (KEYS - 1) - row;
                    int group = keyIdx / 5;
                    String bg = (group == 1) ? "#262626" : "#2e2e2e";
                    g.setFill(Color.web(bg));
                    g.fillRect(0, y, w, ROW_H);
                }
                // 仅遍历此 tile 覆盖的源时间范围
                long srcStart = Math.max(0, (long) (tileX0 / pxMs));
                long srcEnd = Math.min(len + cellMs, (long) ((tileX0 + w) / pxMs) + cellMs);
                long firstTick = (srcStart / cellMs) * cellMs;
                // 网格线: cell (副线) / beat (中线) / bar (主线) — 单 cell 像素 < 4 时跳过 cell 线避免糊掉
                boolean drawCells = (cellMs * pxMs) >= 4.0;
                g.setLineWidth(1);
                for (long t = firstTick; t <= srcEnd; t += cellMs) {
                    double localX = t * pxMs - tileX0;
                    if (localX < -2 || localX > w + 2) continue;
                    if (t % barMs == 0) { g.setStroke(Color.web("#6a6a6a")); g.setLineWidth(1.4); }
                    else if (t % beatMs == 0) { g.setStroke(Color.web("#505050")); g.setLineWidth(1); }
                    else if (drawCells) { g.setStroke(Color.web("#3c3c3c")); g.setLineWidth(1); }
                    else continue;
                    g.strokeLine(localX, RULER_H, localX, TOTAL_H);
                }
                g.setLineWidth(1);
                // 行分割线 + 5键分组的强分割
                for (int row = 0; row <= KEYS; row++) {
                    double y = RULER_H + row * ROW_H;
                    int keyIdx = (KEYS - 1) - row;
                    boolean isGroupBoundary = (row == 0 || row == KEYS || keyIdx == 4 || keyIdx == 9);
                    g.setStroke(Color.web(isGroupBoundary ? "#000" : "#1a1a1a"));
                    g.strokeLine(0, y, w, y);
                }
                // 标尺: bar 编号
                g.setFill(Color.web("#171717"));
                g.fillRect(0, 0, w, RULER_H);
                g.setFont(javafx.scene.text.Font.font(10));
                long firstBar = (srcStart / barMs) * barMs;
                for (long t = firstBar; t <= srcEnd; t += barMs) {
                    double localX = t * pxMs - tileX0;
                    if (localX < -20 || localX > w + 20) continue;
                    g.setStroke(Color.web("#3a3a3a"));
                    g.strokeLine(localX, RULER_H - 6, localX, RULER_H);
                    g.setFill(Color.web("#aaa"));
                    int barNum = (int) (t / barMs) + 1;
                    g.fillText(String.valueOf(barNum), localX + 3, RULER_H - 8);
                }
                g.setStroke(Color.web("#000"));
                g.strokeLine(0, RULER_H, w, RULER_H);
                // 音符: 左边对齐事件时刻 (FL 风格), 落在该时刻所在的 100ms 网格起点
                for (MusicNote n : notes) {
                    int idx = parseKeyIndex(n.getKey());
                    if (idx < 0) continue;
                    double globalLeftX = n.getAbsoluteTime() * pxMs;
                    if (globalLeftX < tileX0 - noteWidth || globalLeftX > tileX0 + w + noteWidth) continue;
                    double localLeftX = globalLeftX - tileX0;
                    int row = (KEYS - 1) - idx;
                    double y = RULER_H + row * ROW_H;
                    Color fill = Color.hsb(200 - (idx / 14.0) * 200, 0.65, 0.95);
                    g.setFill(fill);
                    g.fillRect(localLeftX, y + 2, noteWidth, ROW_H - 4);
                    g.setStroke(Color.web("#ffffff", 0.4));
                    g.strokeRect(localLeftX + 0.5, y + 2.5, noteWidth - 1, ROW_H - 5);
                }
                // 播放头
                double globalPhx = playheadRef[0] * pxMs;
                double localPhx = globalPhx - tileX0;
                if (localPhx >= -2 && localPhx <= w + 2) {
                    g.setStroke(Color.web("#FF4444"));
                    g.setLineWidth(2);
                    g.strokeLine(localPhx, 0, localPhx, TOTAL_H);
                }
            }
        };

        // 鼠标点击 (Pane 接收, e.getX() 是 Pane 相对): ruler → 移动播放头; 行内 → 加/删音符
        gridPane.setOnMousePressed(e -> {
            if (isEditorPlaying[0]) return;
            double x = e.getX();
            double y = e.getY();
            long cellMs = cellMsSup.getAsLong();
            long t = Math.max(0L, Math.min((long) (x / pxPerMs[0]), songLen.get()));
            long snapT = (t / cellMs) * cellMs;
            if (y < RULER_H) {
                playheadRef[0] = snapT;
                redraw.run();
                return;
            }
            int row = (int) ((y - RULER_H) / ROW_H);
            if (row < 0 || row >= KEYS) return;
            int keyIdx = (KEYS - 1) - row;
            String keyName = "1Key" + keyIdx;
            MusicNote toRemove = null;
            for (MusicNote n : notes) {
                if (n.getKey().equals(keyName) && Math.abs(n.getAbsoluteTime() - snapT) < cellMs) {
                    toRemove = n; break;
                }
            }
            if (toRemove != null) {
                pushUndo.run();
                notes.remove(toRemove);
            } else {
                pushUndo.run();
                notes.add(new MusicNote(keyName, snapT));
                ToneGenerator.init();
                ToneGenerator.play(keyIdx);
                flashMapKey.accept(keyIdx);
            }
            redraw.run();
        });

        // 左侧键盘: 顶 = K14 (高音), 底 = K0 (低音)
        VBox keyLane = new VBox();
        Region rulerSpacer = new Region();
        rulerSpacer.setPrefHeight(RULER_H);
        rulerSpacer.setMinHeight(RULER_H);
        rulerSpacer.setMaxHeight(RULER_H);
        rulerSpacer.setStyle("-fx-background-color: #171717;");
        keyLane.getChildren().add(rulerSpacer);
        for (int row = 0; row < KEYS; row++) {
            int keyIdx = (KEYS - 1) - row;
            Button btn = new Button();
            btn.setPrefSize(KEY_W, ROW_H);
            btn.setMinSize(KEY_W, ROW_H);
            btn.setMaxSize(KEY_W, ROW_H);
            Integer code = keyMap.get("1Key" + keyIdx);
            String label = code != null ? prettyKeyName(code) : "?";
            btn.setText("K" + keyIdx + "  " + label);
            String bg = (keyIdx % 5 == 0) ? "#3a3a3a" : "#2c2c2c"; // 每行5键分组淡背景
            btn.setStyle("-fx-background-color: " + bg + "; -fx-text-fill: #e0e0e0; -fx-font-size: 11px; "
                    + "-fx-background-radius: 0; -fx-border-color: #1a1a1a; -fx-border-width: 0 1 1 0;"
                    + "-fx-padding: 0 8 0 8; -fx-alignment: center-left;");
            final int idx = keyIdx;
            btn.setOnAction(e -> {
                ToneGenerator.init();
                ToneGenerator.play(idx);
                flashMapKey.accept(idx);
            });
            keyLane.getChildren().add(btn);
        }
        keyLane.setStyle("-fx-background-color: #171717;");
        keyLane.setMinWidth(KEY_W);
        keyLane.setMaxWidth(KEY_W);

        // ScrollPane 包 gridPane (内含多个 tile Canvas)
        ScrollPane gridScroll = new ScrollPane(gridPane);
        gridScroll.setPannable(true);
        gridScroll.setHbarPolicy(ScrollPane.ScrollBarPolicy.ALWAYS);
        gridScroll.setVbarPolicy(ScrollPane.ScrollBarPolicy.NEVER);
        gridScroll.setStyle("-fx-background: #1e1e1e; -fx-background-color: #1e1e1e;");
        gridScroll.setPrefViewportWidth(900);
        gridScroll.setMinWidth(200);
        gridScroll.setMaxWidth(Double.MAX_VALUE);
        gridScroll.setPrefViewportHeight(TOTAL_H);
        gridScroll.setMinHeight(TOTAL_H + 18);
        gridScroll.setMaxHeight(TOTAL_H + 18);

        HBox body = new HBox(keyLane, gridScroll);
        HBox.setHgrow(gridScroll, Priority.ALWAYS);
        body.setStyle("-fx-background-color: #171717;");

        // 顶部工具栏
        Button playBtn = new Button("▶");
        playBtn.setPrefSize(46, 36);
        playBtn.setStyle("-fx-font-size: 16px; -fx-font-weight: bold; -fx-background-color: #4CAF50; -fx-text-fill: white; -fx-background-radius: 4;");
        Button stopBtn = new Button("⏹");
        stopBtn.setPrefSize(36, 36);
        Button rewBtn = new Button("⏪");
        rewBtn.setPrefSize(36, 36);

        playBtn.setOnAction(e -> {
            if (isEditorPlaying[0]) {
                isEditorPlaying[0] = false;
                playBtn.setText("▶");
                return;
            }
            isEditorPlaying[0] = true;
            playBtn.setText("⏸");
            ToneGenerator.init();
            long startPlayhead = playheadRef[0];
            long startWall = System.currentTimeMillis();
            long maxTime = songLen.get();
            new Thread(() -> {
                // -1 起跳避免漏掉时间正好等于 startPlayhead 的音符 (常见: 第 0ms 第一个音)
                long lastPh = startPlayhead - 1;
                while (isEditorPlaying[0]) {
                    long now = startPlayhead + (System.currentTimeMillis() - startWall);
                    if (now > maxTime) break;
                    for (MusicNote n : notes) {
                        if (n.getAbsoluteTime() > lastPh && n.getAbsoluteTime() <= now) {
                            int idx = parseKeyIndex(n.getKey());
                            if (idx >= 0) {
                                ToneGenerator.play(idx);
                                flashMapKey.accept(idx);
                            }
                        }
                    }
                    lastPh = now;
                    playheadRef[0] = now;
                    Platform.runLater(redraw);
                    try { Thread.sleep(30); } catch (InterruptedException ie) { break; }
                }
                isEditorPlaying[0] = false;
                Platform.runLater(() -> playBtn.setText("▶"));
            }).start();
        });

        stopBtn.setOnAction(e -> {
            isEditorPlaying[0] = false;
            ToneGenerator.stopAll();
            playBtn.setText("▶");
        });
        rewBtn.setOnAction(e -> {
            isEditorPlaying[0] = false;
            playBtn.setText("▶");
            playheadRef[0] = 0;
            redraw.run();
        });

        // 元数据
        Label metaName = new Label(songName);
        metaName.setStyle("-fx-font-size: 14px; -fx-font-weight: bold; -fx-text-fill: #e8e8e8;");
        Label metaInfo = new Label();
        metaInfo.setStyle("-fx-text-fill: #aaa; -fx-font-size: 11px;");
        Runnable updateMeta = () -> metaInfo.setText(
                "音符 " + notes.size() + " · " + String.format("%.1fs", songLen.get() / 1000.0));
        updateMeta.run();
        notes.addListener((javafx.collections.ListChangeListener<MusicNote>) c -> updateMeta.run());

        VBox metaPanel = new VBox(2, metaName, metaInfo);
        metaPanel.setAlignment(Pos.CENTER_LEFT);

        Button saveAsBtn = new Button("💾 另存为");
        saveAsBtn.setOnAction(e -> {
            String defaultName = isNew ? songName : songName + "_edited";
            String[] result = showSongMetadataDialog(
                    isNew ? "保存新歌曲" : "另存为",
                    defaultName, meta[0], meta[1]);
            if (result == null) return;
            String name = result[0];
            String artist = result[1];
            String transcriber = result[2];
            isEditorPlaying[0] = false;
            ToneGenerator.stopAll();
            File target = newSongFile(name);
            notes.sort((a, b) -> Long.compare(a.getAbsoluteTime(), b.getAbsoluteTime()));
            if (writeSongToFile(notes, target, name, artist, transcriber, bpm[0], subdiv[0])) {
                meta[0] = artist; meta[1] = transcriber;
                refreshLibrary();
                stage.close();
            }
        });

        Button saveBtn = new Button(isNew ? "💾 保存为..." : "💾 保存");
        saveBtn.setOnAction(e -> {
            if (sourceFile == null) {
                // 新建歌曲 → 走另存为
                saveAsBtn.fire();
                return;
            }
            isEditorPlaying[0] = false;
            ToneGenerator.stopAll();
            notes.sort((a, b) -> Long.compare(a.getAbsoluteTime(), b.getAbsoluteTime()));
            if (writeSongToFile(notes, sourceFile, songName, meta[0], meta[1], bpm[0], subdiv[0])) {
                refreshLibrary();
                parseJsonMusic(sourceFile);
                updateStatus("状态: 已保存 " + notes.size() + " 音符");
                stage.close();
            }
        });

        // 音色切换
        ComboBox<String> instrumentCombo = new ComboBox<>();
        instrumentCombo.getItems().addAll(ToneGenerator.INSTRUMENTS);
        instrumentCombo.setValue(ToneGenerator.getInstrument());
        instrumentCombo.setPrefWidth(130);
        instrumentCombo.valueProperty().addListener((obs, old, val) -> {
            if (val == null) return;
            isEditorPlaying[0] = false;
            ToneGenerator.stopAll();
            ToneGenerator.setInstrument(val);
            saveSettings();
        });
        Label instLabel = new Label("音色:");
        instLabel.setStyle("-fx-text-fill: #aaa; -fx-font-size: 11px;");

        // 横向缩放: 0.5x – 8x (tile 拼接, 不再受单 Canvas 上限限制)
        Slider zoomSlider = new Slider(0.5, 8.0, 1.0);
        zoomSlider.setPrefWidth(140);
        zoomSlider.setShowTickMarks(false);
        Label zoomLabel = new Label("1.0x");
        zoomLabel.setStyle("-fx-text-fill: #aaa; -fx-font-size: 11px; -fx-min-width: 40;");
        zoomSlider.valueProperty().addListener((obs, old, val) -> {
            pxPerMs[0] = basePxPerMs * val.doubleValue();
            zoomLabel.setText(String.format("%.1fx", val.doubleValue()));
            redraw.run();
        });
        Label zoomIcon = new Label("🔍");
        zoomIcon.setStyle("-fx-text-fill: #aaa; -fx-font-size: 11px;");

        // BPM Spinner + 细分 ComboBox (FL 风格节拍网格)
        Spinner<Integer> bpmSpinner = new Spinner<>(30, 400, bpm[0]);
        bpmSpinner.setEditable(true);
        bpmSpinner.setPrefWidth(70);
        bpmSpinner.valueProperty().addListener((obs, old, val) -> {
            if (val == null) return;
            bpm[0] = val;
            defaultBpm = val;
            saveSettings();
            redraw.run();
        });
        Label bpmLabel = new Label("BPM:");
        bpmLabel.setStyle("-fx-text-fill: #aaa; -fx-font-size: 11px;");

        ComboBox<Integer> subdivCombo = new ComboBox<>();
        subdivCombo.getItems().addAll(1, 2, 3, 4, 6, 8, 12, 16);
        subdivCombo.setValue(subdiv[0]);
        subdivCombo.setPrefWidth(60);
        subdivCombo.valueProperty().addListener((obs, old, val) -> {
            if (val == null) return;
            subdiv[0] = val;
            defaultSubdiv = val;
            saveSettings();
            redraw.run();
        });
        Label subdivLabel = new Label("细分:");
        subdivLabel.setStyle("-fx-text-fill: #aaa; -fx-font-size: 11px;");

        Region toolbarSpacer = new Region();
        HBox.setHgrow(toolbarSpacer, Priority.ALWAYS);
        HBox toolbar = new HBox(8, rewBtn, playBtn, stopBtn,
                new Separator(Orientation.VERTICAL),
                metaPanel,
                new Separator(Orientation.VERTICAL),
                instLabel, instrumentCombo,
                new Separator(Orientation.VERTICAL),
                bpmLabel, bpmSpinner, subdivLabel, subdivCombo,
                new Separator(Orientation.VERTICAL),
                zoomIcon, zoomSlider, zoomLabel,
                toolbarSpacer, saveBtn);
        // 新建时 saveBtn 已经走 saveAsBtn 的逻辑, 不重复加
        if (!isNew) toolbar.getChildren().add(saveAsBtn);
        toolbar.setAlignment(Pos.CENTER_LEFT);
        toolbar.setPadding(new Insets(8, 12, 8, 12));
        toolbar.setStyle("-fx-background-color: #2d2d2d;");

        Label hint = new Label("点击网格 → 加/删音符 (按节拍吸附)   |   点击标尺 → 移动播放头   |   点左侧键名 → 试听   |   🔍 横向缩放   |   ♩ BPM/细分 调节拍   |   Ctrl+Z 撤销 / Ctrl+Shift+Z 重做 / Ctrl+S 保存");
        hint.setStyle("-fx-text-fill: #888; -fx-font-size: 11px;");
        hint.setPadding(new Insets(6, 12, 6, 12));

        // 底部光遇 3×5 按键映射 (与主窗口 pianoGrid 对齐: K0-K4 顶 / K5-K9 中 / K10-K14 底)
        GridPane mapGrid = new GridPane();
        mapGrid.setHgap(4);
        mapGrid.setVgap(4);
        mapGrid.setAlignment(Pos.CENTER);
        for (int idx = 0; idx < 15; idx++) {
            int row = idx / 5;
            int col = idx % 5;
            Integer code = keyMap.get("1Key" + idx);
            String label = code != null ? prettyKeyName(code) : "?";
            Button mapBtn = new Button("K" + idx + "\n" + label);
            mapBtn.setPrefSize(56, 40);
            mapBtn.setStyle(MAP_DEFAULT_STYLE);
            final int keyIdx = idx;
            mapBtn.setOnAction(e -> {
                ToneGenerator.init();
                ToneGenerator.play(keyIdx);
                flashMapKey.accept(keyIdx);
            });
            mapButtons[idx] = mapBtn;
            mapGrid.add(mapBtn, col, row);
        }
        Label mapTitle = new Label("🎮 光遇按键映射 (点击试听 / 播放时同步亮起)");
        mapTitle.setStyle("-fx-text-fill: #aaa; -fx-font-size: 11px;");
        VBox mapPanel = new VBox(4, mapTitle, mapGrid);
        mapPanel.setAlignment(Pos.CENTER);
        mapPanel.setPadding(new Insets(6, 12, 8, 12));
        mapPanel.setStyle("-fx-background-color: #1f1f1f; -fx-border-color: #333; -fx-border-width: 1 0 0 0;");

        VBox root = new VBox(toolbar, body, hint, mapPanel);
        root.setStyle("-fx-background-color: #171717;");
        VBox.setVgrow(body, Priority.ALWAYS);

        // 撤销/重做 (定义在 redraw 之后才能 capture)
        Runnable doUndo = () -> {
            if (undoStack.isEmpty()) return;
            redoStack.push(snapshot.get());
            notes.setAll(undoStack.pop());
            redraw.run();
        };
        Runnable doRedo = () -> {
            if (redoStack.isEmpty()) return;
            undoStack.push(snapshot.get());
            notes.setAll(redoStack.pop());
            redraw.run();
        };

        redraw.run();

        Scene scene = new Scene(root, 1200, 700);
        scene.addEventFilter(KeyEvent.KEY_PRESSED, ke -> {
            if (!ke.isControlDown()) return;
            KeyCode k = ke.getCode();
            if (k == KeyCode.Z) {
                if (ke.isShiftDown()) doRedo.run(); else doUndo.run();
                ke.consume();
            } else if (k == KeyCode.Y) {
                doRedo.run();
                ke.consume();
            } else if (k == KeyCode.S) {
                saveBtn.fire();
                ke.consume();
            }
        });
        stage.setScene(scene);
        stage.setMinWidth(900);
        stage.setMinHeight(640);
        stage.setOnCloseRequest(e -> { isEditorPlaying[0] = false; ToneGenerator.stopAll(); });
        stage.setOnShown(e -> Platform.runLater(redraw));
        stage.show();

        Platform.runLater(redraw);
    }

    @FXML
    protected void onImportClick() {
        FileChooser chooser = new FileChooser();
        chooser.setTitle("选择要导入的曲谱文件 (可多选)");
        chooser.getExtensionFilters().addAll(
                new FileChooser.ExtensionFilter("曲谱文件", "*.json", "*.txt"),
                new FileChooser.ExtensionFilter("所有文件", "*.*")
        );
        Window window = songListView.getScene().getWindow();
        List<File> files = chooser.showOpenMultipleDialog(window);
        if (files == null || files.isEmpty()) return;

        File target = new File(SONGS_DIR);
        if (!target.exists()) target.mkdir();

        int imported = 0, failed = 0;
        for (File src : files) {
            try {
                Files.copy(src.toPath(), new File(target, src.getName()).toPath(),
                        StandardCopyOption.REPLACE_EXISTING);
                imported++;
            } catch (IOException e) {
                failed++;
            }
        }
        refreshLibrary();
        updateStatus(failed == 0
                ? "状态: 已导入 " + imported + " 个曲谱"
                : "状态: 导入 " + imported + " 个，失败 " + failed + " 个");
    }

    @FXML
    protected void refreshLibrary() {
        allSongNames.clear();
        allSongFiles.clear();
        File folder = new File(SONGS_DIR);
        if (!folder.exists()) folder.mkdir();
        File[] files = folder.listFiles((dir, name) -> name.endsWith(".json") || name.endsWith(".txt"));
        if (files != null) {
            for (File f : files) {
                String name = validateAndGetName(f);
                if (name != null) {
                    allSongFiles.add(f);
                    allSongNames.add(name);
                }
            }
        }
        applyFilter();
    }

    private String validateAndGetName(File file) {
        try {
            String content = readSongFile(file).trim();
            JSONObject obj = content.startsWith("{") ? new JSONObject(content) : new JSONArray(content).getJSONObject(0);
            return obj.optString("name", file.getName());
        } catch (Exception e) { return null; }
    }

    /**
     * 兼容多种编码读取曲谱文件。
     * Sky 曲谱来源不一，常见 UTF-8 / UTF-8 BOM / UTF-16 LE BOM / UTF-16 BE BOM 四种。
     */
    private String readSongFile(File file) throws IOException {
        byte[] bytes = Files.readAllBytes(file.toPath());
        if (bytes.length >= 3
                && (bytes[0] & 0xFF) == 0xEF
                && (bytes[1] & 0xFF) == 0xBB
                && (bytes[2] & 0xFF) == 0xBF) {
            return new String(bytes, 3, bytes.length - 3, StandardCharsets.UTF_8);
        }
        if (bytes.length >= 2
                && (bytes[0] & 0xFF) == 0xFF
                && (bytes[1] & 0xFF) == 0xFE) {
            return new String(bytes, 2, bytes.length - 2, StandardCharsets.UTF_16LE);
        }
        if (bytes.length >= 2
                && (bytes[0] & 0xFF) == 0xFE
                && (bytes[1] & 0xFF) == 0xFF) {
            return new String(bytes, 2, bytes.length - 2, StandardCharsets.UTF_16BE);
        }
        return new String(bytes, StandardCharsets.UTF_8);
    }

    private void parseJsonMusic(File file) {
        try {
            playlist.clear();
            String content = readSongFile(file).trim();
            JSONArray arr = content.startsWith("{") ? new JSONArray().put(new JSONObject(content)) : new JSONArray(content);
            JSONObject songObj = arr.getJSONObject(0);
            JSONArray notes = songObj.getJSONArray("songNotes");
            for (int i = 0; i < notes.length(); i++) {
                JSONObject n = notes.getJSONObject(i);
                playlist.add(new MusicNote(n.getString("key"), n.getLong("time")));
            }
            String songName = songObj.optString("name", "未知");
            int noteCount = notes.length();
            this.currentFile = file;
            this.currentSongName = songName;
            this.currentArtist = songObj.optString("author", "");
            this.currentTranscriber = songObj.optString("transcribedBy", "");
            this.currentSongBpm = songObj.optInt("bpm", 120);
            this.currentSongSubdiv = songObj.optInt("subdiv", 4);
            seekFraction = 0;
            Platform.runLater(() -> {
                filePathField.setText(songName);
                progressSlider.setValue(0);
            });
            updateStatus("状态: 就绪 - " + songName + " (" + noteCount + " 音符)");
        } catch (Exception e) { updateStatus("状态: 解析失败"); }
    }

    /**
     * 试听模式: 用 ToneGenerator 把整首曲子从喇叭播出, 不模拟键盘, 不切游戏窗口.
     * 使用主窗口 speedSlider 调速; 切歌/关 toggle/开始演奏/停止 都会终止线程.
     */
    private void startPreview() {
        stopPreview();
        if (playlist.isEmpty()) return;
        isPreviewing = true;
        final List<MusicNote> snapshot = new ArrayList<>(playlist);
        previewThread = new Thread(() -> {
            try {
                ToneGenerator.init();
                TreeMap<Long, List<MusicNote>> chords = new TreeMap<>();
                for (MusicNote n : snapshot) {
                    chords.computeIfAbsent(n.getAbsoluteTime(), k -> new ArrayList<>()).add(n);
                }
                long lastSrc = 0;
                Platform.runLater(() -> updateStatus("状态: 🎵 试听中 (" + String.format("%.1fx", speedSlider.getValue()) + ")"));
                for (Map.Entry<Long, List<MusicNote>> entry : chords.entrySet()) {
                    if (!isPreviewing) break;
                    long thisSrc = entry.getKey();
                    long remaining = thisSrc - lastSrc;
                    while (remaining > 0 && isPreviewing) {
                        double sp = speedSlider.getValue();
                        long chunk = Math.min(remaining, 50L);
                        Thread.sleep(Math.max(1L, (long) (chunk / sp)));
                        remaining -= chunk;
                    }
                    if (!isPreviewing) break;
                    for (MusicNote note : entry.getValue()) {
                        flashKey(note.getKey());
                        int idx = parseKeyIndex(note.getKey());
                        if (idx >= 0) ToneGenerator.play(idx);
                    }
                    lastSrc = thisSrc;
                }
                if (isPreviewing) Platform.runLater(() -> updateStatus("状态: 试听结束"));
            } catch (InterruptedException ignored) {
            } catch (Exception e) {
                Platform.runLater(() -> updateStatus("状态: 试听出错"));
            } finally {
                isPreviewing = false;
                ToneGenerator.stopAll();
            }
        }, "preview-player");
        previewThread.setDaemon(true);
        previewThread.start();
    }

    private void stopPreview() {
        isPreviewing = false;
        Thread t = previewThread;
        if (t != null) {
            t.interrupt();
            previewThread = null;
        }
        ToneGenerator.stopAll();
    }

    @FXML
    protected void onStartPlayClick() {
        if (playlist.isEmpty() || isPlaying) return;
        stopPreview();
        isPlaying = true;
        isPaused = false;

        new Thread(() -> {
            try {
                // 实时倒计时 (Spinner 可调, F1 可跳过)
                int countdownSecs = countdownSpinner != null ? countdownSpinner.getValue() : 5;
                for (int i = countdownSecs; i >= 1; i--) {
                    if (!isPlaying) return;
                    if (skipCountdown) { skipCountdown = false; break; }
                    updateStatus("状态: 即将开始... " + i + "  (F1 跳过)");
                    Thread.sleep(1000);
                }
                skipCountdown = false;

                // 按时间分组：同一时刻的多个音符=和弦，需同时按下
                TreeMap<Long, List<MusicNote>> chords = new TreeMap<>();
                for (MusicNote note : playlist) {
                    chords.computeIfAbsent(note.getAbsoluteTime(), k -> new ArrayList<>()).add(note);
                }

                final long maxMs = chords.isEmpty() ? 1 : chords.lastKey();
                final long startMs = (long) (maxMs * seekFraction);
                seekFraction = 0; // 消费一次 seek

                Robot robot = new Robot();
                long lastSrcMs = startMs;
                updateStatus("状态: 正在演奏 ▶ (" + String.format("%.1fx", speedSlider.getValue()) + ")  F2 暂停 / F4-F5 速度 / F6 停止");

                for (Map.Entry<Long, List<MusicNote>> entry : chords.entrySet()) {
                    if (!isPlaying) break;
                    long thisSrcMs = entry.getKey();
                    if (thisSrcMs < startMs) continue;  // 跳过 seek 起点之前的和弦

                    long remainingSrc = thisSrcMs - lastSrcMs;
                    // 分块睡眠, 边等边响应 pause/stop/速度变化
                    while (remainingSrc > 0 && isPlaying) {
                        if (isPaused) { Thread.sleep(50); continue; }
                        double curSpeed = speedSlider.getValue();
                        long chunkSrc = Math.min(remainingSrc, 50L);
                        long chunkWall = Math.max(1L, (long) (chunkSrc / curSpeed));
                        Thread.sleep(chunkWall);
                        remainingSrc -= chunkSrc;
                    }
                    if (!isPlaying) break;

                    // 同时按下和弦所有键
                    List<Integer> pressed = new ArrayList<>(entry.getValue().size());
                    for (MusicNote note : entry.getValue()) {
                        flashKey(note.getKey());
                        Integer code = keyMap.get(note.getKey());
                        if (code != null) {
                            robot.keyPress(code);
                            pressed.add(code);
                        }
                    }
                    robot.delay(45);
                    for (Integer code : pressed) {
                        robot.keyRelease(code);
                    }
                    lastSrcMs = thisSrcMs;

                    final double progress = (double) thisSrcMs / maxMs;
                    Platform.runLater(() -> {
                        // 用户拖动时不要程序覆盖
                        if (!progressSlider.isValueChanging()) progressSlider.setValue(progress);
                    });
                }

                if (isPlaying) {
                    updateStatus("状态: 播放结束");
                    Platform.runLater(() -> progressSlider.setValue(1.0));
                }
                isPlaying = false;
            } catch (Exception e) {
                isPlaying = false;
                updateStatus("状态: 播放出错");
            }
        }).start();
    }

    @FXML
    protected void onStopPlayClick() {
        isPlaying = false;
        isPaused = false;
        skipCountdown = false;
        seekFraction = 0;
        stopPreview();
        ToneGenerator.stopAll();
        updateStatus("状态: 已停止");
        Platform.runLater(() -> progressSlider.setValue(0));
    }

    @FXML
    protected void onPauseClick() { hotkeyPause(); }

    @FXML
    protected void onResumeClick() { hotkeyResume(); }

    // ============ 全局热键 F1-F5 ============

    private void hotkeyStartOrSkip() {
        if (isPlaying) {
            // 在倒计时中 → 跳过
            skipCountdown = true;
        } else {
            // 未播放 → 设跳过标志后启动
            skipCountdown = true;
            Platform.runLater(this::onStartPlayClick);
        }
    }

    private void hotkeyPause() {
        if (!isPlaying || isPaused) return;
        isPaused = true;
        updateStatus("状态: ⏸ 已暂停  (F3 继续)");
    }

    private void hotkeyResume() {
        if (!isPlaying || !isPaused) return;
        isPaused = false;
        updateStatus("状态: 正在演奏 ▶ (" + String.format("%.1fx", speedSlider.getValue()) + ")");
    }

    private void hotkeySpeedDown() {
        Platform.runLater(() -> {
            double v = Math.max(speedSlider.getMin(), speedSlider.getValue() - 0.1);
            speedSlider.setValue(v);
            if (isPlaying && !isPaused) {
                updateStatus("状态: 正在演奏 ▶ (" + String.format("%.1fx", v) + ")");
            }
        });
    }

    private void hotkeySpeedUp() {
        Platform.runLater(() -> {
            double v = Math.min(speedSlider.getMax(), speedSlider.getValue() + 0.1);
            speedSlider.setValue(v);
            if (isPlaying && !isPaused) {
                updateStatus("状态: 正在演奏 ▶ (" + String.format("%.1fx", v) + ")");
            }
        });
    }

    private void hotkeyStop() {
        Platform.runLater(this::onStopPlayClick);
    }

    private void registerGlobalHotkeys() {
        // 抑制 jnativehook 默认 INFO 日志
        java.util.logging.Logger nlog = java.util.logging.Logger.getLogger(
                com.github.kwhat.jnativehook.GlobalScreen.class.getPackage().getName());
        nlog.setLevel(java.util.logging.Level.WARNING);
        nlog.setUseParentHandlers(false);
        try {
            com.github.kwhat.jnativehook.GlobalScreen.registerNativeHook();
        } catch (com.github.kwhat.jnativehook.NativeHookException e) {
            updateStatus("状态: 全局热键注册失败 " + e.getMessage());
            return;
        }
        com.github.kwhat.jnativehook.GlobalScreen.addNativeKeyListener(
                new com.github.kwhat.jnativehook.keyboard.NativeKeyListener() {
            @Override
            public void nativeKeyPressed(com.github.kwhat.jnativehook.keyboard.NativeKeyEvent e) {
                switch (e.getKeyCode()) {
                    case com.github.kwhat.jnativehook.keyboard.NativeKeyEvent.VC_F1 -> hotkeyStartOrSkip();
                    case com.github.kwhat.jnativehook.keyboard.NativeKeyEvent.VC_F2 -> hotkeyPause();
                    case com.github.kwhat.jnativehook.keyboard.NativeKeyEvent.VC_F3 -> hotkeyResume();
                    case com.github.kwhat.jnativehook.keyboard.NativeKeyEvent.VC_F4 -> hotkeySpeedDown();
                    case com.github.kwhat.jnativehook.keyboard.NativeKeyEvent.VC_F5 -> hotkeySpeedUp();
                    case com.github.kwhat.jnativehook.keyboard.NativeKeyEvent.VC_F6 -> hotkeyStop();
                }
            }
        });
    }

    /**
     * 把 AWT key code 显示为符号 (",", ".", "/" 等), 中文 locale 下 getKeyText 会返回"逗号""句点"
     */
    private static String prettyKeyName(int code) {
        return switch (code) {
            case java.awt.event.KeyEvent.VK_COMMA -> ",";
            case java.awt.event.KeyEvent.VK_PERIOD -> ".";
            case java.awt.event.KeyEvent.VK_SLASH -> "/";
            case java.awt.event.KeyEvent.VK_SEMICOLON -> ";";
            case java.awt.event.KeyEvent.VK_QUOTE -> "'";
            case java.awt.event.KeyEvent.VK_OPEN_BRACKET -> "[";
            case java.awt.event.KeyEvent.VK_CLOSE_BRACKET -> "]";
            case java.awt.event.KeyEvent.VK_BACK_SLASH -> "\\";
            case java.awt.event.KeyEvent.VK_MINUS -> "-";
            case java.awt.event.KeyEvent.VK_EQUALS -> "=";
            case java.awt.event.KeyEvent.VK_BACK_QUOTE -> "`";
            case java.awt.event.KeyEvent.VK_SPACE -> "Space";
            case java.awt.event.KeyEvent.VK_ENTER -> "↵";
            case java.awt.event.KeyEvent.VK_TAB -> "Tab";
            default -> java.awt.event.KeyEvent.getKeyText(code);
        };
    }

    /**
     * "1KeyN" → N (-1 表示无效)
     */
    private static int parseKeyIndex(String keyName) {
        if (keyName == null || !keyName.startsWith("1Key")) return -1;
        try { return Integer.parseInt(keyName.substring(4)); }
        catch (NumberFormatException e) { return -1; }
    }

    private void updateStatus(String msg) {
        Platform.runLater(() -> statusLabel.setText(msg));
    }
}
