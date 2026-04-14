using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Dalamud.Plugin.Services;

namespace Notificator.Services;

public class TelegramService : IDisposable
{
    public const string BotUsername = "@FF14_Notif_bot";
    
    private HttpClient _httpClient;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private long _lastUpdateId;
    private bool _lastProxyEnabled;
    private string _lastProxyAddress = string.Empty;
    private int _lastProxyPort;
    private int _lastProxyType;

    public TelegramService(Configuration config, IPluginLog log)
    {
        _config = config;
        _log = log;
        _httpClient = CreateHttpClient();
    }

    private HttpClient CreateHttpClient()
    {
        _lastProxyEnabled = _config.UseProxy;
        _lastProxyAddress = _config.ProxyAddress;
        _lastProxyPort = _config.ProxyPort;
        _lastProxyType = _config.ProxyType;

        HttpMessageHandler handler;
        if (_config.UseProxy && !string.IsNullOrWhiteSpace(_config.ProxyAddress))
        {
            var scheme = _config.ProxyType == 0 ? "socks5" : "http";
            var proxy = new WebProxy($"{scheme}://{_config.ProxyAddress}:{_config.ProxyPort}");
            handler = new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = true,
                ConnectTimeout = TimeSpan.FromSeconds(10),
            };
            _log.Info($"Telegram using proxy: {scheme}://{_config.ProxyAddress}:{_config.ProxyPort}");
        }
        else
        {
            handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
            };
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private void EnsureHttpClient()
    {
        if (_lastProxyEnabled != _config.UseProxy ||
            _lastProxyAddress != _config.ProxyAddress ||
            _lastProxyPort != _config.ProxyPort ||
            _lastProxyType != _config.ProxyType)
        {
            _httpClient.Dispose();
            _httpClient = CreateHttpClient();
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

        EnsureHttpClient();

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
        EnsureHttpClient();
        _config.AddLog("🔄 Testing connection...");
        return await SendMessageAsync("🎮 FFXIV Notificator: Test message - Connection successful!");
    }

    public async Task<(bool success, string chatId, string username)> CheckForStartCommandAsync()
    {
        EnsureHttpClient();

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
