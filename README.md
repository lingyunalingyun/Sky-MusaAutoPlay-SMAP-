<div align="center">

<img src="Assets/logo.png" width="140" alt="SMAP Logo" />

# SMAP · 光遇-Musa 自动演奏

**Sky-MusaAutoPlay** — 光遇（Sky: Children of the Light）自动弹琴助手 · C# WPF 版

[English](README_EN.md) · [旧 JavaFX 版](JAVA%20version/)

</div>

---

## 简介

SMAP 是一款《光·遇》自动弹琴助手：导入或制作曲谱，即可在游戏里自动演奏乐器。本仓库主体为 **C# WPF 重写版（v1.0）**，旧的 JavaFX 版已停止更新，源码见 [`JAVA version/`](JAVA%20version/) 文件夹。

## 功能

- **自动弹琴** — 按曲谱时间线模拟全局按键，在光遇里自动演奏
- **卷帘编辑器** — 键盘写谱、三连音网格、撤销/重做
- **试听** — 通过扬声器预览，不切游戏窗口
- **进度条拖动 + 实时倍速** — 播放中随意跳转、变速
- **导入** — 支持 `.json` / `.txt` / `.mid`（MIDI 自动移调对齐 C 大调）
- **本地曲库** — 搜索 / 收藏 / 排序 / 标签 / 删除
- **在线曲库** — 登录、上传、下载（缪斯树屋）
- **洞穴音效** — 混响，还原光遇洞穴空间感；共 10 种音色
- **物理键盘弹奏** — 主界面按物理键即出声、同步动画
- **深浅主题** + **多语言**（简体中文 / 繁體中文 / English / 日本語）
- **自动检查更新**

## 安装 / 使用

1. 从 [Releases](https://github.com/lingyunalingyun/Sky-MusaAutoPlay-SMAP-/releases) 下载最新版本。
2. **以管理员身份运行**（模拟全局按键所需，程序已做成启动自动请求 UAC）。
3. 进游戏拿出乐器 → 在 SMAP 里选曲 → 点「开始」或在游戏里按 `F1`。

> 曲谱存放在程序目录下的 `songs` 文件夹（首次导入/下载时自动创建）。

## 从源码构建

需要 [.NET 9 SDK](https://dotnet.microsoft.com/download)。

```bash
dotnet build -c Release
```

## 旧 JavaFX 版

已停止更新，源码与说明见 [`JAVA version/`](JAVA%20version/) 文件夹。

## 许可证

见 [LICENSE](LICENSE)。
