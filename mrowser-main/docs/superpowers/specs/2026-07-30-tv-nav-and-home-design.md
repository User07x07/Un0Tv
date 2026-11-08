# TV navigation + home screen — design

**Date:** 2026-07-30
**Status:** approved, pending implementation plan

## Origin

Field feedback from a tester on a Mi Box 4K:

1. Scrolling up from the top of a page never stops — the page stays put while the viewport keeps moving up, and the user has to scroll back down.
2. There is no way to reach back / address / home controls; a long-press gesture would help.
3. Alternatively, a nav bar that stays fixed on screen.
4. A home/default page with a random background and popular sites would be good.

All four were verified against the code. Items 1 and 2/3 are real defects; item 4 is partly built.

## Verification

**Item 1 — confirmed bug.** `web/CursorController.kt:44-45` scrolls the WebView whenever the cursor sits inside the 48dp edge zone, with no check that the page can still scroll:

```kotlin
if (dirY < 0 && CursorGeometry.isAtTopEdge(y, edgeZonePx)) webView.scrollBy(0, -SCROLL_STEP_PX)
if (dirY > 0 && CursorGeometry.isAtBottomEdge(y, webView.height, edgeZonePx)) webView.scrollBy(0, SCROLL_STEP_PX)
```

`CursorGeometry.step` clamps the cursor to `y = 0`, which keeps it permanently inside the top edge zone, so the mover fires `scrollBy(0, -24)` every 16ms forever. `scrollY` goes negative and blank space opens above the page — exactly the reported symptom. The bottom edge has the same defect; it is less visible because pages usually end with content.

**Items 2/3 — confirmed gap.** `web/CursorLayout.kt:60` opens the chrome bar only on `KEYCODE_MENU`. The Mi Box 4K remote has no MENU key (Assistant, Back, Home, D-pad/OK, Volume, app hotkeys), and HOME is claimed by the system launcher. The only other appearance of the bar is the passive on-load reveal, which auto-hides after 8s. The bar itself already carries back / home / reload / favorite / history / URL — it simply cannot be summoned.

**Item 4 — partly exists.** `home/HomeView.kt` provides the favorites grid, URL pill, history and settings entry points. Two pieces are missing: the background is a flat `@color/surface` (`home_view.xml:6`), and nothing seeds default sites, so first run shows an empty grid.

## Decisions

- Nav access: **long-press BACK**, not a permanently docked bar. Remote-agnostic, costs no viewport, reuses the existing bar.
- Home background: **generated gradients**, not bundled images. No APK weight, crisp at 4K, no artwork to license.
- Seeded sites: **written into favorites on first run**, editable and deletable like any other favorite. No separate non-editable row.
- Seed list: **YouTube and Google only.**

## A. Edge-scroll clamp

**Pure —** `web/CursorGeometry.kt` gains:

```kotlin
fun scrollStep(
    dirY: Int,
    y: Float,
    height: Int,
    zonePx: Float,
    stepPx: Int,
    canScrollUp: Boolean,
    canScrollDown: Boolean
): Int
```

Returns `-stepPx` when moving up at the top edge and the page can still scroll up, `+stepPx` in the mirror case, `0` otherwise. It absorbs the existing `isAtTopEdge` / `isAtBottomEdge` calls; those stay public since they express the edge concept and are already tested.

**Glue —** `CursorController.mover` supplies `webView.canScrollVertically(-1)` and `webView.canScrollVertically(1)`, applies the returned delta, and follows with one safety line:

```kotlin
if (webView.scrollY < 0) webView.scrollTo(0, 0)
```

The boolean gate cannot prevent the final step from overshooting the top by up to 24px, so the correction pins it. The bottom needs no equivalent: a single 24px overshoot flips `canScrollDown` to false and the loop stops.

**Tests —** `CursorGeometryTest`: no scroll when the corresponding `canScroll` flag is false (the regression), scroll when true, zero when the cursor is off the edge, zero when `dirY == 0`.

## B. Long-press BACK opens the chrome bar

`web/CursorLayout.kt` gains a BACK long-press arm mirroring the existing OK long-press: a `Handler`, a 500ms `Runnable`, and a `backLongPressed` flag.

- **Tap BACK** — unchanged. `handleBack()` still runs the chain: exit fullscreen → close a visible bar → `webView.goBack()` → `onExitPage()`.
- **Hold BACK 500ms** — fires `onLongBack()`, and the following `ACTION_UP` is swallowed so no page-back happens.
- Repeated `ACTION_DOWN` events (TV remotes send them while held) are guarded by a `backDown` flag, matching how `okDown` works today.

**Arming conditions.** The timer is armed only when `!chrome.isVisible`. When the bar is already up, BACK keeps its current meaning of closing it.

**Fullscreen.** `onLongBack` is a lambda set by the Activity, wired as:

```kotlin
layout.onLongBack = { if (!chromeClient.isFullscreen) chrome.requestReveal(atTop = true) }
```

`BrowserWebChromeClient.isFullscreen` is already public (`web/BrowserWebChromeClient.kt:25`). Keeping the check in the Activity leaves `CursorLayout` ignorant of the chrome client, matching how `onBack` and `onExitPage` are already wired.

MENU keeps working unchanged.

**Discoverability.** A one-time Toast, "Hold BACK for the address bar", shown on the first page open. Gated by a second flag on `Settings`, `navHintShown: Boolean = false`, persisted alongside `seeded` (see section C) and set as soon as the Toast is shown. Three lines of glue in `MainActivity.openUrl`, no test beyond the JSON round-trip.

## C. Home screen

### Random gradient background

**Pure —** new `home/HomeBackgrounds.kt`:

```kotlin
object HomeBackgrounds {
    data class Gradient(val top: Int, val bottom: Int)
    val ALL: List<Gradient>          // 6 entries
    fun pick(seed: Long): Gradient   // ((seed % size) + size) % size
}
```

Six two-stop dark gradients, all low-luminance so white text and the `#E50914` accent stay legible over them.

**Glue —** `HomeView` picks once per app launch, in `bind`, and applies a `GradientDrawable(Orientation.TOP_BOTTOM, intArrayOf(top, bottom))` as its own background. Picking in `bind` rather than `show()` is deliberate: re-rolling on every return to home would make the color flicker between navigations. `android:background="@color/surface"` is removed from the `home_view.xml` root so the drawable is not painted over.

**Tests —** `HomeBackgroundsTest`: `pick` is deterministic for a given seed, always returns a member of `ALL`, and does not throw or go out of range on a negative seed.

### Seeded favorites

**Pure —** new `data/DefaultFavorites.kt`:

```kotlin
object DefaultFavorites {
    val ALL = listOf(
        Favorite("YouTube", "https://www.youtube.com/tv"),
        Favorite("Google", "https://www.google.com")
    )
}
```

**Seed-once flag —** `Settings` gains two booleans, `seeded: Boolean = false` and `navHintShown: Boolean = false` (the latter used by section B), both round-tripped by `SettingsJson`. Neither gets a row in `SettingsView` — they are internal state, not user preferences. `MainActivity.onCreate` seeds before the first `showHome()`:

```kotlin
if (!settings.get().seeded) {
    DefaultFavorites.ALL.forEach { favorites.add(it) }
    settings.update(settings.get().copy(seeded = true))
}
```

A flag, not an is-favorites-empty check: a user who deletes both entries must not have them reappear. Existing v1.0 installs have no `seeded` field, so `optBoolean` defaults it to false and they receive the seed once on upgrade — intended.

**Tests —** `SettingsJsonTest` extended for `seeded` and `navHintShown` round-trip and for their missing-field defaults.

## Open item

`https://www.youtube.com/tv` may reject the WebView user agent. This needs a check on a real device during implementation; the fallback is `https://m.youtube.com`. Everything else in this design is device-independent and covered by JVM tests.

## Out of scope

- A permanently docked nav bar, and a setting to choose between it and the long-press. Long-press alone is the decision; revisit only if testers still cannot find the bar.
- Any non-global or region-specific seed sites.
- Network-fetched background art — v1.0 hardening keeps the home screen offline.
- `versionCode` / `versionName` bump, which belongs to the release step, not this work.
