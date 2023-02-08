using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TgBot;

class Program
{
    private static readonly IYandexDiskService _service = new YandexDiskService();

    private static readonly ReplyKeyboardMarkup defaultReplyKeyboardMarkup = new(new[]
    {
        new KeyboardButton("Ещё"),
        new KeyboardButton("Сменить папку")
    }) { ResizeKeyboard = true };

    public static async Task Main(string[] args)
    {
        var bot = new TelegramBotClient($"{Secrets.TelegramBotToken}");

        using CancellationTokenSource cts = new();

        ReceiverOptions receiverOptions = new()
        {
            AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
        };

        await _service.LoadImagesAsync();

        bot.StartReceiving(
            updateHandler: async (botClient, update, cancellationToken) =>
            {
                try
                {
                    await HandleUpdateAsync(botClient, update, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error occurred in HandleUpdateAsync: " + ex.Message);
                }
            },
            pollingErrorHandler: async (_, exception, _) =>
            {
                try
                {
                    await HandlePollingErrorAsync(exception);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error occurred in HandlePollingErrorAsync: " + ex.Message);
                }
            },
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        Console.ReadLine();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update,
        CancellationToken cts)
    {
        var settings = new Settings
        {
            Bot = bot,
            CancellationToken = cts,
            Update = update,
        };

        switch (update.Type)
        {
            case UpdateType.Message:
                if (update.Message is not { Text: { } } msg) return;

                var msgText = msg.Text;
                var chatId = msg.Chat.Id;

                settings.ChatId = chatId;
                settings.Cmd = msgText.Split(" ")[0];

                if (msgText.Split(" ").Length > 1)
                {
                    settings.Query = msgText[(msgText.IndexOf(" ", StringComparison.Ordinal) + 1)..];
                }

                if (msg.Chat.FirstName == "Jel")
                {
                    var answer = "зачем пишешь моему боту, дорогая?";
                    Console.WriteLine($"{answer} -> {msg.Chat.FirstName}");
                    return;
                }

                if (msg.Chat.Username != $"{Secrets.MyUsername}")
                {
                    await NoAccessAsync(settings);
                    return;
                }

                switch (settings.Cmd.ToLower())
                {
                    case "/start":
                        await StartAsync(settings);
                        await HelpAsync(settings);
                        return;
                    case "/help":
                        await HelpAsync(settings);
                        return;
                    case "/find":
                        await FindAsync(settings);
                        return;
                    case "/changedir":
                        await ChangeDirAsync(settings);
                        return;
                    case "сменить":
                        var folders = await _service.GetFolders();
                        await bot.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Выбери из списка",
                            replyMarkup: new InlineKeyboardMarkup(
                                folders.Select(f => new[]
                                    { InlineKeyboardButton.WithCallbackData(f.Name!, $"/changedir {f.Name}") })
                            ),
                            cancellationToken: cts);
                        return;
                    default:
                        await FindAsync(settings);
                        return;
                }

            case UpdateType.CallbackQuery:
                var data = update.CallbackQuery!.Data;
                settings.Cmd = data!.Split(" ")[0];
                settings.ChatId = update.CallbackQuery!.From.Id;
                if (data.Split(" ").Length > 1)
                {
                    settings.Query = data[(data.IndexOf(" ", StringComparison.Ordinal) + 1)..];
                }

                switch (settings.Cmd)
                {
                    case "/find":
                        await FindAsync(settings);
                        return;
                    case "/changedir":
                        await ChangeDirAsync(settings);
                        break;
                }

                return;
        }
    }

    private static async Task StartAsync(Settings settings)
    {
        Console.WriteLine($"Бот запущен для {settings.Update.Message!.Chat.Username}");
        await settings.Bot.SendTextMessageAsync(
            text: "Этот бот умеет присылать фотки с яндекс диска.",
            chatId: settings.ChatId,
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private static async Task ChangeDirAsync(Settings settings)
    {
        var parentFolder = settings.Query!;
        if (Secrets.CurrentFolder != parentFolder)
        {
            Secrets.CurrentFolder = parentFolder;
            await _service.LoadImagesAsync();
        }

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"Папка изменена на {Secrets.CurrentFolder}",
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private static async Task NoAccessAsync(Settings settings)
    {
        var msg = settings.Update.Message!;
        Console.WriteLine($"{msg.Chat.Username}, {msg.Chat.FirstName} {msg.Chat.LastName} - Нет доступа.");
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Нет доступа.",
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private static async Task HelpAsync(Settings settings)
    {
        Console.WriteLine("Отправлен список помощи");
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Доступные команды:\n" +
                  "/find <дата, имя> - найти фото по названию.\n" +
                  "/changedir <имя> - сменить папку.\n" +
                  "/help - доступные команды.\n" +
                  "/start - начало работы бота.\n",
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );
    }

    private static async Task FindAsync(Settings settings)
    {
        static async Task SendPhotoAsync(Settings settings)
        {
            var img = settings.Image!;
            await settings.Bot.SendPhotoAsync(
                chatId: settings.ChatId,
                caption: $"<a href=\"{Secrets.OpenInBrowserUrl + img.Name}\">{img.Name}</a><b> {img.DateTime}</b>",
                parseMode: ParseMode.Html,
                photo: (await _service.GetThumbnailImage(img))!,
                replyMarkup: new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithCallbackData("Ещё за эту дату", $"/find {img.DateTime.Date}")),
                cancellationToken: settings.CancellationToken,
                disableNotification: true
            );
            Console.WriteLine($"Отправлено фото {img} пользователю {settings.Update!.Message!.Chat.FirstName}");
        }

        if (settings.Query == null)
        {
            settings.Image = _service.GetRandomImage();
            await SendPhotoAsync(settings);
            return;
        }

        if (DateTime.TryParse(settings.Query.Replace(".", "-"), out var date))
        {
            settings.Image = _service.GetRandomImage(date);
            await SendPhotoAsync(settings);
            return;
        }

        settings.Image = _service.GetImage(settings.Query);

        await SendPhotoAsync(settings);
    }


    private static Task HandlePollingErrorAsync(Exception exception)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(ErrorMessage);

        return Task.CompletedTask;
    }
}

class Settings
{
    public ITelegramBotClient Bot { get; init; }
    public Update Update { get; init; }
    public long ChatId { get; set; }
    public CancellationToken CancellationToken { get; init; }
    public Image? Image { get; set; }
    public string? Cmd { get; set; }
    public string? Query { get; set; }
}