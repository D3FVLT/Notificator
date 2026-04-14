using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Notificator.Services;

namespace Notificator.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _config;
    private readonly TelegramService _telegram;

    private string _botToken = string.Empty;
    private string _chatId = string.Empty;
    private bool _testInProgress;
    private string _testResult = string.Empty;
    private bool _waitingForStart;
    private string _waitingStatus = string.Empty;

    public ConfigWindow(Configuration config, TelegramService telegram)
        : base("Notificator Settings##NotificatorConfig")
    {
        _config = config;
        _telegram = telegram;

        Size = new Vector2(500, 650);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoResize;

        _botToken = config.TelegramBotToken;
        _chatId = config.TelegramChatId;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("NotificatorTabs"))
        {
            if (ImGui.BeginTabItem("Telegram"))
            {
                DrawTelegramTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Level & XP"))
            {
                DrawLevelTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Currency"))
            {
                DrawCurrencyTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Duties"))
            {
                DrawDutyTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Other"))
            {
                DrawOtherTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Logs"))
            {
                DrawLogsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawTelegramTab()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Quick Setup:");
        ImGui.TextWrapped($"1. Open Telegram and find {TelegramService.BotUsername}");
        ImGui.TextWrapped("2. Send /start to the bot");
        ImGui.TextWrapped("3. Click 'Auto-detect Chat ID' below");
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Bot Token:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##BotToken", ref _botToken, 256))
        {
            _config.TelegramBotToken = _botToken;
            _config.Save();
        }
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Pre-filled with default bot. You can use your own.");

        ImGui.Spacing();

        ImGui.Text("Chat ID:");
        ImGui.SetNextItemWidth(-150);
        if (ImGui.InputText("##ChatId", ref _chatId, 64))
        {
            _config.TelegramChatId = _chatId;
            _config.Save();
        }
        ImGui.SameLine();
        
        ImGui.BeginDisabled(_waitingForStart);
        if (ImGui.Button(_waitingForStart ? "Waiting..." : "Auto-detect"))
        {
            _waitingForStart = true;
            _waitingStatus = "Send /start to the bot...";
            CheckForStartCommand();
        }
        ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_waitingStatus))
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), _waitingStatus);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var isConfigured = _telegram.IsConfigured;
        
        if (!isConfigured)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0f, 1f), "⚠ Telegram not configured");
        }
        else
        {
            ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), "✓ Telegram configured");
        }

        ImGui.Spacing();

        ImGui.BeginDisabled(!isConfigured || _testInProgress);
        if (ImGui.Button(_testInProgress ? "Testing..." : "Test Connection"))
        {
            TestConnection();
        }
        ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_testResult))
        {
            ImGui.SameLine();
            ImGui.TextColored(
                _testResult.StartsWith("Success") ? new Vector4(0f, 1f, 0f, 1f) : new Vector4(1f, 0f, 0f, 1f),
                _testResult);
        }
    }

    private async void CheckForStartCommand()
    {
        for (var i = 0; i < 30; i++)
        {
            var (success, chatId, username) = await _telegram.CheckForStartCommandAsync();
            if (success)
            {
                _chatId = chatId;
                _config.TelegramChatId = chatId;
                _config.Save();
                _waitingStatus = $"Found! Chat ID: {chatId} ({username})";
                _waitingForStart = false;
                
                await _telegram.SendMessageAsync($"✅ <b>Connected!</b>\nHello {username}! You will now receive FFXIV notifications here.");
                return;
            }
            await System.Threading.Tasks.Task.Delay(1000);
        }
        
        _waitingStatus = "Timeout. Try again.";
        _waitingForStart = false;
    }

    private void DrawLevelTab()
    {
        ImGui.TextWrapped("Configure notifications for level ups and experience.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var onLevelUp = _config.Notifications.OnLevelUp;
        if (ImGui.Checkbox("Notify on Level Up", ref onLevelUp))
        {
            _config.Notifications.OnLevelUp = onLevelUp;
            _config.Save();
        }

        if (onLevelUp)
        {
            ImGui.Indent();
            
            var threshold = _config.Notifications.LevelUpThreshold;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Minimum Level (0 = any)", ref threshold))
            {
                _config.Notifications.LevelUpThreshold = Math.Max(0, Math.Min(100, threshold));
                _config.Save();
            }
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Only notify when reaching this level or higher");
            
            ImGui.Unindent();
        }

        ImGui.Spacing();

        var onClassChange = _config.Notifications.OnClassJobChange;
        if (ImGui.Checkbox("Notify on Class/Job Change", ref onClassChange))
        {
            _config.Notifications.OnClassJobChange = onClassChange;
            _config.Save();
        }
    }

    private void DrawCurrencyTab()
    {
        ImGui.TextWrapped("Configure notifications when currency reaches certain thresholds.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Gil
        var onGil = _config.Notifications.OnGilThreshold;
        if (ImGui.Checkbox("Gil Threshold", ref onGil))
        {
            _config.Notifications.OnGilThreshold = onGil;
            _config.Save();
        }
        if (onGil)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            var gilThreshold = (int)_config.Notifications.GilThreshold;
            if (ImGui.InputInt("##GilThreshold", ref gilThreshold, 100000))
            {
                _config.Notifications.GilThreshold = Math.Max(0, gilThreshold);
                _config.Save();
            }
        }

        ImGui.Spacing();
        ImGui.Text("Tomestones:");
        ImGui.Indent();

        // Poetics
        var onPoetics = _config.Notifications.OnPoeticsThreshold;
        if (ImGui.Checkbox("Poetics", ref onPoetics))
        {
            _config.Notifications.OnPoeticsThreshold = onPoetics;
            _config.Save();
        }
        if (onPoetics)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var poeticsThreshold = _config.Notifications.PoeticsThreshold;
            if (ImGui.InputInt("##PoeticsThreshold", ref poeticsThreshold, 100))
            {
                _config.Notifications.PoeticsThreshold = Math.Max(0, Math.Min(2000, poeticsThreshold));
                _config.Save();
            }
        }

        // Mathematics (uncapped)
        var onMath = _config.Notifications.OnMathematicsThreshold;
        if (ImGui.Checkbox("Mathematics (uncapped)", ref onMath))
        {
            _config.Notifications.OnMathematicsThreshold = onMath;
            _config.Save();
        }
        if (onMath)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var mathThreshold = _config.Notifications.MathematicsThreshold;
            if (ImGui.InputInt("##MathThreshold", ref mathThreshold, 100))
            {
                _config.Notifications.MathematicsThreshold = Math.Max(0, Math.Min(2000, mathThreshold));
                _config.Save();
            }
        }

        // Mnemonics (capped)
        var onMnem = _config.Notifications.OnMnemonicsThreshold;
        if (ImGui.Checkbox("Mnemonics (capped)", ref onMnem))
        {
            _config.Notifications.OnMnemonicsThreshold = onMnem;
            _config.Save();
        }
        if (onMnem)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var mnemThreshold = _config.Notifications.MnemonicsThreshold;
            if (ImGui.InputInt("##MnemThreshold", ref mnemThreshold, 100))
            {
                _config.Notifications.MnemonicsThreshold = Math.Max(0, Math.Min(2000, mnemThreshold));
                _config.Save();
            }
        }

        ImGui.Unindent();
        ImGui.Spacing();

        // Company Seals
        var onSeals = _config.Notifications.OnCompanySealsThreshold;
        if (ImGui.Checkbox("Company Seals Threshold", ref onSeals))
        {
            _config.Notifications.OnCompanySealsThreshold = onSeals;
            _config.Save();
        }
        if (onSeals)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var sealThreshold = _config.Notifications.CompanySealsThreshold;
            if (ImGui.InputInt("##SealThreshold", ref sealThreshold, 10000))
            {
                _config.Notifications.CompanySealsThreshold = Math.Max(0, Math.Min(90000, sealThreshold));
                _config.Save();
            }
        }

        ImGui.Spacing();

        // MGP
        var onMGP = _config.Notifications.OnMGPThreshold;
        if (ImGui.Checkbox("MGP Threshold", ref onMGP))
        {
            _config.Notifications.OnMGPThreshold = onMGP;
            _config.Save();
        }
        if (onMGP)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var mgpThreshold = _config.Notifications.MGPThreshold;
            if (ImGui.InputInt("##MGPThreshold", ref mgpThreshold, 10000))
            {
                _config.Notifications.MGPThreshold = Math.Max(0, mgpThreshold);
                _config.Save();
            }
        }
    }

    private void DrawDutyTab()
    {
        ImGui.TextWrapped("Configure notifications for duty finder and dungeon events.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var onPop = _config.Notifications.OnDutyPop;
        if (ImGui.Checkbox("Duty Finder Pop", ref onPop))
        {
            _config.Notifications.OnDutyPop = onPop;
            _config.Save();
        }
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "When queue pops (useful when AFK)");

        ImGui.Spacing();

        var onStart = _config.Notifications.OnDutyStart;
        if (ImGui.Checkbox("Duty Start", ref onStart))
        {
            _config.Notifications.OnDutyStart = onStart;
            _config.Save();
        }

        var onComplete = _config.Notifications.OnDutyComplete;
        if (ImGui.Checkbox("Duty Complete", ref onComplete))
        {
            _config.Notifications.OnDutyComplete = onComplete;
            _config.Save();
        }

        var onWipe = _config.Notifications.OnDutyWipe;
        if (ImGui.Checkbox("Party Wipe", ref onWipe))
        {
            _config.Notifications.OnDutyWipe = onWipe;
            _config.Save();
        }
    }

    private void DrawOtherTab()
    {
        ImGui.TextWrapped("Configure other notification types.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Login/Logout:");
        
        var onLogin = _config.Notifications.OnLogin;
        if (ImGui.Checkbox("Login", ref onLogin))
        {
            _config.Notifications.OnLogin = onLogin;
            _config.Save();
        }

        ImGui.SameLine();

        var onLogout = _config.Notifications.OnLogout;
        if (ImGui.Checkbox("Logout", ref onLogout))
        {
            _config.Notifications.OnLogout = onLogout;
            _config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Zone & Combat:");

        var onZone = _config.Notifications.OnZoneChange;
        if (ImGui.Checkbox("Zone Change", ref onZone))
        {
            _config.Notifications.OnZoneChange = onZone;
            _config.Save();
        }

        var onDeath = _config.Notifications.OnDeath;
        if (ImGui.Checkbox("Death", ref onDeath))
        {
            _config.Notifications.OnDeath = onDeath;
            _config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Social:");

        var onComm = _config.Notifications.OnCommendationReceived;
        if (ImGui.Checkbox("Commendation Received", ref onComm))
        {
            _config.Notifications.OnCommendationReceived = onComm;
            _config.Save();
        }

        var onPM = _config.Notifications.OnPrivateMessage;
        if (ImGui.Checkbox("Private Message (Tell)", ref onPM))
        {
            _config.Notifications.OnPrivateMessage = onPM;
            _config.Save();
        }
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Forward incoming tells to Telegram");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Progression:");

        var onGC = _config.Notifications.OnGCRankUp;
        if (ImGui.Checkbox("Grand Company Rank Up", ref onGC))
        {
            _config.Notifications.OnGCRankUp = onGC;
            _config.Save();
        }
    }

    private void DrawLogsTab()
    {
        ImGui.TextWrapped("Recent notification activity.");
        ImGui.Spacing();
        
        if (ImGui.Button("Clear Logs"))
        {
            _config.RecentLogs.Clear();
        }
        
        ImGui.Separator();
        ImGui.Spacing();

        var height = ImGui.GetContentRegionAvail().Y;
        if (ImGui.BeginChild("LogsChild", new Vector2(-1, height), false))
        {
            if (_config.RecentLogs.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No logs yet. Activity will appear here.");
            }
            else
            {
                foreach (var log in _config.RecentLogs)
                {
                    ImGui.TextWrapped(log);
                }
            }
            ImGui.EndChild();
        }
    }

    private async void TestConnection()
    {
        _testInProgress = true;
        _testResult = string.Empty;

        try
        {
            var success = await _telegram.TestConnectionAsync();
            _testResult = success ? "Success!" : "Failed";
        }
        catch (Exception ex)
        {
            _testResult = $"Error: {ex.Message}";
        }
        finally
        {
            _testInProgress = false;
        }
    }

    public void Dispose() { }
}
