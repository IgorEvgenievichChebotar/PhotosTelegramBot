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
        var botClient = new TelegramBotClient($"{Secrets.TelegramBotToken}");

        using CancellationTokenSource cts = new();

        ReceiverOptions receiverOptions = new()
        {
            AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
        };
        
        await _service.PreloadAllPhotosAsync();

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        Console.ReadLine();
    }

    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cts)
    {
        if (update.Message is not { Text: { } } msg)
        {
            return;
        }

        if (msg.Chat.Username != $"{Secrets.MyUsername}")
        {
            await botClient.SendTextMessageAsync(
                chatId: msg.Chat.Id,
                text: "Нет доступа.",
                cancellationToken: cts);
            return;
        }

        var replyMarkup = new ReplyKeyboardMarkup(new[] { new KeyboardButton[] { "Ещё" } })
            { ResizeKeyboard = true };

        var img = _service.GetConcreteImage(msg.Text) ?? _service.GetRandomImage();

        await botClient.SendPhotoAsync(
            chatId: msg.Chat.Id,
            caption: img.Name,
            photo: img.File!,
            replyMarkup: replyMarkup,
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