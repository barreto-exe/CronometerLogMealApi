using CronometerLogMealApi.Clients.GeminiClient;
using Microsoft.AspNetCore.Mvc;

namespace CronometerLogMealApi.Controllers;

public class GeminiController : CronometerControllerBase
{
    private readonly GeminiHttpClient _gemini;

    public GeminiController(GeminiHttpClient gemini)
    {
        _gemini = gemini;
    }

    public record AskRequest(string Prompt);

    public record AskResponse(string? Text, object? Raw);

    [HttpPost("ask")]
    public async Task<ActionResult<AskResponse>> Ask([FromBody] AskRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            return BadRequest("Prompt is required");

        var result = await _gemini.GenerateTextAsync(input.Prompt, ct);

        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        return Ok(new AskResponse(text, result));
    }
}
