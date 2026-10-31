# Un0Tv

**A remote-first Android TV that house a website.**

Un0Tv wraps one web app in a full-screen, D-pad driven Android TV experience. The site itself was built for phones: it expects swipes, has no TV navigation, and serves its films through third-party players covered in adverts. Un0Tv bridges that gap on the TV side, inside one app and without a VPN.

<p align="center">
  <img alt="version" src="https://img.shields.io/badge/version-1.5.0-blueviolet?style=flat-square" />
  <img alt="platform" src="https://img.shields.io/badge/platform-Android%20TV%20%7C%20Google%20TV-3DDC84?style=flat-square" />
  <img alt="dotnet" src="https://img.shields.io/badge/_.NET-MAUI%20%C2%B7%20net10.0--android-512BD4?style=flat-square" />
  <img alt="min sdk" src="https://img.shields.io/badge/min%20Android-5.0%20(API%2021)-0A84FF?style=flat-square" />
  <img alt="arch" src="https://img.shields.io/badge/ABI-arm64--v8a%20%C2%B7%20armeabi--v7a%20%C2%B7%20x86-lightgrey?style=flat-square" />
  <img alt="tested on" src="https://img.shields.io/badge/tested%20on-Sony%20Android%20TV-00A8E1?style=flat-square" />
</p>

> **Tested on a Sony Android TV.** The builds described here were installed and run on a real Sony Android TV — the TV launcher entry, the startup gate, the D-pad highlight, the ad filtering and playback all work there.

---

## Contents

- [What it does](#what-it-does)
- [How it is put together](#how-it-is-put-together)
- [The advert problem, layer by layer](#the-advert-problem-layer-by-layer)
- [Project layout](#project-layout)
- [Requirements](#requirements)
- [Build and run](#build-and-run)
- [Release builds and signing](#release-builds-and-signing)
- [Installing on a TV](#installing-on-a-tv)
- [Remote controls](#remote-controls)
- [Versioning](#versioning)
- [Known limits](#known-limits)

---

## What it does

| Feature | Behaviour |
|---|---|
| **TV launcher** | Declares `LEANBACK_LAUNCHER` and ships a TV banner, so Un0Tv appears on the Android TV home screen, not just in the apps list. |
| **Always landscape** | The activity is locked to landscape with the light bar hidden and the screen kept awake. |
| **Home screen** | Before the site opens, a protection dashboard in the style of Repel: a shield showing whether you are protected, the number of lookups blocked today, and three protection layers, each with its own button. |
| **Activity log** | A second screen lists what was looked up and whether it got through, newest first, with an All / Blocked tab. |
| **D-pad navigation** | The remote's arrows move a moving highlight over the site's clickable items; the centre button presses the highlighted one. |
| **Advert filtering** | Three layers: HaGeZi's Multi PRO list (~214,000 domains) plus a built-in host list on every network request, and an in-page cleaner that runs inside every frame, including the movie player's own. |
| **Pop-up policy** | The movie player's "can I open a pop-up?" check passes silently, while advert windows are refused and sign-in windows are allowed. |
| **Auto play** | Landing on a watch page presses the site's own play control, then lifts the mute so the film starts with sound. |
| **Back button** | Leaves fullscreen, closes a sign-in window, or steps one page back inside the site, before it ever closes the app. |

---

## How it is put together

```mermaid
flowchart TD
    A("MainActivity<br/>remote keys, fullscreen") --> B("MainPage<br/>browser + protection screens")
    B --> H("Home: shield, blocked today,<br/>three layer buttons")
    B --> L("Activity: what was looked up")
    H --> C{"Filter layer on?"}
    C -->|"Off"| E("Site loads, unfiltered")
    C -->|"On"| F("RequestFilter<br/>HaGeZi + built-in list")
    C -->|"List not saved yet"| D("Download once<br/>~30 MB, kept on the TV")
    D --> F
    F --> S("AdStats<br/>counts every decision")
    S --> H
    S --> L
    F --> E
    E --> G("AdCleanupScript<br/>in every frame")
    E --> N("DpadNav<br/>moves the highlight")
    G --> I("Clean playback")
    N --> I
```

The app is deliberately thin. It is **one page, one browser, and a handful of Android-side helpers**. There is no native re-render of the site, no scraping and no mirroring, so whatever the site shows is what you see.

---

## The advert problem, layer by layer

Adverts reach a streaming page through four different doors. Un0Tv closes them separately, because no single trick closes them all.

### 1. Advert hosts at the network level

`RequestFilter.cs` sits in front of every request the page **or a movie player** makes, and answers blocked hosts with an empty reply so the script never arrives. It matches against two lists:

- **HaGeZi's Multi PRO** — about **214,000** advert, tracker, phishing and malware domains. Downloaded once (around 30 MB) and kept on the TV, so later starts load it from disk in a second.
- **A built-in short list** — around 300 host names covering the worst ad exchanges, pop-unders and fake "your device is infected" pages. This one works even before anything is downloaded.

Two rules keep it safe:

- **A keep list always wins.** The site, its sign-in, the movie players and their video hosts are never blocked.
- **Whole host names only, never plain words.** `adserver.com` is blocked; a film titled *Shadow* is not. A subdomain such as `a.b.adsite.com` is caught by an entry for `adsite.com`, which is how the wildcard list is meant to work.

The home screen has three layer buttons, the way Repel lays out its protection:

| Layer | Button | What it does |
|---|---|---|
| **Ad and tracker filter** | `ON` / `OFF` | Turns the app's own filtering on or off. Off means nothing is filtered at all — handy if a film refuses to start and you want to rule the blocker out. |
| **HaGeZi Multi PRO list** | `Get` / `Update` | Fetches the ~214,000-domain list (around 30 MB, kept on the TV), or refreshes it later. |
| **Private DNS** | `Open` | Opens the TV's own settings. Android does not let one app change another app's DNS, so this takes you to the screen where you can. Setting it to `dns.adguard.com` blocks adverts in **every** app on the TV, not just Un0Tv. |

The shield turns green and says **Protected** when the filter is on, and the number under it counts lookups refused since midnight. **Activity** opens the log: one row per address, `Blocked` or `Allowed`, with what it was and how long ago. `Blocked` shows only the refused ones.

The five published HaGeZi files all carry the same domains in different formats, and the app merges all five:

| File | Line format |
|---|---|
| [`adblock/pro.txt`](https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/adblock/pro.txt) | `\|\|adsite.com^` |
| [`dnsmasq/pro.txt`](https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/dnsmasq/pro.txt) | `local=/adsite.com/` |
| [`wildcard/pro.txt`](https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/pro.txt) | `*.adsite.com` |
| [`wildcard/pro-onlydomains.txt`](https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/pro-onlydomains.txt) | `adsite.com` |
| [`rpz/pro.txt`](https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/rpz/pro.txt) | `adsite.com CNAME .` |

`Hagezi.cs` reads every line format, keeps only real host names, and saves the merged result as one plain domain per line. One list failing to download does not lose the other four.

### 2. Advert windows

The player runs a pop-up capability check by asking for a blank 1x1 window. The app hands that check a harmless stand-in so it passes quietly, opens real windows for Google, Firebase and the site's own sign-in, and refuses everything else.

### 3. Advert overlays inside the player

This is the hard one. The QR walls and "confirm you are not a robot" covers are **drawn by the movie player itself**, inside its own frame, where the page's own scripts cannot reach. `AdCleanupScript.cs` is registered to run in **every frame at page start**, so it sees the cover as it is built. It closes the cover using its own X or skip control, and never touches anything holding the video.

### 4. Sound and fullscreen

Browsers only allow a video to start by itself while it is muted, so the player begins silent. The app permits sound-on start and lifts the mute inside the player at the moment it is set. Fullscreen is passed through to Android, and **Back** always leaves fullscreen before it does anything else.

> **Note:** a QR or logo burned into the video picture by the source cannot be removed by any app. Only choosing a different source helps.

---

## Project layout

```
Un0Tv/
├── MainPage.xaml / .xaml.cs        Home dashboard, Activity log, and the browser
├── Activity.cs                     The live counters and the activity rows
├── PageScripts.cs                  Auto play script injected after each load
├── DpadNav.cs                      The moving highlight and its arrow logic
├── Hagezi.cs                       The big block list: download, parse, match
├── version.json                    Base version for Nerdbank.GitVersioning
└── Platforms/Android/
    ├── MainActivity.cs             Remote keys, fullscreen, Back, keyboard detection
    ├── AndroidManifest.xml         TV features and banner
    ├── TvWebView.cs                Native view setup, pop-up window, fullscreen
    ├── TvChromeClient.cs           Pop-up routing, fullscreen callbacks
    ├── TvWebViewClient.cs          Request hook for the filter
    ├── RequestFilter.cs            Keep list, short block list, the switch check
    ├── PopupGuard.cs               Which pop-up windows are allowed
    ├── AdCleanupScript.cs          In-frame advert cleaner
    └── AudioAutoplay.cs            Sound-on autoplay inside the player
```

---

## Requirements

| Item | Version |
|---|---|
| .NET SDK | 10.0 (MAUI workload installed) |
| Android SDK | API 36 build tools, platform `android-36` |
| JDK | 17 |
| Target device | Android TV or Google TV, Android 5.0 (API 21) or newer |
| Tested on | **Sony Android TV** |

> Samsung (Tizen) and LG (webOS) televisions cannot run this app. It needs Android underneath.

### Packages

| Package | Why it is here |
|---|---|
| `Microsoft.Maui.Controls` | The app framework |
| `Microsoft.Extensions.Logging.Debug` | Debug logging |
| `Octokit` | GitHub API client, kept for tooling and future release checks |
| `Nerdbank.GitVersioning` | Stamps the app version from git history |
| `Xamarin.AndroidX.WebKit` | Lets the app run a script inside **every frame**, which is what makes the in-player cleaner possible. Pinned to `1.11.0.5` to stay on Android 5.0 |

---

## Build and run

```powershell
# Debug, straight to a connected TV or emulator
dotnet build -f net10.0-android -c Debug

# Signed release APK, all chip types in one file
dotnet publish -f net10.0-android -c Release
```

The installable file lands in:

```
Un0Tv/bin/Release/net10.0-android/publish/com.DevLink.Un0tv-Signed.apk
```

The project builds for three Android chip types at once (`arm64-v8a`, `armeabi-v7a`, `x86`), so a single APK covers modern TVs, older 32-bit boxes and the emulator. Restricting to one chip type produces a smaller file but an APK that some TVs refuse with *"App not installed"*.

> **If a build fails with** `System.IO.IOException: The directory is not empty`, an earlier build is still holding files. Run `dotnet build-server shutdown`, then build again.

---

## Release builds and signing

Release APKs are signed with your own key, not the throwaway debug key. The key and its passwords live in files that are **kept out of source control**:

| File | Purpose | In git? |
|---|---|---|
| `Un0Tv/un0tv.keystore` | The signing key | **No** |
| `Un0Tv/signing.props` | Key passwords and alias | **No** |

`Un0Tv.csproj` imports `signing.props` only for **Android + Release**, so Debug builds never touch your real key. If that file is missing, Release still builds, but falls back to the debug key.

To create a key from scratch:

```powershell
keytool -genkeypair -v -keystore un0tv.keystore -alias un0tv `
        -keyalg RSA -keysize 2048 -validity 10000
```

**Keep this file forever.** Every future update must be signed with the same key, or the TV will refuse it as an update to the installed app.

---

## Installing on a TV

### Over the network (recommended)

```powershell
adb connect <TV-IP>:5555
adb install "Un0Tv/bin/Release/net10.0-android/publish/com.DevLink.Un0tv-Signed.apk"
```

Turn on developer mode on the TV first: **Settings → System → About → click Build seven times**, then **Settings → System → Developer options → Network debugging**.

### Without a cable or ADB

Copy the APK to a USB stick and open it with a file manager on the TV, or install the *Downloader* app on the TV and point it at the file.

If the TV says **"App not installed"**:

1. Turn on **Install unknown apps** for the app doing the installing.
2. Confirm the TV is Android TV or Google TV, not Tizen or webOS.
3. Confirm the APK carries the TV's chip type (`arm64-v8a` or `armeabi-v7a`).
4. Run `adb install` instead of tapping the file. It prints the real reason the vague message hides.

---

## Remote controls

| Key | What happens |
|---|---|
| **Up / Down / Left / Right** | Moves the highlight to the nearest clickable item in that direction |
| **Center / Enter** | Presses the highlighted item |
| **Back** | Leaves fullscreen, or closes the sign-in window, or steps back one page, or exits the app |

The startup gate uses Android's own focus movement, so the D-pad works there in the normal TV way.

---

## Versioning

Current version: **1.5.0** (Android version code **6**).

`version.json` holds the base version (`1.5`), and `Nerdbank.GitVersioning` is installed to expand it from git history. Android needs two numbers, and both must climb on every install or the TV treats the new file as the same old app and refuses to update it:

| Android setting | What it means | Current value |
|---|---|---|
| `ApplicationDisplayVersion` | The version you see, for example `1.5.0` | `1.5.0` |
| `ApplicationVersion` | A whole number that must increase on every install | `6` |

> **Status:** the two numbers in `Un0Tv.csproj` are still hand-typed rather than driven by the versioning package. **Bump `ApplicationVersion` by one for every APK you install** — a repeat number is the usual reason a TV refuses an update.

---

## Known limits

- **The player's own frame cannot be styled.** The site is never redrawn natively, so its layout on a TV is exactly the layout a phone browser would get.
- **Advert covers change.** The in-frame cleaner works by matching wording, corners and shapes. A cover with entirely new wording can slip through until it is added.
- **The ad filter app's switch is not automatic.** Android will not let one app turn on another app's filter. The startup gate verifies it and opens the right screen; the final toggle is yours to press.
- **Burning-in is forever.** A logo or QR encoded into the video picture itself is part of the film, so no layer can remove it.
- **Only one site.** The address lives in `MainPage.xaml.cs` as `SiteUrl`. The scripts that press play and clean covers are tuned to that site's markup.

---

<p align="center"><sub>Un0Tv 1.2.0 · built for a remote, not a thumb · tested on Sony Android TV</sub></p>
