using Microsoft.Maui.ApplicationModel;

namespace RetroHub
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(MainPage);

            // Set default size and position
            window.Width = 1920;
            window.Height = 1080;
            window.X = 0;
            window.Y = 0;
            
            // Set maximum dimensions
            window.MaximumWidth = double.PositiveInfinity;
            window.MaximumHeight = double.PositiveInfinity;

            // Handle window creation completed
            window.Created += (s, e) =>
            {
#if WINDOWS
                var platformWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (platformWindow != null)
                {
                    // Get window handle
                    var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                    
                    // Get AppWindow
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                    
                    // Set to fullscreen
                    if (appWindow != null)
                    {
                        appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                    }
                }
#endif
            };

            return window;
        }
    }
}