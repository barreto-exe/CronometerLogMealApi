using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Clients.TelegramClient.Models;
using CronometerLogMealApi.Clients.TelegramClient.Requests;
using Microsoft.AspNetCore.Mvc;

namespace CronometerLogMealApi.Controllers;

public class TelegramController : CronometerControllerBase
{
    private readonly TelegramHttpClient _telegram;

    public TelegramController(TelegramHttpClient telegram)
    {
        _telegram = telegram;
    }

    // GET: api/telegram/updates?offset=0
    [HttpGet("updates")]
    public async Task<ActionResult<GetUpdatesResponse?>> GetUpdates([FromQuery] long? offset = null, CancellationToken ct = default)
    {
        var req = new GetUpdatesRequest { Offset = offset, Limit = 100, Timeout = 0 };
        var res = await _telegram.GetUpdatesAsync(req, ct);
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
