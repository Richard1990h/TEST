namespace LittleHelperAI.Shared.Models
{
    /// <summary>
    /// Data Transfer Object (DTO) for authenticated user information.
    /// This model is used across frontend and backend to pass logged-in user data.
    /// </summary>
    public class UserDto
    {
        /// <summary>
        /// Unique identifier of the user (usually primary key in the database).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Public username for the user.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Registered email address of the user.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Role of the user (e.g., "User", "Admin").
        /// Used for role-based access throughout the application.
        /// </summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// Number of credits available to the user for using services.
        /// Now supports partial credits like 0.1, 0.25, etc.
        /// </summary>
        public double Credits { get; set; } = 0.0;

        /// <summary>
        /// The last time the user successfully logged into the system.
        /// Nullable in case the user never logged in before.
        /// </summary>
        public DateTime? LastLogin { get; set; }
    }
}
