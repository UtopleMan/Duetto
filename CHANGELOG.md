# Changelog

All notable changes to Duetto are documented here. This project adheres to
[Semantic Versioning](https://semver.org).

## 1.0.0 — 2026-07-31

First public release.

### Features
- **Dual-pane, keyboard-driven** file browsing with per-OS window chrome
  (Windows, macOS, and GNOME styles).
- **SFTP remote backends** — save connections, browse and transfer files over
  SSH, with TOFU host-key verification.
- **Live recursive search** scoped to the active pane or a remote share.
- **Copy / move / delete** with progress, and **cross-platform Trash**: Windows
  Recycle Bin, the FreeDesktop trash spec on Linux, and the native macOS trash
  API (with "Put Back" support and correct handling of other volumes).
- **Inline rename** and new file / folder creation.
- **Command line**: `duetto [folder]` opens a folder in the left pane; the app
  installs a `duetto` launcher on your PATH automatically.
- **Remembers your workspace** — window position, size, and maximized state, plus
  each pane's last folder, restored on the next launch.
