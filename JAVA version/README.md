# SMAP — Sky-MusaAutoPlay / 光遇自动弹琴助手

中文 · [**English**](README_EN.md)

一款 JavaFX 桌面端《光遇》自动弹琴助手，对标手机端 Sky Studio。支持曲谱播放、键盘模拟、真实音色试听、FL Studio 风格的钢琴卷帘编辑器。

## ✨ 特性

### 演奏
- **曲谱播放** — 加载 Sky Music 标准 JSON / TXT 曲谱（兼容 UTF-8 / UTF-16 LE BOM 编码）
- **和弦同步** — 同时刻多音符按和弦同时按下（不再变琶音）
- **可调倒计时** — 1–10 秒，方便切到游戏窗口
- **可调速度** — 0.5x – 2.0x 实时变速
- **进度条** — 拖动跳转，从任意位置开始
- **试听模式** — 🎵 试听 toggle 开启后，选曲目库直接用真实音色播放（不模拟键盘、不切游戏窗口）

### 真实音色
- 集成 10 套乐器音色：Piano / Harp / Flute / Guitar / Ukulele / Winter Piano / Xylophone / Electric Guitar / Bassoon / Orff（已包含在仓库内）
- 每键 4 路复音池，支持快速重复音
- 编辑器下拉切换，持久化到 `settings.json`

### 钢琴卷帘编辑器（FL 风格）
- **节拍网格** — BPM 网格 + 播放倍速 (1.00–999.99)，beat 中线 / bar 主线
- **自动 BPM 检测** — 打开曲谱时自动分析音符时间，调整 BPM 让音符对齐网格
- **音符方块** 严格 = `cellMs × pxMs`，任何缩放下与节拍 1:1 对齐
- **鼠标交互** — 左键放置、右键删除、中键拖动视图、长按拖动音符、标尺点击移动播放头
- **键盘写谱** — 物理键盘映射键（默认 Y/U/I/O/P/H/J/K/L/;/N/M/,/.//）直接 toggle 音符，← → 移动游标（使用前记得切换到英文 / 大写输入）
- **底部琴键面板** — 3×5 按钮 toggle 音符（有则删、无则加），游标经过时对应琴键高亮
- **横向缩放** — 对数刻度，中点 1.0x，最大 32x
- **撤销/重做** — Ctrl+Z / Ctrl+Shift+Z（或 Ctrl+Y），最多 50 步
- **快捷键** — Space 播放/暂停、R 从头播放、Ctrl+S 保存

### MIDI 导入（v1.3+）
- 📁 导入曲谱 支持 `.mid` / `.midi` 文件
- 自动分析音轨，选择要导入的音轨 + 八度偏移
- 自动映射到光遇 15 键 C 大调音阶（C4–C6）

### 曲目管理
- **曲库** — 自动扫描 `songs/` 目录，1500+ 曲谱实测
- **搜索** — 实时过滤
- **收藏 ★** — 持久化到 `favorites.json`
- **分类标签** — 右键菜单加标签，持久化到 `categories.json`

### ☁ 在线曲库（v1.1+）
- 主窗口「☁ 在线曲库」按钮 → 打开独立窗口，从社区站点 [缪斯树屋](https://musetreehouse.com/sheets.php) 拉取共享曲谱
- 支持搜索（曲名/原唱/创谱人/标签）+ 排序（最新/最热/下载量）+ 难度筛选 + 分页
- 选中曲谱查看详情（BPM、音符数、上传者、简介、标签等）
- 一键下载到 `songs/` 目录，主界面自动刷新即可演奏
- 登录后可直接在客户端右键上传曲谱，也可到 [缪斯树屋](https://musetreehouse.com/pages/sheet_upload.php) 网页端上传

### 🔑 账号登录（v1.2+）
- 左栏底部「🔑 登录」按钮，使用 [缪斯树屋](https://musetreehouse.com) 账号登录
- 登录后可右键曲目「☁ 上传到在线曲库」，共享曲谱给社区
- 登录状态自动记忆，下次启动无需重新登录

### 自动检测更新（v1.3+）
- 启动时异步检查 GitHub Releases 最新版本
- 有新版本弹对话框，可一键跳转下载页

### UI / 系统
- **暗色主题** — 右上角 🌙/☀ 切换
- **可视化琴键** — 主窗口 3×5 按钮网格，演奏时高亮，点击重映射
- **全局热键 (JNativeHook)** — 即使焦点在游戏也能控制：
  - `F1` 开始 / 停止
  - `F2` 暂停 / 继续
  - `F3` 加速　`F4` 减速
  - `F5` 后退 10 个音符　`F6` 前进 10 个音符
- **启动脚本** — `启动.bat` 自动检测 JAVA_HOME

## 📋 环境要求

- **JDK 21 或更高**（项目用 Java 26 编译，可在 `pom.xml` 改回 21）
- **JavaFX 21**（Maven 自动拉取）
- Windows / macOS / Linux（全局热键需 JNativeHook 支持，Linux 需 X11）

## 🚀 快速开始

### 1. 克隆仓库

```bash
git clone https://github.com/lingyunalingyun/Sky-Automatic-Piano-Assistant.git
cd Sky-Automatic-Piano-Assistant
```

### 2. 添加曲谱

把 `.json` 或 `.txt` 格式的 Sky Music 曲谱放到 `songs/` 目录（程序启动会自动扫描）。仓库不预置曲谱。

### 3. 启动

**Windows**：双击 `启动.bat`（自动检测 JAVA_HOME，自动 Maven 编译运行）

**手动**：

```bash
./mvnw javafx:run        # macOS / Linux
.\mvnw.cmd javafx:run    # Windows
```

> 💡 如果光遇是以管理员权限运行的，启动脚本/IDE 也需要以管理员身份运行，否则键盘模拟会被系统拦截。

## 🎹 使用说明

### 主窗口流程
1. 选中曲目库里的曲子 → 状态栏显示就绪
2. （可选）调倒计时秒数 / 速度
3. 点 **▶ 开始演奏 (F1)** → 切到游戏窗口 → 倒计时结束自动开弹
4. 中途 `F2/F3` 暂停继续，`F4/F5` 调速，`F6` 停止

### 编辑器流程
- **➕ 创建歌曲** — 打开空白 piano roll
- **✏ 编辑曲谱** — 编辑当前选中曲目
- **鼠标** — 左键放置、右键删除、中键拖动视图、长按拖动、标尺点击移动播放头
- **键盘** — 映射键 toggle 音符、← → 移游标、Space 播放、R 从头播放（使用前切英文/大写）
- **底部琴键** — 点击 toggle 音符，游标位置对应琴键自动高亮
- 改 BPM / 缩放滑块实时调整
- 保存（覆盖原文件）/ 另存为（新文件）

### 键位重映射
- 主窗口右侧 🎹 虚拟琴键 → 点单元格 → 按键盘任意键 → 完成映射
- 💾 保存按键配置 → 持久化到 `key_config.json`

## 📂 项目结构

```
.
├── src/main/
│   ├── java/org/example/smap/
│   │   ├── HelloApplication.java     # JavaFX 入口
│   │   ├── HelloController.java      # 主控制器 (主窗口 + 编辑器)
│   │   ├── Launcher.java             # 启动包装 (绕过 JavaFX 模块路径限制)
│   │   ├── MusicNote.java            # 音符数据
│   │   ├── MidiImporter.java         # MIDI 转 SMAP 曲谱
│   │   ├── ToneGenerator.java        # 真实音色播放
│   │   └── CloudSheetsWindow.java    # 在线曲库窗口 (HTTP 拉取 + 下载)
│   └── resources/org/example/smap/
│       ├── hello-view.fxml           # 主窗口布局
│       ├── dark.css                  # 暗色主题
│       └── instruments/              # 10 套乐器 wav (15 键 × 10 套)
├── songs/                            # 曲谱目录 (需自行添加)
├── pom.xml                           # Maven 配置
├── 启动.bat                          # Windows 启动脚本
└── README.md
```

## 🛠 技术栈

- **JavaFX 21** — UI
- **JNativeHook 2.2.2** — 全局键盘钩子
- **org.json 20240303** — 曲谱解析
- **javax.sound.sampled** — 音色播放
- **java.awt.Robot** — 键盘模拟

## 📜 版权与免责

- 本项目代码采用 **GNU General Public License v3.0**（详见 [LICENSE](LICENSE)）
- 曲谱由用户社区贡献，版权归原作者
- 本工具**仅供个人娱乐学习**，请勿用于商业用途；自动弹琴可能违反《光遇》用户协议，**使用风险自行承担**

## 🙏 致谢

- 灵感与曲谱格式来自手机端 [Sky Studio](http://m.lvdoukey.com/) by lvdoukey
- 全局热键：[JNativeHook](https://github.com/kwhat/jnativehook) by kwhat

---

**Author**: [lingyunalingyun](https://github.com/lingyunalingyun)
