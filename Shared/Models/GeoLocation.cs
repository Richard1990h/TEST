namespace LittleHelperAI.Shared.Models
{
    public class GeoLocation
    {
        public string Country { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty; // IP
    }
}
