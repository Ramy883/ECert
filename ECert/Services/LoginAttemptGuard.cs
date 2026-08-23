using System.Collections.Concurrent;

namespace ECert.Services;

/// <summary>
/// In-memory guard for failed administrative login attempts.
/// It is intentionally progressive rather than a permanent account lockout,
/// so an attacker cannot disable an administrator account by guessing it.
/// </summary>
public sealed class LoginAttemptGuard
{
    private sealed record AttemptState(int Failures, DateTimeOffset WindowStarted);

    private readonly ConcurrentDictionary<string, AttemptState> _states = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private const int DelayAfterFailures = 5;
    private const int MaxTrackedKeys = 10_000;

    public bool IsDelayed(string username, string clientAddress, out TimeSpan delay)
    {
        var state = GetCurrentState(BuildKey(username, clientAddress));
        if (state is null || state.Failures < DelayAfterFailures)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        var seconds = Math.Min(8, state.Failures - DelayAfterFailures + 1);
        delay = TimeSpan.FromSeconds(seconds);
        return true;
    }

    public bool IsAccountDelayed(string username, out TimeSpan delay) =>
        IsDelayedByKey(BuildKey(username, "account"), out delay);

    public void RegisterFailure(string username, string clientAddress)
    {
        var key = BuildKey(username, clientAddress);
        _states.AddOrUpdate(
            key,
            _ => new AttemptState(1, DateTimeOffset.UtcNow),
            (_, current) => IsExpired(current)
                ? new AttemptState(1, DateTimeOffset.UtcNow)
                : current with { Failures = current.Failures + 1 });

        TrimExpiredEntries();
    }

    public void RegisterAccountFailure(string username) => RegisterFailure(username, "account");

    public void RegisterSuccess(string username, string clientAddress)
    {
        _states.TryRemove(BuildKey(username, clientAddress), out _);
        _states.TryRemove(BuildKey(username, "account"), out _);
    }

    private bool IsDelayedByKey(string key, out TimeSpan delay)
    {
        var state = GetCurrentState(key);
        if (state is null || state.Failures < DelayAfterFailures)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        var seconds = Math.Min(8, state.Failures - DelayAfterFailures + 1);
        delay = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private AttemptState? GetCurrentState(string key)
    {
        if (!_states.TryGetValue(key, out var state))
            return null;

        if (IsExpired(state))
        {
            _states.TryRemove(key, out _);
            return null;
        }

        return state;
    }

    private static bool IsExpired(AttemptState state) =>
        DateTimeOffset.UtcNow - state.WindowStarted >= Window;

    private void TrimExpiredEntries()
    {
        if (_states.Count <= MaxTrackedKeys)
            return;

        foreach (var pair in _states)
        {
            if (IsExpired(pair.Value))
                _states.TryRemove(pair.Key, out _);
        }
    }

    private static string BuildKey(string username, string clientAddress) =>
        $"{username.Trim().ToUpperInvariant()}|{clientAddress}";
}
