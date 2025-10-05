// Services/DocInvalidationService.cs
using System.Collections.Concurrent;

namespace PutDoc.Services;

public sealed class DocInvalidationService
{
    // docId -> version
    private readonly ConcurrentDictionary<Guid,int> _rev = new();

    // docId, actorSessionId, structural, newVersion
    public event Action<Guid,string,bool,int>? Invalidated;

    public int Get(Guid docId) => _rev.TryGetValue(docId, out var v) ? v : 0;

    public int Bump(Guid docId, string actorSessionId, bool structural)
    {
        var v = _rev.AddOrUpdate(docId, 1, static (_, old) => old + 1);
        Invalidated?.Invoke(docId, actorSessionId, structural, v);
        return v;
    }
}