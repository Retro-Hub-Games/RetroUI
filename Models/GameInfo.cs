namespace RetroHub.Models
{
    public class GameInfo
    {
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public string? ExecutablePath { get; set; }
        public string? AppId { get; set; }
        public string? BackgroundImage { get; set; }
        public string? HeroImage { get; set; }
        public string? LogoImage { get; set; }
        public bool IsSteamGame { get; set; }
    }
}
