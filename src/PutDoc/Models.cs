public record DocMeta
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Untitled";
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; } = 0; // increments on each save
}

public record PutDocFile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "PutDoc";
    public Guid RootCollectionId { get; set; }
    public Dictionary<Guid, Collection> Collections { get; set; } = new();
    public Dictionary<Guid, Page> Pages { get; set; } = new();
}

public record Collection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Collection";
    public List<Guid> ChildCollectionIds { get; set; } = new();
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
