namespace TgBot;

public class ParentFolder
{
    public string? Name { get; set; }

    public string? Type { get; set; }

    public ParentFolder(string? path)
    {
        if (path != null)
        {
            Name = path[..path.LastIndexOf('/')].Split('/').Last();
        }

        Type = "dir";
    }

    public override string? ToString()
    {
        return Name;
    }
}