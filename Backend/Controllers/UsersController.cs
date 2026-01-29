using LittleHelperAI.Data;
using LittleHelperAI.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LittleHelperAI.Backend.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }


        // 🔁 Live credits (DB = source of truth)
        [HttpGet("{id}/credits")]
        public async Task<IActionResult> GetLiveCredits(int id)
        {
            var credits = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => u.Credits)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                credits = Math.Round(credits, 2)
            });
        }


        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId = int.Parse(User.FindFirst("id")!.Value);

            var user = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.Role,
                    u.Credits
                })
                .FirstAsync();

            return Ok(user);
        }


        // 🔄 Get all users
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var users = await _context.Users
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    Role = u.Role,
                    Credits = u.Credits
                })
                .ToListAsync();

            return Ok(users);
        }

        // 🛠️ Promote user to Admin
        [HttpPost("promote/{id}")]
        public async Task<IActionResult> Promote(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            user.Role = "Admin";
            await _context.SaveChangesAsync();

            return Ok(new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                Credits = user.Credits
            });
        }

        // 🔻 Demote user to regular
        [HttpPost("demote/{id}")]
        public async Task<IActionResult> Demote(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            user.Role = "User";
            await _context.SaveChangesAsync();

            return Ok(new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                Credits = user.Credits
            });
        }

        // 📝 Update user credits to a specific value
        [HttpPost("update-credits")]
        public async Task<IActionResult> UpdateCredits([FromBody] CreditUpdateModel model)
        {
            var user = await _context.Users.FindAsync(model.UserId);
            if (user == null)
                return NotFound();

            user.Credits = model.Credits;
            await _context.SaveChangesAsync();

            return Ok(new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                Credits = user.Credits
            });
        }

        public class CreditUpdateModel
        {
            public int UserId { get; set; }
            public double Credits { get; set; }
        }

        // ❌ Delete user
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "User deleted", id });
        }
    }
}
