﻿using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

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
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");

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

                if (msg.Chat.Username != $"{Secrets.MyUsername}")
                {
                    await bot.SendTextMessageAsync(
                        chatId: 421981741,
                        text: $"{DateTime.Now} | {msg.Chat.FirstName} написала боту: {msgText}",
                        cancellationToken: cts);
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
                        settings.Image = _service.GetImage(settings.Query!);
                        await LikeAsync(settings);
                        return;
                    case "/likes" or "избранные" or "🖤":
                        await GetLikesAsync(settings);
                        return;
                    default:
                        await FindAsync(settings);
                        return;
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
                    case "/loadlikes":
                        await DownloadArchiveOfOriginalsAsync(settings);
                        return;
                }

                return;
        }
    }

    private static async Task GetLikesAsync(Settings settings)
    {
        var likes = _service.GetLikes(settings.ChatId);
        if (!likes.Any())
        {
            await settings.Bot.SendTextMessageAsync(
                chatId: settings.ChatId,
                text: "Список избранных пуст",
                cancellationToken: settings.CancellationToken);
            return;
        }

        var thumbnails = await _service.GetThumbnailImagesAsync(likes);
        await settings.Bot.SendMediaGroupAsync(
            chatId: settings.ChatId,
            media: thumbnails
                .Take(10) //todo
                .Zip(likes, (ms, i) => new InputMediaPhoto(new InputMedia(ms, i.Name))),
            cancellationToken: settings.CancellationToken
        );

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: "Скачать оригиналы архивом?",
            replyMarkup: new InlineKeyboardMarkup(
                InlineKeyboardButton.WithCallbackData("Скачать", $"/loadlikes {settings.ChatId}")),
            cancellationToken: settings.CancellationToken);
    }

    private static async Task DownloadArchiveOfOriginalsAsync(Settings settings)
    {
        var likes = _service.GetLikes(settings.ChatId);

        static async Task<MemoryStream> CompressImagesToZip(ICollection<Image> images)
        {
            var sw = Stopwatch.StartNew();
            var imageStreams = await _service.GetOriginalImagesAsync(images);
            using var archiveStream = new MemoryStream();
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

            var index = 0;
            foreach (var imageStream in imageStreams)
            {
                var entry = archive.CreateEntry($"image{index}.jpg");
                await using (var entryStream = entry.Open())
                {
                    await imageStream.CopyToAsync(entryStream);
                }

                index++;
            }

            Console.WriteLine(sw.ElapsedMilliseconds + $"ms на архивацию {images.Count} оригиналов");
            return archiveStream;
        }

        var zipArchive = await CompressImagesToZip(likes);

        await settings.Bot.SendDocumentAsync(
            chatId: settings.ChatId,
            document: new InputOnlineFile(new MemoryStream(zipArchive.ToArray()), "originals.zip"),
            caption: "Архив фоток в оригинальном качестве",
            parseMode: ParseMode.Html,
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken);
    }

    private static async Task StartAsync(Settings settings)
    {
        Console.WriteLine($"{DateTime.Now} | Бот запущен для {settings.Update.Message!.Chat.Username}");
        await settings.Bot.SendTextMessageAsync(
            text: "Этот бот умеет присылать фотки с яндекс диска, " +
                  "искать по названию/дате, " +
                  "добавлять в избранное и скачивать оригиналы архивом",
            chatId: settings.ChatId,
            replyMarkup: defaultReplyKeyboardMarkup,
            cancellationToken: settings.CancellationToken,
            disableNotification: true);
    }

    private static async Task LikeAsync(Settings settings)
    {
        if (!_service.AddToLikes(settings.ChatId, settings.Image!))
        {
            await settings.Bot.SendTextMessageAsync(
                chatId: settings.ChatId,
                text: $"{settings.Image!.Name} уже в избранном",
                cancellationToken: settings.CancellationToken);
            return;
        }

        await settings.Bot.SendTextMessageAsync(
            chatId: settings.ChatId,
            text: $"{settings.Image!.Name} добавлено в избранное",
            cancellationToken: settings.CancellationToken);
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
        Console.WriteLine(
            $"{DateTime.Now} | {msg.Chat.Username}, {msg.Chat.FirstName} {msg.Chat.LastName} - Нет доступа");
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
                cancellationToken:
                settings.CancellationToken,
                disableNotification:
                true
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
    public ITelegramBotClient Bot { get; init; }
    public Update Update { get; init; }
    public long ChatId { get; set; }
    public CancellationToken CancellationToken { get; init; }
    public Image? Image { get; set; }
    public string? Cmd { get; set; }
    public string? Query { get; set; }
}