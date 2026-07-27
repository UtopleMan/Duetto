# Duet — feature backlog

Unscheduled ideas. Promote an item by writing a design spec + implementation plan
(`plans/<date>-<feature>-design.md` / `-implementation.md`) before building.

- [ ] Query directories in the background so the user interface is not locked on
  directories with many items. Today `PaneViewModel` lists and sorts on the UI
  thread; a huge dir (network mount, 100k+ entries) freezes the window. Load on a
  background task, stream or batch rows in, show a loading state, cancel a stale
  load when the user navigates away mid-enumeration.
- [ ] Real Connect backend (SFTP/S3/SMB) behind the stub dialog — biggest open
  feature. The drive popover's Connect… row currently opens
  `ConnectStubWindow`; replace with real remote connections and remote shares
  listed in the popover (design spec already reserves a shares section).
- [ ] Push repo to a remote (still no origin configured):
  `git remote add origin <url> && git push -u origin main`.
- [ ] Test Windows/Linux binaries on real target OSes before wide distribution.
  `dist/win-x64/Duet.exe` and `dist/linux-x64/Duet` are cross-compiled from
  macOS and never ran on their targets; verify chrome, trash, shell runner and
  volume/eject behavior there.
