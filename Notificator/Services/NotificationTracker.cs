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
    private readonly Dictionary<uint, long> _lastCurrencyValues = new();
    private bool _wasInCombat;

    // Tomestone item IDs change with patches; we resolve them dynamically from TomestonesItem sheet
    private uint _poeticsItemId;
    private uint _mathematicsItemId;
    private uint _mnemonicsItemId;
    private uint _mgpItemId;

    // Current values exposed for UI
    public long CurrentGil { get; private set; }
    public long CurrentPoetics { get; private set; }
    public long CurrentMathematics { get; private set; }
    public long CurrentMnemonics { get; private set; }
    public long CurrentCompanySeals { get; private set; }
    public long CurrentMGP { get; private set; }
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

        ResolveCurrencyIds();
        SubscribeToEvents();
        InitializeTracking();
    }

    private void ResolveCurrencyIds()
    {
        // Scan SpecialItemBucket to find currency item IDs by matching names
        // We do this once to avoid hardcoding IDs that change between patches
        _poeticsItemId = 0;
        _mathematicsItemId = 0;
        _mnemonicsItemId = 0;
        _mgpItemId = 0;

        try
        {
            var itemSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (itemSheet == null) return;

            unsafe
            {
                var cm = CurrencyManager.Instance();
                if (cm == null) return;

                foreach (var (itemId, _) in cm->SpecialItemBucket)
                {
                    var item = itemSheet.GetRow(itemId);
                    var name = item.Name.ToString().ToLowerInvariant();
                    
                    if (name.Contains("poetics")) _poeticsItemId = itemId;
                    else if (name.Contains("mathematics")) _mathematicsItemId = itemId;
                    else if (name.Contains("mnemonics")) _mnemonicsItemId = itemId;
                    else if (name.Contains("gold saucer") || name.Contains("mgp")) _mgpItemId = itemId;
                }
            }
            
            _log.Debug($"Currency IDs resolved: Poetics={_poeticsItemId}, Math={_mathematicsItemId}, Mnem={_mnemonicsItemId}, MGP={_mgpItemId}");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to resolve currency IDs: {ex.Message}");
        }
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
            UpdateCurrencySnapshot();
            UpdateCurrentInfo();
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

        var currencyManager = CurrencyManager.Instance();
        if (currencyManager == null) return;

        if (_poeticsItemId == 0) ResolveCurrencyIds();
        
        CurrentGil = GetCurrencyAmount(1);
        CurrentPoetics = _poeticsItemId > 0 ? GetCurrencyByItemId(_poeticsItemId) : 0;
        CurrentMathematics = _mathematicsItemId > 0 ? GetCurrencyByItemId(_mathematicsItemId) : 0;
        CurrentMnemonics = _mnemonicsItemId > 0 ? GetCurrencyByItemId(_mnemonicsItemId) : 0;
        CurrentMGP = _mgpItemId > 0 ? GetCurrencyByItemId(_mgpItemId) : GetCurrencyAmount(29);

        var gcId = GetGrandCompanySealId();
        if (gcId > 0) CurrentCompanySeals = GetCurrencyByItemId(gcId);

        if (_config.Notifications.OnGilThreshold)
        {
            if (ShouldNotifyThreshold(1, CurrentGil, _config.Notifications.GilThreshold))
            {
                _config.AddLog($"Gil threshold: {CurrentGil:N0}");
                _ = _telegram.SendMessageAsync($"💰 <b>Gil Threshold Reached!</b>\nCurrent: {CurrentGil:N0} gil");
            }
        }

        if (_config.Notifications.OnPoeticsThreshold && _poeticsItemId > 0)
        {
            if (ShouldNotifyThreshold(_poeticsItemId, CurrentPoetics, _config.Notifications.PoeticsThreshold))
            {
                _config.AddLog($"Poetics threshold: {CurrentPoetics:N0}");
                _ = _telegram.SendMessageAsync($"📀 <b>Poetics Threshold!</b>\nCurrent: {CurrentPoetics:N0}/2000");
            }
        }

        if (_config.Notifications.OnMathematicsThreshold && _mathematicsItemId > 0)
        {
            if (ShouldNotifyThreshold(_mathematicsItemId, CurrentMathematics, _config.Notifications.MathematicsThreshold))
            {
                _config.AddLog($"Mathematics threshold: {CurrentMathematics:N0}");
                _ = _telegram.SendMessageAsync($"📀 <b>Mathematics Threshold!</b>\nCurrent: {CurrentMathematics:N0}");
            }
        }

        if (_config.Notifications.OnMnemonicsThreshold && _mnemonicsItemId > 0)
        {
            if (ShouldNotifyThreshold(_mnemonicsItemId, CurrentMnemonics, _config.Notifications.MnemonicsThreshold))
            {
                _config.AddLog($"Mnemonics threshold: {CurrentMnemonics:N0}");
                _ = _telegram.SendMessageAsync($"📀 <b>Mnemonics Threshold!</b>\nCurrent: {CurrentMnemonics:N0}/2000");
            }
        }

        if (_config.Notifications.OnCompanySealsThreshold && gcId > 0)
        {
            if (ShouldNotifyThreshold(gcId, CurrentCompanySeals, _config.Notifications.CompanySealsThreshold))
            {
                _config.AddLog($"Seals threshold: {CurrentCompanySeals:N0}");
                _ = _telegram.SendMessageAsync($"🎖️ <b>Company Seals Threshold!</b>\nCurrent: {CurrentCompanySeals:N0}");
            }
        }

        if (_config.Notifications.OnMGPThreshold)
        {
            var mgpKey = _mgpItemId > 0 ? _mgpItemId : 29u;
            if (ShouldNotifyThreshold(mgpKey, CurrentMGP, _config.Notifications.MGPThreshold))
            {
                _config.AddLog($"MGP threshold: {CurrentMGP:N0}");
                _ = _telegram.SendMessageAsync($"🎰 <b>MGP Threshold Reached!</b>\nCurrent: {CurrentMGP:N0}");
            }
        }

        UpdateCurrencySnapshot();
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

    private bool ShouldNotifyThreshold(uint currencyId, long currentAmount, long threshold)
    {
        if (!_lastCurrencyValues.TryGetValue(currencyId, out var lastAmount))
        {
            _lastCurrencyValues[currencyId] = currentAmount;
            return false;
        }

        return lastAmount < threshold && currentAmount >= threshold;
    }

    private unsafe long GetCurrencyAmount(uint itemId)
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null) return 0;
        return inventoryManager->GetInventoryItemCount(itemId);
    }

    private unsafe long GetCurrencyByItemId(uint targetItemId)
    {
        var currencyManager = CurrencyManager.Instance();
        if (currencyManager == null) return 0;

        foreach (var (itemId, item) in currencyManager->SpecialItemBucket)
        {
            if (itemId == targetItemId)
                return item.Count;
        }

        foreach (var (itemId, item) in currencyManager->ItemBucket)
        {
            if (itemId == targetItemId)
                return item.Count;
        }

        foreach (var (itemId, item) in currencyManager->ContentItemBucket)
        {
            if (itemId == targetItemId)
                return item.Count;
        }

        return 0;
    }

    private uint GetGrandCompanySealId()
    {
        if (!_playerState.GrandCompany.IsValid) return 0;
        // Storm/Serpent/Flame Seals item IDs
        return _playerState.GrandCompany.RowId switch
        {
            1 => 20,  // Storm Seals
            2 => 21,  // Serpent Seals
            3 => 22,  // Flame Seals
            _ => 0
        };
    }

    private void UpdateCurrencySnapshot()
    {
        _lastCurrencyValues[1] = CurrentGil;
        if (_poeticsItemId > 0) _lastCurrencyValues[_poeticsItemId] = CurrentPoetics;
        if (_mathematicsItemId > 0) _lastCurrencyValues[_mathematicsItemId] = CurrentMathematics;
        if (_mnemonicsItemId > 0) _lastCurrencyValues[_mnemonicsItemId] = CurrentMnemonics;
        
        var gcId = GetGrandCompanySealId();
        if (gcId > 0) _lastCurrencyValues[gcId] = CurrentCompanySeals;
        
        var mgpKey = _mgpItemId > 0 ? _mgpItemId : 29u;
        _lastCurrencyValues[mgpKey] = CurrentMGP;
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
