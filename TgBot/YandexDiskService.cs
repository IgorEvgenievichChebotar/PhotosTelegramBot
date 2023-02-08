using System.Diagnostics;
using Newtonsoft.Json;

namespace TgBot;

public interface IYandexDiskService
{
    Image GetRandomImage();
    Image GetRandomImage(DateTime date);
    Image GetImage(string image);
    void OpenImageInBrowser(string name);
    ICollection<Image> GetImagesByDate(DateTime date);
    Task<IEnumerable<ParentFolder>> GetFolders();
    Task LoadImagesAsync();
    Task<MemoryStream> GetThumbnailImage(Image img);
}

public class YandexDiskService : IYandexDiskService
{
    private readonly List<Image> Images;
    private readonly HttpClient _client;

    public YandexDiskService()
    {
        Images = new List<Image>();
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Add(
            "Authorization",
            $"OAuth {Secrets.OAuthYandexDisk}"
        );
    }

    public async Task<MemoryStream> GetThumbnailImage(Image img)
    {
        var sw = Stopwatch.StartNew();
        var response = await _client.GetAsync(img.Preview, HttpCompletionOption.ResponseHeadersRead);
        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode
            ? $"{DateTime.Now} | Photo {img.Name} downloaded successfully in {sw.ElapsedMilliseconds}ms"
            : $"{DateTime.Now} | Error downloading {img.Name}");

        return new MemoryStream(content);
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
        return Images
                   .FindAll(i => i.DateTime.Date == date)
                   .MinBy(_ => Guid.NewGuid()) ??
               GetRandomImage();
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
            .FirstOrDefault(i => i.Name!.ToLower().Contains(image.ToLower()));
    }

    public async Task<IEnumerable<ParentFolder>> GetFolders()
    {
        var response = await _client.GetAsync(Secrets.GetFoldersRequest);
        var jsonString = await response.Content.ReadAsStringAsync();
        var folders = JsonConvert.DeserializeObject<ICollection<ParentFolder>>(jsonString[22..^2])
            .Where(pf => pf.Type == "dir")
            .ToList();
        return folders;
    }

    public async Task LoadImagesAsync()
    {
        if (Images.Any(i => Secrets.CurrentFolder.Contains(i.ParentFolder!.Name!)))
        {
            Console.WriteLine($"{DateTime.Now} | Фотки в папке {Secrets.CurrentFolder} уже есть; Всего: {Images.Count}");
            return;
        }

        var response = await _client.GetAsync($"{Secrets.GetAllImagesRequest}");

        var jsonString = await response.Content.ReadAsStringAsync();

        Images.AddRange(JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2],
                new ImageExifConverter())
            .Where(i => i.Name!.Contains(".jpg"))
            .Where(i => i.MimeType!.Contains("image/jpeg"))
            .ToList());

        Console.WriteLine($"{DateTime.Now} | фотки загружены из папки {Secrets.CurrentFolder}. Всего: {Images.Count}");
    }

    public void OpenImageInBrowser(string name)
    {
        var img = FindImageByName(name);
        if (img == null) return;
        var url = Secrets.OpenInBrowserUrl + img.Name;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public ICollection<Image> GetImagesByDate(DateTime date)
    {
        var images = Images.Where(i => i.DateTime.Date == date).ToList();
        return images;
    }
}