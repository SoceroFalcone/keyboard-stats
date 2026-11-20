# 1.2 mouse extension review

Independent Impeccable review, 2026-11-15. Initial disposition: fix. One material finding: mouse cells inherited content opacity on hover/press, reducing 12-DIP label contrast below 4.5:1. Other supplied layouts, counters, native focus, selected states, narrow layout, mouse-only and empty states were consistent at scope.

Correction: a scoped HeatCellButton template keeps mouse-cell content at opacity 1 and changes only the border for hover/press. Existing keyboard controls are unchanged. Added two app-owned WPF renders of all five heat levels, using in-process state forcing without operating the OS pointer, and assertions on label contrast and actual trigger border values.

Final verdict: **resolved; disposition ship** for the single scored correction. The reviewer confirmed a minimum 4.94:1 label contrast, preserved selection/focus, and no visible regression in the 1060-width layout. This is a scoped verdict, not a hardware-input or performance certification.

Evidence: `.impeccable/review/v1.2`, particularly `mouse-hover-five-levels-1280.png`, `mouse-pressed-five-levels-1280.png`, `mouse-selected-1280.png`, `overview-detail-1060.png`. The initial pass reviewed 16 captures; the correction added the two forced-state captures. Actual WPF source and the incumbent design context were also reviewed; no web detector was run for WPF.
