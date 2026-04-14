# Notificator

Dalamud plugin that sends Telegram notifications for in-game events in Final Fantasy XIV. Useful when you're AFK in queue, crafting, or just want to keep track of what's happening while away from the screen.

## Installation

1. In-game: `/xlsettings` → **Experimental** tab
2. Add custom repository URL:
   ```
   https://raw.githubusercontent.com/D3FVLT/Notificator/main/pluginmaster.json
   ```
3. Save, then `/xlplugins` → search **Notificator** → Install
4. Open settings: `/notificator` or `/noti`

## Setup

1. Create a Telegram bot via [@BotFather](https://t.me/BotFather) → `/newbot`
2. Copy the bot token
3. Open plugin settings → paste the token
4. Send `/start` to your bot in Telegram
5. Click **Auto-detect Chat ID** in the plugin — it will find your chat automatically
6. Hit **Test Connection** to verify

## What it tracks

### Duties
- **Duty Pop** — queue popped, time to accept (great for AFK queues)
- **Duty Start / Complete / Wipe** — instance lifecycle

### Currencies
Threshold-based notifications for 40+ currencies organized by category:
- **Primary** — Gil, Tomestones (Poetics, Mathematics, Mnemonics), Company Seals, MGP
- **Scrips** — White/Purple Crafters' & Gatherers' Scrips
- **Hunt** — Centurio Seals, Sacks of Nuts, Bicolor Gemstones
- **PvP** — Wolf Marks, Trophy Crystals
- **Beast Tribes, Field Operations, Island Sanctuary, Content currencies** and more

Set a threshold → get a notification when you hit it.

### Combat & World
- **Death** — your character died
- **Zone Change** — moved to a new area
- **Class/Job Change** — switched jobs
- **Commendations** — someone commended you
- **GC Rank Up** — Grand Company promotion

### Social
- **Private Messages (Tells)** — someone whispered you while you were away

### Level
- **Level Up** — with optional minimum level filter (e.g. only notify at 90+)

## Proxy Support

If Telegram is blocked in your region, the plugin has built-in SOCKS5/HTTP proxy support. Configure it in the **Setup** tab — address, port, and protocol type. The proxy is used only for Telegram API calls, not for game traffic.

If you also need Dalamud itself to work through a proxy (for plugin updates), set `HTTP_PROXY` and `HTTPS_PROXY` environment variables before launching the game.

## Building from source

```
dotnet restore
dotnet build --configuration Release
```

Requires [Dalamud SDK](https://github.com/goatcorp/Dalamud) dev environment.

## License

AGPL-3.0
