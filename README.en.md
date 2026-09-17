# 盯盘猫 (DingPanMao) · Taskbar Ticker

**English** · [简体中文](README.md)

A tiny Windows taskbar companion that shows live quotes for whatever you care about — A-shares, Hong Kong and US stocks, indices, gold, crude oil, FX, US Treasury yields — and quietly tells you when price reverses at a session extreme or crosses a support/resistance level.

## Features

- **Lives in the taskbar** — a slim borderless bar pinned over the empty area of the taskbar. It doesn't take a taskbar slot, never steals focus, and stays on top when you switch apps.
- **Any symbol** — 30 built-in instruments, plus a search box in settings where you can look up anything by name or code (A-shares / HK / US / indices / commodities / bonds / FX).
- **Up to 4 slots**, each with one of three sizes:

  | Size | Width | Content |
  |---|---|---|
  | Small | 100 px | name + change % + sparkline |
  | Medium | 122 px | name + change % + chart |
  | Large | 156 px | name + price + change % + chart |

- **Live chart** — last 60 minutes by default, refreshed every 10 seconds.
- **Three kinds of alerts**, with thresholds adapted per instrument via its 14-day ATR:
  - rebound off the session low / pullback from the session high
  - new session high / new session low
  - breaking above resistance / losing support (pivot points + recent swing highs/lows + round numbers)
- **Quiet by design** — no popups, no sounds. When something triggers, the slot flashes twice and its text is temporarily replaced by the alert message, then reverts after 4 seconds.
- **Remembers where you put it** — drag the bar left/right along the taskbar and it comes back to the same spot next launch.
- **10 UI languages** — English, 简体中文, 繁體中文, 日本語, 한국어, Русский, Español, Français, Deutsch, Português.
- **Adjustable appearance** — opacity, font scale, and red-up/green-down or green-up/red-down.
- **Click for details** — a chart window with intraday and the last 60 daily candles, with crosshair readouts (can be disabled in settings).
- **AI analysis** — plug in any service that speaks the OpenAI Responses API (DeepSeek by default) and get a direct call: **strong buy / buy / add / hold / trim / exit**, plus suggested position size, key levels and a stop reference. Multi-turn follow-up supported, and you can reveal the model's reasoning. Your own key, stored locally only — never bundled or uploaded.
- **Mouse wheel** — scroll on a slot to cycle through the built-in instruments.
- **Fallback data sources** — EastMoney first, Sina Finance as backup, and the slot goes grey when everything fails.

## Getting started

1. Open [Releases](https://github.com/AmamiyaYuko07/dingpanmao/releases) and pick a build:

   | File | Size | Notes |
   |---|---|---|
   | `DingPanMao-*-win-x64.zip` | ~400 KB | Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
   | `DingPanMao-*-win-x64-standalone.zip` | ~60–80 MB | Self-contained, unzip and run |

   `latest` is rebuilt automatically on every push to `main`; tags like `v1.0.0` are proper releases.

2. Unzip and run `DingPanMao.exe` — the bar appears just left of the system tray.
3. Right-click the bar → **Settings…** to choose symbols, language and alert switches.

### About AI analysis

The AI feature needs **your own API key**. Nothing is bundled and nothing is uploaded:

1. Open Settings → **AI Analysis**, paste your key and turn the switch on
2. Default endpoint is `https://api.deepseek.com` with model `deepseek-v4-flash`; any OpenAI Responses API compatible service works (e.g. `https://api.openai.com/v1`)
3. The key is stored in plain text at `%LOCALAPPDATA%\DingPanMao\settings.json` — local to your machine only

**Web search**: when enabled, OpenAI endpoints use the native `web_search` tool while DeepSeek and similar fall back to a built-in finance news search (free, no extra key). DeepSeek's `tools` parameter only supports `function`, so other tool types are ignored — hence the fallback.

## Usage

| Action | Result |
|---|---|
| Hold left button and drag | Move the bar; the position is remembered |
| Mouse wheel | Cycle through built-in instruments in that slot |
| Left click | Open the detail window (can be disabled in settings) |
| Right click | Recent alerts / Settings / Reset position / Exit |

Settings live in `%LOCALAPPDATA%\DingPanMao\settings.json`, and unhandled errors are logged to `%LOCALAPPDATA%\DingPanMao\error.log`.

## Data sources

Quotes come from public, key-free endpoints:

| Source | Coverage | Role |
|---|---|---|
| EastMoney | A-shares, HK, US, indices, commodities, bonds, FX | Primary; also provides daily bars and intraday data |
| Sina Finance | A-shares, HK, US, overseas futures and precious metals | Fallback when EastMoney fails |

These are snapshot quotes with a delay of seconds to tens of seconds. **For personal reference only — not suitable as a basis for trading decisions.**

## Adding a language

All UI text lives in [`src/DingPanMao/Resources/Strings.json`](src/DingPanMao/Resources/Strings.json), one object per language. Copy an existing block, translate the values, and the new language shows up in the settings dropdown automatically.

## Building

The repository does not contain compiled binaries (`dist/` is ignored), so build it yourself:

```bash
git clone https://github.com/AmamiyaYuko07/dingpanmao.git
cd dingpanmao

# Dev build
dotnet build src/DingPanMao/DingPanMao.csproj

# Single-file publish (needs the .NET runtime installed, ~400 KB)
dotnet publish src/DingPanMao/DingPanMao.csproj -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist

# Self-contained publish (large, no runtime needed on the target machine)
dotnet publish src/DingPanMao/DingPanMao.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

## Project layout

```
src/DingPanMao/
├── Config/          # settings model and persistence
├── Controls/        # slot control, sparkline, chart with axes
├── Interop/         # taskbar positioning and window styles (Win32)
├── Models/          # symbols, quotes, levels, alert events
├── Resources/       # localization bundle, app icon
├── Services/        # data providers, indicators, alert engine, i18n
└── Views/           # settings, symbol search, detail window
```

## Implementation notes

- The bar is a borderless, topmost, non-activating window (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) positioned to the left of the system tray.
- The tray area is a **child** of `Shell_TrayWnd`, so `FindWindowEx` is required to locate it — `FindWindow` alone won't find it.
- An `EVENT_SYSTEM_FOREGROUND` hook re-asserts topmost whenever any app comes to the foreground.
- Alert thresholds scale with each instrument's 14-day ATR, so sensitivity stays sane in both quiet and violent markets.
- If the quote timestamp lags local time by more than 5 minutes, the market is treated as closed and alerts pause — otherwise a closed session produces false signals.
- Mouse capture is taken on drag, so click-vs-drag is resolved in the window's mouse-up handler rather than by subscribing on the individual slots.

## Icon

The source image sits at `icon.png` in the repo root; `tools/make_icon.py` turns it into a multi-size `.ico`:

```bash
python tools/make_icon.py
```

The script adds a rounded-corner alpha mask (the source has no alpha channel) and rescales/sharpen per size for 16/20/24/32/48/64/128/256, writing to `src/DingPanMao/Resources/app.ico`.

## License

[MIT](LICENSE)
