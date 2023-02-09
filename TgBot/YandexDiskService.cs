using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Newtonsoft.Json;
using Flurl.Http;
using Flurl.Http.Configuration;

namespace TgBot;

public interface IYandexDiskService
{
    Image GetRandomImage();
    Image GetRandomImage(DateTime date);
    Image GetImage(string image);
    void OpenImageInBrowser(string name);
    IList<Image> GetImagesByDate(DateTime date);
    Task<IList<Folder>> GetFoldersAsync();
    Task LoadImagesAsync();
    Task<MemoryStream> GetThumbnailImageAsync(Image img);
    Task<MemoryStream> GetOriginalImageAsync(Image img);
    Task<IList<MemoryStream>> GetThumbnailImagesAsync(IEnumerable<Image> images);
    Task<IList<MemoryStream>> GetOriginalImagesAsync(IEnumerable<Image> images);
    Task<IList<Image>> GetLikesAsync(long chatId);
    Task<string> GetUrlToLikedImages(long chatId);
    Task<string> CreatePublicFolderByChatId(long chatId);
    Task MoveLikedImagesToPublicFolder(long chatId);
    Task AddToLikesAsync(long chatId, Image img);
}

public class YandexDiskService : IYandexDiskService
{
    private readonly List<Image> Images;
    private readonly Dictionary<long, List<Image>> Likes;
    private readonly HttpClient _httpClient;

    public YandexDiskService()
    {
        Likes = new Dictionary<long, List<Image>>(); //todo from memory
        Images = new List<Image>();
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

    public async Task<IList<MemoryStream>> GetThumbnailImagesAsync(IEnumerable<Image> images)
    {
        var streams = new List<MemoryStream>();
        await Parallel.ForEachAsync(
            images, 
            async (i, _) => { streams.Add(await GetThumbnailImageAsync(i)); });
        return streams;
    }

    public async Task<IList<MemoryStream>> GetOriginalImagesAsync(IEnumerable<Image> images)
    {
        var streams = new List<MemoryStream>();
        await Parallel.ForEachAsync(images, async (i, _) => { streams.Add(await GetOriginalImageAsync(i)); });
        return streams;
    }

    public async Task<IList<Image>> GetLikesAsync(long chatId)
    {
        await CreatePublicFolderByChatId(chatId);
        var urlFolderOnDisk = Secrets.GetUrlLikedImagesByChatIdOnDisk(chatId);
        var response = await _httpClient.GetAsync(urlFolderOnDisk);
        var jsonString = await response.Content.ReadAsStringAsync();
        var likes = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2],
                new ImageExifConverter())
            .Where(i => i.Name.Contains(".jpg"))
            .Where(i => i.MimeType!.Contains("image/jpeg"))
            .ToList();
        return likes;
        /*return Likes.ContainsKey(chatId) ? Likes[chatId] : new List<Image>(); //todo get from disk*/
    }

    public async Task<string> GetUrlToLikedImages(long chatId)
    {
        var urlToFolder = await CreatePublicFolderByChatId(chatId);
        await MoveLikedImagesToPublicFolder(chatId);
        return urlToFolder;
    }

    public async Task<string> CreatePublicFolderByChatId(long chatId)
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

    public async Task MoveLikedImagesToPublicFolder(long chatId)
    {
        var likes = await GetLikesAsync(chatId);
        foreach (var img in likes)
        {
            var url = Secrets.GetUrlCopyImageToFolderOnDisk(
                chatId: chatId,
                currentPath: "disk:/" + Secrets.CurrentFolder + "/",
                imgName: img.Name);
            var response = await _httpClient.PostAsync(url, null);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"{DateTime.Now} | Error MoveLikedImagesToPublicFolder: " +
                                  $"{response.StatusCode} {response.ReasonPhrase}");
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var href = json.RootElement.GetProperty("href").GetString()!;
            Console.WriteLine($"Successful MoveLikedImagesToPublicFolder request, href: {href}");
        }
    }

    public async Task AddToLikesAsync(long chatId, Image img)
    {
        await CreatePublicFolderByChatId(chatId);
        var urlCopyImageToFolderOnDisk = Secrets.GetUrlCopyImageToFolderOnDisk(
            chatId: chatId,
            currentPath: "disk:/" + Secrets.CurrentFolder + "/",
            imgName: img.Name);

        await _httpClient.PostAsync(urlCopyImageToFolderOnDisk, null);
        Console.WriteLine();
    }

    public Image GetRandomImage()
    {
        var img = Images
            .Where(i => Secrets.CurrentFolder.Contains(i.ParentFolder!.Name!))
            .OrderBy(_ => Guid.NewGuid())
            .First();
        return img;
    }

    public Image GetRandomImage(DateTime date)
    {
        var randomImage = Images
            .Where(i => i.DateTime.Date == date) //findAll был
            .MinBy(_ => Guid.NewGuid()) ?? GetRandomImage();
        return randomImage;
    }

    public Image GetImage(string image)
    {
        var img = FindImageByName(image);
        return img ?? GetRandomImage();
    }

    private Image? FindImageByName(string image)
    {
        return Images
            .Where(i => Secrets.CurrentFolder.Contains(i.ParentFolder!.Name!))
            .FirstOrDefault(i => i.Name.ToLower().Contains(image.ToLower()));
    }

    public async Task<IList<Folder>> GetFoldersAsync()
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
        if (Images.Any(i => Secrets.CurrentFolder.Contains(i.ParentFolder!.Name!)))
        {
            Console.WriteLine(
                $"{DateTime.Now} | Фотки в папке {Secrets.CurrentFolder} уже есть; Всего: {Images.Count}");
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
            Images.AddRange(newImages);
        }


        var folders = await GetFoldersAsync();
        if (folders.Any())
        {
            Console.WriteLine($"Есть {folders.Count} папок в {Secrets.CurrentFolder}");
        }

        Console.WriteLine(
            $"{DateTime.Now} | {newImages.Count} фоток загружены из папки {Secrets.CurrentFolder}. Всего: {Images.Count}");
    }

    public void OpenImageInBrowser(string name)
    {
        var img = FindImageByName(name);
        if (img == null) return;
        var url = Secrets.OpenInBrowserUrl + img.Name;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public IList<Image> GetImagesByDate(DateTime date)
    {
        var images = Images.Where(i => i.DateTime.Date == date).ToList();
        return images;
    }
}