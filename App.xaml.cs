using System;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Threading.Tasks;

namespace PlayNow.Client
{
    public partial class App : Application
    {
        // Данные твоего бота
        private const string TgBotToken = "7897140364:AAFHNQv-T7udWLhySttmKwGBeN6tlp8W4HA";
        private const string TgChatId = "153140633";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Перехват ошибок интерфейса
            this.DispatcherUnhandledException += (sender, args) =>
            {
                args.Handled = true;
                SendCrashToTelegram("UI Thread Crash", args.Exception);
                MessageBox.Show("Произошла ошибка! Отчет уже отправлен разработчику.", "Сбой", MessageBoxButton.OK, MessageBoxImage.Warning);
            };

            // Перехват фоновых ошибок
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    SendCrashToTelegram("Background Crash", ex);
                    MessageBox.Show("Критическая ошибка! Отчет отправлен разработчику.", "Сбой", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        // ДОБАВЛЕНО СЛОВО "static" - это исправит предупреждение CA1822
        private static void SendCrashToTelegram(string type, Exception ex)
        {
            Task.Run(async () =>
            {
                try
                {
                    string pcName = Environment.MachineName;
                    string message = $"🚨 <b>CRASH REPORT ({type})</b>\n" +
                                     $"💻 <b>ПК:</b> {pcName}\n" +
                                     $"⚠️ <b>Ошибка:</b> {ex.Message}\n" +
                                     $"📋 <b>Стек:</b>\n<code>{ex.StackTrace}</code>";

                    string url = $"https://api.telegram.org/bot{TgBotToken}/sendMessage";
                    var payload = new
                    {
                        chat_id = TgChatId,
                        text = message,
                        parse_mode = "HTML"
                    };

                    using var client = new HttpClient();
                    var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    await client.PostAsync(url, content);
                }
                catch { /* Игнорируем ошибки при отправке самого отчета */ }
            });
        }
    }
}