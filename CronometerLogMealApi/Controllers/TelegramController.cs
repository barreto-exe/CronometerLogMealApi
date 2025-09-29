using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Clients.TelegramClient.Models;
using CronometerLogMealApi.Clients.TelegramClient.Requests;
using Microsoft.AspNetCore.Mvc;

namespace CronometerLogMealApi.Controllers;

public class TelegramController : CronometerControllerBase
{
    private readonly TelegramHttpClient _telegram;
    private static long LastUpdateId = 0;

    public TelegramController(TelegramHttpClient telegram)
    {
        _telegram = telegram;
    }

    // GET: api/telegram/updates?offset=0
    [HttpGet("updates")]
    public async Task<ActionResult<GetUpdatesResponse?>> GetUpdates([FromQuery] long? offset = null, CancellationToken ct = default)
    {
        var req = new GetUpdatesRequest { Offset = offset ?? LastUpdateId, Limit = 100, Timeout = 0 };
        var res = await _telegram.GetUpdatesAsync(req, ct);

        // Set last update id
        if (res != null && res.Ok && res.Result != null)
        {
            var lastUpdate = res.Result.OrderByDescending(u => u.UpdateId).FirstOrDefault();
            if (lastUpdate != null)
            {
                // Store this value in a persistent storage for the next call
                LastUpdateId = lastUpdate.UpdateId + 1;
            }
        }

        return Ok(res);
    }

    public record SendDto(string ChatId, string Text, string? ParseMode = null);

    // POST: api/telegram/send
    [HttpPost("send")]
    public async Task<ActionResult<SendMessageResponse?>> Send([FromBody] SendDto input, CancellationToken ct = default)
    {
        var res = await _telegram.SendMessageAsync(new SendMessageRequest
        {
            ChatId = input.ChatId,
            Text = input.Text,
            ParseMode = input.ParseMode
        }, ct);
        return Ok(res);
    }
}
