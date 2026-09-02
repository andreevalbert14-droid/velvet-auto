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

namespace VelvetVpnAutomator
{
    class Program
    {
        // --- ОТПРАВКА В TELEGRAM С КНОПКОЙ HAPP ---
        private static async Task SendToTelegram(string email, string link)
        {
            string token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") 
                ?? throw new Exception("Missing TELEGRAM_BOT_TOKEN");
            string chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") 
                ?? throw new Exception("Missing TELEGRAM_CHAT_ID");

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
                text = $"✅ Подписка создана\n📧 {email}\n🔗 {link}",
                parse_mode = "HTML",
                reply_markup = keyboard
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await http.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"⚠️ Ошибка отправки в Telegram: {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Исключение при отправке в Telegram: {ex.Message}");
            }
        }

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== Velvet VPN Automator (Headless) ===\n");

            // 1. СОЗДАЁМ ВРЕМЕННУЮ ПОЧТУ
            Console.WriteLine("[1] Создание временной почты...");
            var tempMail = new TempMailPortalClient();
            var (email, token) = await tempMail.CreateInboxAsync();
            Console.WriteLine($"    ✅ Email: {email}");

            // 2. НАСТРОЙКА БРАУЗЕРА (HEADLESS)
            Console.WriteLine("[2] Запуск браузера в headless-режиме...");
            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);

            using var driver = new ChromeDriver(options);
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

            try
            {
                // 3. РЕГИСТРАЦИЯ
                Console.WriteLine("[3] Загрузка страницы Velvet VPN...");
                driver.Navigate().GoToUrl("https://velvetvpn.app/auth/email");
                wait.Until(d => d.FindElement(By.CssSelector("input[type='email']")));
                Console.WriteLine("    ✅ Страница загружена");

                Console.WriteLine("    → Ввод email...");
                var emailInput = driver.FindElement(By.CssSelector("input[type='email']"));
                emailInput.Clear();
                emailInput.SendKeys(email);

                Console.WriteLine("    → Отмечаем галочки...");
                try
                {
                    var checkboxes = wait.Until(d => d.FindElements(By.XPath("//input[@type='checkbox']")));
                    if (checkboxes.Count >= 2)
                    {
                        if (!checkboxes[0].Selected)
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", checkboxes[0]);

                        // Принудительно ставим вторую галочку
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].checked = true;", checkboxes[1]);
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].dispatchEvent(new Event('change', { bubbles: true }));", checkboxes[1]);
                        Console.WriteLine("    → Согласие отмечено (принудительно)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    ⚠️ Ошибка при отметке галочек: {ex.Message}");
                }

                Console.WriteLine("    → Отправка формы...");
                Thread.Sleep(1000);
                emailInput.SendKeys(Keys.Enter);
                Console.WriteLine("    ✅ Форма отправлена");

                // 4. ОЖИДАНИЕ СТРАНИЦЫ /auth/email-confirm
                Console.WriteLine("[4] Ожидание перехода на /auth/email-confirm...");
                bool onConfirmPage = false;
                int attempts = 0;
                while (!onConfirmPage && attempts < 10)
                {
                    Thread.Sleep(1500);
                    if (driver.Url.Contains("/auth/email-confirm"))
                    {
                        onConfirmPage = true;
                        Console.WriteLine("    ✅ Переход выполнен");
                        break;
                    }
                    Console.WriteLine("    ⏳ Обновляем страницу...");
                    driver.Navigate().Refresh();
                    Thread.Sleep(800);
                    attempts++;
                }
                if (!onConfirmPage) throw new Exception("Не удалось перейти на /auth/email-confirm");

                // 5. ПОИСК ПОЛЯ OTP
                Console.WriteLine("[5] Поиск поля OTP...");
                IWebElement? otpInput = null;
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(500);
                    try
                    {
                        otpInput = driver.FindElement(By.CssSelector("input[class*='input'], input[class*='field']"));
                        if (otpInput.Displayed && otpInput.GetAttribute("type") != "email")
                        {
                            Console.WriteLine($"    ✅ Поле OTP найдено (попытка {i+1})");
                            break;
                        }
                    }
                    catch { }
                }
                if (otpInput == null) throw new Exception("Поле OTP не найдено");

                // 6. ПОЛУЧЕНИЕ OTP
                Console.WriteLine("[6] Получение OTP...");
                string? otp = null;
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    Thread.Sleep(300);
                    Console.WriteLine($"    → Проверка почты... ({attempt+1}/15)");
                    var messages = await tempMail.GetMessagesAsync(token);
                    if (messages.Count > 0)
                    {
                        var body = await tempMail.GetMessageContentAsync(token, messages[0].Id);
                        var cleaned = body.Replace("\n", " ").Replace("\r", " ");
                        var matches = Regex.Matches(cleaned, @"\b(\d{6})\b");
                        foreach (Match m in matches)
                        {
                            string candidate = m.Groups[1].Value;
                            if (candidate != "180831" && candidate != "000000")
                            {
                                otp = candidate;
                                Console.WriteLine($"    ✅ OTP: {otp}");
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(otp)) break;
                    }
                }
                if (string.IsNullOrEmpty(otp)) throw new Exception("OTP не получен");

                // 7. ВВОД OTP И ПОДТВЕРЖДЕНИЕ
                otpInput.SendKeys(otp);
                IWebElement? confirmBtn = null;
                for (int i = 0; i < 5; i++)
                {
                    try { confirmBtn = driver.FindElement(By.XPath("//button[contains(text(),'Подтвердить')]")); break; }
                    catch { Thread.Sleep(300); }
                }
                if (confirmBtn != null) ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", confirmBtn);
                Thread.Sleep(2000);

                // 8. АКТИВАЦИЯ ТРИАЛА
                Console.WriteLine("[7] Активация пробной подписки...");
                if (driver.Url.Contains("/lk/welcome"))
                {
                    bool trialActivated = false;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            var trialBtn = driver.FindElement(By.XPath("//span[text()='Начать пробную подписку']/.."));
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", trialBtn);
                            var shortWait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                            shortWait.Until(d => d.Url.Contains("/lk/list"));
                            trialActivated = true;
                            Console.WriteLine("    ✅ Активация успешна");
                            break;
                        }
                        catch
                        {
                            Console.WriteLine($"    ⏳ Попытка {attempt+1}/10, рефреш...");
                            driver.Navigate().Refresh();
                            Thread.Sleep(1500);
                        }
                    }
                    if (!trialActivated) throw new Exception("Не удалось активировать триал");
                }
                else
                {
                    Console.WriteLine("    → Уже на странице подписок, пропускаем");
                }

                // 9. ПЕРЕХОД К ПОДПИСКАМ И ПОЛУЧЕНИЕ ССЫЛКИ
                Console.WriteLine("[8] Получение финальной ссылки...");
                driver.Navigate().GoToUrl("https://velvetvpn.app/lk/list");
                Thread.Sleep(800);
                var subscriptionLink = wait.Until(d => d.FindElement(By.XPath("//a[contains(@class, 'NavListItem-module__root')]")));
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", subscriptionLink);
                Thread.Sleep(800);

                var setupLink = wait.Until(d => d.FindElement(By.XPath("//a[contains(@class, 'Button-module__root') and .//span[text()='Установка и настройка']]")));
                string finalLink = setupLink.GetAttribute("href");
                Console.WriteLine($"\n🔗 Финальная ссылка: {finalLink}");

                System.IO.File.AppendAllText("accounts.txt", $"{email}:{finalLink}\n");
                Console.WriteLine("✅ Сохранено в accounts.txt");

                // 10. ОТПРАВКА В TELEGRAM
                if (!string.IsNullOrEmpty(finalLink))
                {
                    await SendToTelegram(email, finalLink);
                    Console.WriteLine("✅ Отправлено в Telegram");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                // Отправляем ошибку в Telegram (опционально)
            }
            finally
            {
                driver.Quit();
            }

            Console.WriteLine("\n[+] Завершено. Выход.");
        }
    }

    // --- КЛИЕНТ ДЛЯ TEMPMAILPORTAL (без изменений) ---
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