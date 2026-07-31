<div align="center">

<img src="src/Duetto/Assets/AppIcon.png" width="112" alt="Duetto icon" />

# Duetto

**A fast, keyboard-driven dual-pane file manager for Windows, macOS, and Linux.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://github.com/UtopleMan/duetto/actions/workflows/ci.yml/badge.svg)](https://github.com/UtopleMan/duetto/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/UtopleMan/duetto?sort=semver)](https://github.com/UtopleMan/duetto/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/UtopleMan/duetto/total)](https://github.com/UtopleMan/duetto/releases)

</div>

Two panes, everything on the keyboard, and a window that feels native on every
desktop. Copy and move between panes, browse remote servers over SFTP, search in
an instant, and delete straight to the system trash.

<div align="center">

### macOS
<img src="docs/screenshots/macos.png" width="820" alt="Duetto on macOS" />

### Windows
<img src="docs/screenshots/windows.png" width="820" alt="Duetto on Windows" />

### Linux
<img src="docs/screenshots/linux.png" width="820" alt="Duetto on Linux" />

</div>

## Features

- **Dual-pane, keyboard-first** — copy (`F5`), move (`F6`), delete (`F8`), rename
  (`F2`), new file/folder (`F7`); `Tab` switches panes, `Enter` opens, `Backspace`
  goes up.
- **Native on every OS** — the window chrome adapts to Windows, macOS, and GNOME.
- **Remote over SFTP** — save connections and browse or transfer files over SSH,
  with trust-on-first-use host-key verification.
  ([setup & security →](docs/remote-sftp.md))
- **Instant search** — recursive, scoped to the current folder or a remote share.
- **Safe deletes** — everything goes to the system trash: the Recycle Bin on
  Windows, the FreeDesktop trash on Linux, and the native macOS trash (with
  *Put Back* and correct handling of other volumes).
- **Remembers your workspace** — window position, size, and maximized state, plus
  each pane's last folder, restored on the next launch.
- **Command line** — `duetto [folder]` opens a folder in the left pane; a `duetto`
  launcher installs itself on your `PATH` the first time you run the app.

## Install

Download the latest build for your platform from the
[**Releases**](https://github.com/UtopleMan/duetto/releases/latest) page:

| Platform | Download |
| --- | --- |
| Windows (x64) | `duetto-<version>-win-x64.zip` |
| macOS (Apple Silicon) | `Duetto-<version>-arm64.dmg` · `duetto-<version>-osx-arm64.zip` |
| macOS (Intel) | `Duetto-<version>-x64.dmg` · `duetto-<version>-osx-x64.zip` |
| Linux (x64) | `duetto-<version>-linux-x64.zip` |

Builds are self-contained — no runtime to install.

**macOS** builds are unsigned. On first launch, right-click the app and choose
**Open**, or clear the quarantine flag:

```sh
xattr -dr com.apple.quarantine /Applications/Duetto.app
```

**Command line:** after launching once, `duetto` is on your `PATH`:

```sh
duetto .            # open the current directory
duetto ~/Projects   # open a specific folder
```

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/Duetto                       # run the app
dotnet test tests/Duetto.Tests/Duetto.Tests.csproj    # run the tests

scripts/publish-all.sh          # self-contained zips for all platforms
scripts/make-dmg.sh osx-arm64   # macOS .dmg (osx-arm64 | osx-x64)
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for more, and
[docs/remote-sftp.md](docs/remote-sftp.md) for SFTP details.

## License

[MIT](LICENSE) © 2026 UtopleMan
