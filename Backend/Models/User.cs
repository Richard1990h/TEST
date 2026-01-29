public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string OS { get; set; } = "Windows";
    public string Status { get; set; } = "active";
    public DateTime? LastLogin { get; set; }
    public double Credits { get; set; } = 0.0;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // 🎁 Referral System
    public string ReferralCode { get; set; } = "";  // Unique code for this user to share
    public int? ReferredByUserId { get; set; }      // Who referred this user (nullable)
}
