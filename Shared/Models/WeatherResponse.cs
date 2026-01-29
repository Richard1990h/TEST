namespace LittleHelperAI.Shared.Models
{
    public class WeatherResponse
    {
        public Location location { get; set; } = new();
        public Current current { get; set; } = new();

        public class Location
        {
            public string name { get; set; } = string.Empty;
            public string region { get; set; } = string.Empty;
            public string country { get; set; } = string.Empty;
            public string localtime { get; set; } = string.Empty;
        }

        public class Current
        {
            public double temp_c { get; set; }
            public string condition_text { get; set; } = string.Empty;
            public double wind_kph { get; set; }
            public int humidity { get; set; }
        }
    }
}
