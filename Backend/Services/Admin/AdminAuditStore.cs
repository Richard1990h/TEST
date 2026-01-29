using System.Data;
using LittleHelperAI.Data;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Services.Admin;

public sealed class AdminAuditStore
{
    private readonly ApplicationDbContext _db;
    public AdminAuditStore(ApplicationDbContext db) => _db = db;

    public async Task WriteAsync(int adminUserId, string action, string entity, string? entityId, string details, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO admin_audit_log (admin_user_id, action, entity, entity_id, details, created_utc)
VALUES (@aid, @action, @entity, @eid, @details, UTC_TIMESTAMP());
";
        var pA = cmd.CreateParameter(); pA.ParameterName = "@aid"; pA.Value = adminUserId; cmd.Parameters.Add(pA);
        var pAc = cmd.CreateParameter(); pAc.ParameterName = "@action"; pAc.Value = action ?? ""; cmd.Parameters.Add(pAc);
        var pE = cmd.CreateParameter(); pE.ParameterName = "@entity"; pE.Value = entity ?? ""; cmd.Parameters.Add(pE);
        var pEi = cmd.CreateParameter(); pEi.ParameterName = "@eid"; pEi.Value = (object?)entityId ?? DBNull.Value; cmd.Parameters.Add(pEi);
        var pD = cmd.CreateParameter(); pD.ParameterName = "@details"; pD.Value = details ?? ""; cmd.Parameters.Add(pD);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(int total, List<AdminAuditRow> items)> ListAsync(string? q, int take, int skip, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(skip, 0);

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        string where = "";
        var like = (q ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(like))
        {
            where = "WHERE action LIKE @q OR entity LIKE @q OR entity_id LIKE @q OR details LIKE @q";
        }

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM admin_audit_log {where};";
        if (!string.IsNullOrWhiteSpace(where))
        {
            var pQ = countCmd.CreateParameter(); pQ.ParameterName = "@q"; pQ.Value = "%" + like + "%"; countCmd.Parameters.Add(pQ);
        }
        var totalObj = await countCmd.ExecuteScalarAsync(ct);
        var total = totalObj is null ? 0 : Convert.ToInt32(totalObj);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT id, admin_user_id, action, entity, entity_id, details, created_utc
FROM admin_audit_log
{where}
ORDER BY created_utc DESC
LIMIT @take OFFSET @skip;
";
        if (!string.IsNullOrWhiteSpace(where))
        {
            var pQ = cmd.CreateParameter(); pQ.ParameterName = "@q"; pQ.Value = "%" + like + "%"; cmd.Parameters.Add(pQ);
        }
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "@take"; pTake.Value = take; cmd.Parameters.Add(pTake);
        var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "@skip"; pSkip.Value = skip; cmd.Parameters.Add(pSkip);

        var items = new List<AdminAuditRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            items.Add(new AdminAuditRow
            {
                Id = r.GetInt64(0),
                AdminUserId = r.GetInt32(1),
                Action = r.GetString(2),
                Entity = r.GetString(3),
                EntityId = r.IsDBNull(4) ? null : r.GetString(4),
                Details = r.GetString(5),
                CreatedUtc = r.GetDateTime(6)
            });
        }

        return (total, items);
    }

    public sealed class AdminAuditRow
    {
        public long Id { get; set; }
        public int AdminUserId { get; set; }
        public string Action { get; set; } = "";
        public string Entity { get; set; } = "";
        public string? EntityId { get; set; }
        public string Details { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
    }
}
