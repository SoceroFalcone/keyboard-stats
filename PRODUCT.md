# 键迹 · KeyTrail

<!-- impeccable:product-schema 1 -->

## Platform

Windows native desktop (WPF; this platform is outside the skill's web/mobile enum).

## Stack

Delegated by the user: Windows native application, technology and interface details chosen by the implementer. C# / .NET 10 / WPF, using the installed SDK and no third-party runtime dependencies.

## Users

The computer's owner wants to understand their daily keyboard use.

## Product Purpose

Count each keyboard key and the five standard Windows mouse buttons by local calendar day across applications and make daily patterns easy to inspect.

## Operating Context

Runs visibly in the Windows notification area while the owner uses other applications. Opens as a normal desktop window. All statistics stay on this computer.

## Capabilities and Constraints

Daily totals, a keyboard heatmap, hourly distribution, key rankings, history, CSV export, pause/resume, and optional launch at sign-in. Long-press repeats are optional, disabled initially. No entered text, key sequence, application names, window titles, or network transmission. Startup is opt-in. No administrative rights requested. User data must survive restart and interrupted writes.

## Evidence on Hand

New empty project. No historical user statistics, artwork, or brand assets exist. Demonstration fixtures must be labeled and isolated from real statistics.

## Product Principles

- Make collection status obvious and pausing immediate.
- Persist aggregate counts only.
- Open and use without a development environment.
- Show honest empty and error states.
