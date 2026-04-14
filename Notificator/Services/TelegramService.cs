using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Dalamud.Plugin.Services;

namespace Notificator.Services;

public class TelegramService : IDisposable
{
    public const string DefaultBotToken = "8625979759:AAFVY-iYHJF7-jTpJCcKF_0s9kSYdWYd6bo";
    public const string BotUsername = "@FF14_Notif_bot";
    
    private readonly HttpClient _httpClient;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private long _lastUpdateId;

    public TelegramService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        
        if (string.IsNullOrWhiteSpace(_config.TelegramBotToken))
        {
            _config.TelegramBotToken = DefaultBotToken;
            _config.Save();
        }
    }

    public bool IsConfigured => 
        !string.IsNullOrWhiteSpace(_config.TelegramBotToken) && 
        !string.IsNullOrWhiteSpace(_config.TelegramChatId);

    public async Task<bool> SendMessageAsync(string message)
    {
        if (!IsConfigured)
        {
            _log.Warning("Telegram is not configured. Message not sent.");
            _config.AddLog("⚠️ Message not sent - not configured");
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
                _config.AddLog($"✅ Sent: {StripHtml(message)}");
                return true;
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            _log.Error($"Failed to send Telegram message. Status: {response.StatusCode}, Error: {errorContent}");
            _config.AddLog($"❌ Failed: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"Exception sending Telegram message: {ex.Message}");
            _config.AddLog($"❌ Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        _config.AddLog("🔄 Testing connection...");
        return await SendMessageAsync("🎮 FFXIV Notificator: Test message - Connection successful!");
    }

    public async Task<(bool success, string chatId, string username)> CheckForStartCommandAsync()
    {
        try
        {
            var url = $"https://api.telegram.org/bot{_config.TelegramBotToken}/getUpdates?offset={_lastUpdateId + 1}&timeout=1";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                return (false, string.Empty, string.Empty);
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("ok").GetBoolean())
            {
                return (false, string.Empty, string.Empty);
            }

            var results = root.GetProperty("result");
            foreach (var update in results.EnumerateArray())
            {
                _lastUpdateId = update.GetProperty("update_id").GetInt64();
                
                if (update.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("text", out var textElement))
                    {
                        var text = textElement.GetString();
                        if (text == "/start")
                        {
                            var chatId = message.GetProperty("chat").GetProperty("id").GetInt64().ToString();
                            var username = "";
                            
                            if (message.GetProperty("from").TryGetProperty("username", out var usernameEl))
                            {
                                username = usernameEl.GetString() ?? "";
                            }
                            else if (message.GetProperty("from").TryGetProperty("first_name", out var firstNameEl))
                            {
                                username = firstNameEl.GetString() ?? "";
                            }
                            
                            _config.AddLog($"📥 /start from {username} (ID: {chatId})");
                            return (true, chatId, username);
                        }
                    }
                }
            }

            return (false, string.Empty, string.Empty);
        }
        catch (Exception ex)
        {
            _log.Error($"Error checking for /start command: {ex.Message}");
            return (false, string.Empty, string.Empty);
        }
    }

    private static string StripHtml(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", "").Replace("\n", " ");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
