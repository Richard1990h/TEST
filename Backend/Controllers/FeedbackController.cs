using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Data;
using LittleHelperAI.Models; // Feedback + Knowledge (entities)
using LittleHelperAI.Shared.Models; // ✅ Correct FeedbackItem DTO

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/feedback")]
    public class FeedbackController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FeedbackController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Accepts user feedback for a specific chat response.
        /// If marked helpful, saves to knowledge base for future reuse.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] FeedbackItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Message) || string.IsNullOrWhiteSpace(item.Response))
                return BadRequest("Invalid feedback");

            // ✅ Convert DTO to database entity
            var feedback = new Feedback
            {
                UserId = item.UserId,
                Message = item.Message,
                Response = item.Response,
                IsHelpful = item.IsHelpful,
                CreatedAt = DateTime.UtcNow
            };

            _context.Feedback.Add(feedback);
            await _context.SaveChangesAsync();

            // ✅ Add to knowledge base if user approved it
            if (item.IsHelpful)
            {
                var normalized = item.Message.Trim().ToLower();
                var exists = await _context.Knowledge.FirstOrDefaultAsync(k => k.Question == normalized);

                if (exists == null)
                {
                    _context.Knowledge.Add(new Knowledge
                    {
                        Question = normalized,
                        Answer = item.Response.Trim(),
                        AddedByUserId = item.UserId,
                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new { success = true });
        }
    }
}
