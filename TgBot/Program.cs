using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TgBot;

class Program
{
    private static readonly YandexDiskService _service = new();

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

    static async Task HandleUpdateAsync(ITelegramBotClient bot, Update update,
        CancellationToken cts)
    {
        if (update.Message is not { Text: { } } msg)
        {
            return;
        }

        if (msg.Chat.Username != $"{Secrets.MyUsername}")
        {
            await bot.SendTextMessageAsync(
                chatId: msg.Chat.Id,
                text: "Нет доступа.",
                cancellationToken: cts);
            return;
        }

        var text = msg.Text;

        if (text.Contains("/help"))
        {
            await bot.SendTextMessageAsync(
                chatId: msg.Chat.Id,
                text: "Доступные команды:\n" +
                      "/find <...> - найти фото по названию.\n" +
                      "/open <...> - открыть фото на диске.\n" +
                      "Другой ввод будет присылать случайную фотографию.",
                cancellationToken: cts
            );
            return;
        }

        Image img;
        if (text.Contains("/find"))
        {
            if (DateOnly.TryParse(text[6..], out var date))
            {
                var images = _service.GetImagesByDate(date).Take(10); // todo
                await bot.SendMediaGroupAsync(
                    chatId: msg.Chat.Id,
                    media: images.Select(i => new InputMediaPhoto(i.File!)),
                    cancellationToken: cts
                );
                return;
            }

            img = _service.GetImage(text[6..]);
        }
        else
        {
            img = _service.GetRandomImage();
        }

        if (text.Contains("/open"))
        {
            _service.OpenImageInBrowser(text[6..]);
            return;
        }

        await bot.SendPhotoAsync(
            chatId: msg.Chat.Id,
            caption: $"<a href=\"{Secrets.OpenInBrowserUrl + img.Name}\">{img.Name}</a>",
            parseMode: ParseMode.Html,
            photo: img.File!,
            replyMarkup: new ReplyKeyboardMarkup(new KeyboardButton("Ещё")) { ResizeKeyboard = true },
            cancellationToken: cts
        );
    }

    static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception,
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