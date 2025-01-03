using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Timers;

namespace RetroUI
{
    public class SteamGameMonitor
    {
        private Timer checkTimer;
        private Process gameProcess;
        private Action onGameClosed;

        public SteamGameMonitor()
        {
            checkTimer = new Timer(1000); // Check every second
            checkTimer.Elapsed += CheckGameStatus;
        }

        public void StartMonitoring(Process process, Action onGameClosedCallback)
        {
            gameProcess = process;
            onGameClosed = onGameClosedCallback;
            checkTimer.Start();
        }

        public void StopMonitoring()
        {
            checkTimer.Stop();
            gameProcess = null;
        }

        private void CheckGameStatus(object sender, ElapsedEventArgs e)
        {
            if (gameProcess != null)
            {
                try
                {
                    if (gameProcess.HasExited)
                    {
                        checkTimer.Stop();
                        gameProcess = null;
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            onGameClosed?.Invoke();
                        });
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process has already exited
                    checkTimer.Stop();
                    gameProcess = null;
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        onGameClosed?.Invoke();
                    });
                }
            }
        }
    }
} 