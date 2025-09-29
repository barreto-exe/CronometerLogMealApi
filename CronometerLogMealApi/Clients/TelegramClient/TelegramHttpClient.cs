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

    public TelegramHttpClient(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Sends a text message using Telegram's sendMessage method.
    /// </summary>
    public Task<SendMessageResponse?> SendMessageAsync(SendMessageRequest request, CancellationToken ct = default)
        => PostModelAsync<SendMessageResponse, SendMessageRequest>("sendMessage", request, ct);

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
