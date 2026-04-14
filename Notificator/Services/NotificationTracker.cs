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

        // Check configured thresholds
        CheckThreshold("Gil", _config.Notifications.OnGilThreshold, _config.Notifications.GilThreshold, "💰", "Gil");
        CheckThreshold("Poetics", _config.Notifications.OnPoeticsThreshold, _config.Notifications.PoeticsThreshold, "📀", "Poetics");
        CheckThreshold("Mathematics", _config.Notifications.OnMathematicsThreshold, _config.Notifications.MathematicsThreshold, "📀", "Mathematics");
        CheckThreshold("Mnemonics", _config.Notifications.OnMnemonicsThreshold, _config.Notifications.MnemonicsThreshold, "📀", "Mnemonics");
        CheckThreshold("Company Seals", _config.Notifications.OnCompanySealsThreshold, _config.Notifications.CompanySealsThreshold, "🎖️", "Company Seals");
        CheckThreshold("MGP", _config.Notifications.OnMGPThreshold, _config.Notifications.MGPThreshold, "🎰", "MGP");

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

    private static readonly Dictionary<string, string> CurrencyNameMap = new()
    {
        // Scrips
        { "white crafters' scrip", "White Crafters' Scrip" },
        { "white gatherers' scrip", "White Gatherers' Scrip" },
        { "purple crafters' scrip", "Purple Crafters' Scrip" },
        { "purple gatherers' scrip", "Purple Gatherers' Scrip" },
        { "orange crafters' scrip", "Orange Crafters' Scrip" },
        { "orange gatherers' scrip", "Orange Gatherers' Scrip" },
        // PvP
        { "wolf mark", "Wolf Marks" },
        { "trophy crystal", "Trophy Crystals" },
        // Hunt
        { "allied seal", "Allied Seals" },
        { "centurio seal", "Centurio Seals" },
        { "sack of nuts", "Sack of Nuts" },
        // Exploration
        { "bicolor gemstone", "Bicolor Gemstones" },
        { "skybuilders' scrip", "Skybuilders' Scrip" },
        // Occult Crescent
        { "enlightenment silver", "Enlightenment Silver Pieces" },
        { "enlightenment gold", "Enlightenment Gold Pieces" },
        // Eureka/Bozja
        { "bozjan cluster", "Bozjan Clusters" },
        // Island Sanctuary
        { "seafarer's cowrie", "Seafarer's Cowrie" },
        { "islander's cowrie", "Islander's Cowrie" },
        // Other notable
        { "venture", "Ventures" },
        { "faux leaf", "Faux Leaves" },
        { "mgf", "MGF" },
        { "felicitous token", "Felicitous Tokens" },
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

        foreach (var (fragment, displayName) in CurrencyNameMap)
        {
            if (lower.Contains(fragment))
                return displayName;
        }

        // Show unmapped currencies with their raw name
        return rawName;
    }

    private void CheckThreshold(string currencyName, bool enabled, long threshold, string emoji, string label)
    {
        if (!enabled) return;
        if (!CurrentCurrencies.TryGetValue(currencyName, out var current)) return;

        if (!_lastCurrencyValues.TryGetValue(currencyName, out var last))
        {
            _lastCurrencyValues[currencyName] = current;
            return;
        }

        if (last < threshold && current >= threshold)
        {
            _config.AddLog($"{label} threshold: {current:N0}");
            _ = _telegram.SendMessageAsync($"{emoji} <b>{label} Threshold!</b>\nCurrent: {current:N0}");
        }
    }

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
