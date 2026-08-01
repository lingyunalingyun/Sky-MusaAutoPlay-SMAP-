<div align="center">

<img src="Assets/logo.png" width="140" alt="SMAP Logo" />

# SMAP · Sky-MusaAutoPlay

An auto-play helper for **Sky: Children of the Light** · C# WPF edition

[简体中文](README.md) · [Legacy JavaFX version](JAVA%20version/)

</div>

---

## About

SMAP auto-plays instruments in *Sky: Children of the Light* from sheet music you import or create. This repository is now the **C# WPF rewrite (v1.0)**. The old JavaFX edition is no longer maintained — its source lives in [`JAVA version/`](JAVA%20version/).

## Features

- **Auto-play** — simulates global key presses along the sheet timeline
- **Piano-roll editor** — keyboard input, triplet grid, undo/redo
- **Preview** — listen through your speakers without switching to the game
- **Seek + live speed** — scrub and change tempo while playing
- **Import** — `.json` / `.txt` / `.mid` (MIDI auto-transposed to C major)
- **Local library** — search / favorite / sort / tag / delete
- **Online library** — login, upload, download
- **Cave reverb** + 10 instruments
- **Play from your physical keyboard** on the main window
- **Light/Dark theme** + **multilingual** (Simplified/Traditional Chinese, English, Japanese)
- **Auto update check**

## Install / Use

1. Download the latest build from [Releases](https://github.com/lingyunalingyun/Sky-MusaAutoPlay-SMAP-/releases).
2. **Run as administrator** (required to simulate global key input — the app requests UAC automatically on launch).
3. In-game, take out an instrument → pick a song in SMAP → click Start or press `F1` in the game.

> Sheets are stored in a `songs` folder next to the executable (created on first import/download).

## Build from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build -c Release
```

## Legacy JavaFX version

No longer maintained. See [`JAVA version/`](JAVA%20version/).

## License

See [LICENSE](LICENSE).
