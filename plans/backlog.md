# Duet — feature backlog

Unscheduled ideas. Promote an item by writing a design spec + implementation plan
(`plans/<date>-<feature>-design.md` / `-implementation.md`) before building.

- [ ] Query directories in the background so the user interface is not locked on
  directories with many items. Today `PaneViewModel` lists and sorts on the UI
  thread; a huge dir (network mount, 100k+ entries) freezes the window. Load on a
  background task, stream or batch rows in, show a loading state, cancel a stale
  load when the user navigates away mid-enumeration.
