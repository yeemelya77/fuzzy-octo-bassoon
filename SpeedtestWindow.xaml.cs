using System.Windows;
using PlayNow.Client.Services;

namespace PlayNow.Client
{
    public partial class SpeedtestWindow : Window
    {
        private readonly string _serverIp;
        private readonly SpeedtestService _speedService;

        public SpeedtestWindow(string serverIp)
        {
            InitializeComponent();
            this.Loaded += (s, e) => Services.WindowEffect.EnableBlur(this); // <-- ДОБАВИТЬ
            _serverIp = serverIp;
            _speedService = new SpeedtestService();

            Loaded += async (s, e) => await StartTest();
        }

        private async System.Threading.Tasks.Task StartTest()
        {
            try
            {
                LogBox.AppendText($">>> Подключение к {_serverIp}...\n");
                LogBox.AppendText(">>> Запуск iPerf3 (ожидание ~7 секунд)...\n");

                var result = await _speedService.RunTestAsync(_serverIp);

                if (result.Success)
                {
                    TitleTxt.Text = "Тест завершен!";
                    TitleTxt.Foreground = System.Windows.Media.Brushes.SpringGreen;
                    LogBox.AppendText("\n✅ УСПЕШНО!\n");
                    LogBox.AppendText($"Входящая:  {result.DownloadMbps} Mbps\n");
                    LogBox.AppendText($"Исходящая: {result.UploadMbps} Mbps\n");
                    LogBox.AppendText($"Пинг:      {result.PingMs} ms\n");
                }
                else
                {
                    TitleTxt.Text = "Ошибка теста";
                    LogBox.AppendText($"\n❌ ОШИБКА: {result.Error}\n");
                }
            }
            catch (Exception ex)
            {
                LogBox.AppendText($"\nКРИТИЧЕСКИЙ СБОЙ: {ex.Message}\n");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}