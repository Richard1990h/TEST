using LittleHelperAI.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Route("api/plans")]
public sealed class PlanStatusController : ControllerBase
{
    private readonly CreditPolicyService _policy;

    public PlanStatusController(CreditPolicyService policy)
    {
        _policy = policy;
    }

    [HttpGet("status/{userId:int}")]
    public async Task<IActionResult> GetStatus(int userId, CancellationToken ct)
    {
        var status = await _policy.GetPlanStatusAsync(userId, ct);
        return Ok(status);
    }
}
