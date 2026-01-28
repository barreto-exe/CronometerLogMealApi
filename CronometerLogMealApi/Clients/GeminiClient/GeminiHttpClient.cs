using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CronometerLogMealApi.Clients.GeminiClient;

public class GeminiHttpClient
{
    private readonly HttpClient _http;
    private readonly GeminiClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private const int MaxRetries = 3;

    public GeminiHttpClient(HttpClient http, IOptions<GeminiClientOptions> opts)
    {
        _http = http;
        _options = opts.Value;

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<GenerateContentResponse?> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        var req = new GenerateContentRequest
        {
            Contents =
            [
                new GeminiContent
                {
                    Role = "user",
                    Parts = [ new GeminiPart { Text = prompt } ]
                }
            ]
        };
        return await GenerateContentAsync(req, ct);
    }

    /// <summary>
    /// Analyzes an image using Gemini Vision API.
    /// </summary>
    public async Task<GenerateContentResponse?> AnalyzeImageAsync(
        string prompt, 
        byte[] imageBytes, 
        string mimeType = "image/jpeg",
        CancellationToken ct = default)
    {
        var req = new GenerateContentRequest
        {
            Contents =
            [
                new GeminiContent
                {
                    Role = "user",
                    Parts = 
                    [
                        GeminiPart.TextPart(prompt),
                        GeminiPart.ImagePart(imageBytes, mimeType)
                    ]
                }
            ]
        };
        return await GenerateContentAsync(req, ct);
    }

    public async Task<GenerateContentResponse?> GenerateContentAsync(GenerateContentRequest request, CancellationToken ct = default)
    {
        var model = _options.VisionModel ?? _options.Model;
        var path = $"models/{model}:generateContent?key={_options.ApiKey}";
        
        int retryDelay = 2; // seconds
        
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            using var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
            
            // Handle rate limiting with retry
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(retryDelay), ct);
                retryDelay *= 2; // exponential backoff: 2s, 4s, 8s
                continue;
            }
            
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GenerateContentResponse>(json, _jsonOptions);
        }
        
        return null;
    }
}


