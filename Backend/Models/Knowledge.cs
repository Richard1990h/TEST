namespace LittleHelperAI.Models
{
    public class Knowledge
    {
        public int Id { get; set; }
        public string Question { get; set; } = "";
        public string Answer { get; set; } = "";
        public int AddedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
