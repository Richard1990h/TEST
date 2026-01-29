using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LittleHelperAI.Backend.Services;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/snapshots")]
public sealed class AdminSnapshotsController : ControllerBase
{
    private readonly SnapshotService _snapshots;
    private readonly AdminAuditLogger _audit;

    public AdminSnapshotsController(
        SnapshotService snapshots,
        AdminAuditLogger audit)
    {
        _snapshots = snapshots;
        _audit = audit;
    }

    public sealed class SnapshotItemDto
    {
        public string FileName { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public long Bytes { get; set; }
    }

    // =========================
    // LIST SNAPSHOTS
    // =========================
    [HttpGet]
    public IActionResult List()
    {
        var items = _snapshots.ListSnapshots()
            .Select(x => new SnapshotItemDto
            {
                FileName = x.fileName,
                CreatedUtc = x.createdUtc,
                Bytes = x.bytes
            })
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();

        return Ok(items);
    }

    // =========================
    // CREATE SNAPSHOT
    // =========================
    [HttpPost("create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var (fileName, bytes) = await _snapshots.CreateSnapshotAsync(ct);

        await _audit.LogAsync(
            GetActorUserId(),
            "snapshot.create",
            new { fileName, bytes },
            ct
        );

        return Ok(new { fileName });
    }

    // =========================
    // DOWNLOAD SNAPSHOT
    // =========================
    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> Download(string fileName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return BadRequest("Invalid file name.");

        var path = _snapshots.GetSnapshotPath(fileName);
        if (path == null)
            return NotFound();

        // 🔍 Audit downloads as well (important for admin traceability)
        await _audit.LogAsync(
            GetActorUserId(),
            "snapshot.download",
            new { fileName },
            ct
        );

        return PhysicalFile(
            path,
            "application/json",
            Path.GetFileName(path)
        );
    }

    // =========================
    // HELPERS
    // =========================
    private int GetActorUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }
}
