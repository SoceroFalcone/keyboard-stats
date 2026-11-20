# Verification record

## Latest: 1.5 photo keyboard and upright paired layout — 2026-11-15

Release solution build: zero warnings/errors. The WPF harness passes all existing daily/history/settings/hidden-window behavior plus 87 distinct native keycap hit targets, independent keyboard image landmarks, the photo's extra mute key and non-counted Fn, absence of visual numeric-keypad targets, exact textured heat levels, frozen/current-texture reuse and at least 4.87:1 contrast between legends and every opaque sampled keyboard material color. Native hover and press keep labels/materials opaque. Mouse image and all five hit targets rotate together; nose-up and right-of-keyboard placement are asserted at 1280 × 900 and 1060 × 700, alongside the existing physical handedness, side-key order, isolated left/right, counts and shares tests.

An isolated Num7-only day retains its 37 keyboard/combined count, leaves pictured keys unheated and exposes the same count through the All keys action next to the model. No counting or storage code changed. Twenty-six WPF captures were produced; fourteen required captures include top overview, keyboard focus, both device palettes, selection, empty/wheel-only/million-count states, isolated mouse sides, narrow view and visible filtered numpad detail. Evidence is `artifacts/input-layout/preview` and `.impeccable/review/v1.5`.

Lifecycle probe: both device controls and all 95 ImageSource weak references became unreachable after dashboard retirement. Combined visible retained pixels: 6,617,178 bytes (6.31 MiB, not whole-process memory). Both PNGs carry embedded prompts; scan reports 2 rasters, 0 missing. Core/native input checks below remain historical results, not new rerun claims. No mouse clicks were injected into the user's desktop.

The final self-contained 1.5.0 package successfully opened its dashboard, reopened through a second launch without duplicating the instance, loaded the expected 12,846 keyboard / 2,282 mouse demo totals and exited normally. Identical-data background sampling and a current-version refresh probe passed; methodology and limits are in PERFORMANCE.md. Evidence: `artifacts/input-layout/final-package-test.json`, `background-comparison.json`, `performance/refresh.json`.

Independent paired-layout review: **ship**, no material fixes.

## 1.4.1 physical handedness correction — 2026-11-15

Release build: zero warnings/errors. The actual WPF harness passed image-grounded landmarks for all five native hit targets, signed physical-left/right checks relative to nose/heel, forward X2 versus rear X1 ordering, and isolated left-only/right-only heat levels with exact 100% shares. Captures verify the new model against the user's official Viper V4 Pro reference. Existing material, selection, hover/press, contrast, million-count, empty, wheel-only, small-window, overview/history/settings and hidden-window collection/save checks also passed. Twenty-three screenshots were rendered; the ten current mouse/surrounding-layout captures for review are in `.impeccable/review/v1.4.1`.

The lifecycle probe checked all six image-source weak references and the mouse control after retiring the dashboard: all were collectible, with 4,339,222 retained pixel bytes while visible. Exact generation prompt scan: one shipping raster, none missing. Background measurements use identical preseeded 14-day data, recorded separately in PERFORMANCE.md.

The self-contained 1.4.1 package opened its real WPF dashboard with isolated demo data, retained 12,846 keyboard / 2,282 mouse counts, reopened through a second launch without creating a duplicate instance, and exited normally on request. Evidence: `artifacts/mouse-viper/preview/run.log`, `performance/artwork-lifecycle.json`, `final-package-test.json`. Collection and schema code were unchanged; earlier native-hook tests below are historical results, not rerun claims. No desktop mouse clicks were injected.

Fresh independent mouse-region review: **ship**, no material fixes; see visual-review-v1.4.1.md.

## 1.4 realistic mouse model — 2026-11-15

This version's physical-left description was later found incorrect by the user; its original tests missed handedness. The 1.4.1 landmark and isolated-side cases above address that gap.

Release build: zero warnings/errors. The actual WPF harness passed five native part hit targets/selection, correct heat levels, retained material tonal variation, frozen texture reuse when count changes without a level change, actual hover/press opacity and adjacent-label contrast, exact million-scale counts, empty and wheel-only data, 1280 × 900 / 1060 × 700 captures and the previous overview/history/settings/hidden-UI tests. Asset alpha and complete silhouette were inspected; the single shipped raster carries its exact generation prompt (scan: 1 raster, 0 missing).

`ArtworkLifecycleProbe` opened and retired the real dashboard and verified through a weak reference that its mouse control was collected. Retained artwork pixel data while visible: 5,144,578 bytes; no artwork/control remains attached to App.MainWindow after retirement. Performance results use the same mixed keyboard+mouse workload before and after, with separate identical-data background samples; see PERFORMANCE.md for methodology and limits. Evidence: `artifacts/mouse-realistic`, `.impeccable/review/v1.4`.

## 1.3 mouse model — 2026-11-15

Release build: zero warnings/errors. The WPF harness passed all existing overview/history/settings/hidden-window checks and the new model checks: each of five curved native Button regions receives its own hit and rejects empty corners; all regions select their own count; mouse-only/empty days, five heat levels under actual hover/press template triggers, separate keyboard normalization, exact million-scale detail with compact model labels, and both 1280 × 900 / 1060 × 700 layouts. Text contrast remains at least 4.5:1. The model adds no timer, animation or monitoring thread. Evidence: `artifacts/mouse-model/preview` and `.impeccable/review/v1.3`. Core input/storage behavior is unchanged; its previous verification is recorded below.

## 1.2 mouse extension — 2026-11-15

Release build: zero warnings/errors. **21 behavior checks passed**, including all earlier keyboard/storage tests plus five-button mouse counts, double-click/hold semantics, pause/injection filtering, motion/wheel exclusion, midnight/reset, keyboard-file compatibility, CSV names and native mouse-packet parsing. Both Win32 hooks install on the same message thread. Mouse packet fixtures are delivered inside the test process; no synthetic mouse clicks are sent to other applications.

The WPF harness passed independent device totals, five clickable mouse cells, exact shares, keyboard color normalization independent of larger mouse counts, split history totals, mouse-only dates, empty dates, persisted mouse selection, and continued keyboard+mouse autosave after dashboard release. It also verifies all five mouse heat levels remain at least 4.5:1 for text in hover and pressed states, using actual WPF template triggers with in-process forced states. The independent review's one contrast finding was resolved; its final scoped verdict is ship.

New evidence: `artifacts/mouse`, `.impeccable/review/v1.2`, [mouse implementation and limits](MOUSE.md), [scoped review](visual-review-v1.2.md). Earlier results below record the 1.0/1.1 baseline.

Verified on Windows x64 with .NET SDK 10.0.301 on 2026-11-14.

## Build

Release builds succeeded with zero warnings and zero errors. Distribution is a self-contained Windows x64 single executable, with native runtime components extracted by the .NET host on first use.

The final `dist/KeyTrail/KeyTrail.exe` was launched with isolated demonstration data: packaged startup passed, its 14 fixture day files were loaded, and a second launch exited with code 0 while the first process stayed running. The demonstration test process was then closed. No real keyboard collection occurred during that packaging check.

## Automated behavior checks: 17 passed

The console test runner in `tests/KeyTrail.Tests` verifies physical-press deduplication, held-repeat preference, pause and injection filtering, local midnight boundaries, modifier and keypad normalization, Pause keys without release events, persistent aggregate recovery, detached snapshots, concurrent merging and saving, corrupt-primary backup recovery, refusal to overwrite unrecoverable files, safe CSV formatting, and schema/aggregate validation.

Version 1.1 additionally verifies bounded full-day caching with dirty-day preservation, repeated dates in a batch when the cache is smaller than that batch, and streaming CSV equivalence / encoding / atomic replacement. Final result: **17 passed, 0 failed**, including the native checks below.

Two integration checks install the real Win32 keyboard hook on the interactive desktop: explicit test mode receives F24 key-down/up events and counts two distinct presses; the production hook ignores injected F24 input. Hooks are removed after the tests. Native input delivery does not work inside the restricted execution desktop, so those two checks were verified on the actual Windows desktop. No printable text is sent by these tests.

## WPF interaction and rendering checks: passed

`tests/KeyTrail.Preview` constructs the actual WPF application resources and controls in an isolated rendering harness. It suppresses production startup and uses separate demonstration data without installing a keyboard monitor. It checks:

- Overview totals and selected-key inspection.
- Fourteen-day history population.
- Preference persistence and immediate/persistent pause/resume.
- Date changes and zero-data views.
- Rendering at 1280 × 900 and 1060 × 700 device-independent window dimensions.
- Actual selected-key and keyboard-focused navigation states.
- Annual calendar totals, Monday-first cell hit testing, leap days, future-date prevention, year changes and empty years.
- Keyboard activation of a calendar day and exact hourly-cell selection (24 cells).
- Hidden/minimized refresh suspension, dashboard unloading and restored date selection.
- A synthetic internal aggregate continues to be collected and saved by the controller after the dashboard is closed and released.

Images are generated by `RenderTargetBitmap` of the test process's own WPF visual tree, not by capturing the user's desktop or other windows. Captures live in `artifacts/preview` and `.impeccable/review`; all visibly identify demonstration data.

The 1.1 checks and final captures are in `artifacts/performance/preview-final` and `.impeccable/review/v1.1`. At 125% system scaling, 1280 × 900 and 1060 × 700 DIP windows yield larger pixel captures. Both the top of the page and the scrolled hourly / ranking area were checked. The packaged 1.1.0 executable also passed background startup and cooperative `--exit` with the isolated 14-day fixture.

The Windows Computer Use tool's application approval timed out, so no claim is made about completed GUI automation through that tool. The app-owned WPF harness supplied the visual and interaction evidence instead.

## Visual review

An independent Impeccable reviewer examined overview, history, settings, small-window and empty-state renders. Its four material findings were corrected: text/focus contrast, scaled key-label legibility, historical-empty copy, and history interaction instructions. The final verdict is **ship** for those scored corrections; see `docs/visual-review.md` for scope and evidence.

The 1.1 heatmap extension received a fresh independent **ship** review of all nine required and four supporting captures plus WPF source. It found no material UI correction within that extension; see `docs/visual-review-v1.1.md`. This review did not independently measure performance or exercise a live screen reader.

## Deliberate limits

The optional Windows startup registry entry was not enabled on the user's computer during testing. Fn-only hardware keys, secure desktop input, special remote-desktop drivers and every possible international keyboard layout are outside the synthetic integration fixtures. Backup restoration can recover only the previous saved aggregate snapshot.
# 1.6 验证：2026-11-15

Release 与发布构建通过，零警告、零错误。22 项核心检查通过，包括新增的跨年／闰日／包含起止日的范围筛选、设备独立计数、无效自定义范围拒绝，以及日期与设备同时过滤的 CSV；旧版存储和输入行为检查继续通过。本轮没有重跑发送 F24 的桌面钩子测试，采集钩子未修改。

实际 WPF 控件检查通过：日期快捷按钮、原生日历打开与选择、清空日期恢复、全部／键盘／鼠标切换、全年指标独立于每日记录日期范围、7／30 天和自定义范围、错误与空结果、筛选状态在面板释放后恢复。原有键盘与鼠标部位命中、左侧侧键、热度归一化、隐藏小键盘记录、设置、暂停与后台保存检查继续通过。

最终 32 张应用自身渲染图在 `artifacts/ui-refinement/preview-final`，覆盖 1280 × 900 和 1060 × 700 DIP。文字与操作均跟随 Windows DPI；原生日历日按钮显式为 14 DIP，最小 32 × 30 DIP。模型文字对所有热度等级的实际不透明贴图颜色最低对比度 5.65:1。主题来源与人工视觉检查范围见 `visual-review-v1.6.md`。

自包含 1.6.0 安装包的可见演示窗口启动、14 天独立夹具、第二次启动复用实例、协作退出均通过；可执行文件 SHA256 为 `A02B3068D73B9A824EC853A99A3C86F1A34C0C23F868751A7C5C1F0893748702`。运行时数据结构未迁移。
