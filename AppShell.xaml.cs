namespace RetroHub
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("settings", typeof(Views.SettingsPage));
            Routing.RegisterRoute("power", typeof(Views.PowerMenu));
        }
    }
}
