// Services/DocWriterService.cs
using System.Collections.Concurrent;

namespace PutDoc.Services;

public enum WriterResult { Granted, AlreadyYou, Denied, Stolen }

public record WriterInfo(string UserId, string SessionId, DateTimeOffset ExpiresAt);

public sealed class DocWriterService
{
    public event Action<Guid,WriterInfo?>? WriterChanged; // docId, newWriter (null if released)
    private readonly ConcurrentDictionary<Guid,WriterInfo> _owners = new();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(45);

    public (WriterResult Result, WriterInfo? Prev) TryBecomeWriter(Guid docId, string userId, string sessionId, bool force)
    {
        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (_owners.TryGetValue(docId, out var cur))
            {
                var expired = cur.ExpiresAt <= now;

                if (expired || force || cur.SessionId == sessionId)
                {
                    var next = new WriterInfo(userId, sessionId, now + _ttl);
                    if (_owners.TryUpdate(docId, next, cur))
                    {
                        WriterChanged?.Invoke(docId, next);
                        if (force && !expired && cur.SessionId != sessionId) return (WriterResult.Stolen, cur);
                        if (cur.SessionId == sessionId) return (WriterResult.AlreadyYou, cur);
                        return (WriterResult.Granted, cur);
                    }
                    continue;
                }
                return (WriterResult.Denied, cur);
            }
            else
            {
                var next = new WriterInfo(userId, sessionId, now + _ttl);
                if (_owners.TryAdd(docId, next))
                {
                    WriterChanged?.Invoke(docId, next);
                    return (WriterResult.Granted, null);
                }
            }
        }
    }

    public void Heartbeat(Guid docId, string sessionId)
    {
        if (_owners.TryGetValue(docId, out var cur) && cur.SessionId == sessionId)
        {
            _owners[docId] = cur with { ExpiresAt = DateTimeOffset.UtcNow + _ttl };
        }
    }

    public void Release(Guid docId, string sessionId)
    {
        if (_owners.TryGetValue(docId, out var cur) && cur.SessionId == sessionId)
        {
            _owners.TryRemove(docId, out _);
            WriterChanged?.Invoke(docId, null);
        }
    }

    public WriterInfo? Get(Guid docId) => _owners.TryGetValue(docId, out var cur) ? cur : null;
}
