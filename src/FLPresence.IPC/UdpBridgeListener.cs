using System.Net;
using System.Net.Sockets;
using System.Text;
using FLPresence.Core;

namespace FLPresence.IPC;

/// <summary>
/// Localhost-only UDP listener receiving JSON datagrams from the FL Studio
/// bridge script. UDP was chosen over TCP/named pipes because a missing
/// reader simply drops packets — the bridge can never block FL Studio.
/// </summary>
public sealed class UdpBridgeListener : IDisposable
{
    private readonly int _port;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Fired for every well-formed message. Exceptions are swallowed.</summary>
    public event Action<BridgeMessage>? MessageReceived;

    public int Port => _port;
    public bool IsRunning => _loop is { IsCompleted: false };

    public UdpBridgeListener(int port = BridgeProtocol.DefaultPort) => _port = port;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, _port));
        _loop = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udp is not null)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var msg = BridgeProtocol.Parse(json);
                if (msg != null)
                    MessageReceived?.Invoke(msg);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { /* transient; keep listening */ }
            catch (ObjectDisposedException) { break; }
            catch (Exception) { /* never let the loop die on one bad packet */ }
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch (Exception) { }
        try { _udp?.Dispose(); } catch (Exception) { }
        _udp = null;
        _loop = null;
    }

    public void Dispose() => Stop();
}
