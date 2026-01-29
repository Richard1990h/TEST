using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Models;
using LittleHelperAI.Backend.Helpers;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/account")]
    public class AccountController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Change user password - validates current password and updates to new password
        /// </summary>
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(request.CurrentPassword) || 
                string.IsNullOrWhiteSpace(request.NewPassword) || 
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return BadRequest(new { success = false, message = "All fields are required." });
            }

            // Validate new password length
            if (request.NewPassword.Length < 6)
            {
                return BadRequest(new { success = false, message = "New password must be at least 6 characters." });
            }

            // Validate passwords match
            if (request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest(new { success = false, message = "Passwords do not match." });
            }

            // Validate new password is different from current
            if (request.CurrentPassword == request.NewPassword)
            {
                return BadRequest(new { success = false, message = "New password must be different from current password." });
            }

            try
            {
                // Get user ID from claims or request
                if (!int.TryParse(request.UserId?.ToString() ?? "0", out int userId) || userId <= 0)
                {
                    return Unauthorized(new { success = false, message = "User ID is required." });
                }

                // Fetch user from database
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return NotFound(new { success = false, message = "User not found." });
                }

                // Verify current password against database hash
                bool isCurrentPasswordValid = PasswordHelper.Verify(request.CurrentPassword, user.PasswordHash);
                if (!isCurrentPasswordValid)
                {
                    return Unauthorized(new { success = false, message = "Current password is incorrect." });
                }

                // Hash new password and update database
                user.PasswordHash = PasswordHelper.Hash(request.NewPassword);
                user.UpdatedAt = DateTime.UtcNow;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Password changed successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error changing password: {ex.Message}");
                return StatusCode(500, new { success = false, message = "An error occurred while changing password." });
            }
        }

        /// <summary>
        /// Verify current password (used for sensitive operations)
        /// </summary>
        [HttpPost("verify-password")]
        public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Password is required." });
            }

            try
            {
                if (!int.TryParse(request.UserId?.ToString() ?? "0", out int userId) || userId <= 0)
                {
                    return Unauthorized(new { success = false, message = "User ID is required." });
                }

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return NotFound(new { success = false, message = "User not found." });
                }

                bool isPasswordValid = PasswordHelper.Verify(request.Password, user.PasswordHash);
                return Ok(new { success = isPasswordValid, message = isPasswordValid ? "Password verified." : "Password is incorrect." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error verifying password: {ex.Message}");
                return StatusCode(500, new { success = false, message = "An error occurred while verifying password." });
            }
        }
    }

    /// <summary>
    /// Request model for changing password
    /// </summary>
    public class ChangePasswordRequest
    {
        public int? UserId { get; set; }
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for verifying password
    /// </summary>
    public class VerifyPasswordRequest
    {
        public int? UserId { get; set; }
        public string Password { get; set; } = string.Empty;
    }
}