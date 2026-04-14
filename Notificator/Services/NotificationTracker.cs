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

    // Tomestone special IDs (from CurrencyManager)
    private const uint PoeticsSpecialId = 28;
    private const uint MathematicsSpecialId = 50;  // Current uncapped
    private const uint MnemonicsSpecialId = 51;    // Current capped

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
        
        _chatGui.ChatMessage += OnChatMessage;
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
        
        _chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!_config.Notifications.OnPrivateMessage) return;
        
        if (type == XivChatType.TellIncoming)
        {
            var senderName = sender.TextValue;
            var messageText = message.TextValue;
            _config.AddLog($"📩 Tell from {senderName}");
            _ = _telegram.SendMessageAsync($"📩 <b>Private Message</b>\nFrom: {senderName}\n\n{messageText}");
        }
    }

    private void OnLogin()
    {
        InitializeTracking();
        if (_config.Notifications.OnLogin && _playerState.IsLoaded)
        {
            var charName = _playerState.CharacterName;
            var world = _playerState.CurrentWorld.IsValid ? _playerState.CurrentWorld.Value.Name.ToString() : "Unknown";
            _config.AddLog($"Login: {charName} @ {world}");
            _ = _telegram.SendMessageAsync($"🎮 <b>Logged in!</b>\n{charName} @ {world}");
        }
    }

    private void OnLogout(int type, int code)
    {
        if (_config.Notifications.OnLogout)
        {
            _config.AddLog("Logout");
            _ = _telegram.SendMessageAsync("👋 <b>Logged out from FFXIV</b>");
        }
    }

    private void OnLevelChanged(uint classJobId, uint level)
    {
        if (!_config.Notifications.OnLevelUp || !_playerState.IsLoaded) return;

        var newLevel = _playerState.Level;
        if (newLevel > _lastLevel)
        {
            var threshold = _config.Notifications.LevelUpThreshold;
            if (threshold == 0 || newLevel >= threshold)
            {
                var classJob = GetClassJobName(classJobId);
                _config.AddLog($"Level up: {classJob} → {newLevel}");
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
        _config.AddLog($"Class change: {classJob}");
        _ = _telegram.SendMessageAsync($"🔄 <b>Class Changed</b>\nNow playing: {classJob} (Lv. {level})");
    }

    private void OnTerritoryChanged(ushort territoryId)
    {
        if (!_config.Notifications.OnZoneChange) return;

        var territory = _dataManager.GetExcelSheet<TerritoryType>()?.GetRow(territoryId);
        var zoneName = territory?.PlaceName.ValueNullable?.Name.ToString() ?? $"Zone {territoryId}";
        _config.AddLog($"Zone: {zoneName}");
        _ = _telegram.SendMessageAsync($"📍 <b>Zone Changed</b>\nNow in: {zoneName}");
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

        // Gil
        if (_config.Notifications.OnGilThreshold)
        {
            var gil = GetCurrencyAmount(1);
            if (ShouldNotifyThreshold(1, gil, _config.Notifications.GilThreshold))
            {
                _config.AddLog($"Gil threshold: {gil:N0}");
                _ = _telegram.SendMessageAsync($"💰 <b>Gil Threshold Reached!</b>\nCurrent: {gil:N0} gil");
            }
        }

        // Poetics
        if (_config.Notifications.OnPoeticsThreshold)
        {
            var poetics = GetSpecialCurrencyAmount(PoeticsSpecialId);
            if (ShouldNotifyThreshold(PoeticsSpecialId, poetics, _config.Notifications.PoeticsThreshold))
            {
                _config.AddLog($"Poetics threshold: {poetics:N0}");
                _ = _telegram.SendMessageAsync($"📀 <b>Poetics Threshold!</b>\nCurrent: {poetics:N0}/2000");
            }
        }

        // Mathematics (uncapped)
        if (_config.Notifications.OnMathematicsThreshold)
        {
            var math = GetSpecialCurrencyAmount(MathematicsSpecialId);
            if (ShouldNotifyThreshold(MathematicsSpecialId, math, _config.Notifications.MathematicsThreshold))
            {
                _config.AddLog($"Mathematics threshold: {math:N0}");
                _ = _telegram.SendMessageAsync($"📀 <b>Mathematics Threshold!</b>\nCurrent: {math:N0}/2000");
            }
        }

        // Mnemonics (capped)
        if (_config.Notifications.OnMnemonicsThreshold)
        {
            var mnem = GetSpecialCurrencyAmount(MnemonicsSpecialId);
            if (ShouldNotifyThreshold(MnemonicsSpecialId, mnem, _config.Notifications.MnemonicsThreshold))
            {
                _config.AddLog($"Mnemonics threshold: {mnem:N0}");
                _ = _telegram.SendMessageAsync($"📀 <b>Mnemonics Threshold!</b>\nCurrent: {mnem:N0}/2000");
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
                    _config.AddLog($"Seals threshold: {seals:N0}");
                    _ = _telegram.SendMessageAsync($"🎖️ <b>Company Seals Threshold!</b>\nCurrent: {seals:N0}");
                }
            }
        }

        // MGP
        if (_config.Notifications.OnMGPThreshold)
        {
            var mgp = GetSpecialCurrencyAmount(29);
            if (ShouldNotifyThreshold(10029, mgp, _config.Notifications.MGPThreshold))
            {
                _config.AddLog($"MGP threshold: {mgp:N0}");
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
        _lastCurrencyValues[PoeticsSpecialId] = GetSpecialCurrencyAmount(PoeticsSpecialId);
        _lastCurrencyValues[MathematicsSpecialId] = GetSpecialCurrencyAmount(MathematicsSpecialId);
        _lastCurrencyValues[MnemonicsSpecialId] = GetSpecialCurrencyAmount(MnemonicsSpecialId);
        
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
            _config.AddLog($"Commendations: +{gained}");
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
