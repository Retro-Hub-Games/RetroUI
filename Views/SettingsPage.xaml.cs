using SharpDX.XInput;

namespace RetroHub.Views;

public partial class SettingsPage : ContentPage, IDisposable
{
    private Controller _controller;
    private bool _isDisposed;
    private IDispatcherTimer _inputTimer;

    public SettingsPage()
    {
        InitializeComponent();
        InitializeGamepad();
    }

    private void InitializeGamepad()
    {
        _controller = new Controller(UserIndex.One);
        
        // Create a timer to poll the gamepad state
        _inputTimer = Application.Current.Dispatcher.CreateTimer();
        _inputTimer.Interval = TimeSpan.FromMilliseconds(50); // 20 times per second
        _inputTimer.Tick += InputTimer_Tick;
        _inputTimer.Start();
    }

    private void InputTimer_Tick(object sender, EventArgs e)
    {
        if (_controller != null && _controller.IsConnected)
        {
            var state = _controller.GetState();
            
            // Check if B button is pressed
            if (state.Gamepad.Buttons.HasFlag(GamepadButtonFlags.B))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Shell.Current.GoToAsync("..");
                });
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _inputTimer?.Stop();
    }

    private async void OnBackButtonClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
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
