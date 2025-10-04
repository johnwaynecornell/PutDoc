// Services/DocVersionService.cs
using System.Collections.Concurrent;

namespace PutDoc.Services;
public sealed class DocVersionService
{
    private readonly ConcurrentDictionary<Guid,int> _v = new();
    public int Get(Guid docId) => _v.TryGetValue(docId, out var n) ? n : 0;
    public int Bump(Guid docId) => _v.AddOrUpdate(docId, 1, (_, old) => old + 1);
}