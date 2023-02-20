using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Newtonsoft.Json;
using Flurl.Http;

namespace TgBot;

public interface IYandexDiskService
{
    Image GetRandomImage();
    Image? GetRandomImage(DateTime date);
    Image GetImage(string imgName);
    Image? FindImageByName(string imgName);
    void OpenImageInBrowser(string name);
    List<Image> GetImagesByDate(DateTime date);
    Task<List<Folder>> GetFoldersAsync();
    Task LoadImagesAsync();
    Task<byte[]> LoadThumbnailImageAsync(Image img);
    Task<byte[]> LoadOriginalImageAsync(Image img);
    Task<List<byte[]>> LoadThumbnailImagesAsync(IEnumerable<Image> images);
    Task<List<byte[]>> LoadOriginalImagesAsync(IEnumerable<Image> images);
    Task<Dictionary<string, byte[]>> GetLikesAsync(long chatId);
    Task<Dictionary<string, byte[]>> LoadLikesAsync(long chatId);
    Task<string> GetUrlToLikedImagesAsync(long chatId);
    Task<string> GetPublicFolderUrlByChatIdAsync(long chatId);
    Task AddToLikes(long chatId, Image img);
    void DeleteImage(string imgName);
}

public class YandexDiskService : IYandexDiskService
{
    private readonly List<Image> ImagesCache;
    private readonly Dictionary<long, Dictionary<string, byte[]>> LikesCache;
    private readonly HttpClient _httpClient;

    public YandexDiskService()
    {
        ImagesCache = new List<Image>();
        LikesCache = new Dictionary<long, Dictionary<string, byte[]>>();
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

    public async Task<byte[]> LoadThumbnailImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient.GetAsync(img.Preview, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}: {response.StatusCode} {response.ReasonPhrase}");

        return content;
    }

    public async Task<byte[]> LoadOriginalImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient.GetAsync(img.File, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}");

        return content;
    }

    public async Task<List<byte[]>> LoadThumbnailImagesAsync(IEnumerable<Image> images)
    {
        var bytes = new List<byte[]>();
        await Parallel.ForEachAsync(
            images,
            async (i, _) => { bytes.Add(await LoadThumbnailImageAsync(i)); });
        return bytes;
    }

    public async Task<List<byte[]>> LoadOriginalImagesAsync(IEnumerable<Image> images)
    {
        var bytes = new List<byte[]>();
        await Parallel.ForEachAsync(
            images,
            async (i, _) => { bytes.Add(await LoadOriginalImageAsync(i)); });
        return bytes;
    }

    public async Task<Dictionary<string, byte[]>> GetLikesAsync(long chatId) // 2 запроса
    {
        if (LikesCache.ContainsKey(chatId))
        {
            return LikesCache[chatId];
        }

        return await LoadLikesAsync(chatId);
    }

    public async Task<Dictionary<string, byte[]>> LoadLikesAsync(long chatId)
    {
        var urlFolderOnDisk = Secrets.GetUrlLikedImagesByChatIdOnDisk(chatId, limit: 10);
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

        LikesCache[chatId] = new Dictionary<string, byte[]>();

        await Parallel.ForEachAsync(
            images, async (i, _) => { LikesCache[chatId].Add(i.Name, (await LoadThumbnailImageAsync(i)).ToArray()); }
        );

        return LikesCache[chatId];
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

    public async Task AddToLikes(long chatId, Image img)
    {
        var urlCopyImageToFolderOnDisk = Secrets.GetUrlCopyImageToFolderOnDisk(
            chatId: chatId,
            currentPath: "disk:/" + Secrets.TargetFolder + "/",
            imgName: img.Name);

        var task = _httpClient.PostAsync(urlCopyImageToFolderOnDisk, null);

        var bytes = await LoadThumbnailImageAsync(img);

        var dict = await GetLikesAsync(chatId);
        if (!dict.ContainsKey(img.Name))
        {
            dict.Add(img.Name, bytes);
        }
    }

    public void DeleteImage(string imgName)
    {
        var url = Secrets.GetUrlDeleteImageOnDisk(imgName);
        _httpClient.DeleteAsync(url);
        var image = ImagesCache.Find(i => i.Name == imgName);
        ImagesCache.Remove(image!);
    }

    public Image GetRandomImage()
    {
        var img = ImagesCache
            .Where(i => Secrets.TargetFolder.Contains(i.ParentFolder!.Name!))
            .OrderBy(_ => Guid.NewGuid())
            .First();
        return img;
    }

    public Image? GetRandomImage(DateTime date)
    {
        var randomImage = ImagesCache
            .Where(i => i.DateTime.Date == date) //findAll был
            .MinBy(_ => Guid.NewGuid());
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
            .Where(i => Secrets.TargetFolder.Contains(i.ParentFolder!.Name!))
            .FirstOrDefault(i => i.Name.ToLower().Contains(imgName.ToLower()));
    }

    public async Task<List<Folder>> GetFoldersAsync()
    {
        var url = Secrets.GetUrlFoldersRequest();
        var response = await _httpClient.GetAsync(url);
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
        var cacheCount = Secrets.CacheCount;
        if (ImagesCache.Any(i => Secrets.TargetFolder.Contains(i.ParentFolder!.Name!)))
        {
            Console.WriteLine(
                $"{DateTime.Now} | Фотки в папке {Secrets.TargetFolder} уже есть; " +
                $"Всего: {ImagesCache.Count}");
            return;
        }

        var responseCache = await _httpClient.GetAsync($"{Secrets.GetUrlAllImagesOnDisk(limit: cacheCount)}");
        var cacheImagesCount = await LoadAndDeserializeImages(responseCache);

        Console.WriteLine(
            $"{DateTime.Now} | {cacheImagesCount} фоток для кэша загружены из папки {Secrets.TargetFolder}. " +
            $"Всего: {ImagesCache.Count}");

        LoadRemainingAllImagesAsync();

        async void LoadRemainingAllImagesAsync()
        {
            var response = await _httpClient.GetAsync($"{Secrets.GetUrlAllImagesOnDisk(offset: cacheCount)}");
            var remainingImagesCount = await LoadAndDeserializeImages(response);

            Console.WriteLine(
                $"{DateTime.Now} | {remainingImagesCount} оставшихся фоток подгружено из папки {Secrets.TargetFolder}. " +
                $"Всего: {ImagesCache.Count}");
        }
    }

    private async Task<int> LoadAndDeserializeImages(HttpResponseMessage response)
    {
        var images = new List<Image>();
        var jsonString = await response.Content.ReadAsStringAsync();
        if (!jsonString.Contains("image/jpeg")) return images.Count;
        images = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2],
                new ImageExifConverter())
            .Where(i => i.Name.Contains(".jpg"))
            .Where(i => i.MimeType!.Contains("image/jpeg"))
            .ToList();
        ImagesCache.AddRange(images);

        return images.Count;
    }

    public void OpenImageInBrowser(string name)
    {
        var img = FindImageByName(name);
        if (img == null) return;
        var url = Secrets.GetUrlOpenInBrowser + img.Name;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public List<Image> GetImagesByDate(DateTime date)
    {
        var images = ImagesCache.Where(i => i.DateTime.Date == date).ToList();
        return images;
    }
}