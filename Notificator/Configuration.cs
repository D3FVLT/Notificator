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

    public bool UseProxy { get; set; } = false;
    public string ProxyAddress { get; set; } = "127.0.0.1";
    public int ProxyPort { get; set; } = 2080;
    public int ProxyType { get; set; } = 0; // 0=SOCKS5, 1=HTTP

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
public class CurrencyTracking
{
    public bool Enabled { get; set; }
    public long Threshold { get; set; }
}

[Serializable]
public class NotificationSettings
{
    // Level & Experience
    public bool OnLevelUp { get; set; } = true;
    public int LevelUpThreshold { get; set; } = 0;

    // Dynamic currency tracking: key = display name (e.g. "Gil", "Poetics")
    public Dictionary<string, CurrencyTracking> CurrencyThresholds { get; set; } = new();

    // Duty Events
    public bool OnDutyStart { get; set; } = false;
    public bool OnDutyComplete { get; set; } = true;
    public bool OnDutyWipe { get; set; } = false;
    public bool OnDutyPop { get; set; } = true;

    // Zone & Status
    public bool OnZoneChange { get; set; } = false;
    public bool OnClassJobChange { get; set; } = false;

    // Combat & Death
    public bool OnDeath { get; set; } = false;
    
    // Commendations
    public bool OnCommendationReceived { get; set; } = false;

    // Grand Company Rank
    public bool OnGCRankUp { get; set; } = false;
    
    // Private Messages
    public bool OnPrivateMessage { get; set; } = false;

    public CurrencyTracking GetCurrency(string name)
    {
        if (!CurrencyThresholds.TryGetValue(name, out var tracking))
        {
            tracking = new CurrencyTracking();
            CurrencyThresholds[name] = tracking;
        }
        return tracking;
    }
}
