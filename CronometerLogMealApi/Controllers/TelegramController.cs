using CronometerLogMealApi.Clients.TelegramClient;
using CronometerLogMealApi.Clients.TelegramClient.Models;
using CronometerLogMealApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CronometerLogMealApi.Controllers;

public class TelegramController : CronometerControllerBase
{
    private readonly TelegramService _service;
    private readonly TelegramHttpClient _telegram;

    public TelegramController(TelegramService service, TelegramHttpClient telegram)
    {
        _service = service;
        _telegram = telegram;
    }

    // GET: api/telegram/updates?offset=0
    [HttpGet("updates")]
    public async Task<ActionResult<GetUpdatesResponse?>> GetUpdates([FromQuery] long? offset = null, CancellationToken ct = default)
    {
        var res = await _service.GetTelegramUpdates(offset, ct);
        return Ok(res);
    }

    public record SendDto(string ChatId, string Text, string? ParseMode = null);

    // POST: api/telegram/send
    [HttpPost("send")]
    public async Task<ActionResult<SendMessageResponse?>> Send([FromBody] SendDto input, CancellationToken ct = default)
    {
        var res = await _service.SendMessageAsync(input.ChatId, input.Text, input.ParseMode, ct);
        return Ok(res);
    }
}
