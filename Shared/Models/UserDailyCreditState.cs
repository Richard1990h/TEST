using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LittleHelperAI.Shared.Models;

[Table("user_daily_credit_state")]
public sealed class UserDailyCreditState
{
    [Key]
    [Column("user_id")]
    public int UserId { get; set; }

    // Stored as UTC date (yyyy-mm-dd) in DB DATE column
    [Column("utc_day")]
    public DateTime UtcDay { get; set; }

    [Column("daily_allowance")]
    public double DailyAllowance { get; set; }

    [Column("daily_remaining")]
    public double DailyRemaining { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
