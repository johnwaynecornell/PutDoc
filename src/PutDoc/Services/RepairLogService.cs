// Services/RepairLogService.cs
using System.Collections.Concurrent;
using PutDoc.Services;

public sealed class RepairLogService
{
    private readonly ConcurrentDictionary<Guid, PutDocState.PutDocNormalize.RepairSummary> _byDoc = new();

    public void Record(Guid docId, PutDocState.PutDocNormalize.RepairSummary summary)
    {
        if (summary is null || !summary.Any) return;
        _byDoc[docId] = summary;
    }

    public PutDocState.PutDocNormalize.RepairSummary? Consume(Guid docId)
        => _byDoc.TryRemove(docId, out var s) ? s : null;

    public PutDocState.PutDocNormalize.RepairSummary? Peek(Guid docId)
        => _byDoc.TryGetValue(docId, out var s) ? s : null;
}