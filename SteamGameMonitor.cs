using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Timers;
using System.Linq;
using System.Management;
using System.Collections.Generic;

namespace RetroUI
{
    public class SteamGameMonitor
    {
        private Timer checkTimer;
        private Process initialProcess;
        private Action onGameClosed;
        private bool isSteamGame;
        private HashSet<int> monitoredProcessIds = new HashSet<int>();
        private bool isFirstCheck = true;

        public SteamGameMonitor()
        {
            checkTimer = new Timer(1000); // Check every second
            checkTimer.Elapsed += CheckGameStatus;
        }

        public void StartMonitoring(Process process, Action onGameClosedCallback)
        {
            initialProcess = process;
            onGameClosed = onGameClosedCallback;
            monitoredProcessIds.Clear();
            isFirstCheck = true;
            
            // Check if this is a Steam game by looking at the process name or other indicators
            isSteamGame = IsSteamGameProcess(process);
            
            if (isSteamGame)
            {
                monitoredProcessIds.Add(process.Id);
            }
            
            checkTimer.Start();
        }

        public void StopMonitoring()
        {
            checkTimer.Stop();
            initialProcess = null;
            monitoredProcessIds.Clear();
        }

        private void CheckGameStatus(object sender, ElapsedEventArgs e)
        {
            if (initialProcess == null) return;

            try
            {
                if (isSteamGame)
                {
                    var allRelatedProcesses = new List<Process>();
                    var processesToCheck = new Queue<Process>(new[] { initialProcess });
                    var checkedProcessIds = new HashSet<int>();

                    // Breadth-first search through process tree
                    while (processesToCheck.Count > 0)
                    {
                        var currentProcess = processesToCheck.Dequeue();
                        if (checkedProcessIds.Contains(currentProcess.Id)) continue;
                        
                        checkedProcessIds.Add(currentProcess.Id);
                        
                        try
                        {
                            if (!currentProcess.HasExited)
                            {
                                allRelatedProcesses.Add(currentProcess);
                                var children = GetChildProcesses(currentProcess.Id);
                                foreach (var child in children)
                                {
                                    processesToCheck.Enqueue(child);
                                }
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Process has exited, skip it
                            continue;
                        }
                    }

                    // Update monitored process IDs
                    if (isFirstCheck)
                    {
                        foreach (var process in allRelatedProcesses)
                        {
                            monitoredProcessIds.Add(process.Id);
                        }
                        isFirstCheck = false;
                    }

                    // Check if any monitored processes are still running
                    bool anyMonitoredProcessRunning = allRelatedProcesses
                        .Any(p => monitoredProcessIds.Contains(p.Id));

                    if (!anyMonitoredProcessRunning)
                    {
                        checkTimer.Stop();
                        initialProcess = null;
                        monitoredProcessIds.Clear();
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            onGameClosed?.Invoke();
                        });
                    }
                }
                else
                {
                    // For non-Steam games, just check if the process has exited
                    if (initialProcess.HasExited)
                    {
                        checkTimer.Stop();
                        initialProcess = null;
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            onGameClosed?.Invoke();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in CheckGameStatus: {ex.Message}");
                // Only stop monitoring if it's not a transient error
                if (ex is InvalidOperationException)
                {
                    checkTimer.Stop();
                    initialProcess = null;
                    monitoredProcessIds.Clear();
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        onGameClosed?.Invoke();
                    });
                }
            }
        }

        private Process[] GetChildProcesses(int parentId)
        {
            try
            {
                var query = $"Select * From Win32_Process Where ParentProcessId = {parentId}";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                {
                    var childIds = results.Cast<ManagementObject>()
                        .Select(mo => Convert.ToInt32(mo["ProcessId"]))
                        .ToList();

                    return Process.GetProcesses()
                        .Where(p => childIds.Contains(p.Id))
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting child processes: {ex.Message}");
                return new Process[0];
            }
        }

        private bool IsSteamGameProcess(Process process)
        {
            try
            {
                // Use process name or other indicators to determine if it's a Steam game
                return process.ProcessName.Contains("steam") || process.ProcessName.Contains("game");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error determining if process is a Steam game: {ex.Message}");
                return false;
            }
        }
    }
} 