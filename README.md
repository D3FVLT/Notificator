# Notificator - FFXIV Telegram Notification Plugin

Dalamud plugin for sending Telegram notifications about various in-game events in Final Fantasy XIV.

## Installation

1. Open Dalamud settings: `/xlsettings`
2. Go to **Experimental** tab
3. Paste this URL in **Custom Plugin Repositories**:
   ```
   https://raw.githubusercontent.com/YOURUSERNAME/Notificator/main/pluginmaster.json
   ```
4. Click **+** to add, then **Save**
5. Open Plugin Installer (`/xlplugins`) and search for "Notificator"

## Features

### Level & Experience
- **Level Up** — Get notified when you level up (with optional minimum level threshold)
- **Class/Job Change** — Notifications when switching classes

### Currency Tracking
- **Gil** — Notify when reaching a gil threshold
- **Tomestones** — Track Poetics, Heliometry, etc.
- **Company Seals** — Grand Company seal thresholds
- **MGP** — Manderville Gold Saucer Points

### Duty Events
- **Duty Pop** — Perfect for AFK queue notifications
- **Duty Start** — When a duty begins
- **Duty Complete** — Successful duty completion
- **Party Wipe** — When everyone dies

### Other Notifications
- **Login/Logout** — Session tracking
- **Zone Change** — Territory transitions
- **Death** — When your character dies
- **Commendations** — Received player commendations
- **Grand Company Rank Up** — GC promotions

## Installation

### Prerequisites
- XIVLauncher with Dalamud
- A Telegram Bot (created via [@BotFather](https://t.me/BotFather))

### Setting Up Telegram Bot
1. Message [@BotFather](https://t.me/BotFather) on Telegram
2. Send `/newbot` and follow the instructions
3. Copy the bot token (looks like `123456789:ABCdefGHIjklMNOpqrsTUVwxyz`)
4. Get your Chat ID:
   - Message [@userinfobot](https://t.me/userinfobot) to get your user ID
   - Or for group chats, add your bot to the group and use the Telegram API

## Usage

1. Open settings with `/notificator` or `/noti`
2. Enter your Telegram Bot Token and Chat ID
3. Click "Test Connection" to verify
4. Enable desired notifications in each tab
5. Configure thresholds as needed

## Available Dalamud Services Used

| Service | Purpose |
|---------|---------|
| `IPlayerState` | Player level, commendations, GC rank, stats |
| `IClientState` | Login state, territory, class changes |
| `IDutyState` | Duty start/complete/wipe events |
| `IGameInventory` | Currency and item tracking |
| `ICondition` | Combat state, death detection |
| `IDataManager` | Excel sheets for names |

## Trackable Parameters Reference

### IPlayerState Properties
- `Level` — Current job level
- `EffectiveLevel` — Level with sync
- `GetClassJobLevel(ClassJob)` — Any job's level
- `GetClassJobExperience(ClassJob)` — XP for any job
- `PlayerCommendations` — Total commendations
- `GetGrandCompanyRank(GrandCompany)` — GC rank
- `BaseRestedExperience` — Rested XP amount
- `IsMentor`, `IsTradeMentor`, `IsBattleMentor` — Mentor status
- `CharacterName`, `CurrentWorld`, `HomeWorld` — Character info

### IClientState Events
- `LevelChanged` — Level up events
- `ClassJobChanged` — Job switch
- `TerritoryChanged` — Zone changes
- `CfPop` — Duty Finder queue pop
- `Login` / `Logout` — Session events

### IDutyState Events
- `DutyStarted` — Duty begins
- `DutyCompleted` — Duty finished
- `DutyWiped` — Party wipe
- `DutyRecommenced` — Restart after wipe

### Currency IDs (FFXIVClientStructs)
- Gil: Item ID 1
- Tomestone of Poetics: Special ID 28
- Tomestone of Heliometry: Special ID 46
- Company Seals: Special IDs 20/21/22 (by GC)
- MGP: Special ID 29

## Contributing

Pull requests welcome! Please ensure your code follows the existing style.

## License

AGPL-3.0 (required for Dalamud plugins)

## Credits

- [Dalamud](https://github.com/goatcorp/Dalamud) — Plugin framework
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) — Game structure definitions
