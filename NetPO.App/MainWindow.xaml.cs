using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics; // Added for Process
using System.Windows.Threading; // Added for DispatcherTimer

namespace NetPO.App
{
    public partial class MainWindow : Window
    {
        private AppConfig _config;
        private const string ConfigFileName = "config.json";
        private DispatcherTimer _processMonitorTimer;
        private DispatcherTimer _soundSchedulerTimer;
        private bool _isSoundMutedByScheduler = false;

        // WinAPI constants for hiding the taskbar
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const string TaskbarClass = "Shell_TrayWnd";

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // For sound control
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_APPCOMMAND = 0x319;
        private const uint APPCOMMAND_VOLUME_MUTE = 0x80000;
        // These are not strictly needed if we only toggle mute, but good to have for reference
        // private const uint APPCOMMAND_VOLUME_UP = 0xA0000;
        // private const uint APPCOMMAND_VOLUME_DOWN = 0x90000; 


        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            InitializeWebView();
            SetupEventHandlers();
            HideTaskbar();
            InitializeProcessMonitor();
            InitializeSoundScheduler();
            // Ensure sound is unmuted on startup, unless scheduler mutes it immediately
            // We need a robust way to set mute state, SendMessageW with APPCOMMAND_VOLUME_MUTE toggles.
            // For now, we'll call it, assuming it might unmute if muted. A proper check of current state is better.
            // SetSystemMute(false); // This will be handled more carefully in SoundSchedulerTimer_Tick initial call

        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Temporarily show taskbar to allow interaction with settings window if it's not TopMost
            // Or ensure SettingsWindow is Topmost itself
            // For simplicity, we'll assume SettingsWindow can manage its own visibility over the main window.
            
            SettingsWindow settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this; // Makes settings window modal to MainWindow and appear on top
            settingsWindow.Topmost = true; // Ensure it's on top of other applications too
            bool? result = settingsWindow.ShowDialog();
            settingsWindow.Topmost = false; // Reset Topmost after dialog is closed

            if (result == true)
            {
                // Reload config if settings were saved
                LoadConfig();
                // Re-apply any necessary settings, e.g., if DefaultUrl changed
                if (webView.CoreWebView2 != null && webView.Source.ToString() != _config.DefaultUrl)
                {
                    webView.CoreWebView2.Navigate(_config.DefaultUrl);
                }
                Logger.Log("Configuration reloaded after settings change.");
            }
            else
            {
                Logger.Log("Settings window closed without saving.");
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    var jsonString = File.ReadAllText(ConfigFileName);
                    _config = JsonSerializer.Deserialize<AppConfig>(jsonString);
                    if (_config.ScheduleTimes == null) // Ensure ScheduleTimes list exists after deserialization
                    {
                        _config.ScheduleTimes = new List<string>();
                    }
                    Logger.Log($"Loaded config from file. _config is null: {_config == null}. DefaultUrl: '{_config?.DefaultUrl}'. ScheduleTimes count: {_config?.ScheduleTimes?.Count}");
                }
                else
                {
                    // Create a default config if not found
                    _config = new AppConfig
                    {
                        DefaultUrl = "https://example.com",
                        WhiteListUrls = new List<string> { "https://example.com", "https://allowed.com" },
                        AllowedApps = new List<string> { "notepad.exe", "calc.exe" },
                        ScheduleTimes = new List<string>()
                    };
                    var jsonString = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ConfigFileName, jsonString);
                    Logger.Log($"Created default config. _config is null: {_config == null}. DefaultUrl: '{_config?.DefaultUrl}'.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Fallback to a very basic default if config is corrupt or unreadable
                _config = new AppConfig { DefaultUrl = "https://example.com", WhiteListUrls = new List<string> { "https://example.com" }, AllowedApps = new List<string>(), ScheduleTimes = new List<string>() };
                Logger.Log($"Error loading config, using fallback. _config is null: {_config == null}. DefaultUrl: '{_config?.DefaultUrl}'. Exception: {ex.Message}");
            }
        }

        private async void InitializeWebView()
        {
            try
            {
                // Ensure the UserDataFolder path is writable and dedicated for the app
                var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetPO.App_WebView2");
                Directory.CreateDirectory(userDataFolder); // Ensure the directory exists
                Logger.Log($"UserDataFolder for WebView2 set to: {userDataFolder}");

                var environmentOptions = new CoreWebView2EnvironmentOptions();
                // You can add more environment options here if needed, e.g., language
                Logger.Log("CoreWebView2EnvironmentOptions created.");

                var webViewEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, environmentOptions);
                Logger.Log($"CoreWebView2Environment created: {webViewEnvironment != null}. BrowserVersionString: {webViewEnvironment?.BrowserVersionString}");

                Logger.Log("Attempting to call EnsureCoreWebView2Async.");
                await webView.EnsureCoreWebView2Async(webViewEnvironment);
                Logger.Log($"EnsureCoreWebView2Async completed. CoreWebView2 is null: {webView.CoreWebView2 == null}");

                if (webView.CoreWebView2 != null)
                {
                    Logger.Log($"WebView2 initialized successfully. UserDataFolder: {webView.CoreWebView2.Environment.UserDataFolder}");
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                    webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    if (_config != null && !string.IsNullOrEmpty(_config.DefaultUrl))
                    {
                        webView.CoreWebView2.Navigate(_config.DefaultUrl);
                        Logger.Log($"Navigating to DefaultUrl: {_config.DefaultUrl}");
                    }
                    else
                    {
                        Logger.Log("DefaultUrl is not configured or _config is null. Cannot navigate.");
                    }
                }
                else
                {
                    Logger.Log("CoreWebView2 is null after EnsureCoreWebView2Async.");
                    MessageBox.Show("WebView2 control could not be initialized. CoreWebView2 is null.", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"WebView2 initialization failed: {ex.ToString()}");
                MessageBox.Show($"WebView2 initialization failed: {ex.Message}. Please ensure WebView2 Runtime is installed and the application has rights to its data folder.", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Optionally, navigate to a local error page or close the app
            }
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            Logger.Log($"New window requested for URI: {e.Uri}");
            e.Handled = true; // Prevent the new window from opening

            if (_config?.WhiteListUrls != null && _config.WhiteListUrls.Any(url => e.Uri.StartsWith(url, StringComparison.OrdinalIgnoreCase)))
            {
                Logger.Log($"Navigating to whitelisted URI in current window: {e.Uri}");
                webView.CoreWebView2.Navigate(e.Uri);
            }
            else
            {
                Logger.Log($"URI '{e.Uri}' not in whitelist. Redirecting to DefaultUrl: '{_config?.DefaultUrl}'");
                if (webView.CoreWebView2 != null && _config != null && !string.IsNullOrEmpty(_config.DefaultUrl))
                {
                    webView.CoreWebView2.Navigate(_config.DefaultUrl);
                }
                else
                {
                    Logger.Log("Cannot redirect to DefaultUrl: CoreWebView2 is null, _config is null, or DefaultUrl is empty. Navigating to about:blank as a fallback.");
                    if (webView.CoreWebView2 != null)
                    {
                        webView.CoreWebView2.Navigate("about:blank");
                    }
                }
            }
        }

        private void SetupEventHandlers()
        {
            this.Closing += MainWindow_Closing;
            this.Activated += MainWindow_Activated;
            this.Deactivated += MainWindow_Deactivated;
            // Prevent Alt+F4, Alt+Tab, etc. and handle Shift+F1 for settings
            this.PreviewKeyDown += (s, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift && e.Key == Key.F1)
                {
                    SettingsButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if ((Keyboard.Modifiers == ModifierKeys.Alt && (e.SystemKey == Key.F4 || e.SystemKey == Key.Tab)) ||
                    (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Escape) ||
                     e.Key == Key.LWin || e.Key == Key.RWin)
                {
                    e.Handled = true;
                }
            };
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_config?.WhiteListUrls == null || !_config.WhiteListUrls.Any(url => e.Uri.StartsWith(url, StringComparison.OrdinalIgnoreCase)))
            {
                e.Cancel = true;
                Logger.Log($"Navigation to '{e.Uri}' cancelled (not whitelisted). Attempting to redirect to DefaultUrl: '{_config?.DefaultUrl}'.");

                if (webView.CoreWebView2 != null && _config != null && !string.IsNullOrEmpty(_config.DefaultUrl))
                {
                    string currentSource = null;
                    try
                    {
                        // It's possible CoreWebView2.Source is null if no navigation has successfully completed yet.
                        if (webView.CoreWebView2.Source != null)
                        {
                           currentSource = webView.CoreWebView2.Source.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error accessing webView.CoreWebView2.Source: {ex.Message}");
                    }

                    if (currentSource != _config.DefaultUrl)
                    {
                        webView.CoreWebView2.Navigate(_config.DefaultUrl);
                        Logger.Log($"Redirected to DefaultUrl: {_config.DefaultUrl}");
                    }
                    else
                    {
                        Logger.Log("Already on DefaultUrl or navigation to DefaultUrl was just initiated. No redirection needed.");
                    }
                }
                else
                {
                    Logger.Log("Cannot redirect to DefaultUrl: CoreWebView2 is null, _config is null, or DefaultUrl is empty. Navigating to about:blank as a fallback.");
                    // Fallback behavior: navigate to about:blank if DefaultUrl is not available.
                    if (webView.CoreWebView2 != null)
                    {
                        webView.CoreWebView2.Navigate("about:blank");
                    }
                }
            }
        }

        private void HideTaskbar()
        {
            IntPtr taskbarHandle = FindWindow(TaskbarClass, null);
            if (taskbarHandle != IntPtr.Zero)
            {
                ShowWindow(taskbarHandle, SW_HIDE);
            }
        }

        private void ShowTaskbar()
        {
            IntPtr taskbarHandle = FindWindow(TaskbarClass, null);
            if (taskbarHandle != IntPtr.Zero)
            {
                ShowWindow(taskbarHandle, SW_SHOW);
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Logger.Log("Application closing sequence initiated.");
            if (!string.IsNullOrEmpty(_config.AdminPasswordHash))
            {
                PasswordEntryDialog passwordDialog = new PasswordEntryDialog();
                if (passwordDialog.ShowDialog() == true)
                {
                    if (VerifyPassword(passwordDialog.Password, _config.AdminPasswordHash))
                    {
                        Logger.Log("Password verified. Application will close.");
                        ShowTaskbar(); // Show taskbar only if password is correct
                    }
                    else
                    {
                        Logger.Log("Incorrect password entered. Application close cancelled.");
                        MessageBox.Show("Неверный пароль.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        e.Cancel = true; // Prevent closing
                    }
                }
                else
                {
                    Logger.Log("Password entry cancelled by user. Application close cancelled.");
                    e.Cancel = true; // Prevent closing if dialog is cancelled
                }
            }
            else
            {
                ShowTaskbar(); // No password set, allow closing and show taskbar
            }
        }

        private string HashPassword(string password) // Helper for password verification
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private bool VerifyPassword(string enteredPassword, string storedHash) // Helper for password verification
        {
            if (string.IsNullOrEmpty(storedHash)) return true; // No password set means verification passes
            return storedHash == HashPassword(enteredPassword);
        }

        private void InitializeProcessMonitor()
        {
            _processMonitorTimer = new DispatcherTimer();
            _processMonitorTimer.Interval = TimeSpan.FromSeconds(5); // Check every 5 seconds
            _processMonitorTimer.Tick += ProcessMonitorTimer_Tick;
            _processMonitorTimer.Start();
        }

        private void ProcessMonitorTimer_Tick(object sender, EventArgs e)
        {
            MonitorProcesses();
        }

        // Basic process monitoring
        private void MainWindow_Activated(object sender, EventArgs e)
        {
            // When the main window is activated, ensure it is topmost
            // unless an allowed application is currently the foreground window.
            // This check might be complex as determining foreground window reliably
            // and deciding if it's an "allowed app" needs careful handling.
            // For now, let's assume if our app is activated, it should be Topmost.
            // More sophisticated logic might be needed if allowed apps should truly overlay this one.
            this.Topmost = true;
            Logger.Log("MainWindow activated, set Topmost = true.");
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            // When the main window is deactivated, it might be because an allowed app
            // has been brought to the front. In this scenario, we might not want
            // to immediately set Topmost = false, as that could cause flickering
            // or unintended behavior if another non-allowed app takes focus.
            // The logic in MonitorProcesses will handle setting Topmost based on active allowed apps.
            // For now, we can log this event.
            Logger.Log("MainWindow deactivated.");
            // Potentially set this.Topmost = false if an allowed app is now in foreground.
            // This requires checking the foreground window's process name against AllowedApps.
            // This logic is partially covered in MonitorProcesses, but activation/deactivation adds another layer.
        }

        private void MonitorProcesses()
        {
            if (_config?.AllowedApps == null) return;

            bool isAllowedAppActive = false;

            try
            {
                Process[] currentProcesses = Process.GetProcesses();
                foreach (Process process in currentProcesses)
                {
                    try
                    {
                        // We are interested in processes that have a main window and are not our own process.
                        if (process.MainWindowHandle != IntPtr.Zero && process.Id != Process.GetCurrentProcess().Id)
                        {
                            string processName = process.ProcessName + ".exe"; // Often ProcessName doesn't include .exe
                            if (!IsAppAllowed(processName) && !IsAppAllowed(process.ProcessName)) // Check with and without .exe
                            {
                                LogAttemptedAppLaunch(processName, false);
                                Logger.Log($"Attempting to terminate non-allowed process: {processName} (PID: {process.Id})");
                                try
                                {
                                    process.Kill();
                                    Logger.Log($"Successfully terminated process: {processName} (PID: {process.Id})");
                                }
                                catch (Exception killEx)
                                {
                                    Logger.Log($"Failed to terminate process {processName} (PID: {process.Id}): {killEx.Message}");
                                }
 
                            }
                            else
                            {
                                // This is an allowed app
                                isAllowedAppActive = true;
                                // LogAttemptedAppLaunch(processName, true); // Already logged if needed
                            }
                        }
                    }
                    catch (Exception ex) // Catch errors accessing process info (e.g., access denied)
                    {
                        // Logger.Log($"Error accessing process {process.ProcessName}: {ex.Message}"); // Can be too verbose
                    }
                }

                // Adjust Topmost based on whether an allowed app is active
                if (isAllowedAppActive)
                {
                    if (this.Topmost)
                    {
                        this.Topmost = false;
                        Logger.Log("An allowed application is active. Set MainWindow Topmost = false.");
                    }
                }
                else
                {
                    if (!this.Topmost)
                    {
                        // Only set to Topmost if the window is currently active to avoid stealing focus
                        if (this.IsActive)
                        {
                            this.Topmost = true;
                            Logger.Log("No allowed application is active and MainWindow is active. Set MainWindow Topmost = true.");
                        }
                        else
                        {
                            Logger.Log("No allowed application is active, but MainWindow is not. Topmost not changed to avoid stealing focus.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                 Logger.Log($"Error in MonitorProcesses: {ex.Message}");
            }
        }

        private bool IsAppAllowed(string processName)
        {
            // Ensure comparison is case-insensitive and handles potential .exe suffix
            string appNameToCheck = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                                    ? processName 
                                    : processName + ".exe";

            return _config.AllowedApps?.Any(allowedApp => 
                string.Equals(allowedApp, appNameToCheck, StringComparison.OrdinalIgnoreCase) || 
                string.Equals(allowedApp, processName, StringComparison.OrdinalIgnoreCase) // Check without .exe too
            ) ?? false;
        }

        private void LogAttemptedAppLaunch(string appName, bool allowed)
        {
            string status = allowed ? "Allowed" : "Blocked (Attempted)";
            // Basic console logging. Replace with a more robust logging mechanism for production.
            Logger.Log($"App Launch: {appName}, Status: {status}, Timestamp: {DateTime.Now}");
            // TODO: Implement logging to a file or event log as per plan.
        }

        // Sound Scheduler Logic
        private void InitializeSoundScheduler()
        {
            _soundSchedulerTimer = new DispatcherTimer();
            _soundSchedulerTimer.Interval = TimeSpan.FromSeconds(30); // Check every 30 seconds
            _soundSchedulerTimer.Tick += SoundSchedulerTimer_Tick;
            _soundSchedulerTimer.Start();
            Logger.Log("Sound scheduler initialized.");
            // Initial check to set sound state based on schedule immediately
            SoundSchedulerTimer_Tick(null, EventArgs.Empty); 
        }

        private void SoundSchedulerTimer_Tick(object sender, EventArgs e)
        {
            if (_config?.ScheduleTimes == null || !_config.ScheduleTimes.Any())
            {
                if (_isSoundMutedByScheduler) // If sound was muted by us, unmute it
                {
                    SetSystemMute(false, true); // Force unmute
                    _isSoundMutedByScheduler = false;
                    Logger.Log("Sound scheduler disabled or no times. Unmuted system sound.");
                }
                return;
            }

            var sortedTimes = _config.ScheduleTimes
                .Select(t => TimeSpan.TryParse(t, out var ts) ? ts : (TimeSpan?)null)
                .Where(t => t.HasValue)
                .Select(t => t.Value)
                .OrderBy(t => t)
                .ToList();

            if (!sortedTimes.Any()) return;

            var currentTime = DateTime.Now.TimeOfDay;
            bool shouldBeMutedThisTick = false;
            int lastPassedScheduleIndex = -1;

            for (int i = 0; i < sortedTimes.Count; i++)
            {
                if (currentTime >= sortedTimes[i])
                {
                    lastPassedScheduleIndex = i;
                }
                else
                {
                    break; 
                }
            }

            if (lastPassedScheduleIndex != -1)
            {
                // 0th index (first time) -> mute, 1st -> unmute, 2nd -> mute, etc.
                shouldBeMutedThisTick = (lastPassedScheduleIndex % 2 == 0);
            }
            else
            {
                // Before any scheduled time, assume sound is ON (not muted by scheduler)
                shouldBeMutedThisTick = false; 
            }

            // Only change mute state if it's different from the current scheduled state
            if (shouldBeMutedThisTick && !_isSoundMutedByScheduler)
            {
                SetSystemMute(true); // Mute
                _isSoundMutedByScheduler = true;
                Logger.Log($"Scheduler: Muting sound at {DateTime.Now.ToShortTimeString()}. Current state was unmuted by scheduler.");
            }
            else if (!shouldBeMutedThisTick && _isSoundMutedByScheduler)
            {
                SetSystemMute(false); // Unmute
                _isSoundMutedByScheduler = false;
                Logger.Log($"Scheduler: Unmuting sound at {DateTime.Now.ToShortTimeString()}. Current state was muted by scheduler.");
            }
            // If current _isSoundMutedByScheduler already matches shouldBeMutedThisTick, do nothing.
        }

        // Parameter 'forceState' is used for initial setup or when disabling scheduler
        private void SetSystemMute(bool mute, bool forceState = false)
        {
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                // Try to get the main window handle in a few ways
                if (Application.Current != null && Application.Current.MainWindow != null)
                {
                    hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
                }
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = FindWindow(null, this.Title); // Assumes 'this.Title' is set and unique enough
                }
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = Process.GetCurrentProcess().MainWindowHandle;
                }

                if (hwnd != IntPtr.Zero)
                {
                    bool needsToggle = false;
                    if (forceState)
                    {
                        needsToggle = true; 
                    }
                    else
                    {
                        if (mute && !_isSoundMutedByScheduler) 
                        {
                            needsToggle = true;
                        }
                        else if (!mute && _isSoundMutedByScheduler) 
                        {
                            needsToggle = true;
                        }
                    }

                    if (needsToggle)
                    {
                        SendMessageW(hwnd, WM_APPCOMMAND, hwnd, (IntPtr)APPCOMMAND_VOLUME_MUTE);
                        Logger.Log($"SetSystemMute: Sent MUTE toggle. Desired mute: {mute}, Forced: {forceState}, SchedulerMuted: {_isSoundMutedByScheduler}");
                    }
                }
                else
                {
                    Logger.Log("SetSystemMute: Could not get a valid window handle to send mute command.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SetSystemMute: Exception while trying to set system mute: {ex.Message}");
            }
        }
    } // End of MainWindow class

    public class AppConfig
    {
        public string DefaultUrl { get; set; }
        public List<string> WhiteListUrls { get; set; }
        public List<string> AllowedApps { get; set; }
        public string AdminPasswordHash { get; set; }
        public List<string> ScheduleTimes { get; set; }
    }

    public class PasswordEntryDialog : Window
    {
        public string Password { get; private set; }
        private System.Windows.Controls.PasswordBox passwordBox;

        public PasswordEntryDialog()
        {
            Title = "Введите пароль";
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            WindowStyle = WindowStyle.ToolWindow;

            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new System.Windows.Controls.Label { Content = "Для выхода введите пароль администратора:" });
            passwordBox = new System.Windows.Controls.PasswordBox { Margin = new Thickness(0, 5, 0, 10) };
            panel.Children.Add(passwordBox);

            var okButton = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, Width = 75, HorizontalAlignment = HorizontalAlignment.Right };
            okButton.Click += (s, e) => { Password = passwordBox.Password; DialogResult = true; };
            
            var cancelButton = new System.Windows.Controls.Button { Content = "Отмена", IsCancel = true, Width = 75, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,0,5,0) };
            cancelButton.Click += (s,e) => { DialogResult = false; };

            var buttonPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);
            panel.Children.Add(buttonPanel);

            Content = panel;
        }
    }

}