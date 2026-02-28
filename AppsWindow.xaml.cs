using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Diagnostics;
using PlayNow.Client.Services;

namespace PlayNow.Client
{
    // Класс для хранения информации о запущенном процессе
    public class ProcessItem
    {
        public string ExeName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
    }

    public partial class AppsWindow : Window
    {
        public List<string> SelectedApps { get; private set; } = new List<string>();

        private Dictionary<string, AppConfig> _remoteDbRef;
        private List<ProcessItem> _allRunningProcesses = new List<ProcessItem>();

        public AppsWindow(List<string> alreadySelected, Dictionary<string, AppConfig> remoteDb)
        {
            InitializeComponent();
            this.Loaded += (s, e) => Services.WindowEffect.EnableBlur(this);

            SelectedApps = new List<string>(alreadySelected);
            _remoteDbRef = remoteDb;

            BuildAppList(_remoteDbRef);
        }

        // --- 1. ДОБАВЛЕНИЕ ЧЕРЕЗ ФАЙЛ (.EXE) ---
        private void AddCustomExe_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Исполняемые файлы (*.exe)|*.exe",
                Title = "Выберите файл игры или лаунчера"
            };

            if (dialog.ShowDialog() == true)
            {
                string fileName = System.IO.Path.GetFileName(dialog.FileName);
                AddProcessToDatabase(fileName, "📁");
            }
        }

        // --- 2. ДОБАВЛЕНИЕ ИЗ ДИСПЕТЧЕРА ПРОЦЕССОВ ---
        private void AddProcessManual_Click(object sender, RoutedEventArgs e)
        {
            ProcessSearchInput.Text = "";
            LoadRunningProcesses();
            ProcessOverlay.Visibility = Visibility.Visible;
            ProcessSearchInput.Focus();
        }

        private void LoadRunningProcesses()
        {
            _allRunningProcesses.Clear();
            try
            {
                // Получаем процессы, отсеиваем системные, группируем дубликаты
                var procs = Process.GetProcesses()
                    .Where(p => p.Id > 4 && !string.IsNullOrEmpty(p.ProcessName))
                    .GroupBy(p => p.ProcessName)
                    .Select(g => g.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle)) ?? g.First())
                    .Select(p => new ProcessItem
                    {
                        ExeName = p.ProcessName.ToLower() + ".exe",
                        WindowTitle = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? "Фоновый процесс" : p.MainWindowTitle
                    })
                    // Игры и программы с окнами будут наверху
                    .OrderByDescending(p => p.WindowTitle != "Фоновый процесс")
                    .ThenBy(p => p.ExeName)
                    .ToList();

                _allRunningProcesses = procs;
                ProcessListBox.ItemsSource = _allRunningProcesses;
            }
            catch { }
        }

        // Поиск по запущенным процессам
        private void ProcessSearchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = ProcessSearchInput.Text.ToLower();
            if (string.IsNullOrWhiteSpace(query))
            {
                ProcessListBox.ItemsSource = _allRunningProcesses;
            }
            else
            {
                ProcessListBox.ItemsSource = _allRunningProcesses
                    .Where(p => p.ExeName.Contains(query) || p.WindowTitle.ToLower().Contains(query))
                    .ToList();
            }
        }

        // Клик по процессу в списке
        private void ProcessItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ProcessItem item)
            {
                AddProcessToDatabase(item.ExeName, "🖥️");
                ProcessOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ProcessOverlayCancel_Click(object sender, RoutedEventArgs e)
        {
            ProcessOverlay.Visibility = Visibility.Collapsed;
        }

        // Если нажали кнопку "ДОБАВИТЬ ВВОД" (для ручного ввода)
        private void ProcessOverlayAdd_Click(object sender, RoutedEventArgs e)
        {
            string processName = ProcessSearchInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(processName)) return;

            if (!processName.ToLower().EndsWith(".exe"))
                processName += ".exe";

            AddProcessToDatabase(processName, "✍️");
            ProcessOverlay.Visibility = Visibility.Collapsed;
        }

        // --- ОБЩИЙ МЕТОД СОХРАНЕНИЯ В ИНТЕРФЕЙС ---
        private void AddProcessToDatabase(string processName, string icon)
        {
            string customKey = "custom_" + processName.ToLower();

            if (!_remoteDbRef.ContainsKey(customKey))
            {
                _remoteDbRef[customKey] = new AppConfig
                {
                    Name = $"{icon} {processName}",
                    Category = "⚙️ Свои приложения",
                    Processes = new List<string> { processName }
                };
            }

            if (!SelectedApps.Contains(customKey))
            {
                SelectedApps.Add(customKey);
            }

            BuildAppList(_remoteDbRef);
            SearchBox.Text = "";
        }

        private void BuildAppList(Dictionary<string, AppConfig> remoteDb)
        {
            AppsContainer.Children.Clear();
            if (remoteDb == null || !remoteDb.Any()) return;

            var groupedApps = remoteDb.GroupBy(x => x.Value.Category).OrderBy(g => g.Key == "⚙️ Свои приложения" ? 0 : 1);

            foreach (var group in groupedApps)
            {
                var header = new TextBlock
                {
                    Text = group.Key,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 15, 0, 5)
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, "TextAccent");
                AppsContainer.Children.Add(header);

                foreach (var app in group)
                {
                    var cb = new CheckBox
                    {
                        Content = app.Value.Name,
                        IsChecked = SelectedApps.Contains(app.Key),
                        Tag = app.Key,
                        Margin = new Thickness(10, 3, 0, 3)
                    };
                    cb.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
                    AppsContainer.Children.Add(cb);
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = SearchBox.Text.ToLower();
            foreach (var child in AppsContainer.Children)
            {
                if (child is CheckBox cb)
                {
                    string content = cb.Content?.ToString()?.ToLower() ?? "";
                    cb.Visibility = content.Contains(searchText) ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (child is TextBlock header)
                {
                    header.Visibility = string.IsNullOrWhiteSpace(searchText) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in AppsContainer.Children)
                if (child is CheckBox cb) cb.IsChecked = false;
            SelectedApps.Clear();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SelectedApps.Clear();
            foreach (var child in AppsContainer.Children)
                if (child is CheckBox cb && cb.IsChecked == true)
                    SelectedApps.Add(cb.Tag?.ToString() ?? "");
            DialogResult = true;
        }
    }
}