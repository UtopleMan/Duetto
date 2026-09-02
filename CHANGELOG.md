# Changelog

All notable changes to Duetto are documented here. This project adheres to
[Semantic Versioning](https://semver.org).

## Unreleased

### Features
- **File viewer (`F3`)** — press `F3` on the pane cursor or a focused search
  result to open a reusable viewer window. Text files show with line numbers and
  a detected encoding label (UTF-8, UTF-8 BOM, UTF-16 LE/BE); binaries show a
  hex dump; images render inline with their pixel dimensions. Every backend is
  supported — local, SFTP, SMB, S3 and Azure Blob — through the existing
  file-system providers. `Ctrl`/`Cmd+F` opens find-in-file (`Enter`/`n` next,
  `Shift+Enter`/`N` previous), `W` toggles word wrap, `Esc` closes the find box
  and then the window. Text and hex previews load the first 4 MB and say so, with
  an **Open in default app** action alongside; images are decoded up to 64 MB and
  fall back to a hex dump above that. Previewing is not blocked by a running copy
  or delete, and the viewer remembers its own size and position.

### Changed
- **Avalonia 12.1.1** — upgraded from 11.3.18. Window chrome moved from the
  removed `ExtendClientAreaChromeHints`/`SystemDecorations` to the new
  `WindowDecorations` API, and the test suite moved to xunit.v3.
- Windows builds shrank by about 27 MB: Avalonia 12's native packages ship Skia
  and HarfBuzz debug symbols, which are now excluded from published output.

### Security
- **SSH.NET 2026.0.0** — fixes the high-severity advisory
  [GHSA-q939-rpr3-3284](https://github.com/advisories/GHSA-q939-rpr3-3284)
  present in 2025.1.0.

## 1.6.0 — 2026-08-14

### Features
- **Drag and drop** — move or copy files by dragging between the two panes (all
  backends), from Finder/Explorer into a pane (including upload to a remote pane),
  and from a pane out to the OS (local files, export-only). Copy is the default;
  hold Shift to move. A drag-out never deletes the source.
- **Copy path** — double-click a pane's path bar to copy its current path to the
  clipboard.

## 1.5.1 — 2026-08-07

### Fixed
- **Search results keyboard navigation** — Tab now drops the cursor into the
  search results, and while results are open Tab cycles between the left pane and
  the results; arrow keys drive whichever list holds focus. Previously the results
  pane could not be reached or navigated from the keyboard.
- **Idle CPU** — the pane loading spinner (an indeterminate progress bar) kept
  animating even while hidden, redrawing the whole window every frame and pinning
  idle CPU near 17%. It now animates only while a directory is actually loading.

## 1.5.0 — 2026-08-06

### Features
- **Remote over Azure Blob Storage** — connect to Azure Blob Storage and
  Blob-compatible services (the Azurite emulator) with the `Azure.Storage.Blobs`
  SDK, no OS mount. Account-key, connection-string, SAS, or anonymous auth, with
  an optional custom endpoint for emulators/on-prem; the connection root lists
  your containers (or a single configured container). Same-account moves are
  offloaded server-side (Copy Blob). ([setup & security →](docs/remote-azure.md))

### Fixed
- **Drive popover spacing** — added a gap between the "Filter drives" box and the
  "THIS MACHINE" heading, and vertically centred the "Connect…" row labels (now
  "SFTP, AZ Blob, S3 or SMB").
- **Connect dialog** — the Azure auth-mode radios (Account key / Connection string
  / SAS / Anonymous) now wrap instead of overflowing the window edge.

## 1.4.0 — 2026-08-05

### Features
- **Dark mode** — a Dark theme alongside the existing Light one, selectable in
  settings and persisted to `settings.json`. The saved theme is applied at startup
  (restart to switch). The palette is split into parity-checked Light/Dark
  dictionaries, and view/view-model colors are themed throughout so every chrome —
  marks, popovers, transfer strips, the desk background, and selection — follows
  the active theme.

### Fixed
- **Search bar polish** — removed the dead "Names" filter chip (it did nothing),
  and gave the scoped search field symmetric inner spacing so the query text no
  longer touches the `⌘F` hint and both edges match.

## 1.3.0 — 2026-08-04

### Features
- **Open remote files** — press Enter (or double-click) a file on an SFTP, SMB, or
  S3 remote to download it to a private temp folder and open it in your OS default
  app, behind a brief "Opening …" progress strip. The copy is view-only (never
  uploaded back), locked to your user (`0700` on macOS/Linux), and deleted when you
  quit Duetto; a copy left behind by a crashed session is swept on next launch.

## 1.2.1 — 2026-08-03

### Fixed
- **S3 connect no longer crashes on a bad endpoint** — entering an endpoint without a
  scheme (e.g. `minio.example.ts.net`) made the AWS SDK throw at client construction, which
  escaped the connect dialog's error handling and crashed the app. Scheme-less endpoints now
  default to `https://`, and any connect failure is surfaced as an inline dialog error.

## 1.2.0 — 2026-08-03

### Features
- **S3 / S3-compatible remote backend** — connect to Amazon S3 and any
  S3-compatible store (MinIO, Cloudflare R2, Wasabi, Backblaze B2) with the AWS
  SDK for .NET, no OS mount. Access-key, AWS-profile, or anonymous auth (with
  optional STS session token); custom endpoint + region + path-style for
  non-AWS servers. The connection root lists your buckets (or a single configured
  bucket); each bucket browses like a folder. Full read/write support (browse,
  upload/download, copy/move, rename, delete, recursive search), empty folders via
  zero-byte prefix markers, and multipart uploads for large objects. Separate
  **S3** protocol in the Connect dialog and `s3-connections.json`; the drive
  popover merges SFTP, SMB, and S3 connections. ([docs](docs/remote-s3.md))
- **Server-side copy / move (S3)** — transfers between two panes on the same S3
  connection are offloaded to the server via `CopyObject` instead of streaming
  through the client, including cross-bucket copies within the connection.

## 1.1.0 — 2026-08-02

### Features
- **SMB / Samba remote backend** — connect to SMB 2/3 shares (Windows shares,
  Samba, NAS) with a pure-managed client ([SMBLibrary](https://github.com/talaloni/smblibrary)),
  no OS mount. User/password/domain or guest auth; the connection root lists the
  server's shares. Full read/write parity with SFTP (browse, copy/move, rename,
  delete, recursive search, atomic `.part` writes). Separate **Connect SMB…**
  dialog and `smb-connections.json`; the drive popover merges SFTP and SMB shares.
  ([docs](docs/remote-smb.md))
- **Server-side copy / move** — transfers between two panes on the same SMB host
  and share are offloaded to the server instead of streaming through the client,
  avoiding a full download/upload round-trip.

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
