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
    Task<IEnumerable<Dir>> GetDirs();
    Task LoadImagesAsync();
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

    public Image GetRandomImage()
    {
        var img = Images[new Random().Next(Images.Count)];
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
        return Images.FirstOrDefault(i => i.Name!.ToLower().Contains(image.ToLower()));
    }

    public async Task<IEnumerable<Dir>> GetDirs()
    {
        var response = await _client.GetAsync(Secrets.GetDirsRequest);
        var jsonString = await response.Content.ReadAsStringAsync();
        var dirNames = JsonConvert.DeserializeObject<ICollection<Dir>>(jsonString[22..^2])
            .Where(d => d.Type == "dir")
            .ToList();
        return dirNames;
    }

    public async Task LoadImagesAsync()
    {
        var response = await _client.GetAsync($"{Secrets.GetAllImagesRequest}");

        var jsonString = await response.Content.ReadAsStringAsync();

        Images = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2], new ImageExifConverter())
            .Where(i => i.Name!.Contains(".jpg"))
            .Where(i => i.MimeType!.Contains("image/jpeg"))
            .ToList();

        Console.WriteLine(Images.Count + $" фоток загружено из папки {Secrets.PathToDir}.");
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