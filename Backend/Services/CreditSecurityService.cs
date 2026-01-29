using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;

namespace LittleHelperAI.Backend.Services
{
    /// <summary>
    /// 3-LEVEL SECURITY SERVICE FOR CREDIT OPERATIONS
    /// Level 1: Unique audit ID generation and hash validation
    /// Level 2: Before/After credit balance verification
    /// Level 3: Security token validation (anti-replay)
    /// </summary>
    public interface ICreditSecurityService
    {
        Task<CreditAuditLog> CreateAuditedCreditOperation(
            int userId,
            string operationType,
            double creditsAmount,
            string sourceType,
            string? sourceReferenceId = null,
            int? relatedUserId = null,
            string? ipAddress = null,
            string? userAgent = null);

        Task<bool> ValidateAuditEntry(string auditLogId);
        Task<CreditSecurityToken> GenerateSecurityToken(int userId, string operationType);
        Task<bool> ValidateAndConsumeToken(string tokenId, int userId, string operationType);
        string ComputeSecurityHash(string auditId, int userId, double creditsBefore, double creditsAfter, double amount);
    }

    public class CreditSecurityService : ICreditSecurityService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CreditSecurityService> _logger;
        private const string SECRET_SALT = "LH_CREDIT_SECURITY_2025_V1"; // Should be in config

        public CreditSecurityService(ApplicationDbContext context, ILogger<CreditSecurityService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// LEVEL 1 + LEVEL 2: Creates an audited credit operation with security hash
        /// </summary>
        public async Task<CreditAuditLog> CreateAuditedCreditOperation(
            int userId,
            string operationType,
            double creditsAmount,
            string sourceType,
            string? sourceReferenceId = null,
            int? relatedUserId = null,
            string? ipAddress = null,
            string? userAgent = null)
        {
            // Get user's current credits (BEFORE modification)
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User {userId} not found");

            var creditsBefore = user.Credits;
            var creditsAfter = creditsBefore + creditsAmount;

            // SECURITY LEVEL 1: Generate unique audit ID
            var auditId = Guid.NewGuid().ToString();

            // SECURITY LEVEL 2: Compute security hash
            var securityHash = ComputeSecurityHash(auditId, userId, creditsBefore, creditsAfter, creditsAmount);

            // Create audit log entry
            var auditLog = new CreditAuditLog
            {
                Id = auditId,
                UserId = userId,
                OperationType = operationType,
                CreditsAmount = creditsAmount,
                CreditsBefore = creditsBefore,
                CreditsAfter = creditsAfter,
                SourceType = sourceType,
                SourceReferenceId = sourceReferenceId,
                RelatedUserId = relatedUserId,
                SecurityHash = securityHash,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                IsValidated = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.CreditAuditLogs.Add(auditLog);

            // Apply the credit change
            user.Credits = creditsAfter;

            await _context.SaveChangesAsync();

            // Immediately validate the entry (LEVEL 2 verification)
            await ValidateAuditEntry(auditId);

            _logger.LogInformation(
                "[CREDIT_AUDIT] User={UserId} Op={Op} Amount={Amount} Before={Before} After={After} AuditId={AuditId}",
                userId, operationType, creditsAmount, creditsBefore, creditsAfter, auditId);

            return auditLog;
        }

        /// <summary>
        /// LEVEL 2: Validates that an audit entry's hash matches expected values
        /// </summary>
        public async Task<bool> ValidateAuditEntry(string auditLogId)
        {
            var auditLog = await _context.CreditAuditLogs.FindAsync(auditLogId);
            if (auditLog == null)
            {
                _logger.LogWarning("[SECURITY_VIOLATION] Audit entry not found: {AuditId}", auditLogId);
                return false;
            }

            // Recompute hash and compare
            var expectedHash = ComputeSecurityHash(
                auditLog.Id,
                auditLog.UserId,
                auditLog.CreditsBefore,
                auditLog.CreditsAfter,
                auditLog.CreditsAmount);

            if (auditLog.SecurityHash != expectedHash)
            {
                _logger.LogError(
                    "[SECURITY_VIOLATION] Hash mismatch for audit {AuditId}! Expected={Expected} Actual={Actual}",
                    auditLogId, expectedHash, auditLog.SecurityHash);
                return false;
            }

            // Verify credit math
            var expectedAfter = auditLog.CreditsBefore + auditLog.CreditsAmount;
            if (Math.Abs(auditLog.CreditsAfter - expectedAfter) > 0.001)
            {
                _logger.LogError(
                    "[SECURITY_VIOLATION] Credit math mismatch for audit {AuditId}! Before={Before} + Amount={Amount} != After={After}",
                    auditLogId, auditLog.CreditsBefore, auditLog.CreditsAmount, auditLog.CreditsAfter);
                return false;
            }

            // Mark as validated
            auditLog.IsValidated = true;
            auditLog.ValidatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// LEVEL 3: Generate security token for sensitive operations
        /// </summary>
        public async Task<CreditSecurityToken> GenerateSecurityToken(int userId, string operationType)
        {
            var tokenId = Guid.NewGuid().ToString();
            var rawToken = $"{tokenId}:{userId}:{operationType}:{DateTime.UtcNow.Ticks}:{SECRET_SALT}";
            var tokenHash = ComputeSha256Hash(rawToken);

            var token = new CreditSecurityToken
            {
                Id = tokenId,
                UserId = userId,
                TokenHash = tokenHash,
                OperationType = operationType,
                IsUsed = false,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5), // 5 minute validity
                CreatedAt = DateTime.UtcNow
            };

            _context.CreditSecurityTokens.Add(token);
            await _context.SaveChangesAsync();

            return token;
        }

        /// <summary>
        /// LEVEL 3: Validate and consume security token (anti-replay)
        /// </summary>
        public async Task<bool> ValidateAndConsumeToken(string tokenId, int userId, string operationType)
        {
            var token = await _context.CreditSecurityTokens.FindAsync(tokenId);
            if (token == null)
            {
                _logger.LogWarning("[SECURITY_VIOLATION] Token not found: {TokenId}", tokenId);
                return false;
            }

            // Check token ownership
            if (token.UserId != userId)
            {
                _logger.LogError("[SECURITY_VIOLATION] Token user mismatch! Token={TokenUser} Request={RequestUser}", 
                    token.UserId, userId);
                return false;
            }

            // Check operation type
            if (token.OperationType != operationType)
            {
                _logger.LogError("[SECURITY_VIOLATION] Token operation mismatch!");
                return false;
            }

            // Check if already used (replay attack prevention)
            if (token.IsUsed)
            {
                _logger.LogError("[SECURITY_VIOLATION] Token already used (replay attack?): {TokenId}", tokenId);
                return false;
            }

            // Check expiration
            if (token.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("[SECURITY_VIOLATION] Token expired: {TokenId}", tokenId);
                return false;
            }

            // Mark as used
            token.IsUsed = true;
            token.UsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Compute security hash for audit log validation
        /// </summary>
        public string ComputeSecurityHash(string auditId, int userId, double creditsBefore, double creditsAfter, double amount)
        {
            var data = $"{auditId}:{userId}:{creditsBefore:F4}:{creditsAfter:F4}:{amount:F4}:{SECRET_SALT}";
            return ComputeSha256Hash(data);
        }

        private static string ComputeSha256Hash(string rawData)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            foreach (var b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
