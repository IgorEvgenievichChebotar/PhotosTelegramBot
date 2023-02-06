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
        if (update.Message is not { Text: { } } msg)
        {
            return;
        }

        if (msg.Chat.Username != $"{Secrets.MyUsername}")
        {
            await bot.SendTextMessageAsync(
                chatId: msg.Chat.Id,
                text: "Нет доступа.",
                cancellationToken: cts,
                disableNotification: true);
            return;
        }

        var text = msg.Text;

        if (text.Contains("/help"))
        {
            await bot.SendTextMessageAsync(
                chatId: msg.Chat.Id,
                text: "Доступные команды:\n" +
                      "/find <дата, имя> - найти фото по названию.\n" +
                      "/open <имя> - открыть фото на диске.\n",
                cancellationToken: cts,
                disableNotification: true
            );
            return;
        }

        Image img;
        if (text.Contains("/find"))
        {
            if (DateTime.TryParse(text.Split(" ")[1].Replace(".", "-"), out var date))
            {
                var images = _service.GetImagesByDate(date);
                if (!images.Any())
                {
                    await bot.SendTextMessageAsync(
                        chatId: msg.Chat.Id,
                        text: "Нет фотографий за эту дату.",
                        cancellationToken: cts,
                        disableNotification: true);
                }

                await bot.SendMediaGroupAsync(
                    chatId: msg.Chat.Id,
                    media: images.Take(10).Select(i => new InputMediaPhoto(i.File!)),
                    cancellationToken: cts,
                    disableNotification: true
                );

                if (images.Count > 10)
                {
                    return; //todo
                }

                return;
            }

            img = _service.GetImage(text.Split(" ")[1]);
        }
        else
        {
            img = _service.GetRandomImage();
        }

        if (text.Contains("/open"))
        {
            _service.OpenImageInBrowser(text.Split(" ")[1]);
            return;
        }

        await bot.SendPhotoAsync(
            chatId: msg.Chat.Id,
            caption: $"<a href=\"{Secrets.OpenInBrowserUrl + img.Name}\">{img.Name}</a><b> {img.DateTime}</b>",
            parseMode: ParseMode.Html,
            photo: img.File!,
            replyMarkup: new ReplyKeyboardMarkup(new KeyboardButton("Ещё")) { ResizeKeyboard = true },
            cancellationToken: cts,
            disableNotification: true
        );
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