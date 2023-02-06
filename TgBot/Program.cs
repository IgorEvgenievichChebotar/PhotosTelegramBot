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

    public static async Task Main(string[] args)
    {
        var bot = new TelegramBotClient($"{Secrets.TelegramBotToken}");

        using CancellationTokenSource cts = new();

        ReceiverOptions receiverOptions = new()
        {
            AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
        };

        await _service.PreloadImagesAsync();


        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
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

            if (msg.Chat.Username != $"{Secrets.MyUsername}")
            {
                await NoAccess(bot, cts, chatId);
                return;
            }

            if (msgText.Contains("/start"))
            {
                await Start(bot, cts, msg, chatId);
            }

            if (msgText.Contains("/help"))
            {
                await Help(bot, cts, chatId);
                return;
            }

            Image img;
            if (msgText.Contains("/find"))
            {
                img = await Find(bot, cts, msgText, chatId);
            }
            else
            {
                img = _service.GetRandomImage();
            }

            await SendPhoto(bot, cts, chatId, img);
        }
        else if (update.Type == UpdateType.CallbackQuery)
        {
            var fromId = update.CallbackQuery!.From.Id;
            var queryData = update.CallbackQuery.Data!;

            await Find(bot, cts, queryData, fromId);
        }
    }

    private static async Task Start(ITelegramBotClient bot, CancellationToken cts, Message msg, long chatId)
    {
        Console.WriteLine($"Бот запущен для {msg.Chat.Username}");
        await bot.SendTextMessageAsync(
            text: "Ещё?",
            chatId: chatId,
            replyMarkup: new ReplyKeyboardMarkup(new KeyboardButton("Ещё")) { ResizeKeyboard = true },
            cancellationToken: cts);
    }

    private static async Task NoAccess(ITelegramBotClient bot, CancellationToken cts, long chatId)
    {
        Console.WriteLine("Нет доступа.");
        await bot.SendTextMessageAsync(
            chatId: chatId,
            text: "Нет доступа.",
            cancellationToken: cts,
            disableNotification: true);
        return;
    }

    private static async Task Help(ITelegramBotClient bot, CancellationToken cts, long chatId)
    {
        Console.WriteLine("Отправлен список помощи");
        await bot.SendTextMessageAsync(
            chatId: chatId,
            text: "Доступные команды:\n" +
                  "/find <дата, имя> - найти фото по названию.\n",
            cancellationToken: cts,
            disableNotification: true
        );
        return;
    }

    private static async Task SendPhoto(ITelegramBotClient bot, CancellationToken cts, long chatId, Image img)
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

    private static async Task<Image> Find(ITelegramBotClient bot, CancellationToken cts, string msgText, long chatId)
    {
        if (DateTime.TryParse(msgText.Split(" ")[1].Replace(".", "-"), out var date))
        {
            var images = _service.GetImagesByDate(date);
            if (!images.Any())
            {
                Console.WriteLine("Нет фотографий за эту дату.");
                await bot.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Нет фотографий за эту дату.",
                    cancellationToken: cts,
                    disableNotification: true);
            }

            Console.WriteLine("Отправлена группа фотографий");
            var set = new HashSet<Image>(images);
            await bot.SendMediaGroupAsync(
                chatId: chatId,
                media: set.OrderBy(i => Guid.NewGuid()).Take(10).Select(i => new InputMediaPhoto(i.File!)),
                cancellationToken: cts,
                disableNotification: true
            );
        }

        var img = _service.GetImage(msgText.Split(" ")[1]);
        return img;
    }


    private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cts)
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