using Microsoft.AspNetCore.Mvc;

namespace CronometerLogMealApi.CronometerClient.Requests;

public record AuthPayload
{
    [FromHeader(Name = "X-User-Id")]
    public long UserId { get; init; }

    [FromHeader(Name = "X-Auth-Token")]
    public string Token { get; init; }
}
