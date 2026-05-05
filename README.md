# Sky Automatic Piano Assistant / 光遇自动弹琴助手

English · [**中文**](README_CN.md)

A JavaFX desktop assistant for the game *Sky: Children of the Light*, modeled after the mobile Sky Studio app. Plays sheet music by simulating keyboard input, previews scores with real instrument samples, and ships with an FL Studio style piano roll editor.

## ✨ Features

### Playback
- **Sheet music playback** — loads standard Sky Music JSON / TXT scores (UTF-8 and UTF-16 LE BOM tolerant)
- **Chord-aware** — notes sharing a timestamp are pressed simultaneously instead of arpeggiated
- **Configurable countdown** — 1–10 s before play starts, so you can switch focus to the game window
- **Speed control** — 0.5x – 2.0x, adjustable mid-playback
- **Seek bar** — drag to start from any position
- **Preview mode** — `🎵 试听` toggle plays the selected song through your speakers using real samples; no key simulation, no window switching

### Real instrument tones
- 10 instrument sets bundled in the repo: Piano / Harp / Flute / Guitar / Ukulele / Winter Piano / Xylophone / Electric Guitar / Bassoon / Orff
- 4-voice polyphony pool per key, so fast repeats don't cut off
- Editor dropdown switches instruments live; selection persisted to `settings.json`

### Piano roll editor (FL Studio style)
- **Beat grid** — BPM (30–400) × subdivisions per beat (1, 2, 3, 4, 6, 8, 12, 16). Three-tier grid lines: cell, beat, bar
- **Note blocks** strictly equal `cellMs × pxMs` so they always line up 1:1 with the grid at any zoom
- **Horizontal zoom** — 0.5x – 8x, multi-Canvas tiling supports very long songs without hitting GPU texture limits
- **Undo / redo** — Ctrl+Z / Ctrl+Shift+Z (or Ctrl+Y), up to 50 steps
- **Quick save** — Ctrl+S
- **Save dialog** — name / artist / transcriber fields persisted into the song JSON
- **Bottom key map** — 3×5 grid of in-game keys; lights up in sync with playback, click any cell to audition
- **Per-song BPM** — opening an existing song restores its own BPM/subdivision instead of falling back to the global default

### Library management
- **Library** — auto-scans `songs/` (tested with 1500+ files)
- **Search** — real-time filter
- **Favorites ★** — persisted to `favorites.json`
- **Tags** — right-click menu adds tags, persisted to `categories.json`

### UI / system
- **Dark theme** — toggle in the top-right corner (🌙 / ☀)
- **Visual keyboard** — main-window 3×5 button grid lights up while playing; click a cell to remap that key
- **Global hotkeys (JNativeHook)** — work even when the game has focus:
  - `F1` — skip countdown / start now
  - `F2` — pause　`F3` — resume
  - `F4` — slow down　`F5` — speed up
  - `F6` — stop
- **Launcher script** — `启动.bat` (Windows) auto-detects `JAVA_HOME`

## 📋 Requirements

- **JDK 21+** (project builds with Java 26 by default; you can drop the target back to 21 in `pom.xml`)
- **JavaFX 21** (pulled in automatically by Maven)
- Windows / macOS / Linux. Global hotkeys depend on JNativeHook; on Linux they require X11.

## 🚀 Quick start

### Option 1: Pre-built Windows binary

Grab `SkyMusicPlayer-vX.Y-win64.zip` from the [Releases page](https://github.com/lingyunalingyun/Sky-Automatic-Piano-Assistant/releases), extract anywhere writable (Desktop, Documents…), and double-click `SkyMusicPlayer.exe`. The bundled JRE means you do not need to install Java.

On first launch, the app unpacks `songs/` next to the `.exe` and creates `key_config.json` / `settings.json` etc. there. The whole folder is portable — move it freely, your config and library follow.

### Option 2: Build from source

```bash
git clone https://github.com/lingyunalingyun/Sky-Automatic-Piano-Assistant.git
cd Sky-Automatic-Piano-Assistant
./mvnw javafx:run        # macOS / Linux
.\mvnw.cmd javafx:run    # Windows
```

Drop `.json` or `.txt` Sky Music score files into `songs/` and they appear in the library on next launch.

> 💡 If *Sky* runs as Administrator, this app must run as Administrator too — otherwise the OS blocks the simulated keystrokes.

## 🎹 Usage

### Main window
1. Pick a song from the library; the status bar shows it as ready
2. (Optional) Adjust countdown / speed
3. Click **▶ 开始演奏 (F1)**, switch to the *Sky* window before the countdown ends, and the assistant takes over your keyboard
4. While playing: `F2/F3` pause/resume, `F4/F5` change speed, `F6` stop

### Editor
- **➕ 创建歌曲** — opens an empty piano roll (asks for name / artist / transcriber on save)
- **✏ 编辑曲谱** — opens the currently selected song
- Click the grid to add/remove notes (snapped to the BPM cell)
- Click the ruler to move the playhead
- Click a left-side key label to audition that note
- Adjust BPM / subdivision / zoom sliders for the grid you want
- 💾 保存 overwrites the source file; 💾 另存为 creates a new file (only shown when editing an existing song)

### Key remapping
- Main window → right-side 🎹 virtual keyboard → click a cell → press any keyboard key → mapping recorded
- Click 💾 保存按键配置 to persist to `key_config.json`

## 📂 Project layout

```
.
├── src/main/
│   ├── java/org/example/skymusicplayer/
│   │   ├── HelloApplication.java     # JavaFX entry point
│   │   ├── HelloController.java      # Main controller (main window + editor)
│   │   ├── Launcher.java             # Launcher wrapper (bypasses JavaFX module path)
│   │   ├── MusicNote.java            # Note model
│   │   └── ToneGenerator.java        # Real-tone sample player
│   └── resources/org/example/skymusicplayer/
│       ├── hello-view.fxml           # Main window layout
│       ├── dark.css                  # Dark theme
│       ├── icon.png                  # Window icon
│       └── instruments/              # 10 instrument sets × 15 keys
├── songs/                            # Score directory (you fill it)
├── pom.xml                           # Maven config
├── build_exe.ps1                     # jpackage build script
├── 启动.bat                          # Windows launcher
└── README.md
```

## 🛠 Tech stack

- **JavaFX 21** — UI
- **JNativeHook 2.2.2** — global keyboard hooks
- **org.json 20240303** — score parsing
- **javax.sound.sampled** — sample playback
- **java.awt.Robot** — keystroke simulation
- **jpackage** — Windows app-image with bundled JRE

## 📜 License & disclaimer

- Source code: **MIT License** (see [LICENSE](LICENSE))
- Score files are user-contributed; copyrights belong to their original creators
- This tool is **for personal practice and entertainment only**. Automated key input may violate the *Sky* End User License Agreement; **use at your own risk**.

## 🙏 Credits

- Inspiration and score format from the mobile [Sky Studio](http://m.lvdoukey.com/) app by lvdoukey
- Global hotkeys by [JNativeHook](https://github.com/kwhat/jnativehook) (kwhat)

---

**Author**: [lingyunalingyun](https://github.com/lingyunalingyun)
