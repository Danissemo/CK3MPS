using System;
using System.Threading;
using System.Threading.Tasks;

namespace CK3MPS
{
    /// <summary>
    /// Integration boundary for Parity Room transports.
    /// Online relay is the preferred transport; LAN remains a legacy option.
    /// The existing parity payload validation remains unchanged after transport delivery.
    /// </summary>
    internal interface IParityRoomTransport
    {
        string Name { get; }
        Task ConnectAsync(CancellationToken cancellationToken);
        Task SendAsync(string payload, CancellationToken cancellationToken);
        Task<string[]> ReceiveAsync(CancellationToken cancellationToken);
        Task DisconnectAsync();
    }

    internal enum ParityRoomTransportMode
    {
        OnlineRelay,
        LanLegacy
    }

    internal static class ParityRoomTransportPolicy
    {
        public static ParityRoomTransportMode DefaultMode
        {
            get { return ParityRoomTransportMode.OnlineRelay; }
        }

        public static bool IsLegacy(ParityRoomTransportMode mode)
        {
            return mode == ParityRoomTransportMode.LanLegacy;
        }
    }
}
