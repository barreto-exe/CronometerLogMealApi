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

    public async Task<GenerateContentResponse?> GenerateContentAsync(GenerateContentRequest request, CancellationToken ct = default)
    {
        var path = $"models/{_options.Model}:generateContent?key={_options.ApiKey}";
        using var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<GenerateContentResponse>(json, _jsonOptions);
    }
}
