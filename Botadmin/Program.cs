using Botadmin;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    static string logFile = "access_log.txt";
    static string usersFile = "users.txt";
    static string blockedFile = "blocked_users.txt";
    static string feedbackFile = "feedback.txt";
    static string registrationFile = "registrations.txt";
    static string statsFile = "stats.json";
    static string langFolder = "lang";
    static string coursesFile = "courses.txt";

    static Dictionary<long, bool> adminWriteMode = new();
    static Dictionary<long, bool> waitingFeedback = new();
    static Dictionary<long, bool> waitingRegistration = new();
    static Dictionary<long, bool> waitingFile = new();

    static Dictionary<string, Dictionary<string, string>> languages = new();
    static List<long> adminIds = new();

    static async Task Main()
    {
        // 🌐 Environment variables orqali sozlamalarni o‘qish
        string? botToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
        string? adminIdsRaw = Environment.GetEnvironmentVariable("ADMIN_IDS");

        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(adminIdsRaw))
        {
            Console.WriteLine("❌ BOT_TOKEN yoki ADMIN_IDS noto‘g‘ri yoki bo‘sh.");
            return;
        }

        // 🔢 adminlar ro‘yxatini long turiga o‘tkazish
        adminIds = adminIdsRaw.Split(',')
                              .Select(id => long.TryParse(id.Trim(), out var val) ? val : 0)
                              .Where(val => val != 0)
                              .ToList();

        CreateLanguageFiles();
        if (!Directory.Exists(langFolder)) Directory.CreateDirectory(langFolder);
        LoadLanguages();

        var botClient = new TelegramBotClient(botToken);
        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };

        botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cancellationToken: cts.Token);
        var me = await botClient.GetMeAsync();
        Console.WriteLine($"✅ Bot ishga tushdi: @{me.Username}");

        if (!System.IO.File.Exists(statsFile)) System.IO.File.WriteAllText(statsFile, "{}");

        Console.ReadLine();
        cts.Cancel();
    }


    static void LoadLanguages()
    {
        foreach (var file in Directory.GetFiles(langFolder, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            var json = System.IO.File.ReadAllText(file);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null) languages[code] = dict;
        }
    }

    static string GetLang(long userId)
    {
        var file = $"lang_{userId}.txt";
        return System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : "uz";
    }

    static string T(long userId, string key)
    {
        var lang = GetLang(userId);
        return languages.ContainsKey(lang) && languages[lang].ContainsKey(key) ? languages[lang][key] : key;
    }

    static string LoadCourseList()
    {
        return
    @"📚 *IT sohasidagi bepul o‘quv manbalari:*

📘 *Kitoblar:*
- 'C# Dasturlash Asoslari' – PDF [uz/cyr]
- 'Python for Beginners' – https://python.swaroopch.com/
- 'You Don't Know JS' – https://github.com/getify/You-Dont-Know-JS

🌐 *Bepul o‘quv kurslar (saytlar):*
- https://w3schools.com (HTML, CSS, JS)
- https://freecodecamp.org
- https://coursera.org (ba'zilari bepul)
- https://sololearn.com (mobil ilova ham bor)

🎥 *YouTube kanallari:*
- `CodeAcademy UZ` – [https://youtube.com/@CodeAcademyUZ](https://youtube.com/@CodeAcademyUZ)
- `Najot Ta'lim` – [https://youtube.com/@najottalim](https://youtube.com/@najottalim)
- `FreeCodeCamp` – [https://youtube.com/c/Freecodecamp](https://youtube.com/c/Freecodecamp)

📱 *Mobil ilovalar:*
- SoloLearn
- Mimo
- Programming Hub

📩 *Kursga yozilish uchun /start → 📅 Kursga yozilish tugmasini bosing.*";
    }


    static async Task NotifyAdminsAboutNewUser(ITelegramBotClient botClient, User user, CancellationToken token)
    {
        string msg = $"🆕 Yangi foydalanuvchi:\n👤 {user.FirstName} {user.LastName}\n🆔 {user.Id}\n@{user.Username}\n\n" +
                     $"✅ Tasdiqlash: /tasdiqla_{user.Id}\n❌ Bloklash: /blokla_{user.Id}";

        foreach (var admin in adminIds)
        {
            await botClient.SendTextMessageAsync(admin, msg, cancellationToken: token);
        }
    }
    static async Task LogMessage(Message msg)
    {
        if (msg.From == null) return;

        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        long userId = msg.From.Id;
        string username = msg.From.Username ?? "no-username";
        string fullName = $"{msg.From.FirstName} {msg.From.LastName}".Trim();
        string text = msg.Text ?? (msg.Caption ?? "(non-text message)");
        string entry = $"{time} | {userId} | @{username} | {fullName} | \"{text}\"";

        Console.WriteLine(entry);
        await System.IO.File.AppendAllTextAsync(logFile, entry + Environment.NewLine);
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken token)
    {
        if (update.CallbackQuery != null)
        {
            var cq = update.CallbackQuery;
            string data = cq.Data;
            long fromId = cq.From.Id;

            if (adminIds.Contains(fromId))
            {
                if (data.StartsWith("block_"))
                {
                    var id = long.Parse(data.Split('_')[1]);
                    System.IO.File.AppendAllText(blockedFile, id + Environment.NewLine);
                    await botClient.SendTextMessageAsync(id, "🚫 Siz admin tomonidan bloklandingiz.");
                    await botClient.AnswerCallbackQueryAsync(cq.Id, "✅ Bloklandi");
                }
                else if (data.StartsWith("unblock_"))
                {
                    var id = long.Parse(data.Split('_')[1]);
                    var lines = System.IO.File.ReadAllLines(blockedFile).ToList();
                    if (lines.Remove(id.ToString()))
                    {
                        System.IO.File.WriteAllLines(blockedFile, lines);
                        await botClient.SendTextMessageAsync(id, "✅ Siz endi blokdan chiqdingiz.");
                        await botClient.AnswerCallbackQueryAsync(cq.Id, "✅ Blokdan chiqarildi");
                    }
                }
            }
            return;
        }
        if (update.CallbackQuery != null)
        {
            var cq = update.CallbackQuery;
            string data = cq.Data;
            long fromId = cq.From.Id;

            if (data == "sites")
            {
                await botClient.SendTextMessageAsync(fromId, "🌍 *Dasturlash saytlari:*\n- https://github.com\n- https://stackoverflow.com\n- https://w3schools.com\n- https://freecodecamp.org", parseMode: ParseMode.Markdown);
            }
            else if (data == "courses")
            {
                await botClient.SendTextMessageAsync(fromId, "🎓 *Bepul IT kurslar:*\n- https://sololearn.com\n- https://cs50.harvard.edu\n- https://udemy.com (ba'zilari bepul)\n- https://freecodecamp.org", parseMode: ParseMode.Markdown);
            }
            else if (data == "youtube")
            {
                await botClient.SendTextMessageAsync(fromId, "🎥 *YouTube’dagi o‘quv kanallar:*\n- ProgrammingHero\n- Amigoscode\n- Traversy Media\n- Najot Ta'lim\n- CodeAcademy UZ", parseMode: ParseMode.Markdown);
            }
            else if (data == "apps")
            {
                await botClient.SendTextMessageAsync(fromId, "📱 *Mobil ilovalar (IT o‘rganish uchun):*\n- Sololearn\n- Enki\n- Grasshopper\n- Mimo", parseMode: ParseMode.Markdown);
            }
            else if (data == "books")
            {
                await botClient.SendTextMessageAsync(fromId, "📘 *IT kitoblar:*\n- [Clean Code](https://github.com/JuanCrg90/Clean-Code-Notes)\n- [Eloquent JavaScript](https://eloquentjavascript.net)\n- [Python for Beginners](https://python.swaroopch.com/)\n- [Najot Ta'lim Kitoblar](https://t.me/najottalimkitoblar)", parseMode: ParseMode.Markdown);
            }
            else if (data == "news")
            {
                await botClient.SendTextMessageAsync(fromId, "📰 *IT yangiliklar:* \n- https://techcrunch.com\n- https://thenextweb.com\n- https://dev.to", parseMode: ParseMode.Markdown);
            }
            else if (data == "cv")
            {
                await botClient.SendTextMessageAsync(fromId, "📄 *CV yozish maslahatlari:*\n- [Canva CV](https://www.canva.com/resumes/)\n- [Zety CV Builder](https://zety.com/resume-builder)\n- [Rezi AI](https://www.rezi.ai)\n\n📌 Tavsiya: CV 1 sahifa, qisqa va to‘liq bo‘lishi kerak.", parseMode: ParseMode.Markdown);
            }
            else if (data == "jobs")
            {
                await botClient.SendTextMessageAsync(fromId, "💼 *Ish topish saytlari:*\n- https://linkedin.com\n- https://glassdoor.com\n- https://hh.uz\n- https://joblar.uz", parseMode: ParseMode.Markdown);
            }

            await botClient.AnswerCallbackQueryAsync(cq.Id);
            return;
        }

        if (update.Message is not { } msg) return;
        await LogMessage(msg);
      
        var user = msg.From;
        var chatId = msg.Chat.Id;
        var text = msg.Text ?? "";
        long userId = user.Id;

        if (System.IO.File.Exists(blockedFile))
        {
            var blocked = System.IO.File.ReadAllLines(blockedFile);
            if (blocked.Contains(userId.ToString()))
            {
                await botClient.SendTextMessageAsync(chatId, "🚫 Siz bloklangansiz.", cancellationToken: token);
                return;
            }
        }

        if (!System.IO.File.Exists(usersFile) || !System.IO.File.ReadAllLines(usersFile).Contains(userId.ToString()))
        {
            await NotifyAdminsAboutNewUser(botClient, user, token);
        }

        if (text.StartsWith("/tasdiqla_") && adminIds.Contains(userId))
        {
            var parts = text.Split('_');
            if (parts.Length == 2 && long.TryParse(parts[1], out long newUserId))
            {
                System.IO.File.AppendAllText(usersFile, newUserId + Environment.NewLine);
                await botClient.SendTextMessageAsync(chatId, "✅ Foydalanuvchi tasdiqlandi.", cancellationToken: token);
                await botClient.SendTextMessageAsync(newUserId, "👋 Siz botdan foydalanishingiz mumkin.", cancellationToken: token);
            }
            return;
        }

        if (text.StartsWith("/blokla_") && adminIds.Contains(userId))
        {
            var parts = text.Split('_');
            if (parts.Length == 2 && long.TryParse(parts[1], out long newUserId))
            {
                System.IO.File.AppendAllText(blockedFile, newUserId + Environment.NewLine);
                await botClient.SendTextMessageAsync(chatId, "🚫 Foydalanuvchi bloklandi.", cancellationToken: token);
                await botClient.SendTextMessageAsync(newUserId, "🚫 Siz bloklandingiz.", cancellationToken: token);
            }
            return;
        }

        if (text == "/start")
        {
            var langButtons = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("🇺🇿 O‘zbekcha"), new KeyboardButton("🇷🇺 Русский"), new KeyboardButton("🇬🇧 English") },
            })
            { ResizeKeyboard = true };

            await botClient.SendTextMessageAsync(chatId, "Tilni tanlang / Choose language / Выберите язык:", replyMarkup: langButtons, cancellationToken: token);
            return;
        }

        if (text.Contains("O‘zbekcha"))
        {
            System.IO.File.WriteAllText($"lang_{userId}.txt", "uz");
            await botClient.SendTextMessageAsync(chatId, "✅ Til o‘zbek tiliga o‘zgartirildi.", cancellationToken: token);
        }
        else if (text.Contains("Русский"))
        {
            System.IO.File.WriteAllText($"lang_{userId}.txt", "ru");
            await botClient.SendTextMessageAsync(chatId, "✅ Язык изменён на русский.", cancellationToken: token);
        }
        else if (text.Contains("English"))
        {
            System.IO.File.WriteAllText($"lang_{userId}.txt", "en");
            await botClient.SendTextMessageAsync(chatId, "✅ Language changed to English.", cancellationToken: token);
        }


        if (adminWriteMode.ContainsKey(userId) && adminWriteMode[userId])
        {
            adminWriteMode[userId] = false;
            var users = System.IO.File.ReadAllLines(usersFile).Select(long.Parse);
            foreach (var id in users)
                try { await botClient.SendTextMessageAsync(id, text, cancellationToken: token); } catch { }
            await botClient.SendTextMessageAsync(chatId, "✅ Xabar yuborildi", cancellationToken: token);
            return;
        }
      


        if (waitingFeedback.ContainsKey(userId) && waitingFeedback[userId])
        {
            waitingFeedback[userId] = false;
            await System.IO.File.AppendAllTextAsync(feedbackFile, $"{userId}: {text}{Environment.NewLine}");
            await botClient.SendTextMessageAsync(chatId, "✅ Fikringiz uchun rahmat!", cancellationToken: token);
            
            
            // Adminlarga fikr haqida bildirishnoma yuborish
           
            string adminMsg = $"💬 Yangi fikr keldi:\n👤 {user.FirstName} {user.LastName} | @{user.Username}\n🆔 {userId}\n📝 {text}";
            foreach (var adminId in adminIds)
            {
                await botClient.SendTextMessageAsync(adminId, adminMsg, cancellationToken: token);
            }

        }
        if (waitingRegistration.ContainsKey(userId) && waitingRegistration[userId] && msg.Contact != null)
        {
            waitingRegistration[userId] = false;

            var fullName = $"{msg.Contact.FirstName} {msg.Contact.LastName}".Trim();
            var phone = msg.Contact.PhoneNumber;

            await System.IO.File.AppendAllTextAsync(registrationFile, $"{userId}: {fullName} - {phone}{Environment.NewLine}");
            await botClient.SendTextMessageAsync(chatId, "✅ Kontakt qabul qilindi. Tez orada siz bilan bog‘lanamiz.", cancellationToken: token);
            

            string adminMsg = $"📩 Yangi ro‘yxatdan o‘tish:\n👤 {fullName}\n📞 {phone}\n🆔 {userId}";
            foreach (var adminId in adminIds)
            {
                await botClient.SendTextMessageAsync(adminId, adminMsg, cancellationToken: token);
            }

        }
        if (text == "📅 Kursga yozilish")
        {
            waitingRegistration[userId] = true;

            var contactKeyboard = new ReplyKeyboardMarkup(new[]
            {
        new[] { new KeyboardButton("📱 Kontaktni yuborish") { RequestContact = true } }
    })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await botClient.SendTextMessageAsync(chatId, "📩 Iltimos, kontaktni ulashing yoki ismingiz va raqamingizni yozing:", replyMarkup: contactKeyboard, cancellationToken: token);
            return;
        }


        if (waitingRegistration.ContainsKey(userId) && waitingRegistration[userId])
        {
            waitingRegistration[userId] = false;
            await System.IO.File.AppendAllTextAsync(registrationFile, $"{userId}: {text}{Environment.NewLine}");
            await botClient.SendTextMessageAsync(chatId, "📩 Ro‘yxatdan o‘tdingiz. Tez orada siz bilan bog‘lanamiz.", cancellationToken: token);
            return;
        }

        if (waitingFile.ContainsKey(userId) && waitingFile[userId] && (msg.Photo != null || msg.Video != null || msg.Document != null))
        {
            waitingFile[userId] = false;
            var users = System.IO.File.ReadAllLines(usersFile).Select(long.Parse);
            foreach (var id in users)
            {
                try
                {
                    if (msg.Photo != null)
                        await botClient.SendPhotoAsync(id, msg.Photo.Last().FileId, cancellationToken: token);
                    else if (msg.Video != null)
                        await botClient.SendVideoAsync(id, msg.Video.FileId, cancellationToken: token);
                    else if (msg.Document != null)
                        await botClient.SendDocumentAsync(id, msg.Document.FileId, cancellationToken: token);
                }
                catch { }
            }
            await botClient.SendTextMessageAsync(chatId, "✅ Fayl yuborildi.", cancellationToken: token);
            return;
        }

        if (adminIds.Contains(userId) && text == "/admin")
        {
            var panel = new ReplyKeyboardMarkup(new[]
            {
                new[] { new KeyboardButton("📋 Loglar"), new KeyboardButton("📊 Statistika") },
                new[] { new KeyboardButton("📨 Xabar yuborish"), new KeyboardButton("📎 Fayl yuborish") },
                new[] { new KeyboardButton("💬 Fikrlar"), new KeyboardButton("👥 Foydalanuvchilar") },
                new[] { new KeyboardButton("🚫 Bloklash"), new KeyboardButton("✅ Blokdan chiqarish") },
                new[] { new KeyboardButton("🧹 Fikrlarni tozalash") },
            })
            { ResizeKeyboard = true };

            await botClient.SendTextMessageAsync(chatId, "🛠 Admin paneliga xush kelibsiz:", replyMarkup: panel, cancellationToken: token);
            return;
        }

        if (adminIds.Contains(userId))
        {
            if (text == "🚫 Bloklash")
{
    var users = System.IO.File.ReadAllLines(usersFile).Distinct().ToList();
    var blocked = System.IO.File.Exists(blockedFile) ? System.IO.File.ReadAllLines(blockedFile).ToList() : new List<string>();
    var activeUsers = users.Where(u => !blocked.Contains(u)).Take(10); // 10ta chiqadi

    foreach (var uid in activeUsers)
    {
        long id = long.Parse(uid);
        string displayName = $"🆔 {id}";
        var btn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🚫 Bloklash", $"block_{id}"));
        await botClient.SendTextMessageAsync(chatId, displayName, replyMarkup: btn, cancellationToken: token);
    }

    await botClient.SendTextMessageAsync(chatId, "🧾 Bloklanadigan foydalanuvchini tanlang.", cancellationToken: token);
    return;
}

if (text == "✅ Blokdan chiqarish")
{
    var blocked = System.IO.File.Exists(blockedFile) ? System.IO.File.ReadAllLines(blockedFile).Distinct().ToList() : new List<string>();
    if (blocked.Count == 0)
    {
        await botClient.SendTextMessageAsync(chatId, "🚫 Hozirda hech kim bloklanmagan.", cancellationToken: token);
        return;
    }

    foreach (var uid in blocked.Take(10)) // 10ta chiqadi
    {
        long id = long.Parse(uid);
        string displayName = $"🆔 {id}";
        var btn = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("✅ Blokdan chiqarish", $"unblock_{id}"));
        await botClient.SendTextMessageAsync(chatId, displayName, replyMarkup: btn, cancellationToken: token);
    }

    await botClient.SendTextMessageAsync(chatId, "🧾 Blokdan chiqariladigan foydalanuvchini tanlang.", cancellationToken: token);
    return;
}

            if (text == "📨 Xabar yuborish") { adminWriteMode[userId] = true; await botClient.SendTextMessageAsync(chatId, "✏️ Xabar matnini yozing:", cancellationToken: token); return; }
            if (text == "📎 Fayl yuborish") { waitingFile[userId] = true; await botClient.SendTextMessageAsync(chatId, "📤 Rasm, video yoki fayl yuboring:", cancellationToken: token); return; }
            if (text == "💬 Fikrlar")
            {

                if (System.IO.File.Exists(feedbackFile))
                {
                    var feedbacks = System.IO.File.ReadAllLines(feedbackFile);
                    if (feedbacks.Length == 0)
                    {
                        await botClient.SendTextMessageAsync(chatId, "📭 Hozircha hech qanday fikr mavjud emas.", cancellationToken: token);
                    }
                    else
                    {
                        var recentFeedbacks = feedbacks.Reverse().Take(10).Reverse();
                        string allFeedback = string.Join("\n\n", recentFeedbacks);
                        await botClient.SendTextMessageAsync(chatId, $"📋 Oxirgi fikrlar:\n\n{allFeedback}", cancellationToken: token);
                    }
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "📂 Fikr fayli topilmadi.", cancellationToken: token);
                }
                return;
            }
            if (text == "📋 Loglar")
            {
                if (System.IO.File.Exists(logFile))
                {
                    var allLines = System.IO.File.ReadAllLines(logFile);
                    var lastLogs = allLines.Reverse().Take(20).Reverse(); // Oxirgi 20 ta log
                    string logText = string.Join("\n", lastLogs);

                    if (string.IsNullOrWhiteSpace(logText))
                        logText = "📭 Loglar mavjud emas.";

                    // Juda uzun bo‘lsa, qisqartiramiz
                    if (logText.Length > 4000)
                        logText = logText.Substring(logText.Length - 4000);

                    await botClient.SendTextMessageAsync(chatId, $"📋 Oxirgi loglar:\n\n{logText}", cancellationToken: token);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "📂 Log fayli topilmadi.", cancellationToken: token);
                }
                return;
            }


            if (text == "🧹 Fikrlarni tozalash")
            {
                if (System.IO.File.Exists(feedbackFile))
                {
                    System.IO.File.Delete(feedbackFile);
                    await botClient.SendTextMessageAsync(chatId, "🧹 Barcha fikrlar tozalandi.", cancellationToken: token);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "📂 Fikr fayli topilmadi.", cancellationToken: token);
                }
                return;
            }
            if (text == "📊 Statistika")
            {
                int usersCount = System.IO.File.Exists(usersFile) ? System.IO.File.ReadAllLines(usersFile).Length : 0;
                int blockedCount = System.IO.File.Exists(blockedFile) ? System.IO.File.ReadAllLines(blockedFile).Length : 0;
                int feedbackCount = System.IO.File.Exists(feedbackFile) ? System.IO.File.ReadAllLines(feedbackFile).Length : 0;

                string stats = $"📊 Statistika:\n" +
                               $"👥 Umumiy foydalanuvchilar: {usersCount}\n" +
                               $"🚫 Bloklangan foydalanuvchilar: {blockedCount}\n" +
                               $"💬 Fikrlar soni: {feedbackCount}";

                await botClient.SendTextMessageAsync(chatId, stats, cancellationToken: token);
                return;
            }
            if (text == "👥 Foydalanuvchilar")
            {
                if (!System.IO.File.Exists(usersFile))
                {
                    await botClient.SendTextMessageAsync(chatId, "📂 Hech qanday foydalanuvchi topilmadi.", cancellationToken: token);
                    return;
                }

                var ids = System.IO.File.ReadAllLines(usersFile).Take(10); // faqat 30 ta ko‘rsatish
                List<string> lines = new();
                foreach (var idStr in ids)
                {
                    if (long.TryParse(idStr, out long id))
                    {
                        try
                        {
                            var chat = await botClient.GetChatAsync(id, cancellationToken: token);
                            string fullName = $"{chat.FirstName} {chat.LastName}".Trim();
                            string line = $"🆔 {id} | @{chat.Username ?? "yo'q"} | {fullName}";
                            lines.Add(line);
                        }
                        catch
                        {
                            lines.Add($"🆔 {idStr} | ❌ Mavjud emas (bloklangan bo‘lishi mumkin)");
                        }
                    }
                }

                string usersText = string.Join("\n", lines);
                await botClient.SendTextMessageAsync(chatId, $"👥 Foydalanuvchilar ro‘yxati:\n\n{usersText}", cancellationToken: token);
                return;
            }


        }

        var menu = new ReplyKeyboardMarkup(new[]
        {
                   new[] {
                      new KeyboardButton(T(userId, "📚 IT kurslar")),
                     new KeyboardButton(T(userId, "📅 Kursga yozilish")),
                     new KeyboardButton(T(userId, "🌍 Manbalar"))

              },
                     new[] {
                          new KeyboardButton(T(userId, "💬 Fikr bildirish")) 
         
              },

                           new[] {
                          new KeyboardButton(T(userId, "⚙️ Tilni o‘zgartirish"))
                }
                  })
                      { ResizeKeyboard = true };


        if (text == "⚙️ Tilni o‘zgartirish") { await botClient.SendTextMessageAsync(chatId, "Tilni tanlang:", replyMarkup: menu, cancellationToken: token); return; }
        if (text == "📚 IT kurslar")
        {
            string courses = LoadCourseList();
            await botClient.SendTextMessageAsync(chatId, courses, ParseMode.Markdown, cancellationToken: token);
            return;
        }
        if (text == "🌐 Manbalar")
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
        new[] { InlineKeyboardButton.WithCallbackData("🌍 Dasturlash saytlari", "sites") },
        new[] { InlineKeyboardButton.WithCallbackData("🎓 Onlayn bepul kurslar", "courses") },
        new[] { InlineKeyboardButton.WithCallbackData("🎥 YouTube’dagi o‘quv kanallar", "youtube") },
        new[] { InlineKeyboardButton.WithCallbackData("📱 Mobil ilovalar", "apps") },
        new[] { InlineKeyboardButton.WithCallbackData("📘 IT kitoblar", "books") },
        new[] { InlineKeyboardButton.WithCallbackData("📰 IT yangiliklar", "news") },
        new[] { InlineKeyboardButton.WithCallbackData("📄 CV yozish", "cv") },
        new[] { InlineKeyboardButton.WithCallbackData("💼 Ish topish", "jobs") }
    });

            await botClient.SendTextMessageAsync(chatId, "Quyidagilardan birini tanlang:", replyMarkup: keyboard, cancellationToken: token);
            return;
        }
        if (text == "📅 Kursga yozilish") { waitingRegistration[userId] = true; await botClient.SendTextMessageAsync(chatId, "📩 Ismingiz va raqamingizni yozing:", cancellationToken: token); return; }
        if (text == "💬 Fikr bildirish") { waitingFeedback[userId] = true; await botClient.SendTextMessageAsync(chatId, "✏️ Kurs yoki bot haqida fikringiz:", cancellationToken: token); return; }

        await botClient.SendTextMessageAsync(chatId, T(userId, "👋 Menyudan bo‘lim tanlang yoki /start bosing."), replyMarkup: menu, cancellationToken: token);
    }

    static Task HandleErrorAsync(ITelegramBotClient botClient, Exception ex, CancellationToken token)
    {
        Console.WriteLine($"Xatolik: {ex.Message}");
        return Task.CompletedTask;
    }
    static void CreateLanguageFiles()
    {
        Directory.CreateDirectory("lang");

        System.IO.File.WriteAllText("lang/uz.json", @"{
  ""👋 Menyudan bo‘lim tanlang yoki /start bosing."": ""👋 Menyudan bo‘lim tanlang yoki /start bosing."",
  ""📚 IT kurslar"": ""📚 IT kurslar"",
  ""📅 Kursga yozilish"": ""📅 Kursga yozilish"",
  ""💬 Fikr bildirish"": ""💬 Fikr bildirish"",
  ""🌐 Manbalar"": ""🌐 Manbalar"",
  ""⚙️ Tilni o‘zgartirish"": ""⚙️ Tilni o‘zgartirish"",
  ""📩 Ismingiz va raqamingizni yozing."": ""📩 Ismingiz va raqamingizni yozing:"",
  ""✏️ Kurs yoki bot haqida fikringiz."": ""✏️ Kurs yoki bot haqida fikringiz:""
}");

        System.IO.File.WriteAllText("lang/ru.json", @"{
  ""👋 Menyudan bo‘lim tanlang yoki /start bosing."": ""👋 Выберите раздел из меню или нажмите /start."",
  ""📚 IT kurslar"": ""📚 Курсы по IT"",
  ""📅 Kursga yozilish"": ""📅 Записаться на курс"",
  ""💬 Fikr bildirish"": ""💬 Оставить отзыв"",
  ""🌐 Manbalar"": ""🌐 Ресурсы"",
  ""⚙️ Tilni o‘zgartirish"": ""⚙️ Изменить язык"",
  ""📩 Ismingiz va raqamingizni yozing."": ""📩 Введите ваше имя и номер телефона:"",
  ""✏️ Kurs yoki bot haqida fikringiz."": ""✏️ Ваш отзыв о курсе или боте:""
}");

        System.IO.File.WriteAllText("lang/en.json", @"{
  ""👋 Menyudan bo‘lim tanlang yoki /start bosing."": ""👋 Please select a section or press /start."",
  ""📚 IT kurslar"": ""📚 IT Courses"",
  ""📅 Kursga yozilish"": ""📅 Register for course"",
  ""💬 Fikr bildirish"": ""💬 Leave feedback"",
  ""🌐 Manbalar"": ""🌐 Resources"",
  ""⚙️ Tilni o‘zgartirish"": ""⚙️ Change language"",
  ""📩 Ismingiz va raqamingizni yozing."": ""📩 Enter your name and phone number:"",
  ""✏️ Kurs yoki bot haqida fikringiz."": ""✏️ Your feedback about the course or bot:""
}");
      
        Console.ReadLine(); // bu foydalanuvchi biror tugma bosmaguncha kutadi

    }



}