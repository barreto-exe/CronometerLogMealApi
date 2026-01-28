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

        // Extract bot token from base URL for file downloads
        // Base URL format: https://api.telegram.org/bot{TOKEN}/
        var baseUrl = _http.BaseAddress?.ToString() ?? "";
        var tokenStart = baseUrl.IndexOf("/bot") + 4;
        var tokenEnd = baseUrl.LastIndexOf('/');
        _botToken = tokenStart > 3 && tokenEnd > tokenStart 
            ? baseUrl.Substring(tokenStart, tokenEnd - tokenStart) 
            : "";
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
    /// Gets file info from Telegram. Use the file_path to download the file.
    /// </summary>
    public async Task<TelegramFile?> GetFileAsync(string fileId, CancellationToken ct = default)
    {
        var response = await GetAsync<GetFileResponse>($"getFile?file_id={fileId}", ct);
        return response?.Result;
    }

    /// <summary>
    /// Downloads a file from Telegram's servers.
    /// </summary>
    /// <param name="filePath">The file_path from GetFileAsync response</param>
    public async Task<byte[]> DownloadFileAsync(string filePath, CancellationToken ct = default)
    {
        // Files are downloaded from: https://api.telegram.org/file/bot{TOKEN}/{file_path}
        var downloadUrl = $"https://api.telegram.org/file/bot{_botToken}/{filePath}";
        
        using var response = await _http.GetAsync(downloadUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
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

    private async Task<TResponse?> GetAsync<TResponse>(string relativePath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(relativePath, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var model = JsonSerializer.Deserialize<TResponse>(json, _jsonOptions);
        return model;
    }
}

