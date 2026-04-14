using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Notificator.Services;

public class NotificationTracker : IDisposable
{
    private readonly Configuration _config;
    private readonly TelegramService _telegram;
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IDutyState _dutyState;
    private readonly ICondition _condition;
    private readonly IDataManager _dataManager;
    private readonly IChatGui _chatGui;

    private short _lastLevel;
    private short _lastCommendations;
    private byte _lastGCRank;
    private readonly Dictionary<string, long> _lastCurrencyValues = new();
    private bool _wasInCombat;
    private bool _currencyIdsResolved;

    // Resolved tomestone item IDs (from Lumina Item sheet)
    private uint _poeticsItemId;
    private uint _mathematicsItemId;
    private uint _mnemonicsItemId;

    // All currency values exposed for UI, keyed by display name
    public Dictionary<string, long> CurrentCurrencies { get; } = new();
    public Dictionary<string, string> CurrencyCategories { get; } = new();
    public short CurrentLevel { get; private set; }
    public string CurrentClassJob { get; private set; } = string.Empty;
    public string CurrentZone { get; private set; } = string.Empty;
    public short CurrentCommendations { get; private set; }

    public NotificationTracker(
        Configuration config,
        TelegramService telegram,
        IPluginLog log,
        IClientState clientState,
        IPlayerState playerState,
        IDutyState dutyState,
        ICondition condition,
        IDataManager dataManager,
        IChatGui chatGui)
    {
        _config = config;
        _telegram = telegram;
        _log = log;
        _clientState = clientState;
        _playerState = playerState;
        _dutyState = dutyState;
        _condition = condition;
        _dataManager = dataManager;
        _chatGui = chatGui;

        SubscribeToEvents();
        InitializeTracking();
    }

    private void InitializeTracking()
    {
        if (_playerState.IsLoaded)
        {
            _lastLevel = _playerState.Level;
            CurrentLevel = _lastLevel;
            _lastCommendations = _playerState.PlayerCommendations;
            CurrentCommendations = _lastCommendations;
            if (_playerState.GrandCompany.IsValid)
            {
                _lastGCRank = _playerState.GetGrandCompanyRank(_playerState.GrandCompany.Value);
            }
        }
    }

    private void ResolveTomestoneIds()
    {
        if (_currencyIdsResolved) return;

        try
        {
            var itemSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (itemSheet == null) return;

            foreach (var item in itemSheet)
            {
                var name = item.Name.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                var nameLower = name.ToLowerInvariant();
                if (!nameLower.Contains("allagan tomestone")) continue;

                if (nameLower.Contains("poetics")) _poeticsItemId = item.RowId;
                else if (nameLower.Contains("mathematics")) _mathematicsItemId = item.RowId;
                else if (nameLower.Contains("mnemonics")) _mnemonicsItemId = item.RowId;
            }

            _log.Debug($"Tomestone IDs: Poetics={_poeticsItemId}, Math={_mathematicsItemId}, Mnem={_mnemonicsItemId}");
            _currencyIdsResolved = true;
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to resolve tomestone IDs: {ex.Message}");
        }
    }

    private void SubscribeToEvents()
    {
        _clientState.LevelChanged += OnLevelChanged;
        _clientState.ClassJobChanged += OnClassJobChanged;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _clientState.CfPop += OnDutyPop;

        _dutyState.DutyStarted += OnDutyStarted;
        _dutyState.DutyCompleted += OnDutyCompleted;
        _dutyState.DutyWiped += OnDutyWiped;
        
        _chatGui.ChatMessage += OnChatMessage;
    }

    private void UnsubscribeFromEvents()
    {
        _clientState.LevelChanged -= OnLevelChanged;
        _clientState.ClassJobChanged -= OnClassJobChanged;
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        _clientState.CfPop -= OnDutyPop;

        _dutyState.DutyStarted -= OnDutyStarted;
        _dutyState.DutyCompleted -= OnDutyCompleted;
        _dutyState.DutyWiped -= OnDutyWiped;
        
        _chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!_config.Notifications.OnPrivateMessage) return;
        
        if (type == XivChatType.TellIncoming)
        {
            var senderName = sender.TextValue;
            var messageText = message.TextValue;
            _config.AddLog($"Tell from {senderName}");
            _ = _telegram.SendMessageAsync($"📩 <b>Private Message</b>\nFrom: {senderName}\n\n{messageText}");
        }
    }

    private void OnLevelChanged(uint classJobId, uint level)
    {
        if (!_playerState.IsLoaded) return;

        var newLevel = _playerState.Level;
        CurrentLevel = newLevel;
        CurrentClassJob = GetClassJobName(classJobId);

        if (_config.Notifications.OnLevelUp && newLevel > _lastLevel)
        {
            var threshold = _config.Notifications.LevelUpThreshold;
            if (threshold == 0 || newLevel >= threshold)
            {
                _config.AddLog($"Level up: {CurrentClassJob} → {newLevel}");
                _ = _telegram.SendMessageAsync($"⬆️ <b>Level Up!</b>\n{CurrentClassJob}: Level {newLevel}");
            }
        }
        _lastLevel = newLevel;
    }

    private void OnClassJobChanged(uint classJobId)
    {
        CurrentClassJob = GetClassJobName(classJobId);
        if (_playerState.IsLoaded) CurrentLevel = _playerState.Level;
        
        if (!_config.Notifications.OnClassJobChange) return;

        _config.AddLog($"Class change: {CurrentClassJob}");
        _ = _telegram.SendMessageAsync($"🔄 <b>Class Changed</b>\nNow playing: {CurrentClassJob} (Lv. {CurrentLevel})");
    }

    private void OnTerritoryChanged(ushort territoryId)
    {
        var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        CurrentZone = territory?.PlaceName.ValueNullable?.Name.ToString() ?? $"Zone {territoryId}";
        
        if (!_config.Notifications.OnZoneChange) return;

        _config.AddLog($"Zone: {CurrentZone}");
        _ = _telegram.SendMessageAsync($"📍 <b>Zone Changed</b>\nNow in: {CurrentZone}");
    }

    private void OnDutyPop(ContentFinderCondition duty)
    {
        if (!_config.Notifications.OnDutyPop) return;

        var dutyName = duty.Name.ToString();
        _config.AddLog($"Duty pop: {dutyName}");
        _ = _telegram.SendMessageAsync($"🔔 <b>Duty Ready!</b>\n{dutyName}\n⏰ Queue popped!");
    }

    private void OnDutyStarted(object? sender, ushort territoryId)
    {
        if (!_config.Notifications.OnDutyStart) return;

        var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        var dutyName = territory?.ContentFinderCondition.ValueNullable?.Name.ToString() ?? $"Duty {territoryId}";
        _config.AddLog($"Duty start: {dutyName}");
        _ = _telegram.SendMessageAsync($"⚔️ <b>Duty Started</b>\n{dutyName}");
    }

    private void OnDutyCompleted(object? sender, ushort territoryId)
    {
        if (!_config.Notifications.OnDutyComplete) return;

        var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        var dutyName = territory?.ContentFinderCondition.ValueNullable?.Name.ToString() ?? $"Duty {territoryId}";
        _config.AddLog($"Duty complete: {dutyName}");
        _ = _telegram.SendMessageAsync($"✅ <b>Duty Completed!</b>\n{dutyName}");
    }

    private void OnDutyWiped(object? sender, ushort territoryId)
    {
        if (!_config.Notifications.OnDutyWipe) return;

        _config.AddLog("Party wipe");
        _ = _telegram.SendMessageAsync($"💀 <b>Party Wiped!</b>");
    }

    public unsafe void CheckCurrencyThresholds()
    {
        if (!_playerState.IsLoaded) return;

        ResolveTomestoneIds();

        var im = InventoryManager.Instance();
        if (im == null) return;

        // InventoryManager-tracked currencies (Gil, GC Seals, MGP, Tomestones)
        CurrentCurrencies["Gil"] = im->GetInventoryItemCount(1);

        if (_poeticsItemId > 0)
            CurrentCurrencies["Poetics"] = im->GetInventoryItemCount(_poeticsItemId);
        if (_mathematicsItemId > 0)
            CurrentCurrencies["Mathematics"] = im->GetInventoryItemCount(_mathematicsItemId);
        if (_mnemonicsItemId > 0)
            CurrentCurrencies["Mnemonics"] = im->GetInventoryItemCount(_mnemonicsItemId);

        CurrentCurrencies["MGP"] = im->GetInventoryItemCount(29);

        var gcSealId = GetGrandCompanySealId();
        if (gcSealId > 0)
            CurrentCurrencies["Company Seals"] = im->GetInventoryItemCount(gcSealId);

        // CurrencyManager-tracked currencies (scrips, beast tribe tokens, island, etc.)
        var cm = CurrencyManager.Instance();
        if (cm != null)
        {
            var itemSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (itemSheet != null)
            {
                ScanBucketSpecial(cm, itemSheet);
                ScanBucketItem(cm, itemSheet);
                ScanBucketContent(cm, itemSheet);
            }
        }

        // Check all configured thresholds
        foreach (var (name, current) in CurrentCurrencies)
        {
            var tracking = _config.Notifications.CurrencyThresholds.GetValueOrDefault(name);
            if (tracking is not { Enabled: true }) continue;

            if (_lastCurrencyValues.TryGetValue(name, out var last))
            {
                if (last < tracking.Threshold && current >= tracking.Threshold)
                {
                    var emoji = GetCurrencyEmoji(name);
                    _config.AddLog($"{name} threshold: {current:N0}");
                    _ = _telegram.SendMessageAsync($"{emoji} <b>{name} Threshold!</b>\nCurrent: {current:N0}");
                }
            }
        }

        foreach (var (name, amount) in CurrentCurrencies)
            _lastCurrencyValues[name] = amount;
    }

    private unsafe void ScanBucketSpecial(CurrencyManager* cm,
        Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item> itemSheet)
    {
        foreach (var (itemId, item) in cm->SpecialItemBucket)
        {
            var name = ResolveCurrencyDisplayName(itemId, itemSheet);
            if (name != null)
                CurrentCurrencies[name] = item.Count;
        }
    }

    private unsafe void ScanBucketItem(CurrencyManager* cm,
        Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item> itemSheet)
    {
        foreach (var (itemId, item) in cm->ItemBucket)
        {
            var name = ResolveCurrencyDisplayName(itemId, itemSheet);
            if (name != null)
                CurrentCurrencies[name] = item.Count;
        }
    }

    private unsafe void ScanBucketContent(CurrencyManager* cm,
        Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item> itemSheet)
    {
        foreach (var (itemId, item) in cm->ContentItemBucket)
        {
            var name = ResolveCurrencyDisplayName(itemId, itemSheet);
            if (name != null)
                CurrentCurrencies[name] = item.Count;
        }
    }

    private record CurrencyDef(string DisplayName, string Category);

    private static readonly Dictionary<string, CurrencyDef> CurrencyNameMap = new()
    {
        // Scrips
        { "white crafters' scrip", new("White Crafters' Scrip", "Scrips") },
        { "white gatherers' scrip", new("White Gatherers' Scrip", "Scrips") },
        { "purple crafters' scrip", new("Purple Crafters' Scrip", "Scrips") },
        { "purple gatherers' scrip", new("Purple Gatherers' Scrip", "Scrips") },
        { "orange crafters' scrip", new("Orange Crafters' Scrip", "Scrips") },
        { "orange gatherers' scrip", new("Orange Gatherers' Scrip", "Scrips") },
        { "skybuilders' scrip", new("Skybuilders' Scrip", "Scrips") },
        // Hunt
        { "allied seal", new("Allied Seals", "Hunt") },
        { "centurio seal", new("Centurio Seals", "Hunt") },
        { "sack of nuts", new("Sack of Nuts", "Hunt") },
        { "bicolor gemstone", new("Bicolor Gemstones", "Hunt") },
        // PvP
        { "wolf mark", new("Wolf Marks", "PvP") },
        { "trophy crystal", new("Trophy Crystals", "PvP") },
        // Beast Tribes (ARR)
        { "ixali oaknot", new("Ixali Oaknot", "Beast Tribes") },
        { "sylphic goldleaf", new("Sylphic Goldleaf", "Beast Tribes") },
        { "steel amalj'ok", new("Steel Amalj'ok", "Beast Tribes") },
        { "rainbowtide psashp", new("Rainbowtide Psashp", "Beast Tribes") },
        { "titan cobaltpiece", new("Titan Cobaltpiece", "Beast Tribes") },
        // Beast Tribes (HW)
        { "vanu whitebone", new("Vanu Whitebone", "Beast Tribes") },
        { "black copper gil", new("Black Copper Gil", "Beast Tribes") },
        { "carved kupo nut", new("Carved Kupo Nut", "Beast Tribes") },
        // Beast Tribes (SB)
        { "kojin sango", new("Kojin Sango", "Beast Tribes") },
        { "ananta dreamstaff", new("Ananta Dreamstaff", "Beast Tribes") },
        { "namazu koban", new("Namazu Koban", "Beast Tribes") },
        // Beast Tribes (ShB)
        { "fae fancy", new("Fae Fancy", "Beast Tribes") },
        { "qitari compliment", new("Qitari Compliment", "Beast Tribes") },
        { "hammered frogment", new("Hammered Frogment", "Beast Tribes") },
        // Beast Tribes (EW)
        { "arkasodara pana", new("Arkasodara Pana", "Beast Tribes") },
        { "omicron omnitoken", new("Omicron Omnitoken", "Beast Tribes") },
        { "loporrit carat", new("Loporrit Carat", "Beast Tribes") },
        // Beast Tribes (DT)
        { "pelu pelplume", new("Pelu Pelplume", "Beast Tribes") },
        { "yok huy ward", new("Yok Huy Ward", "Beast Tribes") },
        { "mamool ja nanook", new("Mamool Ja Nanook", "Beast Tribes") },
        // Field Operations
        { "bozjan cluster", new("Bozjan Clusters", "Field Operations") },
        { "enlightenment silver", new("Enlightenment Silver Pieces", "Field Operations") },
        { "enlightenment gold", new("Enlightenment Gold Pieces", "Field Operations") },
        // Island Sanctuary
        { "seafarer's cowrie", new("Seafarer's Cowrie", "Island Sanctuary") },
        { "islander's cowrie", new("Islander's Cowrie", "Island Sanctuary") },
        // Content
        { "faux leaf", new("Faux Leaves", "Content") },
        { "mgf", new("MGF", "Content") },
        { "sil'dihn silver", new("Sil'dihn Silver", "Content") },
        { "shishu coin", new("Shishu Coin", "Content") },
        { "aloalo coin", new("Aloalo Coin", "Content") },
        { "felicitous token", new("Felicitous Tokens", "Content") },
        { "cosmocredit", new("Cosmocredit", "Content") },
        { "lunar credit", new("Lunar Credit", "Content") },
        { "phaenna credit", new("Phaenna Credit", "Content") },
        { "oizys credit", new("Oizys Credit", "Content") },
        { "oizys dronebit", new("Oizys Dronebit", "Content") },
        { "corvosi manuscript", new("Corvosi Manuscript", "Content") },
        // Other
        { "venture", new("Ventures", "Other") },
        { "achievement certificate", new("Achievement Certificates", "Other") },
    };

    private string? ResolveCurrencyDisplayName(uint itemId,
        Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item> itemSheet)
    {
        var itemData = itemSheet.GetRow(itemId);
        var rawName = itemData.Name.ToString();
        if (string.IsNullOrEmpty(rawName)) return null;

        var lower = rawName.ToLowerInvariant();

        // Skip island sanctuary raw materials, farm items, and crafting tools
        if (lower.StartsWith("island ") || lower.StartsWith("isle") || lower.StartsWith("sanctuary ") ||
            lower.StartsWith("makeshift ") || lower.StartsWith("flawless ") || lower.StartsWith("raw island") ||
            lower.StartsWith("multicolored") || lower.StartsWith("premium island"))
        {
            if (!lower.Contains("cowrie"))
                return null;
        }

        foreach (var (fragment, def) in CurrencyNameMap)
        {
            if (lower.Contains(fragment))
            {
                CurrencyCategories[def.DisplayName] = def.Category;
                return def.DisplayName;
            }
        }

        // Unmapped currencies go to "Other"
        CurrencyCategories[rawName] = "Other";
        return rawName;
    }

    private static string GetCurrencyEmoji(string name) => name switch
    {
        "Gil" => "💰",
        "MGP" => "🎰",
        "Company Seals" => "🎖️",
        "Poetics" or "Mathematics" or "Mnemonics" => "📀",
        _ when name.Contains("Scrip") => "📜",
        _ when name.Contains("Seal") || name.Contains("Nuts") => "🏅",
        _ when name.Contains("Crystal") || name.Contains("Mark") => "⚔️",
        _ when name.Contains("Cowrie") => "🏝️",
        _ when name.Contains("Enlightenment") || name.Contains("Cluster") => "🗡️",
        _ => "📦",
    };

    private uint GetGrandCompanySealId()
    {
        if (!_playerState.GrandCompany.IsValid) return 0;
        return _playerState.GrandCompany.RowId switch
        {
            1 => 20,  // Storm Seals
            2 => 21,  // Serpent Seals
            3 => 22,  // Flame Seals
            _ => 0
        };
    }

    public void UpdateCurrentInfo()
    {
        if (!_playerState.IsLoaded) return;
        
        CurrentLevel = _playerState.Level;
        CurrentCommendations = _playerState.PlayerCommendations;

        var territoryId = _clientState.TerritoryType;
        if (territoryId > 0)
        {
            var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
            CurrentZone = territory?.PlaceName.ValueNullable?.Name.ToString() ?? $"Zone {territoryId}";
        }
    }

    public void CheckCommendations()
    {
        if (!_playerState.IsLoaded) return;
        
        CurrentCommendations = _playerState.PlayerCommendations;
        
        if (!_config.Notifications.OnCommendationReceived) return;
        if (CurrentCommendations > _lastCommendations && _lastCommendations > 0)
        {
            var gained = CurrentCommendations - _lastCommendations;
            _config.AddLog($"Commendations: +{gained}");
            _ = _telegram.SendMessageAsync($"👏 <b>Commendation{(gained > 1 ? "s" : "")} Received!</b>\n+{gained} (Total: {CurrentCommendations})");
        }
        _lastCommendations = CurrentCommendations;
    }

    public void CheckGCRank()
    {
        if (!_config.Notifications.OnGCRankUp || !_playerState.IsLoaded) return;
        if (!_playerState.GrandCompany.IsValid) return;

        var currentRank = _playerState.GetGrandCompanyRank(_playerState.GrandCompany.Value);
        if (currentRank > _lastGCRank && _lastGCRank > 0)
        {
            var gcName = _playerState.GrandCompany.Value.Name.ToString();
            _config.AddLog($"GC Rank up: {currentRank}");
            _ = _telegram.SendMessageAsync($"🎖️ <b>Grand Company Rank Up!</b>\n{gcName} - Rank {currentRank}");
        }
        _lastGCRank = currentRank;
    }

    public void CheckDeath()
    {
        if (!_config.Notifications.OnDeath) return;

        var inCombat = _condition[ConditionFlag.InCombat];
        var isDead = _condition[ConditionFlag.Unconscious];

        if (_wasInCombat && isDead)
        {
            _config.AddLog("Death");
            _ = _telegram.SendMessageAsync($"💀 <b>You Died!</b>");
        }
        _wasInCombat = inCombat;
    }

    private string GetClassJobName(uint classJobId)
    {
        var classJob = _dataManager.GetExcelSheet<ClassJob>()?.GetRow(classJobId);
        return classJob?.Name.ToString() ?? $"Job {classJobId}";
    }

    public void Dispose()
    {
        UnsubscribeFromEvents();
    }
}
