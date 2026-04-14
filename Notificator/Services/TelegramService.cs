using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Dalamud.Plugin.Services;

namespace Notificator.Services;

public class TelegramService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IPluginLog _log;
    private readonly Configuration _config;

    public TelegramService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public bool IsConfigured => 
        !string.IsNullOrWhiteSpace(_config.TelegramBotToken) && 
        !string.IsNullOrWhiteSpace(_config.TelegramChatId);

    public async Task<bool> SendMessageAsync(string message)
    {
        if (!IsConfigured)
        {
            _log.Warning("Telegram is not configured. Message not sent.");
            return false;
        }

        try
        {
            var encodedMessage = HttpUtility.UrlEncode(message);
            var url = $"https://api.telegram.org/bot{_config.TelegramBotToken}/sendMessage" +
                      $"?chat_id={_config.TelegramChatId}" +
                      $"&text={encodedMessage}" +
                      "&parse_mode=HTML";

            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                _log.Debug($"Telegram message sent: {message}");
                return true;
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            _log.Error($"Failed to send Telegram message. Status: {response.StatusCode}, Error: {errorContent}");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"Exception sending Telegram message: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        return await SendMessageAsync("🎮 FFXIV Notificator: Test message - Connection successful!");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
