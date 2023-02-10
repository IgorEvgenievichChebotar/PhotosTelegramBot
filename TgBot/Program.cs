using System.Globalization;
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
        new KeyboardButton("Сменить папку"),
        new KeyboardButton("Избранные")
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
        (
            bot: bot,
            cancellationToken: cts,
            update: update
        );

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

                if (msg.Chat.Username != $"{Secrets.MyUsername}")
                {
                    await bot.SendTextMessageAsync(
                        chatId: 421981741,
                        text: $"{DateTime.Now} | {msg.Chat.FirstName} написала боту: {msgText}",
                        cancellationToken: cts);
                    await NoAccessAsync(settings);
                    return;
                }

                #region msg commands

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
                    case "сменить":
                        var folders = await _service.GetFoldersAsync();
                        await bot.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Выбери из списка",
                            replyMarkup: new InlineKeyboardMarkup(
                                folders.Select(f => new[]
                                    { InlineKeyboardButton.WithCallbackData(f.Name!, $"/changedir {f.Name}") })
                            ),
                            cancellationToken: cts);
                        return;
                    case "/like":
                        await LikeAsync(settings);
                        return;
                    case "/likes" or "избранные" or "🖤":
                        await GetLikesAsync(settings);
                        return;
                    default:
                        await FindAsync(settings);
                        return;

                    #endregion
                }

            case UpdateType.CallbackQuery:
                var data = update.CallbackQuery!.Data;
                settings.Query = data!.Split(" ")[1];
                settings.Cmd = data.Split(" ")[0];
                settings.ChatId = update.CallbackQuery!.From.Id;
                if (data.Split(" ").Length > 1)
                {
                    settings.Query = data[(data.IndexOf(" ", StringComparison.Ordinal) + 1)..];
                }

                #region callback commands

                switch (settings.Cmd)
                {
                    case "/find":
                        await FindAsync(settings);
                        return;
                    case "/like":
                        settings.Image = _service.GetImage(settings.Query!);
                        await LikeAsync(settings);
                        return;
                    case "/changedir":
                        await ChangeDirAsync(settings);
                        return;
                    case "/openlikes":
                        await GetLikesAsync(settings);
                        return;

                    #endregion
                }

                return;
        }
    }

    private static async Task GetLikesAsync(Settings settings)
    {
        var likes = await _service.DownloadLikesAsync(settings.ChatId);

        if (!likes.Any())
        {
            await settings.Bot.SendTextMessageAsync(
                chatId: settings.ChatId,
                text: "Список избранных пуст",
                cancellationToken: settings.CancellationToken);
            return;
        }

        var select = likes.Select(pair => new InputMediaPhoto(new InputMedia(pair.Value, pair.Key)));
        await settings.Bot.SendMediaGroupAsync(
            chatId: settings.ChatId,
            media: select,
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );

        var urlToLikedImages = _service.GetUrlToLikedImagesAsync(chatId: settings.ChatId);

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"<a href=\"{await urlToLikedImages}\">Папка на диске</a>",
            parseMode: ParseMode.Html,
            cancellationToken: settings.CancellationToken,
            disableNotification: true
        );
    }

    private static async Task StartAsync(Settings settings)
    {
        Console.WriteLine($"{DateTime.Now} | Бот запущен для {settings.Update.Message!.Chat.Username}");
        await settings.Bot.SendTextMessageAsync(
            text: "Этот бот умеет присылать фотки с яндекс диска, " +
                  "искать по названию/дате, " +
                  "добавлять в избранное " +
                  "и формировать папку с оригиналами на яндекс диске.",
            chatId: settings.ChatId,
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private static async Task LikeAsync(Settings settings)
    {
        settings.Image = _service.GetImage(settings.Query!);

        var url = await _service.GetPublicFolderUrlByChatIdAsync(settings.ChatId);
        _service.AddToLikes(settings.ChatId, settings.Image!);

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"{settings.Image!.Name} добавлено в <a href=\"{url}\">избранное</a>",
            disableWebPagePreview: true,
            cancellationToken: settings.CancellationToken,
            parseMode: ParseMode.Html,
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
        Console.WriteLine($"{DateTime.Now} | {msg.Chat.FirstName} написала боту: {msg}");
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Нет доступа",
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private static async Task HelpAsync(Settings settings)
    {
        Console.WriteLine($"{DateTime.Now} | Отправлен список помощи");
        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Доступные команды:\n" +
                  "/find <дата, имя> - найти фото по названию.\n" +
                  "/changedir <имя> - сменить папку.\n" +
                  "/like <имя> - добавить фотку в избранные.\n" +
                  "/likes - избранные фотки.\n" +
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
                photo: (await _service.GetThumbnailImageAsync(img))!,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("Ещё за эту дату", $"/find {img.DateTime.Date}"),
                    InlineKeyboardButton.WithCallbackData("🖤", $"/like {img.Name}"),
                }),
                cancellationToken: settings.CancellationToken,
                disableNotification: true
            );

            var username = (settings.Update.Message is not null
                ? settings.Update.Message.Chat.Username
                : settings.Update.CallbackQuery!.From.Username)!;

            Console.WriteLine(
                $"{DateTime.Now} | Отправлено фото {img} пользователю {username}");
        }

        if (settings.Query == null)
        {
            settings.Image = _service.GetRandomImage();
            await SendPhotoAsync(settings);
            return;
        }

        var dateString = settings.Query.Split(" ")[0];
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        if (DateTime.TryParseExact(
                dateString,
                "dd.MM.yyyy",
                CultureInfo.GetCultureInfo("de-DE"),
                DateTimeStyles.None,
                out var date))
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
    public Settings(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        Bot = bot;
        Update = update;
        CancellationToken = cancellationToken;
    }

    public ITelegramBotClient Bot { get; }
    public Update Update { get; }
    public long ChatId { get; set; }
    public CancellationToken CancellationToken { get; }
    public Image? Image { get; set; }
    public string? Cmd { get; set; }
    public string? Query { get; set; }
}