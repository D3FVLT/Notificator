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
    private readonly NotificationTracker _tracker;

    private string _botToken = string.Empty;
    private string _chatId = string.Empty;
    private bool _testInProgress;
    private string _testResult = string.Empty;
    private bool _waitingForStart;
    private string _waitingStatus = string.Empty;

    private static readonly Vector4 ColorGreen = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 ColorYellow = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 ColorRed = new(1f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 ColorGray = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorBlue = new(0.4f, 0.7f, 1f, 1f);
    private static readonly Vector4 ColorWhite = new(1f, 1f, 1f, 1f);

    public ConfigWindow(Configuration config, TelegramService telegram, NotificationTracker tracker)
        : base("Notificator##NotificatorConfig")
    {
        _config = config;
        _telegram = telegram;
        _tracker = tracker;

        Size = new Vector2(520, 680);
        SizeCondition = ImGuiCond.FirstUseEver;

        _botToken = config.TelegramBotToken;
        _chatId = config.TelegramChatId;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("NotificatorTabs"))
        {
            if (ImGui.BeginTabItem("Setup"))
            {
                DrawTelegramTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Tracking"))
            {
                DrawTrackingTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Duties"))
            {
                DrawDutyTab();
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
        DrawSection("Quick Setup", () =>
        {
            ImGui.TextColored(ColorBlue, "1.");
            ImGui.SameLine();
            ImGui.TextWrapped("Create a bot via @BotFather in Telegram");
            
            ImGui.TextColored(ColorBlue, "2.");
            ImGui.SameLine();
            ImGui.TextWrapped("Paste the bot token below");
            
            ImGui.TextColored(ColorBlue, "3.");
            ImGui.SameLine();
            ImGui.TextWrapped("Send /start to your bot in Telegram");
            
            ImGui.TextColored(ColorBlue, "4.");
            ImGui.SameLine();
            ImGui.TextWrapped("Click Auto-detect to grab your Chat ID");
        });

        ImGui.Spacing();

        DrawSection("Bot Token", () =>
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##BotToken", ref _botToken, 256, ImGuiInputTextFlags.Password))
            {
                _config.TelegramBotToken = _botToken;
                _config.Save();
            }
        });

        DrawSection("Chat ID", () =>
        {
            ImGui.SetNextItemWidth(-120);
            if (ImGui.InputText("##ChatId", ref _chatId, 64))
            {
                _config.TelegramChatId = _chatId;
                _config.Save();
            }
            ImGui.SameLine();
            
            ImGui.BeginDisabled(_waitingForStart);
            if (ImGui.Button(_waitingForStart ? "Waiting..." : "Auto-detect", new Vector2(110, 0)))
            {
                _waitingForStart = true;
                _waitingStatus = "Send /start to the bot...";
                CheckForStartCommand();
            }
            ImGui.EndDisabled();
            
            if (!string.IsNullOrEmpty(_waitingStatus))
            {
                ImGui.TextColored(ColorYellow, _waitingStatus);
            }
        });

        ImGui.Spacing();

        var isConfigured = _telegram.IsConfigured;
        
        if (isConfigured)
        {
            ImGui.TextColored(ColorGreen, "Connected");
        }
        else
        {
            ImGui.TextColored(ColorRed, "Not configured — fill in both fields above");
        }

        ImGui.Spacing();

        ImGui.BeginDisabled(!isConfigured || _testInProgress);
        if (ImGui.Button(_testInProgress ? "Sending..." : "Send Test Message", new Vector2(180, 0)))
        {
            TestConnection();
        }
        ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_testResult))
        {
            ImGui.SameLine();
            ImGui.TextColored(
                _testResult.StartsWith("Success") ? ColorGreen : ColorRed,
                _testResult);
        }
    }

    private void DrawTrackingTab()
    {
        // Status overview
        DrawSection("Current Status", () =>
        {
            DrawStatusRow("Class/Job", _tracker.CurrentClassJob, $"Lv. {_tracker.CurrentLevel}");
            DrawStatusRow("Zone", _tracker.CurrentZone, "");
            DrawStatusRow("Commendations", _tracker.CurrentCommendations.ToString(), "");
        });

        ImGui.Spacing();

        // Level
        DrawSection("Level", () =>
        {
            var onLevelUp = _config.Notifications.OnLevelUp;
            if (ImGui.Checkbox("Notify on Level Up", ref onLevelUp))
            {
                _config.Notifications.OnLevelUp = onLevelUp;
                _config.Save();
            }

            if (onLevelUp)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                var threshold = _config.Notifications.LevelUpThreshold;
                if (ImGui.InputInt("##LvlMin", ref threshold))
                {
                    _config.Notifications.LevelUpThreshold = Math.Max(0, Math.Min(100, threshold));
                    _config.Save();
                }
                ImGui.SameLine();
                ImGui.TextColored(ColorGray, "min level (0=any)");
            }

            var onClassChange = _config.Notifications.OnClassJobChange;
            if (ImGui.Checkbox("Notify on Class/Job Change", ref onClassChange))
            {
                _config.Notifications.OnClassJobChange = onClassChange;
                _config.Save();
            }
        });

        ImGui.Spacing();

        // Currencies
        DrawSection("Currencies", () =>
        {
            DrawCurrencyRow("Gil", _tracker.CurrentGil, -1,
                ref _config.Notifications.OnGilThreshold, ref _config.Notifications.GilThreshold, 100000);

            ImGui.Spacing();
            ImGui.TextColored(ColorBlue, "Tomestones");

            var poeticsThresholdLong = (long)_config.Notifications.PoeticsThreshold;
            DrawCurrencyRow("Poetics", _tracker.CurrentPoetics, 2000,
                ref _config.Notifications.OnPoeticsThreshold, ref poeticsThresholdLong, 100);
            _config.Notifications.PoeticsThreshold = (int)poeticsThresholdLong;

            var mathThresholdLong = (long)_config.Notifications.MathematicsThreshold;
            DrawCurrencyRow("Mathematics", _tracker.CurrentMathematics, 2000,
                ref _config.Notifications.OnMathematicsThreshold, ref mathThresholdLong, 100);
            _config.Notifications.MathematicsThreshold = (int)mathThresholdLong;

            var mnemThresholdLong = (long)_config.Notifications.MnemonicsThreshold;
            DrawCurrencyRow("Mnemonics", _tracker.CurrentMnemonics, 2000,
                ref _config.Notifications.OnMnemonicsThreshold, ref mnemThresholdLong, 100);
            _config.Notifications.MnemonicsThreshold = (int)mnemThresholdLong;

            ImGui.Spacing();
            ImGui.TextColored(ColorBlue, "Other");

            var sealsThresholdLong = (long)_config.Notifications.CompanySealsThreshold;
            DrawCurrencyRow("Company Seals", _tracker.CurrentCompanySeals, 90000,
                ref _config.Notifications.OnCompanySealsThreshold, ref sealsThresholdLong, 10000);
            _config.Notifications.CompanySealsThreshold = (int)sealsThresholdLong;

            var mgpThresholdLong = (long)_config.Notifications.MGPThreshold;
            DrawCurrencyRow("MGP", _tracker.CurrentMGP, -1,
                ref _config.Notifications.OnMGPThreshold, ref mgpThresholdLong, 10000);
            _config.Notifications.MGPThreshold = (int)mgpThresholdLong;
        });

        ImGui.Spacing();

        // Social & Other
        DrawSection("Social & Other", () =>
        {
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

            var onComm = _config.Notifications.OnCommendationReceived;
            if (ImGui.Checkbox("Commendation Received", ref onComm))
            {
                _config.Notifications.OnCommendationReceived = onComm;
                _config.Save();
            }

            var onPM = _config.Notifications.OnPrivateMessage;
            if (ImGui.Checkbox("Private Messages (Tells)", ref onPM))
            {
                _config.Notifications.OnPrivateMessage = onPM;
                _config.Save();
            }

            var onGC = _config.Notifications.OnGCRankUp;
            if (ImGui.Checkbox("Grand Company Rank Up", ref onGC))
            {
                _config.Notifications.OnGCRankUp = onGC;
                _config.Save();
            }
        });
    }

    private void DrawDutyTab()
    {
        DrawSection("Duty Finder", () =>
        {
            var onPop = _config.Notifications.OnDutyPop;
            if (ImGui.Checkbox("Queue Pop", ref onPop))
            {
                _config.Notifications.OnDutyPop = onPop;
                _config.Save();
            }
            ImGui.TextColored(ColorGray, "  Great for AFK queuing");

            ImGui.Spacing();

            var onStart = _config.Notifications.OnDutyStart;
            if (ImGui.Checkbox("Duty Started", ref onStart))
            {
                _config.Notifications.OnDutyStart = onStart;
                _config.Save();
            }

            var onComplete = _config.Notifications.OnDutyComplete;
            if (ImGui.Checkbox("Duty Completed", ref onComplete))
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
        });
    }

    private void DrawLogsTab()
    {
        if (ImGui.Button("Clear"))
        {
            _config.RecentLogs.Clear();
        }
        ImGui.SameLine();
        ImGui.TextColored(ColorGray, $"{_config.RecentLogs.Count} entries");
        
        ImGui.Separator();

        var height = ImGui.GetContentRegionAvail().Y;
        if (ImGui.BeginChild("LogsChild", new Vector2(-1, height), false))
        {
            if (_config.RecentLogs.Count == 0)
            {
                ImGui.TextColored(ColorGray, "No activity yet.");
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

    // --- Helpers ---

    private static void DrawSection(string label, Action content)
    {
        ImGui.TextColored(ColorWhite, label);
        ImGui.Separator();
        ImGui.Indent(8);
        ImGui.Spacing();
        content();
        ImGui.Spacing();
        ImGui.Unindent(8);
    }

    private static void DrawStatusRow(string label, string value, string extra)
    {
        ImGui.TextColored(ColorGray, $"{label}:");
        ImGui.SameLine();
        ImGui.Text(string.IsNullOrEmpty(value) ? "—" : value);
        if (!string.IsNullOrEmpty(extra))
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorBlue, extra);
        }
    }

    private void DrawCurrencyRow(string name, long current, long cap,
        ref bool enabled, ref long threshold, int step)
    {
        if (ImGui.Checkbox($"##{name}Enable", ref enabled))
        {
            _config.Save();
        }
        ImGui.SameLine();

        var currentStr = cap > 0 ? $"{current:N0}/{cap:N0}" : $"{current:N0}";
        ImGui.Text(name);
        ImGui.SameLine();
        var ratio = cap > 0 ? (float)current / cap : 0;
        var color = ratio > 0.9f ? ColorRed : (ratio > 0.7f ? ColorYellow : ColorGray);
        ImGui.TextColored(color, $"({currentStr})");

        if (enabled)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorGray, "@");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            var thresholdInt = (int)threshold;
            if (ImGui.InputInt($"##{name}Threshold", ref thresholdInt, step))
            {
                threshold = Math.Max(0, thresholdInt);
                _config.Save();
            }
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
                _waitingStatus = $"Done! Chat ID: {chatId} ({username})";
                _waitingForStart = false;
                
                await _telegram.SendMessageAsync($"✅ <b>Connected!</b>\nHello {username}! You will now receive FFXIV notifications here.");
                return;
            }
            await System.Threading.Tasks.Task.Delay(1000);
        }
        
        _waitingStatus = "Timeout — send /start and try again";
        _waitingForStart = false;
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
