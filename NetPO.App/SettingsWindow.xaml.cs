using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Diagnostics; // Required for Process.Start
using System.Windows.Navigation; // Required for RequestNavigateEventArgs

namespace NetPO.App
{
    public partial class SettingsWindow : Window
    {
        private AppConfig _config;
        private const string ConfigFileName = "config.json";
        private string _currentPasswordHash; // Store hash of the current password

        public SettingsWindow()
        {
            InitializeComponent();
            LoadConfigAndPopulate();
        }

        private void LoadConfigAndPopulate()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    var jsonString = File.ReadAllText(ConfigFileName);
                    _config = JsonSerializer.Deserialize<AppConfig>(jsonString);
                }
                else
                {
                    _config = new AppConfig { WhiteListUrls = new List<string>(), AllowedApps = new List<string>(), ScheduleTimes = new List<string>() };
                }
                if (_config.ScheduleTimes == null) // Ensure ScheduleTimes list exists
                {
                    _config.ScheduleTimes = new List<string>();
                }

                _currentPasswordHash = _config.AdminPasswordHash;

                PopulateListBox(WhiteListedUrlsListBox, _config.WhiteListUrls);
                PopulateListBox(AllowedAppsListBox, _config.AllowedApps);
                PopulateListBox(ScheduleTimesListBox, _config.ScheduleTimes);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки конфигурации: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                _config = new AppConfig { WhiteListUrls = new List<string>(), AllowedApps = new List<string>(), ScheduleTimes = new List<string>() }; // Fallback
            }
        }

        private void PopulateListBox(System.Windows.Controls.ListBox listBox, List<string> items)
        {
            listBox.Items.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    listBox.Items.Add(item);
                }
            }
        }

        private void AddItemToListBox(System.Windows.Controls.ListBox listBox, System.Windows.Controls.TextBox textBox, List<string> configList)
        {
            var newItem = textBox.Text.Trim();
            if (!string.IsNullOrEmpty(newItem) && !listBox.Items.Contains(newItem))
            {
                listBox.Items.Add(newItem);
                configList.Add(newItem);
                textBox.Clear();
            }
            else if (listBox.Items.Contains(newItem))
            {
                MessageBox.Show("Этот элемент уже существует в списке.", "Дубликат", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RemoveSelectedItemFromListBox(System.Windows.Controls.ListBox listBox, List<string> configList)
        {
            if (listBox.SelectedItem != null)
            {
                var selectedItem = listBox.SelectedItem.ToString();
                listBox.Items.Remove(selectedItem);
                configList.Remove(selectedItem);
            }
        }

        private void AddUrlButton_Click(object sender, RoutedEventArgs e)
        {
            AddItemToListBox(WhiteListedUrlsListBox, NewUrlTextBox, _config.WhiteListUrls);
        }

        private void RemoveUrlButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedItemFromListBox(WhiteListedUrlsListBox, _config.WhiteListUrls);
        }

        private void AddAppButton_Click(object sender, RoutedEventArgs e)
        {
            AddItemToListBox(AllowedAppsListBox, NewAppTextBox, _config.AllowedApps);
        }

        private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedItemFromListBox(AllowedAppsListBox, _config.AllowedApps);
        }

        private void AddScheduleTimeButton_Click(object sender, RoutedEventArgs e)
        {
            var timeText = NewScheduleTimeTextBox.Text.Trim();
            if (TimeSpan.TryParse(timeText, out _))
            {
                AddItemToListBox(ScheduleTimesListBox, NewScheduleTimeTextBox, _config.ScheduleTimes);
            }
            else
            {
                MessageBox.Show("Неверный формат времени. Используйте HH:mm.", "Ошибка формата", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RemoveScheduleTimeButton_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedItemFromListBox(ScheduleTimesListBox, _config.ScheduleTimes);
        }

        private void SavePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            // Password Hashing (basic example, use a strong hashing library like BCrypt.Net in production)
            // For simplicity, we'll do a very basic check here. 
            // IMPORTANT: This is NOT secure for production. 

            if (!string.IsNullOrEmpty(_currentPasswordHash) && !VerifyPassword(CurrentPasswordBox.Password, _currentPasswordHash))
            {
                MessageBox.Show("Текущий пароль неверен.", "Ошибка пароля", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (NewPasswordBox.Password != ConfirmNewPasswordBox.Password)
            {
                MessageBox.Show("Новые пароли не совпадают.", "Ошибка пароля", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(NewPasswordBox.Password))
            {
                _config.AdminPasswordHash = null; // Remove password
                 MessageBox.Show("Пароль удален.", "Пароль", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _config.AdminPasswordHash = HashPassword(NewPasswordBox.Password); // Set new password
                MessageBox.Show("Пароль успешно сохранен.", "Пароль", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            _currentPasswordHash = _config.AdminPasswordHash; // Update current hash
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmNewPasswordBox.Clear();
        }

        private string HashPassword(string password)
        {
            // IMPORTANT: Replace with a strong hashing algorithm (e.g., BCrypt.Net or Argon2)
            // This is a placeholder and NOT secure.
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private bool VerifyPassword(string enteredPassword, string storedHash)
        {
            // IMPORTANT: This verification must match the hashing method.
            if (string.IsNullOrEmpty(storedHash)) return true; // No password set
            return storedHash == HashPassword(enteredPassword);
        }


        private void SaveAndCloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var jsonString = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, jsonString);
                MessageBox.Show("Настройки сохранены.", "Сохранено", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true; // Indicates settings were saved
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения конфигурации: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть ссылку: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}