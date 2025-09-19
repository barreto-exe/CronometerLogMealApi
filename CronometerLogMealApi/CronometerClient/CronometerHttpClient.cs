using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CronometerLogMealApi.CronometerClient.Requests;
using CronometerLogMealApi.Models;

namespace CronometerLogMealApi.CronometerClient;

/// <summary>
/// Typed HTTP client for Cronometer Mobile API v2.
/// Base URL: https://mobile.cronometer.com/api/v2
/// Methods map to the Postman collection you shared.
/// </summary>
public class CronometerHttpClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    public CronometerHttpClient(HttpClient http)
    {
        _http = http;
        // Default headers
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Use Web defaults to ensure:
        // - camelCase property names
        // - camelCase dictionary keys
        // - case-insensitive deserialization, etc.
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // Keep nulls out of payloads unless explicitly set
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => PostModelAsync<LoginResponse, LoginRequest>("login", request, ct);

    public Task<GetFoodsResponse> GetFoodsAsync(GetFoodsRequest request, CancellationToken ct = default)
        => PostModelAsync<GetFoodsResponse, GetFoodsRequest>("get_foods", request, ct);

    public Task<AddServingResponse> AddServingAsync(AddServingRequest request, CancellationToken ct = default)
        => PostModelAsync<AddServingResponse, AddServingRequest>("add_serving", request, ct);

    public Task<AddMultiServingResponse> AddMultiServingAsync(AddMultiServingRequest request, CancellationToken ct = default)
        => PostModelAsync<AddMultiServingResponse, AddMultiServingRequest>("multi_add_serving", request, ct);

    public Task<FindFoodResponse> FindFoodAsync(FindFoodRequest request, CancellationToken ct = default)
        => PostModelAsync<FindFoodResponse, FindFoodRequest>("find_food", request, ct);

    public Task<GetNutritionScoresResponse> GetNutritionScoresAsync(GetNutritionScoresRequest request, CancellationToken ct = default)
        => PostModelAsync<GetNutritionScoresResponse, GetNutritionScoresRequest>("get_nutrition_scores", request, ct);

    public Task<UpdateOrderResponse> UpdateOrderAsync(UpdateOrderRequest request, CancellationToken ct = default)
        => PostModelAsync<UpdateOrderResponse, UpdateOrderRequest>("update_order", request, ct);

    private async Task<TResponse> PostModelAsync<TResponse, TRequest>(string relativePath, TRequest payload, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(relativePath, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // Try to deserialize into TResponse first (in case you later add strong properties)
        var model = JsonSerializer.Deserialize<TResponse>(json, _jsonOptions);
        if (model is null) throw new InvalidOperationException($"Empty response from {relativePath}");

        // If the response model supports Raw payload, set it for full-fidelity access
        if (model is IRawResponse rawHolder)
        {
            using var doc = JsonDocument.Parse(json);
            rawHolder.Raw = doc.RootElement.Clone();
        }
        return model;
    }
}
