using System.Text.Json.Serialization;

public record PutDocFile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "PutDoc";
    public Guid RootLeafId { get; set; }
    public Dictionary<Guid, Leaf> Leafs { get; set; } = new();
    public Dictionary<Guid, Page> Pages { get; set; } = new();
}

public record Leaf
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Leaf";
    public List<Guid> ChildLeafIds { get; set; } = new();
    public List<Guid> PageIds { get; set; } = new();
}

public record Page
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Page";
    public List<Snippet> Snippets { get; set; } = new();
}

public record Snippet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Html { get; set; } = "<p>New snippet</p>";
}
