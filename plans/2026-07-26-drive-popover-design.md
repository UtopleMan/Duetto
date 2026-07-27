# Drive popover — design spec

Date: 2026-07-26
Source: Claude design spec "Duetto File Manager.dc.html", turn 2, option **2a**
(project 9547189c-a040-4169-8fed-38dc0d79972e on claude.ai/design).
Scope decided with user: **drives + Connect stub** — no remote backend yet.
Eject on **macOS and Linux only**.

## What

A drive popover opened by clicking the leading segment of a pane's path bar.
Lists local volumes with capacity, a (currently empty) connected-shares
section, a Connect… entry (placeholder dialog until the remote backend
exists), and an Eject row for the current volume when it is ejectable.

Option 2b ("This machine" as a pane) is out of scope. Design 1g (Connect
dialog) is stubbed with a placeholder.

## Components

### Duetto.Core

- `FileSystem/VolumeInfo.cs` — record: `Name`, `MountPath`, `TotalBytes`,
  `FreeBytes`, `Format` (e.g. "APFS · 512 GB"), `IsEjectable`.
- `FileSystem/VolumeCatalog.cs` — builds `IReadOnlyList<VolumeInfo>`:
  - Source: `DriveInfo.GetDrives()`, filtered to `IsReady` and
    `Fixed | Removable | Network`.
  - macOS: skip `/System/Volumes/*` (system snapshot noise); name `/` from
    its volume label, falling back to "Macintosh HD".
  - Name: `VolumeLabel`, falling back to the mount directory name.
  - `IsEjectable`: `DriveType.Removable`, or mount under `/Volumes` (mac) or
    `/media`, `/run/media`, `/mnt` (linux). Root (`/`, `C:\`) never ejectable.
  - Pure row-building takes injected drive data so it is unit-testable;
    a thin wrapper reads real `DriveInfo`.
- `FileSystem/VolumeEjector.cs` — ejects by mount path:
  - macOS: `diskutil eject <mount>`.
  - Linux: `gio mount -u <mount>`; if `gio` is missing, `umount <mount>`.
  - Windows: not offered (row hidden).
  - Process-runner injected for tests; returns success or the tool's stderr
    line as the error message.

### Duetto (app)

- `ViewModels/DrivePopoverViewModel.cs` — one per pane:
  - `Refresh()` on open: reload volumes, detect current volume by longest
    `MountPath` prefix of the pane's `CurrentPath`.
  - `FilterText` narrows the volume list (case-insensitive substring on
    name and mount).
  - Selecting a volume: `pane.NavigateTo(MountPath)` and close.
  - `EjectCommand`: runs `VolumeEjector` off the UI thread; on failure shows
    the error as text at the bottom of the popover; on success closes and,
    if the pane was inside the ejected volume, navigates the pane home.
  - `Shares` collection exists but stays empty; the shares section is
    hidden while empty.
  - `ConnectCommand`: opens placeholder dialog.
  - Header text: "Open in left pane" / "Open in right pane".
- `Views/PaneView.axaml` — path bar splits into:
  - Leading **volume chip** button: 10px color swatch, volume display name
    (mono), ▾. Active pane: white chip, `#b9cbea` border, accent text, subtle
    shadow (design 2a). Inactive pane: plain, dimmed.
  - Remaining path (relative to the volume mount) in mono, as today.
  - Chip opens a `Flyout` (placement bottom-start) with the popover card.
- Popover card (~392px, `#fdfcfa`, 10px radius, shadow per design):
  - Header strip: "Open in <side> pane", right-aligned "type to filter" hint.
  - "THIS MACHINE" section: rows of swatch · name + mount (mono) over a 3px
    capacity bar · free-space text right-aligned (mono). Bar color by usage:
    > 90% `#b8443c`, > 75% `#c07a3a`, else `#2f8f5b`. Current volume row
    tinted `#eef1f7`. Row hover `#eef1f7`.
  - Divider. "CONNECTED SHARES" section only when `Shares` is non-empty.
  - Divider. **Connect…** row: dashed accent swatch, "Connect…" in accent,
    "SFTP, S3 or SMB" hint, right-aligned "Ctrl K" (⌘K on mac). Opens the
    placeholder dialog.
  - **Eject <name>** row: only when the current volume is ejectable and OS
    is mac/linux. Disabled while an eject is running.
  - Error line (red, small) at the bottom when the last eject failed.
- Placeholder Connect dialog: small modal in the AboutWindow style —
  "Remote connections (SFTP, S3, SMB) are coming soon." + Close. Ctrl/⌘ K
  inside the popover triggers it; no global shortcut yet.
- Keyboard inside the popover: typing filters, ↑/↓ move the highlighted row,
  Enter opens it, Esc closes. Light-dismiss on outside click (Flyout default).
- All three chromes share `PaneView`, so the chip and popover appear in
  win/mac/gnome without per-chrome work.

## Data flow

Chip click → `DrivePopoverViewModel.Refresh()` (sync, `DriveInfo` is fast;
wrapped in try/catch like `BuildPlaces`) → Flyout opens bound to the VM →
row click / Enter → `pane.NavigateTo(mount)` → Flyout closes. Eject runs on
a background task; result marshalled back via `Dispatcher.UIThread`.

## Error handling

- `DriveInfo` access failures: volume skipped (same pattern as
  `MainViewModel.BuildPlaces`).
- Eject failure: stderr line shown in the popover; popover stays open.
- Navigation to a vanished mount: `NavigateTo` already no-ops when the
  directory does not exist.

## Testing

- Core (xunit): catalog building from fake drive data (filtering, naming,
  mac snapshot skip), ejectable rules per platform/mount, ejector command
  construction with injected runner (no real processes).
- ViewModel: current-volume detection (longest prefix), filter narrowing,
  eject error surfacing (fake ejector).
- Headless UI (Avalonia.Headless): chip present in path bar, click opens
  popover, volume click navigates the pane, eject row hidden on
  non-ejectable volume.

## Out of scope

- Real SFTP/S3/SMB backend, live shares, working Connect dialog (1g).
- Option 2b ("This machine" rendered as a pane).
- Windows eject.
- Global Ctrl K shortcut outside the popover.
