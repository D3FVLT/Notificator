using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
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

    private short _lastLevel;
    private short _lastCommendations;
    private byte _lastGCRank;
    private readonly Dictionary<uint, long> _lastCurrencyValues = new();
    private bool _wasInCombat;

    public NotificationTracker(
        Configuration config,
        TelegramService telegram,
        IPluginLog log,
        IClientState clientState,
        IPlayerState playerState,
        IDutyState dutyState,
        ICondition condition,
        IDataManager dataManager)
    {
        _config = config;
        _telegram = telegram;
        _log = log;
        _clientState = clientState;
        _playerState = playerState;
        _dutyState = dutyState;
        _condition = condition;
        _dataManager = dataManager;

        SubscribeToEvents();
        InitializeTracking();
    }

    private void InitializeTracking()
    {
        if (_playerState.IsLoaded)
        {
            _lastLevel = _playerState.Level;
            _lastCommendations = _playerState.PlayerCommendations;
            if (_playerState.GrandCompany.IsValid)
            {
                _lastGCRank = _playerState.GetGrandCompanyRank(_playerState.GrandCompany.Value);
            }
            UpdateCurrencySnapshot();
        }
    }

    private void SubscribeToEvents()
    {
        _clientState.LevelChanged += OnLevelChanged;
        _clientState.ClassJobChanged += OnClassJobChanged;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        _clientState.CfPop += OnDutyPop;
        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;

        _dutyState.DutyStarted += OnDutyStarted;
        _dutyState.DutyCompleted += OnDutyCompleted;
        _dutyState.DutyWiped += OnDutyWiped;
    }

    private void UnsubscribeFromEvents()
    {
        _clientState.LevelChanged -= OnLevelChanged;
        _clientState.ClassJobChanged -= OnClassJobChanged;
        _clientState.TerritoryChanged -= OnTerritoryChanged;
        _clientState.CfPop -= OnDutyPop;
        _clientState.Login -= OnLogin;
        _clientState.Logout -= OnLogout;

        _dutyState.DutyStarted -= OnDutyStarted;
        _dutyState.DutyCompleted -= OnDutyCompleted;
        _dutyState.DutyWiped -= OnDutyWiped;
    }

    private void OnLogin(object? sender, EventArgs e)
    {
        InitializeTracking();
        if (_config.Notifications.OnLogin && _playerState.IsLoaded)
        {
            var charName = _playerState.CharacterName;
            var world = _playerState.CurrentWorld.IsValid ? _playerState.CurrentWorld.Value.Name.ToString() : "Unknown";
            _ = _telegram.SendMessageAsync($"🎮 <b>Logged in!</b>\n{charName} @ {world}");
        }
    }

    private void OnLogout(object? sender, int type)
    {
        if (_config.Notifications.OnLogout)
        {
            _ = _telegram.SendMessageAsync("👋 <b>Logged out from FFXIV</b>");
        }
    }

    private void OnLevelChanged(uint classJobId)
    {
        if (!_config.Notifications.OnLevelUp || !_playerState.IsLoaded) return;

        var newLevel = _playerState.Level;
        if (newLevel > _lastLevel)
        {
            var threshold = _config.Notifications.LevelUpThreshold;
            if (threshold == 0 || newLevel >= threshold)
            {
                var classJob = GetClassJobName(classJobId);
                _ = _telegram.SendMessageAsync($"⬆️ <b>Level Up!</b>\n{classJob}: Level {newLevel}");
            }
        }
        _lastLevel = newLevel;
    }

    private void OnClassJobChanged(uint classJobId)
    {
        if (!_config.Notifications.OnClassJobChange) return;

        var classJob = GetClassJobName(classJobId);
        var level = _playerState.Level;
        _ = _telegram.SendMessageAsync($"🔄 <b>Class Changed</b>\nNow playing: {classJob} (Lv. {level})");
    }

    private void OnTerritoryChanged(ushort territoryId)
    {
        if (!_config.Notifications.OnZoneChange) return;

        var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        var zoneName = territory?.PlaceName.ValueNullable?.Name.ToString() ?? $"Zone {territoryId}";
        _ = _telegram.SendMessageAsync($"📍 <b>Zone Changed</b>\nNow in: {zoneName}");
    }

    private void OnDutyPop(ContentFinderCondition duty)
    {
        if (!_config.Notifications.OnDutyPop) return;

        var dutyName = duty.Name.ToString();
        _ = _telegram.SendMessageAsync($"🔔 <b>Duty Ready!</b>\n{dutyName}\n⏰ Queue popped!");
    }

    private void OnDutyStarted(object? sender, ushort territoryId)
    {
        if (!_config.Notifications.OnDutyStart) return;

        var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        var dutyName = territory?.ContentFinderCondition.ValueNullable?.Name.ToString() ?? $"Duty {territoryId}";
        _ = _telegram.SendMessageAsync($"⚔️ <b>Duty Started</b>\n{dutyName}");
    }

    private void OnDutyCompleted(object? sender, ushort territoryId)
    {
        if (!_config.Notifications.OnDutyComplete) return;

        var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        var dutyName = territory?.ContentFinderCondition.ValueNullable?.Name.ToString() ?? $"Duty {territoryId}";
        _ = _telegram.SendMessageAsync($"✅ <b>Duty Completed!</b>\n{dutyName}");
    }

    private void OnDutyWiped(object? sender, ushort territoryId)
    {
        if (!_config.Notifications.OnDutyWipe) return;

        _ = _telegram.SendMessageAsync($"💀 <b>Party Wiped!</b>");
    }

    public unsafe void CheckCurrencyThresholds()
    {
        if (!_playerState.IsLoaded) return;

        var currencyManager = CurrencyManager.Instance();
        if (currencyManager == null) return;

        // Gil
        if (_config.Notifications.OnGilThreshold)
        {
            var gil = GetCurrencyAmount(1); // Gil Item ID
            if (ShouldNotifyThreshold(1, gil, _config.Notifications.GilThreshold))
            {
                _ = _telegram.SendMessageAsync($"💰 <b>Gil Threshold Reached!</b>\nCurrent: {gil:N0} gil");
            }
        }

        // Tomestones
        if (_config.Notifications.OnTomestonesThreshold)
        {
            var tomestoneId = GetTomestoneItemId(_config.Notifications.TomestoneTypeToTrack);
            var tomestones = GetSpecialCurrencyAmount(tomestoneId);
            if (ShouldNotifyThreshold(tomestoneId, tomestones, _config.Notifications.TomestonesThreshold))
            {
                _ = _telegram.SendMessageAsync($"📀 <b>Tomestone Threshold Reached!</b>\nCurrent: {tomestones:N0}");
            }
        }

        // Company Seals
        if (_config.Notifications.OnCompanySealsThreshold)
        {
            var gcId = GetGrandCompanySealId();
            if (gcId > 0)
            {
                var seals = GetSpecialCurrencyAmount(gcId);
                if (ShouldNotifyThreshold(gcId, seals, _config.Notifications.CompanySealsThreshold))
                {
                    _ = _telegram.SendMessageAsync($"🎖️ <b>Company Seals Threshold!</b>\nCurrent: {seals:N0}");
                }
            }
        }

        // MGP
        if (_config.Notifications.OnMGPThreshold)
        {
            var mgp = GetSpecialCurrencyAmount(29); // MGP Special ID
            if (ShouldNotifyThreshold(10029, mgp, _config.Notifications.MGPThreshold))
            {
                _ = _telegram.SendMessageAsync($"🎰 <b>MGP Threshold Reached!</b>\nCurrent: {mgp:N0}");
            }
        }

        UpdateCurrencySnapshot();
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

    private unsafe long GetSpecialCurrencyAmount(uint specialId)
    {
        var currencyManager = CurrencyManager.Instance();
        if (currencyManager == null) return 0;

        foreach (var (itemId, item) in currencyManager->SpecialItemBucket)
        {
            if (item.SpecialId == specialId)
            {
                return item.Count;
            }
        }

        return 0;
    }

    private uint GetTomestoneItemId(TomestoneType type)
    {
        return type switch
        {
            TomestoneType.Poetics => 28,
            TomestoneType.Heliometry => 46,
            _ => 28
        };
    }

    private uint GetGrandCompanySealId()
    {
        if (!_playerState.GrandCompany.IsValid) return 0;
        
        var gcId = _playerState.GrandCompany.RowId;
        return gcId switch
        {
            1 => 20, // Maelstrom
            2 => 21, // Twin Adder
            3 => 22, // Immortal Flames
            _ => 0
        };
    }

    private void UpdateCurrencySnapshot()
    {
        _lastCurrencyValues[1] = GetCurrencyAmount(1); // Gil
        _lastCurrencyValues[GetTomestoneItemId(_config.Notifications.TomestoneTypeToTrack)] = 
            GetSpecialCurrencyAmount(GetTomestoneItemId(_config.Notifications.TomestoneTypeToTrack));
        
        var gcId = GetGrandCompanySealId();
        if (gcId > 0)
        {
            _lastCurrencyValues[gcId] = GetSpecialCurrencyAmount(gcId);
        }
        
        _lastCurrencyValues[10029] = GetSpecialCurrencyAmount(29); // MGP
    }

    public void CheckCommendations()
    {
        if (!_config.Notifications.OnCommendationReceived || !_playerState.IsLoaded) return;

        var currentComms = _playerState.PlayerCommendations;
        if (currentComms > _lastCommendations)
        {
            var gained = currentComms - _lastCommendations;
            _ = _telegram.SendMessageAsync($"👏 <b>Commendation{(gained > 1 ? "s" : "")} Received!</b>\n+{gained} (Total: {currentComms})");
        }
        _lastCommendations = currentComms;
    }

    public void CheckGCRank()
    {
        if (!_config.Notifications.OnGCRankUp || !_playerState.IsLoaded) return;
        if (!_playerState.GrandCompany.IsValid) return;

        var currentRank = _playerState.GetGrandCompanyRank(_playerState.GrandCompany.Value);
        if (currentRank > _lastGCRank)
        {
            var gcName = _playerState.GrandCompany.Value.Name.ToString();
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
