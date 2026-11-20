# KeyTrail 1.6: date tools and readable history

User direction: implement the selected 21st references, preserve the existing photographic keyboard/upright mouse, maintain low background memory and readable native Chinese type. The user's later instruction superseded the original green heat-palette requirement: update both UI and heatmaps, using their selected Meridian Blue theme.

Palette: sidebar #E2E5EA, ground #EEF0F3, surface #FFFFFF, ink #181B21, muted #5A6072, primary #3B608F, line #C9CFD8, warm selection #B8694A. Heat scale #EEF0F3 / #D8E2EE / #A8C2DD / #3B608F / #1F3A5C. Source: https://21st.dev/community/themes/meridian-blue (retrieved CSS archived in artifacts/ui-refinement/reference/meridian-blue.css). No new artwork, browser runtime, blur, animation clock, or chart library.

Type in WPF device-independent pixels: body/controls 14; secondary text 13; peripheral notes 12; section headings 17; page headings 25; primary metrics 30. Segoe UI / Microsoft YaHei UI. Model key labels retain their geometry-aware fitting. A 1060 DIP window and Windows scaling must remain usable.

Layout:
```
gray rail | page title + subtitle                         export
          | daily: [previous | date + calendar | next] [today yesterday]
          | history: [all | keyboard | mouse]
          | four compact metrics
          | existing keyboard + upright mouse (unchanged)
```
History uses two clearly labelled scopes: the annual heatmap and annual metrics follow the year navigator and device selector; daily records follow date presets/custom inclusive range and the same device selector. The records' export follows both of those list filters. Active minutes remain labelled as combined keyboard/mouse minutes, because existing data does not store device-specific active minutes.

Grounded references, viewed through the 21st plugin on 2026-11-15:
- https://21st.dev/@isaiahbjork/components/app-dashboard-shell — alignment and toolbar rhythm, adapted to the existing flat pine/sage identity.
- https://21st.dev/@kuratlielia/components/activity-heatmap — quiet calendar labels and legend.
- https://21st.dev/@sean0205/components/c-calendar-16 — visible date shortcuts and explicit range.
- https://21st.dev/@cnippet-dev/components/segmented-control-tabs — grouped, mutually exclusive filters with keyboard focus.

Brief review: retain a single strong device illustration, put date controls on their own row to avoid shrinking Chinese text, and use the user-selected theme. Reuse native WPF controls and existing small summaries; no component source or web dependency is needed. Native calendar days are explicitly 14 DIP with 32 × 30 DIP minimum targets; button hover/press preserves foreground contrast.
