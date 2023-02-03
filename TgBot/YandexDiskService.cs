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

    public async Task<Image> GetRandomImage()
    {
        await PreloadAllPhotosAsync();

        var randomImage = Images[new Random().Next(Images.Count)];

        Console.WriteLine(randomImage.Name);

        return randomImage;
    }

    private async Task PreloadAllPhotosAsync()
    {
        if (Images.Any()) return;

        var response = await _client.GetAsync($"{Secrets.GeneralRequest}");

        var jsonString = await response.Content.ReadAsStringAsync();

        Images = JsonConvert.DeserializeObject<List<Image>>(jsonString[22..^2]);
    }
}