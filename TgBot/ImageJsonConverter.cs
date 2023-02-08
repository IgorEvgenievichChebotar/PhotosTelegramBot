using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TgBot;

public class ImageExifConverter : JsonConverter<Image>
{
    public override Image ReadJson(
        JsonReader reader,
        Type objectType,
        Image existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        var img = new Image();
        try
        {
            img = new Image
            {
                Name = (string)jo["name"],
                File = (string)jo["file"],
                MimeType = (string)jo["mime_type"],
                Size = (long)jo["size"],
                ParentFolder = new ParentFolder((string)jo["path"]),
                Preview = (string)jo["preview"]
            };
            try
            {
                img.DateTime = DateTime.Parse(jo["exif"]["date_time"].ToString());
            }
            catch (Exception)
            {
                img.DateTime = DateTime.Now;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Ошибка преобразования файла {(string)jo["name"]}");
        }
        return img;
    }

    public override void WriteJson(JsonWriter writer, Image value, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}