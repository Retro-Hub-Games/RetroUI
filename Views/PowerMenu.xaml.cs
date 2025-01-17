using SharpDX.XInput;
using System.Diagnostics;

namespace RetroHub.Views;

public partial class PowerMenu : ContentPage, IDisposable
{
    private Controller _controller;
    private bool _isDisposed;
    private IDispatcherTimer _inputTimer;
    private Frame[] _optionButtons;
    private int _selectedIndex = 0;
    private DateTime _lastInputTime = DateTime.MinValue;
    private const int INPUT_DELAY_MS = 200; // Slower navigation

    public PowerMenu()
    {
        InitializeComponent();
        InitializeGamepad();
        InitializeButtons();
    }

    private void InitializeGamepad()
    {
        _controller = new Controller(UserIndex.One);
        _optionButtons = new[] { ShutdownButton, RestartButton, ExitButton, CancelButton };
        
        // Create a timer to poll the gamepad state
        _inputTimer = Application.Current.Dispatcher.CreateTimer();
        _inputTimer.Interval = TimeSpan.FromMilliseconds(50);
        _inputTimer.Tick += InputTimer_Tick;
        _inputTimer.Start();

        // Set initial selection
        UpdateButtonSelection();
    }

    private void InitializeButtons()
    {
        ShutdownButton.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(OnShutdown) });
        RestartButton.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(OnRestart) });
        ExitButton.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(OnExit) });
        CancelButton.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(OnCancel) });
    }

    private void InputTimer_Tick(object sender, EventArgs e)
    {
        if (_controller != null && _controller.IsConnected)
        {
            var state = _controller.GetState();
            var buttons = state.Gamepad.Buttons;
            var currentTime = DateTime.Now;

            // Only process input after delay
            if ((currentTime - _lastInputTime).TotalMilliseconds >= INPUT_DELAY_MS)
            {
                if (buttons.HasFlag(GamepadButtonFlags.DPadUp))
                {
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    UpdateButtonSelection();
                    _lastInputTime = currentTime;
                }
                else if (buttons.HasFlag(GamepadButtonFlags.DPadDown))
                {
                    _selectedIndex = Math.Min(_optionButtons.Length - 1, _selectedIndex + 1);
                    UpdateButtonSelection();
                    _lastInputTime = currentTime;
                }
                else if (buttons.HasFlag(GamepadButtonFlags.A))
                {
                    HandleOptionSelected(_selectedIndex);
                    _lastInputTime = currentTime;
                }
                else if (buttons.HasFlag(GamepadButtonFlags.B))
                {
                    HandleCancel();
                    _lastInputTime = currentTime;
                }
            }
        }
    }

    private void UpdateButtonSelection()
    {
        for (int i = 0; i < _optionButtons.Length; i++)
        {
            _optionButtons[i].BackgroundColor = i == _selectedIndex ? Colors.Gray : Colors.Transparent;
        }
    }

    private async void HandleOptionSelected(int index)
    {
        // Immediately stop all input handling
        _inputTimer?.Stop();
        _controller = null;
        _isDisposed = true;  // Mark as disposed to prevent further input
        
        switch (index)
        {
            case 0: // Shutdown
                Process.Start("shutdown", "/s /t 0");
                break;
            case 1: // Restart
                Process.Start("shutdown", "/r /t 0");
                break;
            case 2: // Exit
                Environment.Exit(0);
                break;
            case 3: // Cancel
                await Navigation.PopModalAsync();
                break;
        }
    }

    private async void HandleCancel()
    {
        // Immediately stop all input handling
        _inputTimer?.Stop();
        _controller = null;
        _isDisposed = true;  // Mark as disposed to prevent further input
        await Navigation.PopModalAsync();
    }

    private void OnShutdown()
    {
#if WINDOWS
        Process.Start(new ProcessStartInfo("shutdown", "/s /t 0")
        {
            UseShellExecute = true,
            CreateNoWindow = true
        });
#endif
    }

    private void OnRestart()
    {
#if WINDOWS
        Process.Start(new ProcessStartInfo("shutdown", "/r /t 0")
        {
            UseShellExecute = true,
            CreateNoWindow = true
        });
#endif
    }

    private void OnExit()
    {
        Application.Current.Quit();
    }

    private async void OnCancel()
    {
        await Navigation.PopModalAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _inputTimer?.Stop();
    }

    protected override bool OnBackButtonPressed()
    {
        // Immediately stop all input handling
        _inputTimer?.Stop();
        _controller = null;
        _isDisposed = true;  // Mark as disposed to prevent further input
        return base.OnBackButtonPressed();
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _inputTimer?.Stop();
            _inputTimer = null;
            _controller = null;
            _isDisposed = true;
        }
    }
}
