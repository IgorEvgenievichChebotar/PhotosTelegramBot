using System.Diagnostics;
using Newtonsoft.Json;

namespace TgBot;

public class YandexDiskService
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

    public Image GetImage(string image)
    {
        var img = FindImageByName(image);
        return img ?? GetRandomImage();
    }

    private Image? FindImageByName(string image)
    {
        return Images.FirstOrDefault(i => i.Name!.ToLower().Contains(image.ToLower()));
    }

    public async Task PreloadImagesAsync()
    {
        if (Images.Any()) return;

        var response = await _client.GetAsync($"{Secrets.GeneralRequest}");

        var jsonString = await response.Content.ReadAsStringAsync();

        Images = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2])
            .Where(i => i.Name!.Contains(".jpg"))
            .Where(i => i.MimeType!.Contains("image/jpeg"))
            .ToList();

        Console.WriteLine(Images.Count + " фоток загружено.");
    }

    public void OpenImageInBrowser(string name)
    {
        var img = FindImageByName(name);
        if (img == null) return;
        var url = Secrets.OpenInBrowserUrl + img.Name;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}