using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System;
using System.Windows.Input;
using System.Windows.Threading;
using SharpDX.XInput;
using System.Threading.Tasks;

namespace RetroUI
{
    public partial class RomHome : Page
    {
        private MainWindow mainWindow;
        private readonly Dispatcher _dispatcher;
        public ObservableCollection<SystemCategory> Systems { get; private set; }
        private Controller xboxController;
        private SwitchProController switchController;
        private bool usingSwitchController = false;
        private bool isGamepadInitialized = false;

        public RomHome(MainWindow mainWindow)
        {
            InitializeComponent();
            this.mainWindow = mainWindow;
            _dispatcher = Dispatcher.CurrentDispatcher;
            Systems = new ObservableCollection<SystemCategory>();
            DataContext = this;

            // Register for keyboard events
            this.KeyDown += RomHome_KeyDown;
            this.Focusable = true;
            this.Focus();

            // Initialize gamepad
            InitializeGamepad();

            // Register for unloaded event
            this.Unloaded += RomHome_Unloaded;
        }

        private void InitializeGamepad()
        {
            xboxController = new Controller(UserIndex.One);
            switchController = new SwitchProController();
            
            if (xboxController.IsConnected)
            {
                isGamepadInitialized = true;
                usingSwitchController = false;
            }
            else if (switchController.IsConnected)
            {
                isGamepadInitialized = true;
                usingSwitchController = true;
            }
            
            if (isGamepadInitialized)
            {
                Task.Run(GamepadPollingLoop);
            }
        }

        private async Task GamepadPollingLoop()
        {
            const int pollDelay = 32;
            const int movementDelay = 150;
            var lastMoveTime = DateTime.Now;

            while (isGamepadInitialized)
            {
                if (usingSwitchController)
                {
                    switchController.Update();
                    if (switchController.IsConnected)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            var now = DateTime.Now;
                            // Handle Switch Pro Controller navigation
                            if ((now - lastMoveTime).TotalMilliseconds >= movementDelay)
                            {
                                if (switchController.Buttons.HasFlag(SwitchProButtons.B))
                                {
                                    mainWindow.NavigateToMain();
                                }
                                // Add more Switch Pro Controller specific navigation here
                                lastMoveTime = now;
                            }
                        });
                    }
                }
                else if (xboxController.IsConnected)
                {
                    var state = xboxController.GetState();
                    if (state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.B))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            mainWindow.NavigateToMain();
                        });
                    }
                    // Add more Xbox controller specific navigation here
                }
                
                await Task.Delay(pollDelay);
            }
        }

        private void RomHome_KeyDown(object sender, KeyEventArgs e)
        {
            if (!mainWindow.IsGameRunning && (e.Key == Key.Escape || e.Key == Key.B))
            {
                mainWindow.NavigateToMain();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!mainWindow.IsGameRunning)
            {
                mainWindow.NavigateToMain();
            }
        }

        private void Rom_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (mainWindow.IsGameRunning) return;

            if (sender is Border border && border.DataContext is RomInfo rom)
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo(rom.Path) { UseShellExecute = true });
                    if (process != null)
                    {
                        // Create AppInfo for the ROM
                        var appInfo = new AppInfo
                        {
                            Name = rom.Name,
                            Path = rom.Path,
                            Icon = rom.SystemIcon
                        };

                        // Set as currently playing
                        mainWindow.NowPlaying = appInfo;
                        
                        process.EnableRaisingEvents = true;
                        process.Exited += (s, args) =>
                        {
                            _dispatcher.Invoke(() =>
                            {
                                mainWindow.NowPlaying = null;
                                Debug.WriteLine($"ROM closed: {rom.Name}");
                            });
                        };

                        Debug.WriteLine($"Launched ROM: {rom.Name} ({rom.Path})");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch ROM {rom.Name}: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void AddSystem(SystemCategory system)
        {
            if (_dispatcher.CheckAccess())
            {
                if (!Systems.Contains(system))
                {
                    Systems.Add(system);
                    Debug.WriteLine($"Added system: {system.Name} with {system.Roms.Count} ROMs");
                }
            }
            else
            {
                _dispatcher.Invoke(() => AddSystem(system));
            }
        }

        private void RomHome_Unloaded(object sender, RoutedEventArgs e)
        {
            isGamepadInitialized = false;
            this.Unloaded -= RomHome_Unloaded;
        }

        public void Dispose()
        {
            isGamepadInitialized = false;
        }
    }

    public class SystemCategory
    {
        private readonly Dispatcher _dispatcher;
        public string Name { get; set; }
        public ObservableCollection<RomInfo> Roms { get; private set; }

        public SystemCategory()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Roms = new ObservableCollection<RomInfo>();
        }

        public void AddRom(RomInfo rom)
        {
            if (_dispatcher.CheckAccess())
            {
                Roms.Add(rom);
            }
            else
            {
                _dispatcher.Invoke(() => Roms.Add(rom));
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is SystemCategory other)
            {
                return Name == other.Name;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Name?.GetHashCode() ?? 0;
        }
    }

    public class RomInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public BitmapSource SystemIcon { get; set; }
        public string System { get; set; }
    }
} 