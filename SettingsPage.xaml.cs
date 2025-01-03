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
using System.Threading;

namespace RetroUI
{
    public partial class SettingsPage : Page
    {
        private MainWindow mainWindow;
        private string romFolderPath;

        public SettingsPage(MainWindow mainWindow)
        {
            InitializeComponent();
            this.mainWindow = mainWindow;
            
            // Load saved ROM folder path if it exists
            romFolderPath = Properties.Settings.Default.RomFolderPath;
            if (!string.IsNullOrEmpty(romFolderPath))
            {
                RomFolderPath.Text = romFolderPath;
            }
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
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select ROM folder";
            dialog.ShowNewFolderButton = true;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                romFolderPath = dialog.SelectedPath;
                RomFolderPath.Text = romFolderPath;
                Properties.Settings.Default.RomFolderPath = romFolderPath;
                Properties.Settings.Default.Save();
            }
        }

        private async void ScanRoms_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(romFolderPath) || !Directory.Exists(romFolderPath))
            {
                MessageBox.Show("Please select a valid ROM folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                ScanStatus.Text = "Scanning for ROMs...";
                await Task.Run(() => ScanForRoms(romFolderPath));
                ScanStatus.Text = "ROM scan complete!";
                await Task.Delay(2000); // Show completion message for 2 seconds
                ScanStatus.Text = string.Empty;
            }
            catch (Exception ex)
            {
                ScanStatus.Text = "Error scanning ROMs.";
                MessageBox.Show($"Error scanning ROMs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScanForRoms(string folderPath)
        {
            string[] supportedExtensions = new[] { ".nes", ".snes", ".gba", ".gbc", ".n64", ".z64", ".iso", ".cue", ".bin" };
            
            var romFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()));

            foreach (var romFile in romFiles)
            {
                Dispatcher.Invoke(() =>
                {
                    ScanStatus.Text = $"Found: {Path.GetFileName(romFile)}";
                });

                // Here you would typically:
                // 1. Create a ROM entry in your database/storage
                // 2. Extract metadata (if available)
                // 3. Generate or download thumbnails
                // 4. Add to your ROM collection
                
                Thread.Sleep(100); // Slow down the scan a bit so users can see progress
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
