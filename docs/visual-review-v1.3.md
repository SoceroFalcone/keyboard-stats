# 1.3 mouse-model panel review

Independent native WPF review, 2026-11-15. **Disposition: SHIP** for the mouse-model panel at 1280 × 900 and 1060 × 700 window sizes. No material P1/P2 findings and no required fixes.

The reviewer inspected all eight supplied, current, 120-DPI app-owned captures, plus the scoped control, style, panel and harness implementation. It confirmed recognizable physical button locations, five attributable values, neutral non-data housings, exact selected counts/shares, readable empty and wheel-only states, compact large counts with exact detail, no narrow-window clipping, readable hover/press states, distinct focus/selection, native button semantics and geometry-confined hit testing. The static geometry and cached painting fit the existing low-overhead direction.

Evidence: `.impeccable/review/v1.3/overview-1280.png`, `mouse-selected-1280.png`, `mouse-model-1060.png`, `mouse-only-1280.png`, `mouse-empty-1280.png`, `mouse-large-count-1280.png`, `mouse-hover-five-levels-1280.png`, `mouse-pressed-five-levels-1280.png`. Each capture has a visible demonstration-data notice. Hover/press fixtures force all five in-process WPF states together to exercise the palette, without moving or clicking the OS pointer.

This is a code-led local replacement of the five mouse cells, preserving the incumbent system in DESIGN.md and its sidecar. The scoped direction is recorded in docs/surface.md; legacy chart descriptions in DESIGN.md remain superseded by the earlier scoped extensions. No web detector was run for native code. The reviewer did not rerun the harness, inspect physical hardware, or certify all possible display scales; those claims are outside this verdict.
