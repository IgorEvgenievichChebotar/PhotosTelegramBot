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
    Task<FileStream> GetThumbnailImage(Image img);
}

public class YandexDiskService : IYandexDiskService
{
    private List<Image> Images;
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

    public async Task<FileStream> GetThumbnailImage(Image img)
    {
        var startNew = Stopwatch.StartNew();
        Console.WriteLine(startNew.ElapsedMilliseconds + "мс - начало");

        var response = await _client.GetAsync(img.Preview, HttpCompletionOption.ResponseHeadersRead);

        var content = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine(response.IsSuccessStatusCode ? $"Photo {img.Name} downloaded successfully." : "Ошибка");

        using var memoryStream = new MemoryStream(content);
        var fileStream = new FileStream(img.Name!, FileMode.Create, FileAccess.Write);
        memoryStream.WriteTo(fileStream);
        await fileStream.DisposeAsync();
        
        Console.WriteLine(startNew.ElapsedMilliseconds + $"мс на получение фотки {img.Name}");

        return new FileStream(img.Name!, FileMode.Open, FileAccess.Read);
    }

    public Image GetRandomImage()
    {
        var img = Images
            .Where(i => Secrets.ParentFolder.Contains(i.ParentFolder!.Name!))
            .OrderBy(i => Guid.NewGuid())
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
            .Where(i => Secrets.ParentFolder.Contains(i.ParentFolder!.Name!))
            .FirstOrDefault(i => i.Name!.ToLower().Contains(image.ToLower()));
    }

    public async Task<IEnumerable<ParentFolder>> GetFolders()
    {
        var response = await _client.GetAsync(Secrets.GetDirsRequest);
        var jsonString = await response.Content.ReadAsStringAsync();
        var dirNames = JsonConvert.DeserializeObject<ICollection<ParentFolder>>(jsonString[22..^2])
            .Where(pf => pf.Type == "dir")
            .ToList();
        return dirNames;
    }

    public async Task LoadImagesAsync()
    {
        if (Images.Any(i => Secrets.ParentFolder.Contains(i.ParentFolder!.Name!)))
        {
            Console.WriteLine($"фотки уже есть в папке {Secrets.ParentFolder}. Всего: {Images.Count}");
            return;
        }

        var response = await _client.GetAsync($"{Secrets.GetAllImagesRequest}");

        var jsonString = await response.Content.ReadAsStringAsync();

        Images.AddRange(JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2],
                new ImageExifConverter())
            .Where(i => i.Name!.Contains(".jpg"))
            .Where(i => i.MimeType!.Contains("image/jpeg"))
            .ToList());

        Console.WriteLine($"фотки загружены из папки {Secrets.ParentFolder}. Всего: {Images.Count}");
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