using LittleHelperAI.Backend.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationStore _store;

    public NotificationsController(NotificationStore store)
        => _store = store;

    // =====================================================
    // LIST
    // =====================================================
    [HttpGet("list")]
    public async Task<IActionResult> List(
        [FromQuery] int take = 30,
        CancellationToken ct = default)
        => Ok(await _store.ListAsync(GetUserId(), take, ct));

    // =====================================================
    // UNREAD COUNT (back-compat)
    // =====================================================
    [HttpGet("unread")]
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct = default)
        => Ok(new { count = await _store.CountUnreadAsync(GetUserId(), ct) });

    // =====================================================
    // MARK READ
    // =====================================================
    [HttpPost("mark-read/{id:long}")]
    public async Task<IActionResult> MarkRead(
        [FromRoute] long id,
        CancellationToken ct = default)
    {
        await _store.MarkReadAsync(GetUserId(), id, ct);
        return Ok();
    }

    // =====================================================
    // 🧹 CLEAR READ
    // =====================================================
    [HttpPost("clear-read")]
    public async Task<IActionResult> ClearRead(CancellationToken ct = default)
    {
        await _store.ClearReadAsync(GetUserId(), ct);
        return Ok();
    }

    // =====================================================
    // 🗑️ CLEAR ALL
    // =====================================================
    [HttpPost("clear-all")]
    public async Task<IActionResult> ClearAll(CancellationToken ct = default)
    {
        await _store.ClearAllAsync(GetUserId(), ct);
        return Ok();
    }

    // =====================================================
    // USER ID RESOLUTION
    // =====================================================
    private int GetUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
