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
        if (update.Message is { Text: { } } msg)
        {
            var msgText = msg.Text;
            var chatId = msg.Chat.Id;

            var cmd = msgText.Split(" ");

            if (msg.Chat.Username != $"{Secrets.MyUsername}")
            {
                await NoAccessAsync(bot, cts, chatId);
                return;
            }

            Image img;
            switch (cmd[0])
            {
                case "/start":
                    await StartAsync(bot, cts, msg, chatId);
                    await HelpAsync(bot, cts, chatId);
                    return;
                case "/help":
                    await HelpAsync(bot, cts, chatId);
                    return;
                case "/find":
                    await FindAsync(bot, cts, msgText, chatId);
                    return;
                default:
                    img = _service.GetRandomImage();
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
                await SendPhotoAsync(bot, cts, chatId, img);
            }
        }
        else if (update.Type == UpdateType.CallbackQuery)
        {
            var fromId = update.CallbackQuery!.From.Id;
            var queryData = update.CallbackQuery.Data!;
            var cmd = queryData.Split(" ");

            switch (cmd[0])
            {
                case "/find":
                    await FindAsync(bot, cts, cmd[1], fromId);
                    return;
                case "/change":
                    await ChangeDirAsync(bot, cts, cmd[1], fromId);
                    break;
            }
        }
    }

    private static async Task ChangeDirAsync(ITelegramBotClient bot, CancellationToken cts, string data, long fromId)
    {
        var date = data.Replace("_", " ");
        if (Secrets.PathToDir != date)
        {
            Secrets.PathToDir = date;
            await _service.LoadImagesAsync();
        }

        await bot.SendTextMessageAsync(
            chatId: fromId,
            text: $"Папка изменена на {Secrets.PathToDir}",
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: cts,
            disableNotification: true);
    }

    private static async Task StartAsync(ITelegramBotClient bot, CancellationToken cts, Message msg, long chatId)
    {
        Console.WriteLine($"Бот запущен для {msg.Chat.Username}");
        await bot.SendTextMessageAsync(
            text: "Этот бот умеет присылать фотки с яндекс диска.",
            chatId: chatId,
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: cts);
    }

    private static async Task NoAccessAsync(ITelegramBotClient bot, CancellationToken cts, long chatId)
    {
        Console.WriteLine("Нет доступа.");
        await bot.SendTextMessageAsync(
            chatId: chatId,
            text: "Нет доступа.",
            cancellationToken: cts,
            disableNotification: true);
    }

    private static async Task HelpAsync(ITelegramBotClient bot, CancellationToken cts, long chatId)
    {
        Console.WriteLine("Отправлен список помощи");
        await bot.SendTextMessageAsync(
            chatId: chatId,
            text: "Доступные команды:\n" +
                  "/find <дата, имя> - найти фото по названию.\n" +
                  "/changedir <имя> - сменить папку.\n" +
                  "/help - доступные команды.\n" +
                  "/start - начало работы бота.\n",
            cancellationToken: cts,
            disableNotification: true
        );
    }

    private static async Task SendPhotoAsync(ITelegramBotClient bot, CancellationToken cts, long chatId, Image img)
    {
        Console.WriteLine("Отправлено фото");
        await bot.SendPhotoAsync(
            chatId: chatId,
            caption: $"<a href=\"{Secrets.OpenInBrowserUrl + img.Name}\">{img.Name}</a><b> {img.DateTime}</b>",
            parseMode: ParseMode.Html,
            photo: img.File!,
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("Ещё за эту дату", $"/find {img.DateTime.Date}")),
            cancellationToken: cts,
            disableNotification: true
        );
    }

    private static async Task FindAsync(ITelegramBotClient bot, CancellationToken cts, string data, long chatId)
    {
        if (DateTime.TryParse(data.Replace(".", "-"), out var date))
        {
            var img = _service.GetRandomImage(date);
            await SendPhotoAsync(bot, cts, chatId, img);
            return;
        }

        await SendPhotoAsync(bot, cts, chatId, _service.GetImage(data.Split(" ")[1]));
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