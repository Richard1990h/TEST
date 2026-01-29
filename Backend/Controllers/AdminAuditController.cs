using LittleHelperAI.Backend.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/audit")]

public sealed class AdminAuditController : ControllerBase
{
    private readonly AdminAuditStore _store;

    public AdminAuditController(AdminAuditStore store)
    {
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] int take = 200,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        var (total, items) = await _store.ListAsync(q, take, skip, ct);
        return Ok(new { total, items });
    }
}
