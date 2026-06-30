namespace PortaFile.Transfer.Protocol;

using PortaFile.Protocol;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface IPacketHandler
{
    Task HandleAsync(Packet packet, CancellationToken cancellationToken);
}
