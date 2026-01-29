using LittleHelperAI.Backend.Services.Admin;
using LittleHelperAI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace LittleHelperAI.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/db")]
public sealed class AdminDbController : ControllerBase
{
    private static readonly HashSet<string> AllowedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "users",
        "chathistory",
        "knowledge_entries",
        "learned_knowledge",
        "fact_entries",
        "code_rules",
        "project_intents",
        "feature_graphs",
        "generated_projects",
        "llm_calls",
        "build_repairs",
        "code_knowledge",
        "feature_templates",
        "user_stripe_subscriptions",
        "stripeplan_policies",
        "user_daily_credit_state",
        "user_notifications",
        "admin_audit_log",
        "stripeplans",
        "userplans"
    };

    private readonly ApplicationDbContext _db;
    private readonly AdminAuditStore _audit;

    public AdminDbController(ApplicationDbContext db, AdminAuditStore audit)
    {
        _db = db;
        _audit = audit;
    }

    // =========================
    // TABLE LIST
    // =========================
    [HttpGet("tables")]
    public IActionResult Tables()
        => Ok(AllowedTables.OrderBy(x => x).ToList());

    // =========================
    // READ TABLE
    // =========================
    [HttpGet("table/{table}")]
    public async Task<IActionResult> GetTable(
        string table,
        int take = 100,
        int skip = 0,
        string? q = null,
        CancellationToken ct = default)
    {
        if (!AllowedTables.Contains(table))
            return BadRequest("Table not allowed");

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        var dbName = await GetDbNameAsync(conn, ct);
        var columns = await GetColumnsAsync(conn, dbName, table, ct);
        var pk = await GetPrimaryKeyAsync(conn, dbName, table, ct);

        var searchable = columns
            .Where(c =>
                c.DataType.Contains("char", StringComparison.OrdinalIgnoreCase) ||
                c.DataType.Contains("text", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"`{c.Name}` LIKE @q")
            .ToList();

        var where = (!string.IsNullOrWhiteSpace(q) && searchable.Count > 0)
            ? "WHERE " + string.Join(" OR ", searchable)
            : "";

        // Count
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM `{table}` {where};";
        if (!string.IsNullOrWhiteSpace(where))
        {
            var p = countCmd.CreateParameter();
            p.ParameterName = "@q";
            p.Value = "%" + q!.Trim() + "%";
            countCmd.Parameters.Add(p);
        }

        var total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));

        // Data
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM `{table}` {where} LIMIT @take OFFSET @skip;";
        if (!string.IsNullOrWhiteSpace(where))
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@q";
            p.Value = "%" + q!.Trim() + "%";
            cmd.Parameters.Add(p);
        }

        var pTake = cmd.CreateParameter();
        pTake.ParameterName = "@take";
        pTake.Value = take;
        cmd.Parameters.Add(pTake);

        var pSkip = cmd.CreateParameter();
        pSkip.ParameterName = "@skip";
        pSkip.Value = skip;
        cmd.Parameters.Add(pSkip);

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return Ok(new
        {
            table,
            primaryKey = pk,
            columns,
            total,
            rows
        });
    }

    // =========================
    // INSERT
    // =========================
    [HttpPost("table/{table}")]
    public async Task<IActionResult> Insert(
        string table,
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        if (!AllowedTables.Contains(table))
            return BadRequest("Table not allowed");

        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            body.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (data == null || data.Count == 0)
            return BadRequest("No data");

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        var dbName = await GetDbNameAsync(conn, ct);
        var pk = await GetPrimaryKeyAsync(conn, dbName, table, ct);

        var columns = data.Keys
            .Where(k => !string.Equals(k, pk, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (columns.Count == 0)
            return BadRequest("No insertable columns");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"INSERT INTO `{table}` ({string.Join(",", columns.Select(c => $"`{c}`"))}) " +
            $"VALUES ({string.Join(",", columns.Select((_, i) => "@p" + i))});";

        for (var i = 0; i < columns.Count; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@p" + i;
            p.Value = data[columns[i]] ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        await _audit.WriteAsync(GetUserId(), "insert", table, null, $"Inserted row (affected={affected})", ct);

        return Ok(new { affected });
    }

    // =========================
    // UPDATE
    // =========================
    [HttpPut("table/{table}/{id}")]
    public async Task<IActionResult> Update(
        string table,
        string id,
        [FromBody] JsonElement body,
        CancellationToken ct = default)
    {
        if (!AllowedTables.Contains(table))
            return BadRequest("Table not allowed");

        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            body.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (data == null || data.Count == 0)
            return BadRequest("No data");

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        var dbName = await GetDbNameAsync(conn, ct);
        var pk = await GetPrimaryKeyAsync(conn, dbName, table, ct);
        if (string.IsNullOrWhiteSpace(pk))
            return BadRequest("No primary key");

        var columns = data.Keys
            .Where(k => !string.Equals(k, pk, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (columns.Count == 0)
            return BadRequest("No updatable columns");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"UPDATE `{table}` SET {string.Join(",", columns.Select((c, i) => $"`{c}`=@p{i}"))} " +
            $"WHERE `{pk}`=@id;";

        for (var i = 0; i < columns.Count; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@p" + i;
            p.Value = data[columns[i]] ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        var pId = cmd.CreateParameter();
        pId.ParameterName = "@id";
        pId.Value = id;
        cmd.Parameters.Add(pId);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        await _audit.WriteAsync(GetUserId(), "update", table, id, $"Updated row (affected={affected})", ct);

        return Ok(new { affected });
    }

    // =========================
    // DELETE
    // =========================
    [HttpDelete("table/{table}/{id}")]
    public async Task<IActionResult> Delete(string table, string id, CancellationToken ct = default)
    {
        if (!AllowedTables.Contains(table))
            return BadRequest("Table not allowed");

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        var dbName = await GetDbNameAsync(conn, ct);
        var pk = await GetPrimaryKeyAsync(conn, dbName, table, ct);
        if (string.IsNullOrWhiteSpace(pk))
            return BadRequest("No primary key");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM `{table}` WHERE `{pk}`=@id;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = id;
        cmd.Parameters.Add(p);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        await _audit.WriteAsync(GetUserId(), "delete", table, id, $"Deleted row (affected={affected})", ct);

        return Ok(new { affected });
    }

    // =========================
    // HELPERS
    // =========================
    private static async Task<string> GetDbNameAsync(IDbConnection conn, CancellationToken ct)
    {
        await using var cmd = ((System.Data.Common.DbConnection)conn).CreateCommand();
        cmd.CommandText = "SELECT DATABASE();";
        return (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "";
    }

    private static async Task<List<Col>> GetColumnsAsync(IDbConnection conn, string db, string table, CancellationToken ct)
    {
        await using var cmd = ((System.Data.Common.DbConnection)conn).CreateCommand();
        cmd.CommandText = @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @t
ORDER BY ORDINAL_POSITION;";
        var pDb = cmd.CreateParameter(); pDb.ParameterName = "@db"; pDb.Value = db; cmd.Parameters.Add(pDb);
        var pT = cmd.CreateParameter(); pT.ParameterName = "@t"; pT.Value = table; cmd.Parameters.Add(pT);

        var list = new List<Col>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new Col
            {
                Name = r.GetString(0),
                DataType = r.GetString(1),
                IsNullable = r.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase)
            });

        return list;
    }

    private static async Task<string?> GetPrimaryKeyAsync(IDbConnection conn, string db, string table, CancellationToken ct)
    {
        await using var cmd = ((System.Data.Common.DbConnection)conn).CreateCommand();
        cmd.CommandText = @"
SELECT k.COLUMN_NAME
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
 ON t.CONSTRAINT_NAME = k.CONSTRAINT_NAME
WHERE t.TABLE_SCHEMA=@db AND t.TABLE_NAME=@t AND t.CONSTRAINT_TYPE='PRIMARY KEY'
LIMIT 1;";
        var pDb = cmd.CreateParameter(); pDb.ParameterName = "@db"; pDb.Value = db; cmd.Parameters.Add(pDb);
        var pT = cmd.CreateParameter(); pT.ParameterName = "@t"; pT.Value = table; cmd.Parameters.Add(pT);
        return (await cmd.ExecuteScalarAsync(ct))?.ToString();
    }

    private int GetUserId()
    {
        var id = User.Claims.FirstOrDefault(c => c.Type == "id")?.Value;
        return int.TryParse(id, out var v) ? v : 0;
    }

    public sealed class Col
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public bool IsNullable { get; set; }
    }
}
