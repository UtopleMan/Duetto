# Avalonia 12 Upgrade + SVG and PDF Preview

Move Duetto to Avalonia 12.1.1 and current NuGets (including a high-severity SSH.NET
advisory fix), then extend the F3 viewer with vector (SVG) rendering and true PDF page
rendering — all managed or single-native-library, no embedded browser engine.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Order of work (important)

1. **Phase 1 of this plan (the upgrade) goes first**, on a clean tree, before any viewer
   code exists. Upgrading and adding a feature in the same diff makes a failed test
   impossible to attribute.
2. Then `plans/2026-08-31-file-preview-f3.md` — the base viewer (text, hex, image, F3
   wiring, find, wrap). That plan's `PreviewKind`, `PreviewLoader` and `ViewerWindow` are
   prerequisites for everything below.
3. Then Phases 2–4 here, which add `Vector` and `Pdf` cases to those same types.

## Research findings this plan is built on (do not re-litigate)

| Question | Finding |
| --- | --- |
| Avalonia latest | **12.1.1**. 12.0.0 shipped 2026-04-07; .NET 8+ only (repo is already net10.0) |
| Avalonia's Skia | `Avalonia.Skia 12.1.1` → `SkiaSharp >= 3.119.4`. Latest SkiaSharp is 4.151.1 — do **not** drag a package in that forces 4.x unless verified |
| SVG | `Svg.Controls.Avalonia` 12.0.0.17 (MIT) — depends on Avalonia + `Svg.Model` + `Svg.SceneGraph`, **no SkiaSharp**. Preferred over `Svg.Controls.Skia.Avalonia`, which pulls SkiaSharp 4.x |
| PDF | `Docnet.Core` 2.6.0 (MIT, pdfium). Verified nupkg contains natives for `osx-arm64`, `osx-x64`, `win-x64`, `win-x86`, `linux`, `linux-arm`, `linux-arm64` (~5–6 MB each, so ~6 MB per published artifact). No SkiaSharp coupling |
| PDF alternative rejected | `PDFtoImage` 5.4.0 needs `SkiaSharp >= 4.150.1` — conflicts with Avalonia 12.1's 3.119.4 line. Fresher pdfium, but not worth forcing Avalonia onto an unverified Skia major |
| Browser engine | Rejected. CEF is 120–200 MB per platform, breaks `PublishSingleFile`, forces macOS helper-app signing, and adds a Chromium CVE treadmill — and still renders no office format on its own |
| Test framework | `Avalonia.Headless.XUnit` 12.1.1 depends on **xunit.v3** (`xunit.v3.extensibility.core >= 3.2.2`). The suite must migrate off xunit 2.9.3 |
| Security | `SSH.NET 2025.1.0` carries a **known high-severity advisory** (GHSA-q939-rpr3-3284). Fixed by 2026.0.0 |

### Known migration hotspots in this codebase
> Corrected during Phase 1 — the first bullet below was wrong as originally researched.
> See the Phase 1 Summary for what Avalonia 12 actually removed.

- `src/Duetto/Views/MainWindow.axaml.cs:66-68` — only `ExtendClientAreaChromeHints` is removed in 12
  (along with `SystemDecorations`); `ExtendClientAreaToDecorationsHint` and
  `ExtendClientAreaTitleBarHeightHint` survive and are still required. `WindowDecorations`
  (`None`/`BorderOnly`/`Full`) replaces the two removed ones.
  `tests/Duetto.Tests/Ui/ChromeTests.cs:30,42` asserts on them.
- `src/Duetto/Program.cs:23` calls `builder.UseSkia()` explicitly in headless mode — Avalonia 12
  can throw `InvalidOperationException` unless `UseHarfBuzz()` is added (package `Avalonia.HarfBuzz`).
- `Avalonia.Diagnostics` is referenced but `AttachDevTools` is never called — drop the package
  (use `AvaloniaUI.DiagnosticsSupport` + `AttachDeveloperTools()` only if DevTools are wanted).
- Clipboard: `PaneView.axaml.cs:320` and `CommandBar.axaml.cs:56` call `clipboard.SetTextAsync`.
  Resolved in Phase 1: the method moved to `Avalonia.Input.Platform.ClipboardExtensions`;
  both sites needed only the extra `using`, no `DataTransfer` port.
- Drag-and-drop is **already** on the new API (`DataTransfer`, `DataTransferItem`,
  `e.DataTransfer`, `DoDragDropAsync`), but Phase 1 found one break:
  `DoDragDropAsync` now demands `PointerPressedEventArgs`, not `PointerEventArgs`.
- `SelectionModel<T>` is used by `PaneViewModel` and `SearchViewModel`; Avalonia 12 changed
  selection-from-event overrides. Verify behavior, don't assume breakage.
- Test suite size: 413 `[Fact]`, 239 `[AvaloniaFact]`, 9 `[Theory]` across ~90 files.

## Phase 1: Avalonia 12 + NuGet upgrade
Status: Complete

Do this phase alone, on its own branch (`feature/avalonia-12`), with no feature work mixed in.

- [x] Record the baseline first: run `dotnet test` on `main` and write the passing test count into the Phase Summary
- [x] Bump `src/Duetto/Duetto.csproj`: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Headless`, `Avalonia.Themes.Fluent` → `12.1.1`
- [x] Remove the `Avalonia.Diagnostics` package reference (no `AttachDevTools` call exists)
- [x] Add `Avalonia.HarfBuzz` and chain `.UseHarfBuzz()` in `Program.cs` where `UseSkia()` is called
- [x] Migrate `MainWindow.ApplyChrome` from `ExtendClientArea*` to `WindowDecorations`, keeping Win/Gnome/Mac chrome behavior identical
- [x] Update `ChromeTests` to assert the new decoration property instead of `ExtendClientAreaToDecorationsHint`
- [x] Verify `IClipboard.SetTextAsync` still resolves; if removed, port both call sites to the `DataTransfer` clipboard API
- [x] Verify `SelectionModel<T>` usage in `PaneViewModel` / `SearchViewModel` still behaves (cursor, marks, Shift+arrow ranges)
- [x] Confirm compiled bindings (already `AvaloniaUseCompiledBindingsByDefault=true`) still compile every `.axaml`; fix any `{Binding}` that relied on reflection binding
- [x] Bump `src/Duetto.Core/Duetto.Core.csproj`: `SSH.NET` → `2026.0.0` (**security fix**), `AWSSDK.S3` → `4.0.102.4`, `Azure.Storage.Blobs` → `12.29.2`; leave `SMBLibrary` at `1.5.7.1` (current)
- [x] Re-check `CommunityToolkit.Mvvm` for a newer release at upgrade time and bump if one exists
- [x] Migrate the test project to xunit.v3: replace `xunit` 2.9.3 with `xunit.v3` (>= 3.2.2), `xunit.runner.visualstudio` → `4.0.0`, `Microsoft.NET.Test.Sdk` → `18.9.0`, `coverlet.collector` → `10.0.1`, `Avalonia.Headless.XUnit` → `12.1.1`, and apply the v3 project shape (test project becomes an executable)
- [x] Fix v3 fallout across the suite: `ITestOutputHelper` namespace, `IAsyncLifetime` signature changes, any `Assert` overloads removed in v3
- [x] Run `dotnet list package --vulnerable` and confirm it reports nothing
- [x] Publish all four RIDs and record each artifact's size delta against the 1.6.0 baseline (~43 MB)

### Verification Plan
- `dotnet build Duetto.slnx` — 0 errors; no new warnings beyond pre-existing ones
- `dotnet test` — passing count **equals the recorded baseline** (661 test attributes today); zero skips beyond the already-gated `Category=Integration` tests
- `dotnet list package --vulnerable --include-transitive` — no advisories reported
- `dotnet list package --outdated` — only intentionally-pinned packages remain listed
- `VERSION=0.0.0-upgrade scripts/publish-all.sh` — all four RIDs publish; `dist/*/Duetto*` exists for each
- `dist/osx-arm64/Duetto` launches, both panes list files, F5 copy works, drag between panes works, `Cmd+F` search works, double-click path copy puts text on the clipboard
- `scripts/smoke.sh` — SFTP/SMB/S3 integration tests pass against the docker backends

### Verification Results (2026-09-01)
| Check | Result |
| --- | --- |
| `dotnet build Duetto.slnx --no-incremental` | 0 errors. Warning set identical to baseline: CS4014 x6, xUnit1031 x4, CA1416 x4, xUnit2031 x2, MVVMTK0034 x2 |
| `dotnet test` | **739 passed, 0 failed, 0 skipped** — equals the `main` baseline exactly |
| `dotnet list package --vulnerable --include-transitive` | Clean on all three projects. GHSA-q939-rpr3-3284 gone |
| `dotnet list package --outdated` | Only `xunit.v3` 3.2.2 (4.0.0 available) — deliberately pinned, see summary |
| `VERSION=0.0.0-upgrade scripts/publish-all.sh` | All four RIDs published; `dist/<rid>/Duetto[.exe]` present for each |
| Published app launch | `dist/osx-arm64/Duetto` runs on the real macOS backend and stays up. `--screenshot` from the published single-file build renders all three chromes (win / gnome / mac) with both panes listed, placeholders, and the F-key footer |
| `scripts/smoke.sh` | **15 passed, 0 failed** (SMB + SFTP + S3 + Azure) against the docker backends. Ran via a port-remap override because host 9000 was held by an unrelated container; see summary |

Not verified by automation: the interactive F5-copy / pane-drag / `Cmd+F` / double-click-path-copy gestures on the real macOS window. Those paths are exercised by the 739 headless UI tests (46 assertions touch `SelectionModel`), but nobody drove the native window by hand.

### Phase Summary

**Baseline on `main`:** `dotnet test` = 739 passed / 0 failed / 0 skipped. (The plan's "661 test attributes" counts `[Fact]`/`[AvaloniaFact]`/`[Theory]` declarations; 739 is the executed count after theory expansion.)

**Branch:** `feature/avalonia-12`. 13 files changed, +69/-50, excluding this plan.

**Package moves**
- `src/Duetto`: Avalonia / Avalonia.Desktop / Avalonia.Headless / Avalonia.Themes.Fluent 11.3.18 -> 12.1.1; `Avalonia.Diagnostics` dropped; `Avalonia.HarfBuzz` 12.1.1 added.
- `src/Duetto.Core`: SSH.NET 2025.1.0 -> **2026.0.0** (advisory fix), AWSSDK.S3 -> 4.0.102.4, Azure.Storage.Blobs -> 12.29.2. SMBLibrary stays 1.5.7.1.
- `CommunityToolkit.Mvvm` re-checked: 8.4.2 is still the latest, no bump.
- `tests/Duetto.Tests`: xunit 2.9.3 -> **xunit.v3 3.2.2**, xunit.runner.visualstudio -> 4.0.0, Microsoft.NET.Test.Sdk -> 18.9.0, coverlet.collector -> 10.0.1, Avalonia.Headless.XUnit -> 12.1.1, `<OutputType>Exe</OutputType>` added.

**Corrections to the plan's research (do not repeat the old assumption)**
- `ExtendClientAreaToDecorationsHint` and `ExtendClientAreaTitleBarHeightHint` are **not** removed in Avalonia 12 — both still exist and are still needed. Only `ExtendClientAreaChromeHints` was removed. `SystemDecorations` was also removed; the new `WindowDecorations` enum (`None` / `BorderOnly` / `Full`) replaces both. The documented Avalonia 12 custom-title-bar recipe is `ExtendClientAreaToDecorationsHint=true` **plus** `WindowDecorations=None`, which is exactly what `MainWindow.ApplyChrome` now does for Win/Gnome. Mac is left at the default `Full`.
- `IClipboard.SetTextAsync` was **not** removed — it moved to an extension method in `Avalonia.Input.Platform.ClipboardExtensions`. Both call sites needed only `using Avalonia.Input.Platform;`, no `DataTransfer` port.
- xunit.v3 fallout was nil: no `ITestOutputHelper` namespace change, no `IAsyncLifetime` signature change, no removed `Assert` overload hit this suite. The only change needed was the project shape.

**Migration hotspots actually hit**
1. `GotFocusEventArgs` -> `FocusChangedEventArgs` (`PaneView.axaml.cs:257`).
2. `DragDrop.DoDragDropAsync` now takes `PointerPressedEventArgs`, not `PointerEventArgs`. The drag starts from a *move* event once the 4px threshold is crossed, so `PaneView` now stashes the originating `PointerPressedEventArgs` in `_dragTrigger` alongside `_dragOrigin`, and `ClearDragStart()` resets both.
3. `TextBox.Watermark` -> `PlaceholderText` (25 XAML attributes across 4 views). The `SearchViewModel.SearchWatermark` property name is unchanged — it is domain naming, not the Avalonia API.
4. `Bitmap.Save(string, int?)` obsolete -> `Save(path, PngBitmapEncoderOptions.Default)` in `App.axaml.cs` (the `--screenshot` path).
5. `Program.BuildAvaloniaApp` headless branch now chains `.UseHarfBuzz()` after `.UseSkia()`.
6. `SelectionModel<T>` needed no change. 46 assertions across 9 UI test files cover cursor / marks / Shift+arrow and all pass.

**Two decisions worth knowing about**
- *`xunit.v3` pinned to 3.2.2, not 4.0.0.* `Avalonia.Headless.XUnit` 12.1.1 declares `xunit.v3.extensibility.core >= 3.2.2` and was built against the 3.x extensibility ABI. 4.0.0 would satisfy the range on paper but is an untested major for that adapter. Revisit when Avalonia ships a Headless.XUnit built against v4.
- *`xUnit1051` suppressed via `<NoWarn>` in the test csproj.* xunit.v3 ships a new analyzer that wants `TestContext.Current.CancellationToken` passed to every cancellable call; it fires at 36 distinct sites across 7 test files. Adopting it is a test-quality change, not upgrade fallout, and 36 edits inside test bodies would have made a regression un-attributable in an upgrade-only diff. **Follow-up: adopt `TestContext.Current.CancellationToken` and remove the `NoWarn`** (`ConnectionManagerTests` 12, `SearchServiceTests` 8, `ShellRunnerTests` 6, `SmbConnectionManagerTests` 5, `TransferEngineTests` 3, `S3ServerSideCopyProviderTests` 1, `AzureServerSideCopyProviderTests` 1).

**Size regression found and fixed.** Avalonia 12's Windows native packages ship `libSkiaSharp.pdb` (84 MB) and `libHarfBuzzSharp.pdb` (21 MB). `-p:DebugType=none` only suppresses *managed* PDBs, so the first win-x64 publish came out at **72.7 MB**, up 27 MB. `Duetto.csproj` now carries an `ExcludeNativeSymbolsFromPublish` target that strips `.pdb` from `ResolvedFileToPublish`, bringing it back to 47.5 MB. Note this also means `scripts/publish-all.sh` should be run against a clean `dist/<rid>/` — publish does not delete stale files, and the zip step picks up whatever is in the directory.

**Artifact sizes** (baseline is 1.5.0; `dist/` holds no 1.6.0 zips):

| RID | 1.5.0 | Avalonia 12 | Delta |
| --- | --- | --- | --- |
| linux-x64 | 43.8 MB | 45.6 MB | +1.8 MB |
| osx-arm64 | 45.7 MB | 47.0 MB | +1.3 MB |
| osx-x64 | 47.5 MB | 48.8 MB | +1.3 MB |
| win-x64 | 45.5 MB | 47.5 MB | +2.0 MB |

**Smoke-test environment note.** `scripts/smoke.sh` fails on this machine with `Bind for 0.0.0.0:9000 failed: port is already allocated` — an unrelated `booker-minio` container holds 9000/9001. The run above used a scratch compose override (`ports: !override` — a plain `ports:` list *merges* rather than replaces) mapping MinIO to 19000/19001 with `DUETTO_S3_TEST_ENDPOINT=http://127.0.0.1:19000`. Nothing about the upgrade; the repo script is unmodified.

## Phase 2: SVG preview
Status: Complete

Prerequisite: the F3 viewer plan is implemented (`PreviewKind`, `PreviewLoader`, `ViewerWindow`).

- [x] Add `Svg.Controls.Avalonia` (12.0.0.17 or later 12.x) to `src/Duetto/Duetto.csproj`
- [x] Confirm the package resolves **without** pulling SkiaSharp 4.x: `dotnet list package --include-transitive | grep -i skiasharp` must still show the 3.119.x line Avalonia 12.1 pins
- [x] Spike (timebox, record the answer in the Phase Summary): load an SVG from an in-memory `Stream` rather than a file path, and note the exact type/method used
- [x] Add `PreviewKind.Vector`; detect SVG in `ContentSniffer` by an `<svg` root element (skipping leading whitespace, XML declaration, DOCTYPE and comments) — extension alone is not enough, and an `.svg` that fails the sniff falls back to `Text`
- [x] Budget: SVG loads within the existing 4 MiB text budget; a larger SVG is shown as truncated text, never partially parsed
- [x] `PreviewLoader` returns the raw SVG bytes for `Vector` (no decode in Core — Core stays Avalonia-free)
- [x] `ViewerViewModel` decodes bytes into the SVG image type; a parse failure falls back to text mode with the source markup, not an error
- [x] `ViewerWindow` renders vector mode scaled-to-fit, with the source dimensions (viewBox) in the header
- [x] Tests: `ContentSnifferTests` — `<svg>` document detected as `Vector`; `.svg` extension with HTML content not detected; SVG larger than the budget reported truncated. `ViewerTests` — a small SVG sets the image, a malformed SVG falls back to text mode

### Verification Plan
- `dotnet build Duetto.slnx` — succeeds
- `dotnet test --filter "FullyQualifiedName~Preview|FullyQualifiedName~Viewer"` — green
- `dotnet list package --include-transitive --project src/Duetto` — SkiaSharp still on the 3.119.x line
- Manual: F3 on `src/Duetto/Assets/*.svg` (or any repo SVG) renders the graphic; F3 on an SVG over 4 MiB shows the truncation footer

### Verification Results (2026-09-02)
| Check | Result |
| --- | --- |
| `dotnet build Duetto.slnx --no-incremental` | 0 errors. Warning set identical to the Phase 1 baseline: CS4014 x6, xUnit1031 x4, CA1416 x4, xUnit2031 x2, MVVMTK0034 x2 |
| `dotnet test --filter "FullyQualifiedName~Preview\|FullyQualifiedName~Viewer\|FullyQualifiedName~ContentSniffer"` | 79 passed, 0 failed |
| `dotnet test` | **823 passed, 0 failed, 0 skipped** (822 before the render test was added; 812 was the pre-Phase-2 count) |
| `dotnet list package --include-transitive` on `src/Duetto` | SkiaSharp still 3.119.4 across all four native asset packages. The SVG stack adds `ShimSkiaSharp`, `Svg.Custom`, `Svg.Model`, `Svg.SceneGraph`, all 5.2.3, none referencing SkiaSharp |
| Rendered graphic | `ViewerTests.Svg_actually_paints_into_the_window` drives the real `ViewerWindow` through the full load path under headless **Skia** (`UseHeadlessDrawing = false`), captures the frame and asserts the centre pixel is the SVG's `#3060c0` fill. The captured frame was also eyeballed once: header reads `fill.svg · 142 B · SVG` with `120 × 60` on the right, graphic scaled to fit |
| SVG over the budget | `PreviewLoaderTests.Svg_over_the_budget_is_truncated_text_rather_than_partial_markup` — an over-budget SVG comes back as truncated `Text` with `ImageBytes` null, so the footer's `HasTruncation` notice shows and no partial markup is ever parsed |

Not verified: the F3 gesture against a real SVG in a native window. `dotnet run --project src/Duetto` cannot start in this agent session — `Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661`, thrown inside `AvaloniaNativePlatform.Initialize` before any app code loads (no WindowServer access from the tool session). Unrelated to this phase; the same headless-Skia path that the render test exercises is what the native window uses to draw.

### Phase Summary

**Branch:** `feature/file-preview-f3` — Phase 2 extends viewer code that has not been merged to `main` yet, so it continues on that branch rather than starting a fresh one.

**Spike answer (SVG from a `Stream`).** `Avalonia.Svg.SvgSource.Load(Stream, Svg.Model.SvgParameters?)` — **static**, nullable-returning, and it works with no Avalonia application initialised at all (the parse produces a managed `ShimSkiaSharp.SKPicture`, not a GPU resource). `Avalonia.Svg.SvgImage` implements `IImage`, so it drops straight into `Image.Source`; `SvgImage.Size` comes from `SvgSource.Picture.CullRect`, which is the viewBox. Nothing else from the package is needed — no style include, no `AvaloniaResource`, no theme registration.

**Malformed-input behaviour found by the spike (this is why the catch list is wide).** `SvgSource.Load` throws `System.NullReferenceException` on non-SVG XML (`<html>…`) and `System.Xml.XmlException` on truncated markup. Catching `NullReferenceException` is normally wrong; here it is a third-party defect on a documented input class, and the alternative is an unhandled crash on a malformed file.

**Design decisions**
- *`ContentSniffer` sniffs content, never extensions.* SVG is detected by walking the head past whitespace, `<?…?>`, `<!--…-->` and `<!DOCTYPE …>` to an `<svg` root whose name is properly terminated (`>`, `/`, or whitespace). Lives in `SvgMarkupDetector` (internal) so `ContentSniffer` stays a one-screen decision table. An `.svg` file holding HTML sniffs as `Text`; `<svgish>` does not match; an unterminated prologue does not match.
- *BOM handled before the sniff.* The detector runs on `head[bomLength..]`, so a UTF-8 BOM'd SVG is still `Vector`. UTF-16 SVG is **not** detected (the scan is byte-wise ASCII) and falls through to `Text` — acceptable, that encoding is vanishingly rare for SVG.
- *`PreviewLoader.VectorContent` reuses `TextContent`.* Vector content carries **both** the decoded markup in `Lines` and the raw bytes in `ImageBytes`, so the view model's parse-failure fallback is `Kind = Text; FillLines(content.Lines)` with no second decode. `EncodingLabel` is blanked so the header shows the kind label `SVG`.
- *Over-budget SVGs never reach the parser.* `VectorContent` returns the plain truncated `TextContent` when `IsTruncated`, so `ImageBytes` is null and partial markup is never handed to `SvgSource`.
- *`ViewerViewModel.Image` widened from `Bitmap?` to `IImage?`* — `Bitmap` and `SvgImage` both implement it, so raster and vector share one property and one clear-on-reload path.
- *Vector gets its own `<Image>` element*, `Stretch="Uniform"` with no `StretchDirection`, so a small SVG scales **up** to fit. The raster element keeps `DownOnly` (upscaling a bitmap only blurs it) and its `ScrollViewer`.

**Files touched:** `PreviewKind.cs`, `ContentSniffer.cs`, `PreviewLoader.cs`, new `SvgMarkupDetector.cs`, `ViewerViewModel.cs`, `ViewerWindow.axaml`, `Duetto.csproj`, plus `ContentSnifferTests` (+6), `PreviewLoaderTests` (+2), `ViewerTests` (+3).

**Note for Phase 4.** `Svg.Controls.Avalonia` is MIT; its `Svg.Model` / `Svg.SceneGraph` / `ShimSkiaSharp` dependencies are MIT too. The third-party notice Phase 4 asks for needs no extra licence beyond MIT for the SVG half.

## Phase 3: PDF page preview via Docnet.Core
Status: Not started

- [ ] Add `Docnet.Core` 2.6.0 to `src/Duetto.Core/Duetto.Core.csproj`
- [ ] Spike (timebox, record in the Phase Summary): confirm the exact API — `DocLib.Instance.GetDocReader(bytes, new PageDimensions(...))`, `GetPageCount()`, `GetPageReader(index)`, `GetImage()`, `GetPageWidth()`, `GetPageHeight()` — and the returned pixel order (expected BGRA, 4 bytes/pixel). Write the finding down; the rest of the phase depends on it
- [ ] Add `PdfPageRenderer` in `src/Duetto.Core/Preview/PdfPageRenderer.cs`: opens PDF bytes, exposes page count, renders one page index to `(byte[] Pixels, int Width, int Height)`
- [ ] Treat `DocLib.Instance` as an application-lifetime singleton (per the library's own guidance) and serialize all calls behind a `Lock` — pdfium is not thread-safe
- [ ] Render at a scale derived from the viewer's current width, capped (e.g. max 2400 px on the long edge) so a poster-size page cannot allocate unbounded pixels
- [ ] Caps and failure handling: refuse PDFs over a `PdfMaxBytes` limit (default 128 MiB) with a message plus the existing **Open in default app** action; password-protected or corrupt PDFs surface a one-line reason, never a stack trace
- [ ] Add `PreviewKind.Pdf`; sniff on the `%PDF-` magic bytes. This overrides the hex path — a PDF is never shown as a hex dump
- [ ] `PreviewLoader` fetches the **whole** PDF (within `PdfMaxBytes`) since page rendering needs random access — unlike text/hex, the partial-fetch budget does not apply
- [ ] `ViewerViewModel`: `PageIndex`, `PageCount`, `PageText` (`3 / 12`), `NextPage()`, `PreviousPage()`; convert rendered pixels into a `WriteableBitmap`; render off the UI thread through the existing `LoadScheduler` seam
- [ ] `ViewerWindow`: page image scaled-to-fit, page navigation in the footer, `PageDown`/`PageUp`/arrows step pages, wrap/find hidden in PDF mode
- [ ] Dispose page readers and the document reader deterministically; do not dispose `DocLib.Instance`
- [ ] Tests in `tests/Duetto.Tests/Core/PdfPageRendererTests.cs` using a tiny PDF committed under `tests/Duetto.Tests/Assets/`:
  - [ ] Page count matches the fixture
  - [ ] Rendering page 0 yields `Width * Height * 4` bytes, non-uniform content
  - [ ] Out-of-range page index throws a clear exception (or returns null — pick one and assert it)
  - [ ] A file over `PdfMaxBytes` is refused by the loader before any pdfium call
  - [ ] A truncated/corrupt PDF produces a handled failure, not a crash
- [ ] Tests in `tests/Duetto.Tests/Ui/ViewerPdfTests.cs`: F3 on a PDF sets `PageCount`, `NextPage` advances and re-renders, navigation clamps at both ends

### Verification Plan
- `dotnet build Duetto.slnx` — succeeds
- `dotnet test --filter "FullyQualifiedName~Pdf"` — green
- `dotnet test` — full suite green
- `VERSION=0.0.0-pdf scripts/publish-all.sh` then run `dist/osx-arm64/Duetto` and F3 a real multi-page PDF — pages render and navigate **from the published single-file build**, proving the pdfium native survives self-extract
- Record each artifact's size delta (expect roughly +6 MB per RID)

### Phase Summary
_(write when phase completes)_

## Phase 4: Packaging, docs, release
Status: Not started

- [ ] Confirm every RID's published app renders text, hex, image, SVG and PDF (Windows and Linux runs are the risky ones — this repo has an open backlog item that those binaries have never run on their targets)
- [ ] README: add the viewer to the feature list, document `F3` and the supported formats, and state the limits (4 MiB text budget, 64 MiB image cap, 128 MiB PDF cap)
- [ ] README: note that PDF rendering embeds pdfium (BSD-3) and SVG rendering uses Svg.Model — add the third-party notices
- [ ] CHANGELOG: one entry covering the Avalonia 12 upgrade (with the SSH.NET security fix called out) and one for the viewer feature set
- [ ] `plans/backlog.md`: tick the viewer item, and add a follow-up item for office-document preview (docx/xlsx/pptx extraction) with a pointer to the research in this plan
- [ ] Bump the version and publish release artifacts

### Verification Plan
- `grep -n "F3" README.md CHANGELOG.md` — both document the viewer
- `VERSION=<next> scripts/publish-all.sh` — four artifacts produced; sizes recorded in the Final Recap
- `dotnet list package --vulnerable --include-transitive` — clean at release time

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete)_

## Deployment Plan
_(write when all phases complete)_
