using System.Data;
using LittleHelperAI.Backend.Models;
using LittleHelperAI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LittleHelperAI.Backend.Services.Notifications;

/// <summary>
/// DB-backed notification store using raw SQL so we do NOT need to change EF models or DbContext.
/// </summary>
public sealed class NotificationStore
{
    private readonly ApplicationDbContext _db;

    public NotificationStore(ApplicationDbContext db)
    {
        _db = db;
    }

    // =====================================================
    // CREATE
    // =====================================================
    public async Task CreateAsync(
        int userId,
        string title,
        string message,
        string? actionUrl,
        CancellationToken ct)
    {
        await CreateManyAsync(new[] { userId }, title, message, actionUrl, ct);
    }

    public async Task CreateManyAsync(
        IEnumerable<int> userIds,
        string title,
        string message,
        string? actionUrl,
        CancellationToken ct)
    {
        var ids = userIds?.Distinct().Where(x => x > 0).ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0) return;

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var dbTransaction = tx.GetDbTransaction();

        foreach (var uid in ids)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = dbTransaction;
            cmd.CommandText = @"
INSERT INTO user_notifications
(user_id, title, message, action_url, is_read, created_utc)
VALUES (@uid, @title, @msg, @url, 0, UTC_TIMESTAMP());
";
            var pUid = cmd.CreateParameter(); pUid.ParameterName = "@uid"; pUid.Value = uid; cmd.Parameters.Add(pUid);
            var pT = cmd.CreateParameter(); pT.ParameterName = "@title"; pT.Value = title ?? ""; cmd.Parameters.Add(pT);
            var pM = cmd.CreateParameter(); pM.ParameterName = "@msg"; pM.Value = message ?? ""; cmd.Parameters.Add(pM);
            var pU = cmd.CreateParameter(); pU.ParameterName = "@url"; pU.Value = (object?)actionUrl ?? DBNull.Value; cmd.Parameters.Add(pU);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    // =====================================================
    // LIST / COUNT
    // =====================================================
    public async Task<List<UserNotificationDto>> ListAsync(
        int userId,
        int take,
        CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 100);

        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, user_id, title, message, action_url, is_read, created_utc, read_utc
FROM user_notifications
WHERE user_id = @uid
ORDER BY created_utc DESC
LIMIT @take;
";
        var pUid = cmd.CreateParameter(); pUid.ParameterName = "@uid"; pUid.Value = userId; cmd.Parameters.Add(pUid);
        var pTake = cmd.CreateParameter(); pTake.ParameterName = "@take"; pTake.Value = take; cmd.Parameters.Add(pTake);

        var list = new List<UserNotificationDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new UserNotificationDto
            {
                Id = r.GetInt64(0),
                UserId = r.GetInt32(1),
                Title = r.GetString(2),
                Message = r.GetString(3),
                ActionUrl = r.IsDBNull(4) ? null : r.GetString(4),
                IsRead = r.GetInt32(5) == 1,
                CreatedUtc = r.GetDateTime(6),
                ReadUtc = r.IsDBNull(7) ? null : r.GetDateTime(7)
            });
        }

        return list;
    }

    public async Task<int> CountUnreadAsync(int userId, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM user_notifications WHERE user_id = @uid AND is_read = 0;";
        var pUid = cmd.CreateParameter(); pUid.ParameterName = "@uid"; pUid.Value = userId; cmd.Parameters.Add(pUid);

        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is null ? 0 : Convert.ToInt32(obj);
    }

    // =====================================================
    // MARK READ
    // =====================================================
    public async Task MarkReadAsync(int userId, long id, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE user_notifications
SET is_read = 1, read_utc = UTC_TIMESTAMP()
WHERE id = @id AND user_id = @uid;
";
        var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = id; cmd.Parameters.Add(pId);
        var pUid = cmd.CreateParameter(); pUid.ParameterName = "@uid"; pUid.Value = userId; cmd.Parameters.Add(pUid);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // =====================================================
    // 🧹 CLEAR
    // =====================================================
    public async Task ClearReadAsync(int userId, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM user_notifications WHERE user_id = {userId} AND is_read = 1",
            ct
        );
    }

    public async Task ClearAllAsync(int userId, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM user_notifications WHERE user_id = {userId}",
            ct
        );
    }

    // =====================================================
    // 🕒 AUTO-EXPIRE OLD READ
    // =====================================================
    public async Task CleanupOldReadAsync(int days = 30, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"
DELETE FROM user_notifications
WHERE is_read = 1
AND read_utc IS NOT NULL
AND read_utc < UTC_TIMESTAMP() - INTERVAL {days} DAY;
",
            ct
        );
    }
}
