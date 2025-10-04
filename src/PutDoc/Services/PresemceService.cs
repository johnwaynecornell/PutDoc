using System.Collections.Concurrent;

namespace PutDoc.Services;

public enum LockResult { Granted, AlreadyHeldByYou, Denied, Stolen }

public record LockKey(Guid DocId, string TargetKey)  // e.g. "snippet:{id}" or "frag:{snippetId}:{puid}:{scope}"
{
    public override string ToString() => $"{DocId:N}:{TargetKey}";
}

public record LockInfo(string OwnerUserId, string OwnerClientId, DateTimeOffset ExpiresAt);

public sealed class PresenceService
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<LockKey, LockInfo> _locks = new();

    private static bool SameOwner(LockInfo i, string userId, string clientId) =>
        i.OwnerUserId == userId && i.OwnerClientId == clientId;

    public (LockResult Result, LockInfo? Previous) TryAcquire(LockKey key, string userId, string clientId, bool @override)
    {
        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (_locks.TryGetValue(key, out var cur))
            {
                var expired = cur.ExpiresAt <= now;

                if (expired || @override || SameOwner(cur, userId, clientId))
                {
                    var newInfo = new LockInfo(userId, clientId, now + _ttl);
                    if (_locks.TryUpdate(key, newInfo, cur))
                    {
                        if (@override && !expired && !SameOwner(cur, userId, clientId))
                            return (LockResult.Stolen, cur);

                        return SameOwner(cur, userId, clientId)
                            ? (LockResult.AlreadyHeldByYou, cur)
                            : (LockResult.Granted, cur);
                    }
                    continue; // CAS retry
                }

                return (LockResult.Denied, cur);
            }
            else
            {
                var info = new LockInfo(userId, clientId, now + _ttl);
                if (_locks.TryAdd(key, info)) return (LockResult.Granted, null);
                // else retry (race)
            }
        }
    }

    public void Heartbeat(LockKey key, string userId, string clientId)
    {
        if (_locks.TryGetValue(key, out var cur) && cur.OwnerUserId == userId && cur.OwnerClientId == clientId)
            _locks[key] = cur with { ExpiresAt = DateTimeOffset.UtcNow + _ttl };
    }

    public void Release(LockKey key, string userId, string clientId)
    {
        if (_locks.TryGetValue(key, out var cur) && SameOwner(cur, userId, clientId))
            _locks.TryRemove(key, out _);
    }

    public (bool Exists, LockInfo? Holder) Get(LockKey key)
    {
        if (_locks.TryGetValue(key, out var cur))
            return (true, cur);
        return (false, null);
    }
}
