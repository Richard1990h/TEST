using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Models;
using LittleHelperAI.Shared.Models;
using LittleHelperAI.Backend.Helpers;
using LittleHelperAI.Backend.Services;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly CreditPolicyService _creditPolicy;

        public AuthController(ApplicationDbContext context, IConfiguration config, CreditPolicyService creditPolicy)
        {
            _context = context;
            _config = config;
            _creditPolicy = creditPolicy;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Username/email and password are required.");

            var identifier = request.Identifier.Trim().ToLower();

            var user = await _context.Users.FirstOrDefaultAsync(u =>
                u.Username.ToLower() == identifier || u.Email.ToLower() == identifier);

            if (user == null)
            {
                Console.WriteLine("No account found for: " + identifier);
                return Unauthorized("Account does not exist.");
            }

            var passwordValid = PasswordHelper.Verify(request.Password, user.PasswordHash);

            if (!passwordValid)
            {
                return Unauthorized("Incorrect password.");
            }

            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            try
            {
                await _creditPolicy.GetPlanStatusAsync(user.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Daily credit check failed on login: " + ex.Message);
            }

            var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

            var jwtKey = _config["Jwt:Key"];
            var jwtIssuer = _config["Jwt:Issuer"];
            var jwtAudience = _config["Jwt:Audience"];

            if (string.IsNullOrWhiteSpace(jwtKey) || string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience))
                return StatusCode(500, "JWT configuration is missing. Please configure Jwt:Key, Jwt:Issuer and Jwt:Audience.");

            var key = Encoding.UTF8.GetBytes(jwtKey);

            var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Subject = new System.Security.Claims.ClaimsIdentity(new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.Username),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new System.Security.Claims.Claim("id", user.Id.ToString()),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, user.Role)
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = jwtIssuer,
                Audience = jwtAudience,
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var jwtToken = tokenHandler.WriteToken(token);

            return Ok(new
            {
                token = jwtToken,
                expires = tokenDescriptor.Expires,
                user = new
                {
                    user.Id,
                    user.Username,
                    user.Email,
                    user.Role,
                    user.Credits,
                    user.LastLogin
                }
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FirstName) ||
                string.IsNullOrWhiteSpace(request.LastName) ||
                string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return BadRequest("All fields are required.");
            }

            if (request.Password != request.ConfirmPassword)
            {
                return BadRequest("Passwords do not match.");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return BadRequest("Invalid email format.");
            }

            if (request.Password.Length < 6)
            {
                return BadRequest("Password must be at least 6 characters.");
            }

            var exists = await _context.Users.AnyAsync(u =>
                u.Username.ToLower() == request.Username.ToLower() ||
                u.Email.ToLower() == request.Email.ToLower());

            if (exists)
                return Conflict("User already exists.");

            User? referrer = null;
            LittleHelperAI.Shared.Models.ReferralSettings? referralSettings = null;

            if (!string.IsNullOrWhiteSpace(request.ReferralCode))
            {
                referrer = await _context.Users
                    .FirstOrDefaultAsync(u => u.ReferralCode.ToLower() == request.ReferralCode.ToLower());

                if (referrer != null)
                {
                    referralSettings = await _context.ReferralSettings.FirstOrDefaultAsync();
                    if (referralSettings == null)
                    {
                        referralSettings = new LittleHelperAI.Shared.Models.ReferralSettings
                        {
                            ReferrerCredits = 50.0,
                            RefereeCredits = 25.0,
                            IsEnabled = true
                        };
                    }
                }
            }

            var referralCode = GenerateReferralCode(request.Username);

            var user = new User
            {
                Username = request.Username.Trim(),
                Email = request.Email.Trim(),
                PasswordHash = PasswordHelper.Hash(request.Password),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                OS = request.OS ?? "Windows",
                Role = "User",
                Status = "active",
                Credits = 0,
                CreatedAt = DateTime.UtcNow,
                ReferralCode = referralCode,
                ReferredByUserId = referrer?.Id
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            if (referrer != null && referralSettings != null && referralSettings.IsEnabled)
            {
                user.Credits += referralSettings.RefereeCredits;
                referrer.Credits += referralSettings.ReferrerCredits;

                var transaction = new LittleHelperAI.Shared.Models.ReferralTransaction
                {
                    ReferrerId = referrer.Id,
                    RefereeId = user.Id,
                    ReferralCode = request.ReferralCode!,
                    ReferrerCreditsAwarded = referralSettings.ReferrerCredits,
                    RefereeCreditsAwarded = referralSettings.RefereeCredits,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ReferralTransactions.Add(transaction);

                await _context.SaveChangesAsync();

                return Ok($"User registered successfully! You received {referralSettings.RefereeCredits} bonus credits from referral.");
            }

            return Ok("User registered successfully.");
        }

        private string GenerateReferralCode(string username)
        {
            var prefix = username.Length >= 4
                ? username.Substring(0, 4).ToUpper()
                : username.ToUpper().PadRight(4, 'X');

            var suffix = Convert.ToBase64String(BitConverter.GetBytes(DateTime.UtcNow.Ticks))
                .Replace("/", "")
                .Replace("+", "")
                .Replace("=", "")
                .Substring(0, 4)
                .ToUpper();

            return $"{prefix}-{suffix}";
        }
    }
}
