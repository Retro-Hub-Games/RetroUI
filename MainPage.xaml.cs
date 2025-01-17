using System.Collections.ObjectModel;
using RetroHub.Models;
using Microsoft.Win32;
using System.Diagnostics;
using Microsoft.Maui.Devices;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using SharpDX.XInput;
using Microsoft.Maui.Controls;
using SharpDX.XInput;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#if WINDOWS
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.Gaming.Input;
#endif

namespace RetroHub
{
    public static class CollectionViewExtensions
    {
        public static IEnumerable<Element> GetVisibleElements(this CollectionView collectionView)
        {
            if (collectionView.ItemTemplate == null) return Enumerable.Empty<Element>();

            var elements = new List<Element>();
            foreach (var item in collectionView.ItemsSource)
            {
                var view = collectionView.ItemTemplate.CreateContent() as View;
                if (view != null)
                {
                    view.BindingContext = item;
                    elements.Add(view);
                }
            }
            return elements;
        }
    }

    public partial class MainPage : ContentPage, INotifyPropertyChanged, IDisposable
    {
        public ObservableCollection<GameInfo> InstalledApps { get; } = new();
        private ObservableCollection<GameInfo> _allApps = new();
        private int _selectedIndex = 0;
        private bool _gamepadConnected = false;
        private IDispatcherTimer _gamepadTimer;
        private bool _isSearchVisible;
        private bool _wasYButtonPressed;
        private SearchBar _searchBar;
        private CollectionView _gamesCollection;
        private Controller _controller;
        private bool _isDisposed;
        private IDispatcherTimer _inputTimer;
        private int _selectedUtilityIndex = -1;
        private Frame[] _utilityCards;
        private bool _isUtilitySelected = false;
        private double _currentScrollSpeed = 0;
        private const double BASE_SCROLL_SPEED = 5.0; // Reduced from default
        private const double MAX_SCROLL_SPEED = 15.0; // Reduced from default
        private const double ACCELERATION = 0.2; // Reduced from default
        private const double DECELERATION = 0.8; // Increased for slower deceleration
        private DateTime _lastUtilityInputTime = DateTime.MinValue;
        private const int UTILITY_INPUT_DELAY_MS = 150; // Slower utility card navigation
        private bool _isPowerMenuActive = false;
        private int _selectedGameIndex = 0;
        private List<GameInfo> _gamesList = new List<GameInfo>();

        public bool IsSearchVisible
        {
            get => _isSearchVisible;
            set
            {
                if (_isSearchVisible != value)
                {
                    _isSearchVisible = value;
                    OnPropertyChanged();
                    if (_isSearchVisible)
                    {
                        MainThread.BeginInvokeOnMainThread(() => _searchBar?.Focus());
                    }
                }
            }
        }

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;

            // Create a timer with higher polling rate for smoother input
            _inputTimer = Application.Current.Dispatcher.CreateTimer();
            _inputTimer.Interval = TimeSpan.FromMilliseconds(150); // Increased from default for smoother updates
            _inputTimer.Tick += InputTimer_Tick;
            _inputTimer.Start();
            
            // Get references to controls
            _searchBar = this.FindByName<SearchBar>("SearchBar");
            GamesScrollView = this.FindByName<ScrollView>("GamesScrollView");
            GamesCollection = this.FindByName<CollectionView>("GamesCollection");

            // Add selection changed handler
            GamesCollection.SelectionChanged += GamesCollection_SelectionChanged;

            // Initialize controller
            _controller = new Controller(UserIndex.One);

            // Initialize utility cards array
            _utilityCards = new Frame[]
            {
                this.FindByName<Frame>("SettingsCard"),
                this.FindByName<Frame>("StoreCard"),
                this.FindByName<Frame>("PowerCard")
            };
            
            LoadInstalledApps();
            InitializeGamepadSupport();
            InitializeGamepad();
        }

        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);
            
            if (Window != null)
            {
                Window.Activated += Window_Activated;
            }
        }

        private void Window_Activated(object sender, EventArgs e)
        {
#if WINDOWS
            var platformWindow = (sender as Microsoft.Maui.Controls.Window)?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (platformWindow != null)
            {
                platformWindow.Content.AddHandler(
                    Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                    new Microsoft.UI.Xaml.Input.KeyEventHandler(OnKeyDown),
                    true);
            }
#endif
        }

#if WINDOWS
        private void OnKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Space)
            {
                MainThread.BeginInvokeOnMainThread(() => ToggleSearch());
                args.Handled = true;
            }
        }
#endif

        protected override bool OnBackButtonPressed()
        {
            if (IsSearchVisible)
            {
                IsSearchVisible = false;
                return true;
            }
            return base.OnBackButtonPressed();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            this.Focused += MainPage_Focused;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            this.Focused -= MainPage_Focused;
            
            if (Window != null)
            {
                Window.Activated -= Window_Activated;
            }
            _inputTimer?.Stop();
        }

        private void MainPage_Focused(object sender, FocusEventArgs e)
        {
            this.Focus();
        }

        private void ToggleSearch()
        {
            IsSearchVisible = !IsSearchVisible;
        }

        private void InitializeGamepadSupport()
        {
            _gamepadTimer = Application.Current.Dispatcher.CreateTimer();
            _gamepadTimer.Interval = TimeSpan.FromMilliseconds(100);
            _gamepadTimer.Tick += GamepadTimer_Tick;
            _gamepadTimer.Start();
        }

        private void GamepadTimer_Tick(object sender, EventArgs e)
        {
#if WINDOWS
            var gamepad = Windows.Gaming.Input.Gamepad.Gamepads.FirstOrDefault();
            if (gamepad != null)
            {
                if (!_gamepadConnected)
                {
                    _gamepadConnected = true;
                    UpdateSelectedCard();
                }

                var reading = gamepad.GetCurrentReading();

                // Check Y button for search
                bool isYButtonPressed = (reading.Buttons & Windows.Gaming.Input.GamepadButtons.Y) != 0;
                if (isYButtonPressed && !_wasYButtonPressed)
                {
                    MainThread.BeginInvokeOnMainThread(() => ToggleSearch());
                }
                _wasYButtonPressed = isYButtonPressed;

                // Navigate left
                if ((reading.Buttons & Windows.Gaming.Input.GamepadButtons.DPadLeft) != 0 ||
                    reading.LeftThumbstickX < -0.5)
                {
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        UpdateSelectedCard();
                    }
                }
                // Navigate right
                else if ((reading.Buttons & Windows.Gaming.Input.GamepadButtons.DPadRight) != 0 ||
                         reading.LeftThumbstickX > 0.5)
                {
                    if (_selectedIndex < InstalledApps.Count - 1)
                    {
                        _selectedIndex++;
                        UpdateSelectedCard();
                    }
                }
                // Launch game with A button
                if ((reading.Buttons & Windows.Gaming.Input.GamepadButtons.A) != 0)
                {
                    LaunchSelectedGame();
                }
            }
            else
            {
                _gamepadConnected = false;
            }
#endif
        }

        private void UpdateSelectedCard()
        {
            if (_gamesCollection != null && InstalledApps.Count > 0)
            {
                _gamesCollection.ScrollTo(_selectedIndex, position: ScrollToPosition.MakeVisible);
                var selectedItem = InstalledApps[_selectedIndex];
                _gamesCollection.SelectedItem = selectedItem;
            }
        }

        private void LaunchSelectedGame()
        {
            if (_selectedIndex >= 0 && _selectedIndex < InstalledApps.Count)
            {
                var game = InstalledApps[_selectedIndex];
                LaunchGame(game);
            }
        }

        private void LaunchGame(GameInfo game)
        {
#if WINDOWS
            try
            {
                if (!string.IsNullOrEmpty(game.ExecutablePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = game.ExecutablePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", $"Failed to launch game: {ex.Message}", "OK");
                });
            }
#endif
        }

        private void LoadInstalledApps()
        {
#if WINDOWS
            try
            {
                // Get Steam installation path from registry
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                if (key != null)
                {
                    var steamPath = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        // Get Steam library folders
                        var libraryFolders = new List<string> { Path.Combine(steamPath, "steamapps") };
                        var libraryFoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                        
                        if (File.Exists(libraryFoldersVdf))
                        {
                            foreach (var line in File.ReadAllLines(libraryFoldersVdf))
                            {
                                if (line.Contains("\"path\""))
                                {
                                    var path = line.Split('"')[3].Replace("\\\\", "\\");
                                    libraryFolders.Add(Path.Combine(path, "steamapps"));
                                }
                            }
                        }

                        // Scan each library folder for installed games
                        foreach (var libraryFolder in libraryFolders)
                        {
                            if (Directory.Exists(libraryFolder))
                            {
                                var manifestFiles = Directory.GetFiles(libraryFolder, "appmanifest_*.acf");
                                foreach (var manifest in manifestFiles)
                                {
                                    var manifestContent = File.ReadAllText(manifest);
                                    var nameMatch = System.Text.RegularExpressions.Regex.Match(manifestContent, "\"name\"\\s+\"([^\"]+)\"");
                                    var appIdMatch = System.Text.RegularExpressions.Regex.Match(manifestContent, "\"appid\"\\s+\"([^\"]+)\"");
                                    
                                    if (nameMatch.Success && appIdMatch.Success)
                                    {
                                        var gameName = nameMatch.Groups[1].Value;
                                        var appId = appIdMatch.Groups[1].Value;

                                        // Steam artwork paths
                                        var steamAppsPath = Path.Combine(steamPath, "steam", "appcache", "librarycache");
                                        var headerImage = Path.Combine(steamAppsPath, $"{appId}_header.jpg");
                                        var heroImage = Path.Combine(steamAppsPath, $"{appId}_hero.jpg");
                                        var logoImage = Path.Combine(steamAppsPath, $"{appId}_logo.png");
                                        var backgroundImage = Path.Combine(steamAppsPath, $"{appId}_library_hero.jpg");

                                        _allApps.Add(new GameInfo 
                                        { 
                                            Name = gameName,
                                            AppId = appId,
                                            Icon = File.Exists(headerImage) ? headerImage : "dotnet_bot.png",
                                            HeroImage = File.Exists(heroImage) ? heroImage : null,
                                            LogoImage = File.Exists(logoImage) ? logoImage : null,
                                            BackgroundImage = File.Exists(backgroundImage) ? backgroundImage : null,
                                            ExecutablePath = $"steam://rungameid/{appId}",
                                            IsSteamGame = true
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback to sample data if there's an error
                var sampleApps = new[]
                {
                    new GameInfo { Name = "Sample Game 1", Icon = "dotnet_bot.png", IsSteamGame = false },
                    new GameInfo { Name = "Sample Game 2", Icon = "dotnet_bot.png", IsSteamGame = false }
                };
                foreach (var app in sampleApps)
                {
                    _allApps.Add(app);
                }
            }
#endif

            // Add all apps to the displayed list
            foreach (var app in _allApps)
            {
                InstalledApps.Add(app);
            }
            _gamesList = InstalledApps.ToList();
            GamesCollection.ItemsSource = _gamesList;
            UpdateGameSelection();
        }

        private void InitializeGamepad()
        {
            // Create a timer to poll the gamepad state
            _inputTimer = Application.Current.Dispatcher.CreateTimer();
            _inputTimer.Interval = TimeSpan.FromMilliseconds(150); // Increased from default for smoother updates
            _inputTimer.Tick += InputTimer_Tick;
            _inputTimer.Start();

            // Set scroll speed
            if (GamesScrollView != null)
            {
                GamesScrollView.HorizontalScrollBarVisibility = ScrollBarVisibility.Never;
            }
        }

        private async void InputTimer_Tick(object sender, EventArgs e)
        {
            // Skip all controller input if power menu is active
            if (_isPowerMenuActive)
                return;

            if (_controller != null && _controller.IsConnected)
            {
                var state = _controller.GetState();
                var buttons = state.Gamepad.Buttons;
                var thumbLeft = state.Gamepad.LeftThumbY;
                var thumbLeftX = state.Gamepad.LeftThumbX;
                const short THUMB_THRESHOLD = 8000; // Lower threshold for more responsive input
                var currentTime = DateTime.Now;

                // Down button or thumbstick down to enter utility cards section
                if (buttons.HasFlag(GamepadButtonFlags.DPadDown) || thumbLeft < -THUMB_THRESHOLD)
                {
                    if (!_isUtilitySelected)
                    {
                        _isUtilitySelected = true;
                        _selectedUtilityIndex = 0;
                        UpdateUtilityCardSelection();
                        ClearGameSelection();
                    }
                }
                // Up button or thumbstick up to exit utility cards section
                else if (buttons.HasFlag(GamepadButtonFlags.DPadUp) || thumbLeft > THUMB_THRESHOLD)
                {
                    if (_isUtilitySelected)
                    {
                        _isUtilitySelected = false;
                        ClearUtilityCardSelection();
                        _selectedGameIndex = Math.Max(0, Math.Min(_selectedGameIndex, _gamesList.Count - 1)); // Ensure valid game index
                        UpdateGameSelection(); // Restore game selection
                    }
                }
                // Handle navigation based on current section
                else if (_isUtilitySelected)
                {
                    // Reset scroll speed when in utility mode
                    _currentScrollSpeed = 0;

                    // Utility cards navigation with analog support
                    if (buttons.HasFlag(GamepadButtonFlags.DPadLeft) || thumbLeftX < -THUMB_THRESHOLD)
                    {
                        _selectedUtilityIndex = Math.Max(0, _selectedUtilityIndex - 1);
                        UpdateUtilityCardSelection();
                    }
                    else if (buttons.HasFlag(GamepadButtonFlags.DPadRight) || thumbLeftX > THUMB_THRESHOLD)
                    {
                        _selectedUtilityIndex = Math.Min(_utilityCards.Length - 1, _selectedUtilityIndex + 1);
                        UpdateUtilityCardSelection();
                    }

                    // A button activation
                    if (buttons.HasFlag(GamepadButtonFlags.A))
                    {
                        ActivateSelectedUtilityCard();
                    }
                }
                else
                {
                    // Games navigation with smooth scrolling and analog support
                    bool isScrolling = false;
                    double scrollAmount = 0;

                    // Calculate scroll amount based on thumbstick or d-pad
                    if (Math.Abs((int)thumbLeftX) > THUMB_THRESHOLD)
                    {
                        // Analog scrolling based on thumbstick position
                        double thumbPercentage = Math.Abs((int)thumbLeftX) / 32768.0; // Normalize to 0-1
                        _currentScrollSpeed = Math.Min(_currentScrollSpeed + (ACCELERATION * thumbPercentage), MAX_SCROLL_SPEED);
                        scrollAmount = _currentScrollSpeed * (thumbLeftX > 0 ? 1 : -1);
                        isScrolling = true;
                    }
                    else if (buttons.HasFlag(GamepadButtonFlags.DPadLeft))
                    {
                        _currentScrollSpeed = Math.Min(_currentScrollSpeed + ACCELERATION, MAX_SCROLL_SPEED);
                        scrollAmount = -(_currentScrollSpeed + BASE_SCROLL_SPEED);
                        isScrolling = true;
                    }
                    else if (buttons.HasFlag(GamepadButtonFlags.DPadRight))
                    {
                        _currentScrollSpeed = Math.Min(_currentScrollSpeed + ACCELERATION, MAX_SCROLL_SPEED);
                        scrollAmount = _currentScrollSpeed + BASE_SCROLL_SPEED;
                        isScrolling = true;
                    }

                    // Apply scrolling with smooth interpolation
                    if (isScrolling)
                    {
                        await GamesScrollView.ScrollToAsync(
                            Math.Max(0, GamesScrollView.ScrollX + scrollAmount), 
                            0, 
                            false
                        );
                        
                        // Find the visible game based on scroll position
                        var frames = GamesCollection.GetVisualTreeDescendants().OfType<Frame>().ToList();
                        double scrollX = GamesScrollView.ScrollX;
                        double viewportCenter = scrollX + (GamesScrollView.Width / 2);
                        
                        for (int i = 0; i < frames.Count && i < _gamesList.Count; i++)
                        {
                            var frame = frames[i];
                            double frameLeft = frame.X;
                            double frameRight = frame.X + frame.Width;
                            
                            if (viewportCenter >= frameLeft && viewportCenter <= frameRight)
                            {
                                if (_selectedGameIndex != i)
                                {
                                    _selectedGameIndex = i;
                                    UpdateGameSelection();
                                }
                                break;
                            }
                        }
                    }
                    // Apply deceleration when not scrolling
                    else if (_currentScrollSpeed > 0)
                    {
                        _currentScrollSpeed = Math.Max(0, _currentScrollSpeed * DECELERATION);
                        if (_currentScrollSpeed > 0.1) // Continue momentum scrolling
                        {
                            await GamesScrollView.ScrollToAsync(
                                Math.Max(0, GamesScrollView.ScrollX + (_currentScrollSpeed * Math.Sign(scrollAmount))),
                                0,
                                false
                            );
                        }
                    }
                    // Games navigation
                    if (buttons.HasFlag(GamepadButtonFlags.DPadLeft) || thumbLeftX < -THUMB_THRESHOLD)
                    {
                        _selectedGameIndex = Math.Max(0, _selectedGameIndex - 1);
                        UpdateGameSelection();
                        await GamesScrollView.ScrollToAsync(280 * _selectedGameIndex, 0, false); // 280 is the WidthRequest from XAML
                    }
                    else if (buttons.HasFlag(GamepadButtonFlags.DPadRight) || thumbLeftX > THUMB_THRESHOLD)
                    {
                        _selectedGameIndex = Math.Min(_gamesList.Count - 1, _selectedGameIndex + 1);
                        UpdateGameSelection();
                        await GamesScrollView.ScrollToAsync(280 * _selectedGameIndex, 0, false); // 280 is the WidthRequest from XAML
                    }

                    // Launch game with A button
                    if (buttons.HasFlag(GamepadButtonFlags.A) && _selectedGameIndex < _gamesList.Count)
                    {
                        LaunchGame(_gamesList[_selectedGameIndex]);
                    }
                }
            }
        }

        private void UpdateGameSelection()
        {
            if (_gamesList.Count == 0) return;

            // Keep index in bounds
            _selectedGameIndex = Math.Max(0, Math.Min(_selectedGameIndex, _gamesList.Count - 1));

            // Update visual selection
            var borders = GamesCollection.GetVisualTreeDescendants().OfType<Border>().ToList();
            var cardBorders = borders.Where(b => b.Parent is Frame && !(b.Parent.Parent is Border)).Take(_gamesList.Count).ToList();

            // Clear all highlights first
            foreach (var border in cardBorders)
            {
                border.BackgroundColor = Color.FromArgb("#1E1E1E");
            }

            // Set the selected card highlight
            if (_selectedGameIndex >= 0 && _selectedGameIndex < _gamesList.Count && _selectedGameIndex < cardBorders.Count)
            {
                cardBorders[_selectedGameIndex].BackgroundColor = Colors.Purple;
            }

            // Ensure selected game is visible
            if (_selectedGameIndex >= 0 && _selectedGameIndex < _gamesList.Count)
            {
                var selectedGame = _gamesList[_selectedGameIndex];
                GamesCollection.ScrollTo(selectedGame, animate: false, position: ScrollToPosition.MakeVisible);
            }
        }

        public void ClearGameSelection()
        {
            GamesCollection.SelectedItem = null;
            
            // Clear visual selection
            var borders = GamesCollection.GetVisualTreeDescendants().OfType<Border>().ToList();
            var cardBorders = borders.Where(b => b.Parent is Frame && !(b.Parent.Parent is Border)).ToList();
            
            foreach (var border in cardBorders)
            {
                border.BackgroundColor = Color.FromArgb("#1E1E1E");
            }
        }

        private void UpdateUtilityCardSelection()
        {
            for (int i = 0; i < _utilityCards.Length; i++)
            {
                _utilityCards[i].BackgroundColor = i == _selectedUtilityIndex ? Colors.DarkSlateBlue : Color.FromArgb("#1E1E1E");
            }
        }

        private void ClearUtilityCardSelection()
        {
            _selectedUtilityIndex = -1;
            foreach (var card in _utilityCards)
            {
                card.BackgroundColor = Color.FromArgb("#1E1E1E");
            }
        }

        private void ActivateSelectedUtilityCard()
        {
            // Clear any game selection before activating utility card
            GamesCollection.SelectedItem = null;

            switch (_selectedUtilityIndex)
            {
                case 0:
                    Settings_Tapped(null, null);
                    break;
                case 1:
                    Store_Tapped(null, null);
                    break;
                case 2:
                    Power_Tapped(null, null);
                    break;
            }
        }

        private async void Settings_Tapped(object sender, EventArgs e)
        {
            GamesCollection.SelectedItem = null;
            await Shell.Current.GoToAsync("settings");
        }

        private void Store_Tapped(object sender, EventArgs e)
        {
            GamesCollection.SelectedItem = null;
            OpenSteamStore();
        }

        private void OpenSteamStore()
        {
#if WINDOWS
            // Open Steam store
            Process.Start(new ProcessStartInfo
            {
                FileName = "steam://store",
                UseShellExecute = true
            });
#endif
        }

        private async void Power_Tapped(object sender, EventArgs e)
        {
            GamesCollection.SelectedItem = null;
            await ShowPowerOptions();
        }

        private async Task ShowPowerOptions()
        {
            // Disable game selection and mark power menu as active
            _isPowerMenuActive = true;
            GamesCollection.SelectionMode = SelectionMode.None;
            GamesCollection.SelectedItem = null;
            GamesCollection.IsEnabled = false;  // Completely disable the collection

            // Stop the input timer while power menu is open
            _inputTimer?.Stop();
            
            try 
            {
                await Navigation.PushModalAsync(new Views.PowerMenu());
            }
            finally 
            {
                // Always re-enable everything in finally block
                _isPowerMenuActive = false;
                GamesCollection.SelectionMode = SelectionMode.Single;
                GamesCollection.IsEnabled = true;  // Re-enable the collection

                // Clear any potential selection that might have occurred
                GamesCollection.SelectedItem = null;

                // Restart the input timer
                _inputTimer?.Start();
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = e.NewTextValue?.ToLower() ?? "";
            InstalledApps.Clear();

            foreach (var app in _allApps.Where(app => 
                string.IsNullOrEmpty(searchText) || 
                app.Name?.ToLower().Contains(searchText) == true))
            {
                InstalledApps.Add(app);
            }
        }

        private async void AppCard_Tapped(object sender, TappedEventArgs e)
        {
            if (sender is Border border && border.BindingContext is GameInfo game)
            {
#if WINDOWS
                try
                {
                    if (!string.IsNullOrEmpty(game.ExecutablePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = game.ExecutablePath,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"Failed to launch game: {ex.Message}", "OK");
                }
#endif
            }
        }

        private void OnGameSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUtilitySelected)
            {
                // Clear selection if we're in utility cards mode
                GamesCollection.SelectedItem = null;
            }
        }

        private void GamesCollection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPowerMenuActive)
            {
                // If power menu is active, prevent selection
                GamesCollection.SelectedItem = null;
                return;
            }

            // Normal selection handling here
            if (GamesCollection.SelectedItem != null)
            {
                // Your existing game launch logic
                var selectedGame = GamesCollection.SelectedItem as GameInfo;
                if (selectedGame != null)
                {
                    LaunchGame(selectedGame);
                }
            }
        }

        public new event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _inputTimer?.Stop();
                _controller = null;
                _isDisposed = true;
            }
        }
    }
}
