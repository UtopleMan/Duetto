# Changelog

All notable changes to Duetto are documented here. This project adheres to
[Semantic Versioning](https://semver.org).

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
