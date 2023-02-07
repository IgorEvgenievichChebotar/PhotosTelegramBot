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

        if (update.Message is { Text: { } } msg)
        {
            var msgText = msg.Text;
            var chatId = msg.Chat.Id;

            settings.ChatId = chatId;
            settings.Cmd = msgText.Split(" ")[0];
            if (msgText.Split(" ").Length > 1)
            {
                settings.Query = msgText.Split(" ")[1];
            }

            if (msg.Chat.Username != $"{Secrets.MyUsername}")
            {
                await NoAccessAsync(settings);
                return;
            }

            switch (settings.Cmd)
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
                default:
                    settings.Image = _service.GetRandomImage();
                    break;
            }

            if (msgText.ToLower().Contains("сменить папку"))
            {
                var dirs = await _service.GetDirs();

                await bot.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Выбери из списка",
                    replyMarkup: new InlineKeyboardMarkup(
                        dirs.Select(d => new[]
                            { InlineKeyboardButton.WithCallbackData(d.Name!, $"/change {d.Name}") })
                    ),
                    cancellationToken: cts);
            }
            else
            {
                await SendPhotoAsync(settings);
            }
        }
        else if (update.Type == UpdateType.CallbackQuery)
        {
            settings.Cmd = update.CallbackQuery!.Data!.Split(" ")[0];
            settings.Query = update.CallbackQuery!.Data!.Split(" ")[1];
            settings.ChatId = update.CallbackQuery!.From.Id;

            switch (settings.Cmd)
            {
                case "/find":
                    await FindAsync(settings);
                    return;
                case "/change":
                    await ChangeDirAsync(settings);
                    break;
            }
        }
    }

    private static async Task ChangeDirAsync(Settings settings)
    {
        var date = settings.Query!.Replace("_", " ");
        if (Secrets.PathToDir != date)
        {
            Secrets.PathToDir = date;
            await _service.LoadImagesAsync();
        }

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"Папка изменена на {Secrets.PathToDir}",
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private static async Task StartAsync(Settings settings)
    {
        Console.WriteLine($"Бот запущен для {settings.Update!.Message!.Chat.Username}");
        await settings.Bot.SendTextMessageAsync(
            text: "Этот бот умеет присылать фотки с яндекс диска.",
            chatId: settings.ChatId!,
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken);
    }

    private static async Task NoAccessAsync(Settings settings)
    {
        Console.WriteLine("Нет доступа.");
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId!,
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

    private static async Task SendPhotoAsync(Settings settings)
    {
        Console.WriteLine("Отправлено фото");
        var img = settings.Image!;
        await settings.Bot.SendPhotoAsync(
            chatId: settings.ChatId!,
            caption: $"<a href=\"{Secrets.OpenInBrowserUrl + img.Name}\">{img.Name}</a><b> {img.DateTime}</b>",
            parseMode: ParseMode.Html,
            photo: img.File!,
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("Ещё за эту дату", $"/find {img.DateTime.Date}")),
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );
    }

    private static async Task FindAsync(Settings settings)
    {
        if (DateTime.TryParse(settings.Query!.Replace(".", "-"), out var date))
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
    public Update? Update { get; init; }
    public long ChatId { get; set; }
    public CancellationToken CancellationToken { get; init; }
    public Image? Image { get; set; }
    public string? Cmd { get; set; }
    public string? Query { get; set; }
}