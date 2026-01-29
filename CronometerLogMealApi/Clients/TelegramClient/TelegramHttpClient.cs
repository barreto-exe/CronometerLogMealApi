using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CronometerLogMealApi.Clients.TelegramClient.Requests;
using CronometerLogMealApi.Clients.TelegramClient.Models;

namespace CronometerLogMealApi.Clients.TelegramClient;

/// <summary>
/// Typed HTTP client for Telegram Bot API.
/// Base URL should be configured like: https://api.telegram.org/bot{BotToken}/
/// </summary>
public class TelegramHttpClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _botToken;

    public TelegramHttpClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Extract bot token from base address (format: https://api.telegram.org/bot{TOKEN}/)
        var baseUrl = _http.BaseAddress?.ToString() ?? string.Empty;
        var tokenStart = baseUrl.IndexOf("/bot", StringComparison.Ordinal);
        if (tokenStart >= 0)
        {
            var tokenEnd = baseUrl.LastIndexOf('/');
            _botToken = baseUrl.Substring(tokenStart + 4, tokenEnd - tokenStart - 4);
        }
        else
        {
            _botToken = string.Empty;
        }
    }

    /// <summary>
    /// Sends a text message using Telegram's sendMessage method.
    /// </summary>
    public Task<SendMessageResponse?> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => PostModelAsync<SendMessageResponse, SendMessageRequest>("sendMessage", request, ct);

    /// <summary>
    /// Retrieves incoming updates for the bot (polling).
    /// </summary>
    public Task<GetUpdatesResponse?> GetUpdatesAsync(GetUpdatesRequest request, CancellationToken ct = default)
        => PostModelAsync<GetUpdatesResponse, GetUpdatesRequest>("getUpdates", request, ct);

    /// <summary>
    /// Gets file information including the file path needed for download.
    /// </summary>
    public Task<GetFileResponse?> GetFileAsync(GetFileRequest request, CancellationToken ct = default)
        => PostModelAsync<GetFileResponse, GetFileRequest>("getFile", request, ct);

    /// <summary>
    /// Downloads a file from Telegram's servers using the file path from GetFileAsync.
    /// </summary>
    /// <param name="filePath">File path returned by getFile API</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>File content as byte array</returns>
    public async Task<byte[]> DownloadFileAsync(string filePath, CancellationToken ct = default)
    {
        var fileUrl = $"https://api.telegram.org/file/bot{_botToken}/{filePath}";
        return await _http.GetByteArrayAsync(fileUrl, ct).ConfigureAwait(false);
    }

    private async Task<TResponse?> PostModelAsync<TResponse, TRequest>(string relativePath, TRequest payload, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(relativePath, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var model = JsonSerializer.Deserialize<TResponse>(json, _jsonOptions);
        return model;
    }
}
