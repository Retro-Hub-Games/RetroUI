using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using System.IO;
using System.Diagnostics;
using Application = System.Windows.Application;
using System;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Collections.ObjectModel;

namespace RetroUI
{
    public partial class SettingsPage : Page
    {
        private MainWindow mainWindow;

        public SettingsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            this.mainWindow = mainWindow;
            
            // Initialize settings
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            // Load auto-start setting
            AutoStartCheckbox.IsChecked = mainWindow.IsAutoStartEnabled();
            
            // Load theme setting (default to Dark)
            ThemeComboBox.SelectedIndex = 0;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.NavigateToMain();
        }

        private void AutoStartCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            mainWindow.SetAutoStart(true);
        }

        private void AutoStartCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            mainWindow.SetAutoStart(false);
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Theme switching logic (to be implemented)
        }

        private void BrowseRomFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                RomFolderPath.Text = dialog.SelectedPath;
                SaveRomFolderPath(dialog.SelectedPath);
            }
        }

        private void SaveRomFolderPath(string path)
        {
            Properties.Settings.Default.RomFolderPath = path;
            Properties.Settings.Default.Save();
        }

        private async void ScanRoms_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(RomFolderPath.Text))
            {
                MessageBox.Show("Please select a ROM folder first.");
                return;
            }

            try
            {
                // Create UI elements first on the UI thread
                var romHome = new RomHome(mainWindow);
                ScanStatus.Text = "Scanning ROMs...";

                // Run the scan in background
                var romScanner = new RomScanner();
                var scanResults = await Task.Run(() => romScanner.ScanRomFolder(RomFolderPath.Text));

                // Process results on UI thread
                if (scanResults != null && scanResults.Any())
                {
                    foreach (var result in scanResults)
                    {
                        var systemCategory = new SystemCategory { Name = result.SystemName };
                        foreach (var (romName, romPath) in result.Roms)
                        {
                            var romInfo = new RomInfo
                            {
                                Name = romName,
                                Path = romPath,
                                System = result.SystemName
                            };

                            // Add ROM to category on UI thread
                            await Dispatcher.InvokeAsync(() => systemCategory.Roms.Add(romInfo));
                        }

                        // Add system to RomHome on UI thread
                        await Dispatcher.InvokeAsync(() => romHome.AddSystem(systemCategory));
                    }

                    // Update UI and navigate on UI thread
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var totalRoms = scanResults.Sum(s => s.Roms.Count);
                        ScanStatus.Text = $"Found {totalRoms} ROMs across {scanResults.Count} systems";
                        mainWindow.NavigateToRomHome(romHome);
                    });
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ScanStatus.Text = "No ROMs found in the selected folder";
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error scanning ROMs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ScanStatus.Text = "Error scanning ROMs";
                });
            }
        }

        private async void ScanFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = dialog.SelectedPath;
                await Task.Run(() =>
                {
                    ScanFolder(selectedPath);
                });
            }
        }

        private void ScanFolder(string folderPath)
        {
            try
            {
                var files = Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (!IsExcludedExecutable(file))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ProcessGameExecutable(file);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error scanning folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
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
                    if (!((MainWindow)Application.Current.MainWindow).InstalledApps.Any(app => 
                        app.Path.Equals(appInfo.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        ((MainWindow)Application.Current.MainWindow).InstalledApps.Add(appInfo);
                        ((MainWindow)Application.Current.MainWindow).allApps.Add(appInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing executable {exePath}: {ex.Message}");
            }
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
    }
}
