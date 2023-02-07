namespace TgBot;

public class Dir
{
    private string? _name;

    public string? Name
    {
        get => _name?.Replace(" ", "_");
        set => _name = value?.Replace("_", " ");
    }

    public string? Type { get; set; }

    public override string? ToString()
    {
        return _name;
    }
}