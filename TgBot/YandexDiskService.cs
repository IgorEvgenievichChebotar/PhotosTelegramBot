using System.Diagnostics;
using Newtonsoft.Json;

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
    IList<Image> GetLikes(long chatId);
    bool AddToLikes(long chatId, Image img);
}

public class YandexDiskService : IYandexDiskService
{
    private readonly List<Image> Images;
    private readonly Dictionary<long, List<Image>> Likes;
    private readonly HttpClient _client;

    public YandexDiskService()
    {
        Likes = new Dictionary<long, List<Image>>(); //todo from memory
        Images = new List<Image>();
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Add(
            "Authorization",
            $"OAuth {Secrets.OAuthYandexDisk}"
        );
    }

    public async Task<MemoryStream> GetThumbnailImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _client.GetAsync(img.Preview, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}: {response.StatusCode} {response.ReasonPhrase}");

        return new MemoryStream(content);
    }

    public async Task<MemoryStream> GetOriginalImageAsync(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _client.GetAsync(img.File, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}");

        return new MemoryStream(content);
    }

    public async Task<IList<MemoryStream>> GetThumbnailImagesAsync(IEnumerable<Image> images)
    {
        var streams = new List<MemoryStream>();
        await Parallel.ForEachAsync(images, async (i, _) => { streams.Add(await GetThumbnailImageAsync(i)); });
        return streams;
    }

    public async Task<IList<MemoryStream>> GetOriginalImagesAsync(IEnumerable<Image> images)
    {
        var streams = new List<MemoryStream>();
        await Parallel.ForEachAsync(images, async (i, _) => { streams.Add(await GetOriginalImageAsync(i)); });
        return streams;
    }

    public IList<Image> GetLikes(long chatId)
    {
        return Likes.ContainsKey(chatId) ? Likes[chatId] : new List<Image>();
    }

    public bool AddToLikes(long chatId, Image img)
    {
        if (Likes.ContainsKey(chatId))
        {
            if (Likes[chatId].Contains(img))
            {
                return false;
            }

            Likes[chatId].Add(img);
            return true;
        }

        Likes.Add(chatId, new List<Image> { img });
        return true;
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
        var response = await _client.GetAsync(Secrets.FoldersRequest);
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

        var response = await _client.GetAsync($"{Secrets.GetAllImagesRequest}");

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