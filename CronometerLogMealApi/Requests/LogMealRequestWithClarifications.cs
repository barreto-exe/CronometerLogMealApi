using System.Text.Json.Serialization;

namespace CronometerLogMealApi.Requests;

/// <summary>
/// Extended log meal request that includes clarification information from LLM.
/// </summary>
public class LogMealRequestWithClarifications : LogMealRequest
{
    /// <summary>
    /// Whether clarification is needed before logging.
    /// </summary>
    [JsonPropertyName("needsClarification")]
    public bool NeedsClarification { get; set; }

    /// <summary>
    /// List of clarifications needed from the user.
    /// </summary>
    [JsonPropertyName("clarifications")]
    public List<LlmClarificationItem>? Clarifications { get; set; }

    /// <summary>
    /// Error message if parsing failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Clarification item as returned by the LLM.
/// </summary>
public class LlmClarificationItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;
}
