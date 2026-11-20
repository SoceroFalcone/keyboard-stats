# 1.1 heatmap finish review

Disposition: **ship**. Independent read-only Impeccable review, 2026-11-14.

Scope: replace usage bars with a full-year daily activity calendar, 24 hourly square cells and textual key rankings; preserve the established WPF interface and pine/sage system. The supplied user image was a contribution-grid reference, not a whole-app redesign specification.

Evidence: all 13 images in `.impeccable/review/v1.1`, including 1280 × 900 and 1060 × 700 window dimensions at 125% scaling, focused calendar, empty year, empty day, selected key, focused navigation, settings and scrolled hourly areas. Reviewed the reference, craft floor, design context and relevant WPF source. No web detector was used for native WPF.

Findings: no material UI corrections. Annual totals, calendar labels, year navigation, exact textual counts, future-date state, empty states and keyboard operations are present. The 24 hourly cells and textual rankings fit both widths. Native scrolling exposes lower content. Checked primary text combinations exceed 4.5:1 contrast. The yearly grid uses one drawing surface and hourly cells reuse 24 buttons.

The reviewer noted original DESIGN.md bar-chart prose as known documentation drift. The scoped implementation contract in `docs/surface.md` now describes its replacement; the established design token files are preserved.

Limits: screenshot/source review only. Build, behavior tests, performance numbers, shortcut target and installation verification were performed by the build agent; the reviewer did not repeat them. Live screen-reader behavior and display scaling beyond 125% were not validated. The ship verdict covers this heatmap extension.
