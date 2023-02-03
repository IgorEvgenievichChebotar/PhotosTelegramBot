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
        var randomImage = Images[new Random().Next(Images.Count)];

        Console.WriteLine(randomImage.Name);

        return randomImage;
    }

    public Image? GetConcreteImage(string image)
    {
        return Images.FirstOrDefault(i => i.Name!.ToLower().Contains(image.ToLower()));
    }

    public async Task PreloadAllPhotosAsync()
    {
        if (Images.Any()) return;

        var response = await _client.GetAsync($"{Secrets.GeneralRequest}");

        var jsonString = await response.Content.ReadAsStringAsync();

        Images = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2])
            .Where(i => i.Name!.Contains(".jpg"))
            .ToList();
    }
}