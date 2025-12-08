using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CronometerLogMealApi.Clients.OpenAIClient;

public class OpenAIHttpClient
{
    private readonly HttpClient _http;
    private readonly OpenAIClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public OpenAIHttpClient(HttpClient http, IOptions<OpenAIClientOptions> opts)
    {
        _http = http;
        _options = opts.Value;

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<ChatCompletionResponse?> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        var req = new ChatCompletionRequest
        {
            Model = _options.Model,
            Messages =
            [
                new ChatMessage { Role = "user", Content = prompt }
            ]
        };

        return await GenerateContentAsync(req, ct);
    }

    public async Task<ChatCompletionResponse?> GenerateContentAsync(ChatCompletionRequest request, CancellationToken ct = default)
    {
        // Ensure model is set if not provided in request
        if (string.IsNullOrEmpty(request.Model))
        {
            request.Model = _options.Model;
        }

        var path = "chat/completions";
        using var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
        
        using var res = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ChatCompletionResponse>(json, _jsonOptions);
    }
}
