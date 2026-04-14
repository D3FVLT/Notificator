using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace Notificator;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;

    public NotificationSettings Notifications { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

[Serializable]
public class NotificationSettings
{
    // Level & Experience
    public bool OnLevelUp { get; set; } = true;
    public int LevelUpThreshold { get; set; } = 0; // 0 = any level

    // Currency Thresholds
    public bool OnGilThreshold { get; set; } = false;
    public long GilThreshold { get; set; } = 1000000;

    public bool OnTomestonesThreshold { get; set; } = false;
    public int TomestonesThreshold { get; set; } = 2000;
    public TomestoneType TomestoneTypeToTrack { get; set; } = TomestoneType.Heliometry;

    public bool OnCompanySealsThreshold { get; set; } = false;
    public int CompanySealsThreshold { get; set; } = 80000;

    public bool OnMGPThreshold { get; set; } = false;
    public int MGPThreshold { get; set; } = 100000;

    // Duty Events
    public bool OnDutyStart { get; set; } = false;
    public bool OnDutyComplete { get; set; } = true;
    public bool OnDutyWipe { get; set; } = false;
    public bool OnDutyPop { get; set; } = true;

    // Zone & Status
    public bool OnZoneChange { get; set; } = false;
    public bool OnClassJobChange { get; set; } = false;
    public bool OnLogin { get; set; } = true;
    public bool OnLogout { get; set; } = false;

    // Combat & Death
    public bool OnDeath { get; set; } = false;
    
    // Commendations
    public bool OnCommendationReceived { get; set; } = false;

    // Grand Company Rank
    public bool OnGCRankUp { get; set; } = false;

    // Retainer Ventures
    public bool OnRetainerVentureComplete { get; set; } = false;

    // Party
    public bool OnPartyInvite { get; set; } = false;

    // Custom Thresholds per Currency
    public Dictionary<uint, long> CustomCurrencyThresholds { get; set; } = new();
}

public enum TomestoneType
{
    Poetics = 28,
    Heliometry = 46,
    // Add more as needed
}
