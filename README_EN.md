<div align="center">

<img src="Assets/logo.png" width="140" alt="SMAP Logo" />

# SMAP · Sky-Musa Auto Play

An auto-play (auto-piano) helper for **Sky: Children of the Light** — reimagined as a music player, in C# WPF.

[简体中文](README.md) · [Download Releases](https://github.com/lingyunalingyun/Sky-MusaAutoPlay-SMAP-/releases) · [Old JavaFX build](JAVA%20version/)

</div>

---

## Overview

SMAP plays Sky's instruments for you: import or create a sheet, and it performs it in-game.
The latest release is a **full C# WPF rewrite** with a **music-player UI** — an account + collections sidebar, a cover-art library in the middle, the virtual keys on the right, and a full-width player bar at the bottom, plus a play queue, an inline cloud library, and a settings view.

> ⚠️ SMAP must **run as administrator** (required to simulate global key presses; it auto-requests UAC on launch).

## Screenshots

Dark theme:

![Dark theme](docs/screenshots/dark_zh.png)

Light theme / English:

![Light / English](docs/screenshots/light_en.png)

## Features

### Playback
- **Auto-play** — simulates global key presses along the sheet timeline (uses scan codes + a short hold, so per-frame game polling doesn't miss notes).
- **Player bar** — cover / title·artist·transcriber / favorite star / shuffle·prev·play·next·queue / preview mode / cave reverb / instrument / speed.
- **Progress bar** — a full-width thin line pinned to the container's top edge; on hover it thickens and shows a white handle + a time pill that track the play position in real time. Drag to seek, even while paused.
- **Play queue** — a slide-out queue on the right; double-click a library song to enqueue; three modes (repeat-all / repeat-one / shuffle); auto-advance to the next track after a **2-second gap**.
- **Live speed control** + **global hotkeys**: F1 start/stop · F2 pause · F3 slower · F4 faster · F5 back 5s · F6 forward 10s.

### Library & Collections
- **Local library** — rich rows with cover art (title / artist / transcriber / duration); hover reveals add-to-queue / favorite / more; right-click menu with 7 actions (add to queue / add to collection / remove from library / open file location / edit / song info / upload).
- **My Collections (playlists)** — create / rename / delete / **drag to reorder** (with a drop indicator); the ⭐ star means "saved in any collection" (music-player style).
- **Inline cloud library** — browse the MuseTreehouse online library right in the middle pane, with **infinite scroll**, sort (newest / hottest / downloads) + difficulty filter, and one-click download.
- **Import** — `.json` / `.txt` / `.mid` (MIDI is auto-transposed to C major).

### Authoring & Sound
- **Piano-roll editor** — type notes on the keyboard, triplet grid, undo/redo.
- **Cave reverb** — recreates Sky's cave acoustics.
- **Instruments** — 10 of them (Piano / Harp / Guitar / Flute / Ukulele / Winter Piano / Xylophone / Electric Guitar / Bassoon / Orff), names localized per language.

### UI & Settings
- **Custom rounded window** + **live dark/light theme** + **Sky-style diamond keys** (with a flip animation on trigger).
- **Languages** — Simplified Chinese / Traditional Chinese / English / Japanese.
- **Settings view** (slides in from the middle/right panels) — language / theme / check updates / upload logs / UI scale / font scale / start countdown / key mapping.
- **Auto-requests admin** on launch + **automatic update check**.

## Install / Use

1. Download the latest **SMAP-Setup.exe** from [Releases](https://github.com/lingyunalingyun/Sky-MusaAutoPlay-SMAP-/releases).
2. Run it (it requests admin rights), pick an install path in the wizard; desktop / Start-menu shortcuts are created automatically.
3. Take out an instrument in-game → double-click a song in SMAP to enqueue → press the bottom ▶ or `F1` in-game.

> Sheets live in the `songs` folder next to the app (created on first import / download).
> For a bulk sheet library, see the [`曲谱库/`](曲谱库/) folder in this repo.

## Build from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

    dotnet build -c Release

Installer packaging lives in [`Installer/`](Installer/) (`build-installer.ps1`).

## Old JavaFX build

No longer maintained; source in [`JAVA version/`](JAVA%20version/).

## License

See [LICENSE](LICENSE). For study/hobby use; sheet copyrights belong to their transcribers.
