using Newtonsoft.Json;

namespace TgBot;

public class Image
{
    public string? Name { get; set; }
    public string? File { get; set; }
    [JsonProperty("mime_type")]
    public string? MimeType { get; set; }
}