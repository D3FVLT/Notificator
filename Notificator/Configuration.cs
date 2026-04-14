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
    
    public List<string> RecentLogs { get; set; } = new();
    public int MaxLogEntries { get; set; } = 100;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
    
    public void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        RecentLogs.Insert(0, $"[{timestamp}] {message}");
        if (RecentLogs.Count > MaxLogEntries)
        {
            RecentLogs.RemoveAt(RecentLogs.Count - 1);
        }
    }
}

[Serializable]
public class NotificationSettings
{
    // Level & Experience
    public bool OnLevelUp { get; set; } = true;
    public int LevelUpThreshold { get; set; } = 0;

    // Currency Thresholds
    public bool OnGilThreshold { get; set; } = false;
    public long GilThreshold { get; set; } = 1000000;

    // Tomestones - separate tracking
    public bool OnPoeticsThreshold { get; set; } = false;
    public int PoeticsThreshold { get; set; } = 1800;
    
    public bool OnMathematicsThreshold { get; set; } = false;
    public int MathematicsThreshold { get; set; } = 1800;
    
    public bool OnMnemonicsThreshold { get; set; } = false;
    public int MnemonicsThreshold { get; set; } = 1800;

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
    
    // Private Messages
    public bool OnPrivateMessage { get; set; } = false;

    // Custom Thresholds per Currency
    public Dictionary<uint, long> CustomCurrencyThresholds { get; set; } = new();
}
