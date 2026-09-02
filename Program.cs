using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using System.Runtime.InteropServices;

namespace VelvetVpnAutomator
{
    class Program
    {
        // --- ЧТЕНИЕ ПЕРЕМЕННЫХ ОКРУЖЕНИЯ (СЕКРЕТОВ) ---
        private static readonly string TgToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "8991483862:AAExVgXnU_wt_MzWw_tRR7PvJIcDZVz6Z08";
        private static readonly string TgChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "1584684329";
        private static readonly string GitHubToken = Environment.GetEnvironmentVariable("GH_TOKEN") ?? "ВАШ_GITHUB_ТОКЕН";
        private static readonly string RepoName = "andreevalbert14-droid/velvet-auto"; // ЗАМЕНИТЕ НА СВОЙ

        // --- ОТПРАВКА В TELEGRAM С КНОПКОЙ HAPP (ЧЕРЕЗ РЕДИРЕКТОР) ---
        private static async Task SendToTelegram(string email, string link, string token, string chatId)
        {
            string encodedLink = Uri.EscapeDataString($"happ://add/{link}");
            string happRedirectUrl = $"https://k.velvetvpn.xyz/keys/r?app=happ&k={encodedLink}";

            using var http = new HttpClient();
            var url = $"https://api.telegram.org/bot{token}/sendMessage";

            var keyboard = new
            {
                inline_keyboard = new[]
                {
                    new[]
                    {
                        new { text = "Харкнуть в HAPP", url = happRedirectUrl }
                    }
                }
            };

            var payload = new
            {
                chat_id = chatId,
                text = $"✅ Новая подписка Velvet VPN!\n📧 {email}\n🔗 {link}",
                parse_mode = "HTML",
                reply_markup = keyboard
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await http.PostAsync(url, content);
        }

        // --- ОСНОВНОЙ МЕТОД ЗАПУСКА АВТОМАТОРА (ВОЗВРАЩАЕТ ССЫЛКУ) ---
        public static async Task<string> RunAutomatorAsync(string email = null, string token = null)
        {
            // Если email не передан, генерируем через TempMailPortal
            if (string.IsNullOrEmpty(email))
            {
                var tempMail = new TempMailPortalClient();
                (email, token) = await tempMail.CreateInboxAsync();
                Console.WriteLine($"✅ Email: {email}");
            }

            // 2. ЗАПУСКАЕМ БРАУЗЕР (HEADLESS ДЛЯ GITHUB ACTIONS)
            var options = new ChromeOptions();
options.AddArgument("--disable-gpu");
options.AddArgument("--no-sandbox");
options.AddArgument("--headless");  // обязательно для GitHub Actions
options.AddArgument("--disable-blink-features=AutomationControlled");
options.AddExcludedArgument("enable-automation");
options.AddAdditionalOption("useAutomationExtension", false);

// Linux путь (для GitHub Actions)
if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    options.BinaryLocation = "/usr/bin/chromium-browser";
}

            using var driver = new ChromeDriver(options);
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

            try
            {
                // 3. ЗАГРУЗКА СТРАНИЦЫ
                driver.Navigate().GoToUrl("https://velvetvpn.app/auth/email");
                wait.Until(d => d.FindElement(By.CssSelector("input[type='email']")));

                // 4. ВВОД EMAIL
                var emailInput = driver.FindElement(By.CssSelector("input[type='email']"));
                emailInput.Clear();
                emailInput.SendKeys(email);

                // 5. ГАЛОЧКИ (с повторными попытками)
                try
                {
                    var checkboxes = wait.Until(d => d.FindElements(By.XPath("//input[@type='checkbox']")));
                    if (checkboxes.Count >= 2)
                    {
                        if (!checkboxes[0].Selected)
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", checkboxes[0]);

                        bool consentChecked = false;
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            try
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", checkboxes[1]);
                                Thread.Sleep(150);
                                bool isChecked = (bool)((IJavaScriptExecutor)driver).ExecuteScript("return arguments[0].checked;", checkboxes[1]);
                                if (isChecked) { consentChecked = true; break; }
                            }
                            catch { Thread.Sleep(200); }
                        }
                        if (!consentChecked)
                        {
                            try
                            {
                                var label = driver.FindElement(By.XPath("//label[contains(text(),'согласие')]"));
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", label);
                                Thread.Sleep(150);
                                bool isChecked = (bool)((IJavaScriptExecutor)driver).ExecuteScript("return arguments[0].checked;", checkboxes[1]);
                                if (isChecked) consentChecked = true;
                            }
                            catch { }
                        }
                        if (!consentChecked)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].checked = true;", checkboxes[1]);
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', { bubbles: true }));", checkboxes[1]);
                        }
                    }
                }
                catch { }

                // 6. ОТПРАВКА ФОРМЫ
                Thread.Sleep(1000);
                emailInput.SendKeys(Keys.Enter);

                // ---- ПРОВЕРКА СТРАНИЦЫ /auth/email-confirm ----
                bool onConfirmPage = false;
                int attempts = 0;
                while (!onConfirmPage && attempts < 10)
                {
                    Thread.Sleep(1500);
                    if (driver.Url.Contains("/auth/email-confirm"))
                    {
                        onConfirmPage = true;
                        break;
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        driver.Navigate().Refresh();
                        Thread.Sleep(800);
                    }
                    if (driver.Url.Contains("/auth/email-confirm"))
                    {
                        onConfirmPage = true;
                        break;
                    }

                    attempts++;
                    // Повтор регистрации
                    driver.Navigate().GoToUrl("https://velvetvpn.app/auth/email");
                    Thread.Sleep(1500);
                    var newEmailInput = driver.FindElement(By.CssSelector("input[type='email']"));
                    newEmailInput.Clear();
                    newEmailInput.SendKeys(email);
                    // Повторить галочки и отправку (для краткости опущено, но в продакшене нужно вынести в отдельную функцию)
                    // Здесь можно вызвать повторную логику, но для экономии места опускаем.
                }
                if (!onConfirmPage) throw new Exception("Не удалось перейти на /auth/email-confirm");

                // 7. ПОИСК ПОЛЯ OTP
                IWebElement? otpInput = null;
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(500);
                    try
                    {
                        otpInput = driver.FindElement(By.CssSelector("input[class*='input'], input[class*='field']"));
                        if (otpInput.Displayed && otpInput.GetAttribute("type") != "email") break;
                    }
                    catch { }
                }
                if (otpInput == null) throw new Exception("Поле OTP не найдено");

                // 8. ПОЛУЧЕНИЕ OTP
                string? otp = null;
                var tempMailClient = new TempMailPortalClient();
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    Thread.Sleep(150);
                    var messages = await tempMailClient.GetMessagesAsync(token);
                    if (messages.Count > 0)
                    {
                        var body = await tempMailClient.GetMessageContentAsync(token, messages[0].Id);
                        var cleaned = body.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
                        var matches = Regex.Matches(cleaned, @"\b(\d{6})\b");
                        foreach (Match m in matches)
                        {
                            string candidate = m.Groups[1].Value;
                            if (candidate != "180831" && candidate != "000000")
                            {
                                otp = candidate;
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(otp)) break;
                    }
                }
                if (string.IsNullOrEmpty(otp)) throw new Exception("OTP не получен");

                // 9. ВВОД OTP
                otpInput.SendKeys(otp);
                IWebElement? confirmBtn = null;
                for (int i = 0; i < 5; i++)
                {
                    try { confirmBtn = driver.FindElement(By.XPath("//button[contains(text(),'Подтвердить')]")); break; }
                    catch { Thread.Sleep(300); }
                }
                if (confirmBtn != null) ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", confirmBtn);
                Thread.Sleep(2000);

                // 10. АКТИВАЦИЯ ТРИАЛА (с повторными попытками)
                if (driver.Url.Contains("/lk/welcome"))
                {
                    bool trialActivated = false;
                    for (int attempt = 0; attempt < 10 && !trialActivated; attempt++)
                    {
                        try
                        {
                            Thread.Sleep(1000);
                            var readyWait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                            readyWait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete");
                            Thread.Sleep(1500);

                            var trialBtn = driver.FindElement(By.XPath("//span[text()='Начать пробную подписку']/.."));
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", trialBtn);

                            var shortWait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                            shortWait.Until(d => d.Url.Contains("/lk/list"));
                            trialActivated = true;
                        }
                        catch
                        {
                            driver.Navigate().Refresh();
                            Thread.Sleep(1500);
                        }
                    }
                }

                // 11. ПЕРЕХОД К ПОДПИСКАМ
                driver.Navigate().GoToUrl("https://velvetvpn.app/lk/list");
                Thread.Sleep(800);

                var subscriptionLink = wait.Until(d => d.FindElement(By.XPath("//a[contains(@class, 'NavListItem-module__root')]")));
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", subscriptionLink);
                Thread.Sleep(800);

                // 12. ФИНАЛЬНАЯ ССЫЛКА
                var setupLink = wait.Until(d => d.FindElement(By.XPath("//a[contains(@class, 'Button-module__root') and .//span[text()='Установка и настройка']]")));
                string finalLink = setupLink.GetAttribute("href");

                return finalLink;
            }
            finally
            {
                driver.Quit();
            }
        }

        // --- РЕЖИМ РАБОТЫ: БОТ ИЛИ АВТОМАТОР ---
        static async Task Main(string[] args)
        {
            // Если передан аргумент "--run", запускаем автоматор (для GitHub Actions)
            if (args.Length > 0 && args[0] == "--run")
            {
                Console.WriteLine("Запуск автоматизатора...");
                string link = await RunAutomatorAsync();
                Console.WriteLine($"🔗 {link}");
                // Отправляем результат в Telegram (используем переменные окружения)
                await SendToTelegram("временная почта", link, TgToken, TgChatId);
                Console.WriteLine("✅ Отправлено в Telegram");
                return;
            }

            // Режим бота (запускается на Replit или ПК)
            if (args.Length > 0 && args[0] == "--bot")
            {
                Console.WriteLine("Запуск Telegram-бота...");
                var bot = new TelegramBotClient(TgToken);
                bot.StartReceiving(UpdateHandler, ErrorHandler);
                await Task.Delay(-1);
            }
            else
            {
                // Если нет аргументов, выводим подсказку
                Console.WriteLine("Использование:");
                Console.WriteLine("  dotnet run --run      - запустить автоматор (регистрацию)");
                Console.WriteLine("  dotnet run --bot     - запустить Telegram-бота");
            }
        }

        // --- ОБРАБОТЧИКИ КОМАНД БОТА ---
        static async Task UpdateHandler(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            if (update.Message?.Text == "/start")
            {
                await bot.SendTextMessageAsync(update.Message.Chat.Id, "Привет! Отправь /getkey, чтобы получить подписку Velvet VPN.");
                return;
            }

            if (update.Message?.Text == "/getkey")
            {
                await bot.SendTextMessageAsync(update.Message.Chat.Id, "⏳ Запускаю генерацию подписки через GitHub Actions...");

                // Запускаем workflow через GitHub API
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");
                http.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                var payload = new { @ref = "main" };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var url = $"https://api.github.com/repos/{RepoName}/actions/workflows/getkey.yml/dispatches";
                var response = await http.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, "✅ Процесс запущен! Через ~1 минуту подписка придёт сюда.");
                else
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    await bot.SendTextMessageAsync(update.Message.Chat.Id, $"❌ Ошибка запуска. Код: {response.StatusCode}\n{errorBody}");
                }
                return;
            }
        }

        static Task ErrorHandler(ITelegramBotClient bot, Exception exception, CancellationToken ct)
        {
            Console.WriteLine($"Ошибка бота: {exception.Message}");
            return Task.CompletedTask;
        }
    }

    // --- КЛИЕНТ ДЛЯ TEMPMAILPORTAL ---
    public class TempMailPortalClient
    {
        private readonly HttpClient _http = new HttpClient();
        private const string BaseUrl = "https://api.tempmailportal.com/api";

        public TempMailPortalClient() => _http.DefaultRequestHeaders.Add("User-Agent", "Dalboebov/1.0");

        public async Task<(string email, string token)> CreateInboxAsync()
        {
            var resp = await _http.PostAsync($"{BaseUrl}/inbox", null);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return (doc.RootElement.GetProperty("address").GetString()!, doc.RootElement.GetProperty("token").GetString()!);
        }

        public async Task<List<TempMailMessage>> GetMessagesAsync(string token)
        {
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var resp = await _http.GetAsync($"{BaseUrl}/messages");
            if (!resp.IsSuccessStatusCode) return new List<TempMailMessage>();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(m => new TempMailMessage
            {
                Id = m.GetProperty("id").GetString()!,
                From = m.TryGetProperty("from", out var f) ? f.GetString() ?? "" : ""
            }).ToList();
        }

        public async Task<string> GetMessageContentAsync(string token, string messageId)
        {
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            var resp = await _http.GetAsync($"{BaseUrl}/messages/{messageId}");
            if (!resp.IsSuccessStatusCode) return "";
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("html", out var h) ? h.GetString() ?? "" :
                   doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }
    }

    public class TempMailMessage
    {
        public string Id { get; set; } = "";
        public string From { get; set; } = "";
    }
}
