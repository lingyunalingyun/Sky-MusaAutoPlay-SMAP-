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

> 🎵 **v2.0**: game instrument timbres filled out to **37** (incl. 27 original Sky sounds), plus per-instrument transpose, hold-to-sustain, and random-speed playback.

> ⚠️ SMAP must **run as administrator** (required to simulate global key presses; it auto-requests UAC on launch).

## Screenshots

| Dark · Local library (Chinese) | Light · Cloud library (English) |
|:---:|:---:|
| ![Dark](docs/screenshots/dark_zh.png) | ![Light](docs/screenshots/light_en.png) |
| **Traditional Chinese · Settings** | **Japanese · Play queue** |
| ![TW](docs/screenshots/light_tw.png) | ![JA](docs/screenshots/dark_ja.png) |

## Features

### Playback
- **Auto-play** — simulates global key presses along the sheet timeline (uses scan codes + a short hold, so per-frame game polling doesn't miss notes).
- **Player bar** — cover / title·artist·transcriber / favorite star / shuffle·prev·play·next·queue / preview mode / cave reverb / instrument / pitch (transpose) / speed (incl. random).
- **Progress bar** — a full-width thin line pinned to the container's top edge; on hover it thickens and shows a white handle + a time pill that track the play position in real time. Drag to seek, even while paused.
- **Play queue** — a slide-out queue on the right; double-click a library song to enqueue and play; three modes (repeat-all / repeat-one / shuffle); auto-advance to the next track after a **2-second gap**.
- **Live speed control** + **global hotkeys**: F1 start/stop · F2 pause · F3 slower · F4 faster · F5 back 5s · F6 forward 10s.

### Library & Collections
- **Local library** — rich rows with cover art (title / artist / transcriber / duration); hover reveals add-to-queue / favorite / more; right-click menu with 7 actions (add to queue / add to collection / remove from library / open file location / edit / song info / upload).
- **My Collections (playlists)** — create / rename / delete / **drag to reorder** (with a drop indicator); the ⭐ star means "saved in any collection" (music-player style).
- **Inline cloud library** — browse the MuseTreehouse online library right in the middle pane, with **infinite scroll**, sort (newest / hottest / downloads) + difficulty filter, and one-click download.
- **Import** — `.json` / `.txt` / `.mid` (MIDI is auto-transposed to C major).

### Authoring & Sound
- **Piano-roll editor** — type notes on the keyboard, triplet grid, undo/redo.
- **Cave reverb** — recreates Sky's cave acoustics.
- **Instruments** — **37 of them**, including 27 original Sky timbres extracted from the game (Violin / Cello / Saxophone / Ocarina / Harmonica / Pipa / Glockenspiel / Handpan / Horn / Piccolo… etc.), names localized per language.
- **Per-instrument transpose** — independent pitch offset per instrument (12 semitones + octaves), saved locally; low instruments default to their in-game register, one-click reset.
- **Hold to sustain** — wind / bowed-string instruments sound continuously while a key is held.

### UI & Settings
- **Custom rounded window** + **live dark/light theme** + **Sky-style diamond keys** (with a flip animation on trigger).
- **Languages** — Simplified Chinese / Traditional Chinese / English / Japanese.
- **Settings view** (slides in from the middle/right panels) — language / theme / check updates / upload logs / UI scale / font scale / start countdown / key mapping.
- **Auto-requests admin** on launch + **automatic update check**.

## Shortcuts

**Global hotkeys** (work in-game too):

| Key | Action | Key | Action |
|---|---|---|---|
| `F1` | Start / Stop | `F2` | Pause |
| `F3` | Slower | `F4` | Faster |
| `F5` | Back 5s | `F6` | Forward 10s |

**Virtual keys** (15 keys, remappable in Settings):

| Register | Keys |
|---|---|
| High | `Y` `U` `I` `O` `P` |
| Mid | `H` `J` `K` `L` `;` |
| Low | `N` `M` `,` `.` `/` |

> Wind / bowed-string instruments sustain while you hold a key.

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
