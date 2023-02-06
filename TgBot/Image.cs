using Newtonsoft.Json;

namespace TgBot;

public class Image
{
    public string? Name { get; set; }
    public string? File { get; set; }
    public string? MimeType { get; set; }
    [JsonProperty("exif.date_time")]
    public DateOnly Date { get; set; }
}