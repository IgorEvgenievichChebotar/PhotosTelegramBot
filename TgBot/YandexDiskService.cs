using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Newtonsoft.Json;
using Flurl.Http;

namespace TgBot;

public interface IYandexDiskService
{
    Image GetRandomImage();
    Image GetRandomImage(DateTime date);
    Image GetImage(string imgName);
    Image? FindImageByName(string imgName);
    void OpenImageInBrowser(string name);
    List<Image> GetImagesByDate(DateTime date);
    Task<List<Folder>> GetFoldersAsync();
    Task LoadImagesAsync();
    Task<MemoryStream> GetThumbnailImageAsync(Image img);
    Task<MemoryStream> GetOriginalImageAsync(Image img);
    Task<List<MemoryStream>> GetThumbnailImagesAsync(IEnumerable<Image> images);
    Task<List<MemoryStream>> DownloadOriginalImagesAsync(IEnumerable<Image> images);
    Task<Dictionary<string, MemoryStream>> DownloadLikesAsync(long chatId);
    Task<string> GetUrlToLikedImagesAsync(long chatId);
    Task<string> GetPublicFolderUrlByChatIdAsync(long chatId);
    void AddToLikes(long chatId, Image img);
}

public class YandexDiskService : IYandexDiskService
{
    private readonly List<Image> ImagesCache;
    private readonly HttpClient _httpClient;

    public YandexDiskService()
    {
        ImagesCache = new List<Image>();
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add(
            "Authorization",
            $"OAuth {Secrets.OAuthYandexDisk}"
        );
        FlurlHttp.Configure(settings =>
            settings.BeforeCall = call =>
                call.Request.WithHeader("Authorization", $"OAuth {Secrets.OAuthYandexDisk}")
        );
    }

    public async Task<MemoryStream> GetThumbnailImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient.GetAsync(img.Preview, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}: {response.StatusCode} {response.ReasonPhrase}");

        return new MemoryStream(content);
    }

    public async Task<MemoryStream> GetOriginalImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient.GetAsync(img.File, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}");

        return new MemoryStream(content);
    }

    public async Task<List<MemoryStream>> GetThumbnailImagesAsync(IEnumerable<Image> images)
    {
        var streams = new List<MemoryStream>();
        await Parallel.ForEachAsync(
            images,
            async (i, _) => { streams.Add(await GetThumbnailImageAsync(i)); });
        return streams;
    }

    public async Task<List<MemoryStream>> DownloadOriginalImagesAsync(IEnumerable<Image> images)
    {
        var streams = new List<MemoryStream>();
        await Parallel.ForEachAsync(images, async (i, _) => { streams.Add(await GetOriginalImageAsync(i)); });
        return streams;
    }

    public async Task<Dictionary<string, MemoryStream>> DownloadLikesAsync(long chatId) // 2 запроса
    {
        var urlFolderOnDisk = Secrets.GetUrlLikedImagesByChatIdOnDisk(chatId);
        var response = await _httpClient.GetAsync(urlFolderOnDisk);
        if (response.StatusCode == HttpStatusCode.NotFound) // папки нет
        {
            await GetPublicFolderUrlByChatIdAsync(chatId); //создать папку
            response = await _httpClient.GetAsync(urlFolderOnDisk); // повторный запрос
        }

        var jsonString = await response.Content.ReadAsStringAsync();
        var images = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2],
                new ImageExifConverter())
            .Where(i => i.Name.Contains(".jpg"))
            .Where(i => i.MimeType!.Contains("image/jpeg"));

        var likes = new Dictionary<string, MemoryStream>();
        await Parallel.ForEachAsync(
            images, async (i, _) => { likes.Add(i.Name, await GetThumbnailImageAsync(i)); }
        );

        return likes;
    }

    public async Task<string> GetUrlToLikedImagesAsync(long chatId)
    {
        return await GetPublicFolderUrlByChatIdAsync(chatId);
    }

    public async Task<string> GetPublicFolderUrlByChatIdAsync(long chatId) // 0-3 запроса
    {
        var createFolderUrl = Secrets.GetUrlFolderOnDisk(chatId);
        var publishFolderUrl = Secrets.GetUrlPublishFolderOnDisk(chatId);

        string publicUrl;
        var getFolderIfExists = await _httpClient.GetAsync(createFolderUrl);
        if (getFolderIfExists.StatusCode == HttpStatusCode.NotFound)
        {
            var createdFolderResponse = await _httpClient.PutAsync(createFolderUrl, null); //создаем
            var publishedFolderResponse = await _httpClient.PutAsync(publishFolderUrl, null); //публикуем

            if (createdFolderResponse.IsSuccessStatusCode && publishedFolderResponse.IsSuccessStatusCode)
            {
                var response = await _httpClient.GetAsync(createFolderUrl);
                var content = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(content);
                publicUrl = json.RootElement.GetProperty("public_url").GetString()!;
                Console.WriteLine($"{DateTime.Now} | Successful CreatePublicFolderByChatId: " +
                                  $"{response.StatusCode} {response.ReasonPhrase}");
                return publicUrl;
            }

            Console.WriteLine($"{DateTime.Now} | Error CreatePublicFolderByChatId: " +
                              $"{createdFolderResponse.StatusCode} {publishedFolderResponse.ReasonPhrase}");
        }

        var existingFolderRequest = await getFolderIfExists.Content.ReadAsStringAsync();
        var jsonDocument = JsonDocument.Parse(existingFolderRequest);
        publicUrl = jsonDocument.RootElement.GetProperty("public_url").GetString()!;
        Console.WriteLine($"{DateTime.Now} | Successful CreatePublicFolderByChatId: " +
                          $"{getFolderIfExists.StatusCode} {getFolderIfExists.ReasonPhrase}");
        return publicUrl;
    }

    public void AddToLikes(long chatId, Image img)
    {
        var urlCopyImageToFolderOnDisk = Secrets.GetUrlCopyImageToFolderOnDisk(
            chatId: chatId,
            currentPath: "disk:/" + Secrets.CurrentFolder + "/",
            imgName: img.Name);
        _httpClient.PostAsync(urlCopyImageToFolderOnDisk, null);
    }

    public Image GetRandomImage()
    {
        var img = ImagesCache
            .Where(i => Secrets.CurrentFolder.Contains(i.ParentFolder!.Name!))
            .OrderBy(_ => Guid.NewGuid())
            .First();
        return img;
    }

    public Image GetRandomImage(DateTime date)
    {
        var randomImage = ImagesCache
            .Where(i => i.DateTime.Date == date) //findAll был
            .MinBy(_ => Guid.NewGuid()) ?? GetRandomImage();
        return randomImage;
    }

    public Image GetImage(string imgName)
    {
        var img = FindImageByName(imgName);
        return img ?? GetRandomImage();
    }

    public Image? FindImageByName(string imgName)
    {
        return ImagesCache
            .Where(i => Secrets.CurrentFolder.Contains(i.ParentFolder!.Name!))
            .FirstOrDefault(i => i.Name.ToLower().Contains(imgName.ToLower()));
    }

    public async Task<List<Folder>> GetFoldersAsync()
    {
        var response = await _httpClient.GetAsync(Secrets.FoldersRequest);
        var jsonString = await response.Content.ReadAsStringAsync();
        var folders = JsonConvert.DeserializeObject<ICollection<Folder>>(
                jsonString[22..^2],
                new FolderJsonConverter())
            .Where(pf => pf.Type == "dir")
            .ToList();
        return folders;
    }

    public async Task LoadImagesAsync()
    {
        if (ImagesCache.Any(i => Secrets.CurrentFolder.Contains(i.ParentFolder!.Name!)))
        {
            Console.WriteLine(
                $"{DateTime.Now} | Фотки в папке {Secrets.CurrentFolder} уже есть; Всего: {ImagesCache.Count}");
            return;
        }

        var response = await _httpClient.GetAsync($"{Secrets.GetUrlAllImagesOnDisk}");

        var jsonString = await response.Content.ReadAsStringAsync();

        var newImages = new List<Image>();
        if (jsonString.Contains("image/jpeg"))
        {
            newImages = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2],
                    new ImageExifConverter())
                .Where(i => i.Name.Contains(".jpg"))
                .Where(i => i.MimeType!.Contains("image/jpeg"))
                .ToList();
            ImagesCache.AddRange(newImages);
        }


        var folders = await GetFoldersAsync();
        if (folders.Any())
        {
            Console.WriteLine($"Есть {folders.Count} папок в {Secrets.CurrentFolder}");
        }

        Console.WriteLine(
            $"{DateTime.Now} | {newImages.Count} фоток загружены из папки {Secrets.CurrentFolder}. Всего: {ImagesCache.Count}");
    }

    public void OpenImageInBrowser(string name)
    {
        var img = FindImageByName(name);
        if (img == null) return;
        var url = Secrets.OpenInBrowserUrl + img.Name;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public List<Image> GetImagesByDate(DateTime date)
    {
        var images = ImagesCache.Where(i => i.DateTime.Date == date).ToList();
        return images;
    }
}