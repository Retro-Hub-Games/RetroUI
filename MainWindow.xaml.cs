using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using SharpDX.XInput;
using System.Threading.Tasks;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RetroUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Controller controller;
        private int currentSelectedIndex = 0;
        private bool isGamepadInitialized = false;
        private Border previousSelectedBorder = null;
        public ObservableCollection<AppInfo> allApps { get; private set; } // Store all apps
        private string currentSearchText = "";
        private AppInfo currentlyPlaying;
        private const string AppName = "RetroHub";
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private SettingsPage settingsPage;
        private SteamGameMonitor gameMonitor;

        public ObservableCollection<AppInfo> InstalledApps { get; set; }

        public AppInfo NowPlaying
        {
            get => currentlyPlaying;
            set
            {
                if (currentlyPlaying != value)
                {
                    currentlyPlaying = value;
                    IsGameRunning = value != null; // Update IsGameRunning based on NowPlaying
                    OnPropertyChanged(nameof(NowPlaying));
                }
            }
        }
        
        private bool _isGameRunning;
        public bool IsGameRunning
        {
            get => _isGameRunning;
            set
            {
                if (_isGameRunning != value)
                {
                    _isGameRunning = value;
                    OnPropertyChanged(nameof(IsGameRunning));
                }
            }
        }

        private struct GamepadState
        {
            public short LeftThumbX;
            public GamepadButtonFlags Buttons;

            public static GamepadState FromGamepad(Gamepad gamepad)
            {
                return new GamepadState
                {
                    LeftThumbX = gamepad.LeftThumbX,
                    Buttons = gamepad.Buttons
                };
            }

            public bool Equals(GamepadState other)
            {
                return LeftThumbX == other.LeftThumbX && 
                       Buttons == other.Buttons;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            InstalledApps = new ObservableCollection<AppInfo>();
            allApps = new ObservableCollection<AppInfo>(); // Initialize allApps
            LoadInstalledApps();
            DataContext = this;
            
            // Set window to full screen
            WindowState = WindowState.Maximized;
            
            // Initialize gamepad support
            InitializeGamepad();
            
            // Add key handler for escape to exit
            KeyDown += MainWindow_KeyDown;
            
            // Add this debug code temporarily
            try
            {
                var uri = new Uri("pack://application:,,,/Images/retrohub-logo.png");
                var bitmap = new BitmapImage(uri);
                // If this works, the image exists and is accessible
                Debug.WriteLine("Image loaded successfully");
            }
            catch (Exception ex)
            {
                // This will help identify the problem
                Debug.WriteLine($"Error loading image: {ex.Message}");
                MessageBox.Show($"Error loading image: {ex.Message}");
            }
            
            // Set up auto-start
            SetupAutoStart();
            gameMonitor = new SteamGameMonitor();
        }

        private void InitializeGamepad()
        {
            controller = new Controller(UserIndex.One);
            isGamepadInitialized = controller.IsConnected;
            
            if (isGamepadInitialized)
            {
                // Start polling for gamepad input
                Task.Run(GamepadPollingLoop);
            }
        }

        private async Task GamepadPollingLoop()
        {
            const int pollDelay = 16; // Approximately 60Hz refresh rate
            var lastState = new GamepadState();

            while (isGamepadInitialized)
            {
                if (controller.IsConnected && !IsGameRunning)
                {
                    var gamepad = controller.GetState().Gamepad;
                    var currentState = GamepadState.FromGamepad(gamepad);
                    
                    // Only process input if the state has changed
                    if (!currentState.Equals(lastState))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            // Handle thumbstick movement
                            if (gamepad.LeftThumbX > 20000) // Right movement
                            {
                                MoveSelection(1);
                            }
                            else if (gamepad.LeftThumbX < -20000) // Left movement
                            {
                                MoveSelection(-1);
                            }

                            // Handle buttons
                            if (gamepad.Buttons.HasFlag(GamepadButtonFlags.A))
                            {
                                LaunchSelectedApp();
                            }
                            else if (gamepad.Buttons.HasFlag(GamepadButtonFlags.Y))
                            {
                                ToggleSearch();
                            }
                            else if (gamepad.Buttons.HasFlag(GamepadButtonFlags.B))
                            {
                                if (MainContent.Visibility == Visibility.Visible)
                                {
                                    var romHome = new RomHome(this);
                                    NavigateToRomHome(romHome);
                                }
                                else if (MainFrame.Content is RomHome)
                                {
                                    NavigateToMain();
                                }
                            }
                            else if (gamepad.Buttons.HasFlag(GamepadButtonFlags.Start))
                            {
                                PowerMenuOverlay.Visibility = PowerMenuOverlay.Visibility == Visibility.Collapsed ? 
                                    Visibility.Visible : Visibility.Collapsed;
                            }
                        });

                        lastState = currentState;
                    }
                }
                
                await Task.Delay(pollDelay);
            }
        }

        private void MoveSelection(int direction)
        {
            if (InstalledApps.Count == 0) return;

            // Remove highlight from previous selection
            if (previousSelectedBorder != null)
            {
                previousSelectedBorder.BorderBrush = null;
                previousSelectedBorder.BorderThickness = new Thickness(0);
            }

            // Update selection index
            currentSelectedIndex += direction;
            if (currentSelectedIndex >= InstalledApps.Count) currentSelectedIndex = 0;
            if (currentSelectedIndex < 0) currentSelectedIndex = InstalledApps.Count - 1;

            // Get the ItemsControl
            var itemsControl = (ItemsControl)FindName("AppsList");
            if (itemsControl != null)
            {
                // Get the container and highlight the new selection
                var container = (ContentPresenter)itemsControl.ItemContainerGenerator
                    .ContainerFromIndex(currentSelectedIndex);
                
                if (container != null)
                {
                    var border = VisualTreeHelper.GetChild(container, 0) as Border;
                    if (border != null)
                    {
                        border.BorderBrush = Brushes.White;
                        border.BorderThickness = new Thickness(2);
                        border.ScrollIntoView(); // Custom extension method needed
                        previousSelectedBorder = border;
                    }
                }
            }
        }

        private void LaunchSelectedApp()
        {
            if (currentSelectedIndex >= 0 && currentSelectedIndex < InstalledApps.Count)
            {
                var app = InstalledApps[currentSelectedIndex];
                try
                {
                    var process = Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
                    if (process != null)
                    {
                        NowPlaying = app;
                        IsGameRunning = true;
                        isGamepadInitialized = false; // Disable gamepad input

                        // Start monitoring the game process
                        gameMonitor.StartMonitoring(process, () =>
                        {
                            // This will be called when the game closes
                            Dispatcher.Invoke(() =>
                            {
                                NowPlaying = null;
                                IsGameRunning = false;
                                isGamepadInitialized = true; // Re-enable gamepad input
                                InitializeGamepad(); // Reinitialize gamepad
                            });
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch {app.Name}: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    NowPlaying = null;
                    IsGameRunning = false;
                    isGamepadInitialized = true;
                    InitializeGamepad();
                }
            }
        }

        private string GetSteamAppId(string path)
        {
            try
            {
                // Try to extract Steam App ID from the path
                var match = System.Text.RegularExpressions.Regex.Match(path, @"steamapps\\common\\([^\\]+)");
                if (match.Success)
                {
                    return match.Groups[1].Value.ToLower();
                }
            }
            catch { }
            return null;
        }

        private void LoadInstalledApps()
        {
            InstalledApps.Clear();
            allApps.Clear();

            // First, try to load Steam games
            LoadSteamGames();

            // Then load other games as before...
            // (keep your existing game directory scanning code)
        }

        private void LoadSteamGames()
        {
            try
            {
                // Try to find Steam installation from registry
                string steamPath = GetSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    var steamLibraries = GetSteamLibraryFolders(steamPath);
                    foreach (var libraryPath in steamLibraries)
                    {
                        string steamAppsPath = Path.Combine(libraryPath, "steamapps");
                        if (Directory.Exists(steamAppsPath))
                        {
                            // Process manifest files
                            foreach (var manifestFile in Directory.GetFiles(steamAppsPath, "appmanifest_*.acf"))
                            {
                                ProcessSteamManifest(manifestFile, steamAppsPath);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading Steam games: {ex.Message}");
            }
        }

        private string GetSteamPath()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam") ??
                                 Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    return key?.GetValue("InstallPath") as string;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding Steam path: {ex.Message}");
                return null;
            }
        }

        private List<string> GetSteamLibraryFolders(string steamPath)
        {
            var libraries = new List<string> { steamPath };

            try
            {
                string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFoldersPath))
                {
                    string[] lines = File.ReadAllLines(libraryFoldersPath);
                    foreach (string line in lines)
                    {
                        if (line.Contains("\"path\""))
                        {
                            string path = line.Split('"')[3].Replace("\\\\", "\\");
                            if (Directory.Exists(path) && !libraries.Contains(path))
                            {
                                libraries.Add(path);
                                Debug.WriteLine($"Found Steam library: {path}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading Steam library folders: {ex.Message}");
            }

            return libraries;
        }

        private void ProcessSteamManifest(string manifestPath, string steamAppsPath)
        {
            try
            {
                string[] manifestLines = File.ReadAllLines(manifestPath);
                string installDir = null;
                string gameName = null;

                foreach (string line in manifestLines)
                {
                    string trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("\"installdir\""))
                    {
                        installDir = trimmedLine.Split('"')[3];
                    }
                    else if (trimmedLine.StartsWith("\"name\""))
                    {
                        gameName = trimmedLine.Split('"')[3];
                    }
                }

                if (!string.IsNullOrEmpty(installDir) && !string.IsNullOrEmpty(gameName))
                {
                    string gameDir = Path.Combine(steamAppsPath, "common", installDir);
                    if (Directory.Exists(gameDir))
                    {
                        // Find the main game executable
                        var exeFiles = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories)
                            .Where(exe => !IsExcludedExecutable(exe))
                            .OrderByDescending(exe => new FileInfo(exe).Length);

                        foreach (var exe in exeFiles.Take(1)) // Take the largest exe file
                        {
                            var icon = GetAppIcon(exe);
                            if (icon != null)
                            {
                                var appInfo = new AppInfo
                                {
                                    Name = gameName, // Use the name from Steam manifest
                                    Icon = icon,
                                    Path = exe
                                };

                                if (!InstalledApps.Any(app => app.Path.Equals(appInfo.Path, StringComparison.OrdinalIgnoreCase)))
                                {
                                    Debug.WriteLine($"Found Steam game: {appInfo.Name} at {appInfo.Path}");
                                    InstalledApps.Add(appInfo);
                                    allApps.Add(appInfo);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing Steam manifest {manifestPath}: {ex.Message}");
            }
        }

        private bool IsExcludedDirectory(string directory)
        {
            string[] excludedDirs = new[]
            {
                "windows", "system32", "program files", "microsoft", 
                "common files", "temp", "tmp", "cache", "crash reports",
                "logs", "redistributable", "redist", "installer",
                "uninstall", "backup", "common"
            };

            string dirName = Path.GetFileName(directory).ToLower();
            return excludedDirs.Any(excluded => dirName.Contains(excluded));
        }

        private void ProcessGameExecutable(string exePath)
        {
            try
            {
                var fileInfo = new FileInfo(exePath);
                // Only process files larger than 5MB (to skip small utility executables)
                if (fileInfo.Length < 5000000) return;

                var icon = GetAppIcon(exePath);
                if (icon != null)
                {
                    var appInfo = new AppInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(exePath),
                        Icon = icon,
                        Path = exePath
                    };

                    // Check if this game is already added (avoid duplicates)
                    if (!InstalledApps.Any(app => app.Path.Equals(appInfo.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        Debug.WriteLine($"Found game: {appInfo.Name} at {appInfo.Path}");
                        InstalledApps.Add(appInfo);
                        allApps.Add(appInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing executable {exePath}: {ex.Message}");
            }
        }

        private bool IsExcludedExecutable(string exePath)
        {
            string[] excludedTerms = new[]
            {
                "unins", "crash", "launcher", "config", "setup", "redist", "update",
                "prereq", "dotnet", "vcredist", "installer", "settings", "tool",
                "helper", "runtime", "service", "manager", "support", "diagnostic"
            };

            string fileName = Path.GetFileNameWithoutExtension(exePath).ToLower();
            return excludedTerms.Any(term => fileName.Contains(term));
        }

        private BitmapSource GetAppIcon(string exePath)
        {
            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon != null)
                    {
                        // Get the largest size icon
                        using (var bitmap = icon.ToBitmap())
                        {
                            // Create a new bitmap with the desired size (64x64 for better quality)
                            using (var resized = new System.Drawing.Bitmap(64, 64))
                            {
                                using (var graphics = System.Drawing.Graphics.FromImage(resized))
                                {
                                    // Set high quality interpolation mode
                                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                                    // Draw the icon with a white background for better appearance
                                    graphics.Clear(System.Drawing.Color.Transparent);
                                    graphics.DrawImage(bitmap, 0, 0, 64, 64);

                                    // Convert to BitmapSource with higher DPI
                                    var handle = resized.GetHbitmap();
                                    try
                                    {
                                        return Imaging.CreateBitmapSourceFromHBitmap(
                                            handle,
                                            IntPtr.Zero,
                                            Int32Rect.Empty,
                                            BitmapSizeOptions.FromWidthAndHeight(64, 64));
                                    }
                                    finally
                                    {
                                        DeleteObject(handle);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting icon from {exePath}: {ex.Message}");
            }
            return null;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (controller != null)
            {
                // Stop the polling loop by setting isGamepadInitialized to false
                isGamepadInitialized = false;
            }
        }

        private void AppCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is AppInfo app)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch {app.Name}: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (SearchBorder.Visibility == Visibility.Visible)
                {
                    ToggleSearch();
                }
                else
                {
                    Close();
                }
            }
        }

        private void ToggleSearch()
        {
            if (SearchBorder.Visibility == Visibility.Collapsed)
            {
                SearchBorder.Visibility = Visibility.Visible;
                SearchBox.Focus();
            }
            else
            {
                SearchBorder.Visibility = Visibility.Collapsed;
                SearchBox.Text = "";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            currentSearchText = SearchBox.Text.ToLower();
            FilterApps();
        }

        private void FilterApps()
        {
            InstalledApps.Clear();
            var filteredApps = allApps.Where(app => 
                app.Name.ToLower().Contains(currentSearchText));
            
            foreach (var app in filteredApps)
            {
                InstalledApps.Add(app);
            }
        }

        private void SetupAutoStart()
        {
            try
            {
                // Get the path of the executable
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                exePath = exePath.Replace(".dll", ".exe"); // Fix for .NET Core/5+ publishing

                // Registry method
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key != null)
                    {
                        // Add the application to the startup list
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }

                // Backup method using Startup folder
                string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolderPath, $"{AppName}.lnk");

                if (!File.Exists(shortcutPath))
                {
                    CreateShortcut(exePath, shortcutPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set up auto-start: {ex.Message}");
                // Optionally show a message to the user
                // MessageBox.Show("Failed to set up auto-start. You may need to run the application as administrator.");
            }
        }

        private void CreateShortcut(string exePath, string shortcutPath)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            var shortcut = shell.CreateShortcut(shortcutPath);
            
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.Description = "Launch RetroHub";
            shortcut.Save();
        }

        private void Store_Click(object sender, RoutedEventArgs e)
        {
            // Implement store functionality
            MessageBox.Show("Store functionality coming soon!");
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            NavigateToSettings();
        }

        public void NavigateToSettings()
        {
            MainContent.Visibility = Visibility.Collapsed;
            if (settingsPage == null)
            {
                settingsPage = new SettingsPage(this);
            }
            MainFrame.Navigate(settingsPage);
        }

        public void NavigateToMain()
        {
            MainFrame.Navigate(null);
            MainContent.Visibility = Visibility.Visible;
        }

        public bool IsAutoStartEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                return key?.GetValue(AppName) != null;
            }
        }

        public void SetAutoStart(bool enable)
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                exePath = exePath.Replace(".dll", ".exe");

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(AppName, $"\"{exePath}\"");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set auto-start: {ex.Message}");
            }
        }

        private void Power_Click(object sender, RoutedEventArgs e)
        {
            PowerMenuOverlay.Visibility = Visibility.Visible;
        }

        private void CancelPowerMenu_Click(object sender, RoutedEventArgs e)
        {
            PowerMenuOverlay.Visibility = Visibility.Collapsed;
        }

        private void Shutdown_Click(object sender, RoutedEventArgs e)
        {
            PowerMenuOverlay.Visibility = Visibility.Collapsed;
            var result = MessageBox.Show("Are you sure you want to shutdown the computer?", 
                "Confirm Shutdown", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                Process.Start("shutdown", "/s /t 0");
            }
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            PowerMenuOverlay.Visibility = Visibility.Collapsed;
            var result = MessageBox.Show("Are you sure you want to restart the computer?", 
                "Confirm Restart", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                Process.Start("shutdown", "/r /t 0");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void NavigateToRomHome(RomHome romHome)
        {
            MainContent.Visibility = Visibility.Collapsed;
            MainFrame.Navigate(romHome);
        }
    }

    public class AppInfo
    {
        public string Name { get; set; }
        public BitmapSource Icon { get; set; }
        public string Path { get; set; }
    }

    // Extension method for scrolling
    public static class UIElementExtensions
    {
        public static void ScrollIntoView(this FrameworkElement element)
        {
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null && !(parent is ScrollViewer))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is ScrollViewer scrollViewer)
            {
                var elementPosition = element.TransformToAncestor(scrollViewer)
                    .Transform(new Point(0, 0));
                scrollViewer.ScrollToHorizontalOffset(elementPosition.X);
            }
        }
    }
}
