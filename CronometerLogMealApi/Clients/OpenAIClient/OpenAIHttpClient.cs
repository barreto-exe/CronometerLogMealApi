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
    private readonly ILogger<OpenAIHttpClient> _logger;

    public OpenAIHttpClient(HttpClient http, IOptions<OpenAIClientOptions> opts, ILogger<OpenAIHttpClient> logger)
    {
        _http = http;
        _options = opts.Value;
        _logger = logger;

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
        var contentString = JsonSerializer.Serialize(request, _jsonOptions);

        int maxRetries = 3;
        int delayMilliseconds = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var content = new StringContent(contentString, Encoding.UTF8, "application/json");
                using var res = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
                res.EnsureSuccessStatusCode();

                var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return JsonSerializer.Deserialize<ChatCompletionResponse>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                if (i == maxRetries - 1 || ct.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Final attempt {Attempt} of {MaxRetries} failed for LLM request.", i + 1, maxRetries);
                    throw;
                }

                _logger.LogWarning(ex, "Attempt {Attempt} of {MaxRetries} failed for LLM request. Retrying in {Delay}ms...", i + 1, maxRetries, delayMilliseconds);
                await Task.Delay(delayMilliseconds, ct);
                delayMilliseconds *= 2; // Exponential backoff
            }
        }

        return null; // Should not be reached due to throw in loop
    }
}
