using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PortaFile.Protocol;
using PortaFile.Services;

namespace PortaFile.Services;

public interface ISerialTransport : IDisposable
{
    bool IsOpen { get; }
    Stream Stream { get; }
    void Open(SerialSettings settings);
    void SetBaudRate(int baudRate);
    Task SendAsync(Packet packet, SerialSettings settings, CancellationToken cancellationToken);
    void Close();
}
