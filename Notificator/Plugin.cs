using System;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Notificator.Services;
using Notificator.Windows;

namespace Notificator;

public sealed class Plugin : IDalamudPlugin
{
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    private readonly ICommandManager _commandManager;
    private readonly IFramework _framework;

    private readonly Configuration _config;
    private readonly TelegramService _telegram;
    private readonly NotificationTracker _tracker;
    private readonly WindowSystem _windowSystem;
    private readonly ConfigWindow _configWindow;

    private DateTime _lastPeriodicCheck = DateTime.MinValue;
    private const int PeriodicCheckIntervalSeconds = 5;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IPluginLog log,
        IFramework framework,
        IClientState clientState,
        IPlayerState playerState,
        IDutyState dutyState,
        ICondition condition,
        IDataManager dataManager,
        IChatGui chatGui)
    {
        PluginInterface = pluginInterface;
        _commandManager = commandManager;
        _framework = framework;

        _config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _telegram = new TelegramService(_config, log);
        _tracker = new NotificationTracker(
            _config, _telegram, log, clientState, playerState, 
            dutyState, condition, dataManager, chatGui);

        _windowSystem = new WindowSystem("Notificator");
        _configWindow = new ConfigWindow(_config, _telegram, _tracker);
        _windowSystem.AddWindow(_configWindow);

        _commandManager.AddHandler("/notificator", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "Open Notificator settings"
        });

        _commandManager.AddHandler("/noti", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "Open Notificator settings (shorthand)"
        });

        pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        pluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUi;

        _framework.Update += OnFrameworkUpdate;
    }

    private void OnCommand(string command, string args)
    {
        _configWindow.Toggle();
    }

    private void OnOpenConfigUi()
    {
        _configWindow.IsOpen = true;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPeriodicCheck).TotalSeconds < PeriodicCheckIntervalSeconds)
            return;

        _lastPeriodicCheck = now;

        _tracker.UpdateCurrentInfo();
        _tracker.CheckCommendations();
        _tracker.CheckGCRank();
        _tracker.CheckDeath();
        _tracker.CheckCurrencyThresholds();
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;

        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUi;

        _commandManager.RemoveHandler("/notificator");
        _commandManager.RemoveHandler("/noti");

        _windowSystem.RemoveAllWindows();
        _configWindow.Dispose();

        _tracker.Dispose();
        _telegram.Dispose();
    }
}
