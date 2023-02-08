namespace TgBot;

public class Image
{
    public string? Name { get; set; }
    public string? File { get; set; }
    public string? MimeType { get; set; }
    public long? Size { get; set; }
    public DateTime DateTime { get; set; }
    public ParentFolder? ParentFolder { get; set; }

    public override string ToString()
    {
        return Name!;
    }
}