using DiscordRPC;
using FLPresence.Core;
using FileLogger = FLPresence.Core.FileLogger;

namespace FLPresence.Discord;

/// <summary>
/// Discord Rich Presence over Discord's local IPC protocol (named pipe),
/// via the maintained community wrapper (DiscordRichPresence / Lachee).
/// No tokens, no bot — only the local Discord client pipe.
/// </summary>
public sealed class DiscordPresenceService : IDisposable
{
    private DiscordRpcClient? _client;
    private readonly FileLogger _log;
    private readonly object _lock = new();
    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private bool _needsConnect;

    public bool RpcConnected { get; private set; }
    public string? ClientId { get; private set; }
    public string LastError { get; private set; } = "";
    public DateTime? LastUpdateUtc { get; private set; }
    public string LastUpdateSummary { get; private set; } = "";
    public const string LargeAssetKey = "flpresence";

    public event Action<bool>? ConnectionChanged;

    public DiscordPresenceService(FileLogger log) => _log = log;

    /// <summary>Point the service at an Application ID. Idempotent; reconnects
    /// only when the ID changes. Safe while Discord is not running yet.</summary>
    public void Connect(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return;
        lock (_lock)
        {
            if (ClientId == clientId && !_needsConnect && _client is { IsInitialized: true })
                return;
            if (ClientId != clientId)
            {
                DeinitializeNoLock();
                ClientId = clientId;
            }
            _needsConnect = true;
        }
        EnsureConnected();
    }

    /// <summary>Pump/retry loop entry. Called periodically by the companion;
    /// retries at most every 5 s while disconnected, so a Discord started
    /// later is picked up automatically.</summary>
    public void EnsureConnected()
    {
        lock (_lock)
        {
            if (ClientId is null || RpcConnected) return;
            if (DateTime.UtcNow - _lastAttemptUtc < TimeSpan.FromSeconds(5)) return;
            _lastAttemptUtc = DateTime.UtcNow;
            try
            {
                if (_needsConnect || _client is not { IsInitialized: true })
                {
                    DeinitializeNoLock();
                    _client = NewClient(ClientId);
                    _client.Initialize();
                    _needsConnect = false;
                    _log.Info("Discord RPC connecting…");
                }
                // The client was created with autoEvents: true, so its
                // background event loop owns IPC reads and callbacks. Calling
                // Invoke() here throws when automatic events are enabled and
                // repeatedly prevents stable READY handling.
            }
            catch (Exception ex)
            {
                _log.Debug($"Discord connect attempt failed: {ex.Message}");
            }
        }
    }

    /// <summary>False when not sent (Discord not ready) — caller must retry.</summary>
    public bool Update(PresencePayload payload)
    {
        lock (_lock)
        {
            var client = _client;
            if (client is null || !client.IsInitialized || !RpcConnected) return false;
            try
            {
                var rpc = new RichPresence
                {
                    Details = Truncate(payload.Details, 128),
                    State = Truncate(payload.State, 128),
                    Assets = new Assets
                    {
                        LargeImageKey = LargeAssetKey,
                        LargeImageText = Truncate(
                            string.IsNullOrEmpty(payload.SmallImageText) ? "FLPresence" : payload.SmallImageText, 128),
                        SmallImageKey = payload.SmallImageKey,
                        SmallImageText = Truncate(payload.SmallImageText, 128),
                    },
                };
                if (payload.StartUtc is not null)
                    rpc.Timestamps = new Timestamps { Start = DateTime.SpecifyKind(payload.StartUtc.Value, DateTimeKind.Utc) }; // DiscordRPC ignores Kind: local time = +offset = timer stuck at 0:00
                client.SetPresence(rpc);
                LastUpdateUtc = DateTime.UtcNow;
                LastUpdateSummary = $"[{payload.Mode}] '{payload.Details}' | '{payload.State}' " +
                                    $"[large={LargeAssetKey} small={payload.SmallImageKey}]" +
                                    (payload.StartUtc is null ? "" : " [timer]");
                _log.Debug($"Discord SetActivity: {LastUpdateSummary} " +
                           $"(app {ClientId})");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("SetPresence failed", ex);
                LastError = ex.Message;
                return false;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            try { if (_client is { IsInitialized: true }) _client.ClearPresence(); }
            catch (Exception) { }
        }
    }

    public void Deinitialize()
    {
        lock (_lock)
        {
            DeinitializeNoLock();
            ClientId = null;
        }
    }

    private void DeinitializeNoLock()
    {
        try { if (_client is { IsInitialized: true }) { _client.ClearPresence(); _client.Deinitialize(); } }
        catch (Exception) { }
        try { _client?.Dispose(); } catch (Exception) { }
        _client = null;
        RpcConnected = false;
    }

    private DiscordRpcClient NewClient(string clientId)
    {
        var c = new DiscordRpcClient(clientId, autoEvents: true)
        {
            Logger = new LogShim(_log),
        };
        c.OnReady += (_, e) =>
        {
            RpcConnected = true;
            LastError = "";
            _log.Info($"Discord RPC ready (user {e.User.Username})");
            _log.Info($"RPC READY: true; Discord Application ID: {ClientId}; " +
                      $"large asset key: '{LargeAssetKey}'");
            ConnectionChanged?.Invoke(true);
        };
        c.OnConnectionFailed += (_, e) =>
        {
            RpcConnected = false;
            _needsConnect = true; // recreate on next retry
            LastError = $"Discord pipe connect failed (pipe {e.FailedPipe})";
            _log.Info(LastError);
            ConnectionChanged?.Invoke(false);
        };
        c.OnClose += (_, e) =>
        {
            RpcConnected = false;
            _needsConnect = true;
            LastError = $"Discord connection closed ({e.Code}: {e.Reason})";
            _log.Info(LastError);
            ConnectionChanged?.Invoke(false);
        };
        c.OnError += (_, e) =>
        {
            LastError = $"Discord RPC error: {e.Message}";
            _log.Warn(LastError);
        };
        return c;
    }

    private static string Truncate(string s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    public void Dispose() => Deinitialize();

    /// <summary>Routes the RPC library's internal logs into our file logger.</summary>
    private sealed class LogShim : DiscordRPC.Logging.ILogger
    {
        private readonly FileLogger _log;
        public DiscordRPC.Logging.LogLevel Level { get; set; } = DiscordRPC.Logging.LogLevel.Warning;
        public LogShim(FileLogger log) => _log = log;
        public void Trace(string message, params object[] args) => _log.Debug(Format(message, args));
        public void Info(string message, params object[] args) => _log.Info(Format(message, args));
        public void Warning(string message, params object[] args) => _log.Warn(Format(message, args));
        public void Error(string message, params object[] args) => _log.Error(Format(message, args));
        private static string Format(string message, object[] args)
        {
            try { return args.Length == 0 ? message : string.Format(message, args); }
            catch (Exception) { return message; }
        }
    }
}
