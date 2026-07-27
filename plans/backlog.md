# Duetto — feature backlog

Unscheduled ideas. Promote an item by writing a design spec + implementation plan
(`plans/<date>-<feature>-design.md` / `-implementation.md`) before building.

- [x] Query directories in the background so the user interface is not locked on
  directories with many items. Done via `PaneViewModel`'s `LoadScheduler` seam
  (production `BackgroundScheduler` = `Task.Run`): listing + sort run off the UI
  thread, an `IsLoading` "Loading…" overlay shows during the load, and a per-pane
  load CTS cancels a stale load when the user navigates away mid-enumeration. See
  `plans/background-file-operations.md` (Phase 2), which also backgrounded
  delete/trash and rename.
- [ ] Real Connect backend (SFTP/S3/SMB) behind the stub dialog — biggest open
  feature. The drive popover's Connect… row currently opens
  `ConnectStubWindow`; replace with real remote connections and remote shares
  listed in the popover (design spec already reserves a shares section).
- [ ] Push repo to a remote (still no origin configured):
  `git remote add origin <url> && git push -u origin main`.
- [x] Add the app icon from the graphic design. `scripts/make-icon.py` now
  renders the design "9a / final mark" (amber-over-blue phase-shifted waves with
  an ivory lens on a dark tile) from the Duetto design spec, pure-stdlib. Wired
  across `Duetto.app` icns, window/taskbar icon (`Icon="/Assets/AppIcon.png"` on
  `MainWindow`), the Win chrome title-bar app mark, and the About dialog mark.
- [x] Rename the app everywhere from Duet to Duetto. The old split
  (solution=Duetto, app=Duet) is unified on Duetto: project dirs/namespaces
  (`src/Duetto`, `src/Duetto.Core`, `tests/Duetto.Tests`), assembly and binary
  names, window/menu titles, About dialog, `Duetto.app` bundle + Info.plist
  (`dk.truecon.duetto`), `DUETTO_LOG`/`DUETTO_FOCUS_LOG` env vars, publish
  scripts, and docs. Dir history preserved via `git mv`; build + all 131 tests
  green after the rename.
- [ ] Test Windows/Linux binaries on real target OSes before wide distribution.
  `dist/win-x64/Duetto.exe` and `dist/linux-x64/Duetto` are cross-compiled from
  macOS and never ran on their targets; verify chrome, trash, shell runner and
  volume/eject behavior there.
