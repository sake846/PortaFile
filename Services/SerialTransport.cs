using System.IO.Ports;
using PortaFile.Protocol;

namespace PortaFile.Services;

public sealed class SerialTransport : IDisposable
{
    private SerialPort? _serialPort;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsOpen => _serialPort?.IsOpen == true;
    public Stream Stream => _serialPort?.BaseStream ?? throw new InvalidOperationException("Serial port is not open.");

    public void Open(SerialSettings settings)
    {
        Close();

        _serialPort = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = -1,
            WriteTimeout = -1,
            DtrEnable = true,
            RtsEnable = false
        };

        _serialPort.Open();
    }

    public async Task SendAsync(Packet packet, SerialSettings settings, CancellationToken cancellationToken)
    {
        var frame = PacketCodec.Encode(packet);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_serialPort is null)
            {
                throw new InvalidOperationException("Serial port is not open.");
            }

            var useRts = settings.DuplexMode == DuplexMode.HalfDuplex &&
                         settings.HalfDuplexControl == HalfDuplexControl.Rts;

            if (useRts)
            {
                _serialPort.RtsEnable = true;
                await Task.Delay(2, cancellationToken);
            }

            await _serialPort.BaseStream.WriteAsync(frame, cancellationToken);
            await _serialPort.BaseStream.FlushAsync(cancellationToken);

            if (useRts)
            {
                await Task.Delay(CalculateTransmitDelay(frame.Length, settings.BaudRate), cancellationToken);
                _serialPort.RtsEnable = false;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Close()
    {
        if (_serialPort is null)
        {
            return;
        }

        try
        {
            _serialPort.Close();
        }
        finally
        {
            _serialPort.Dispose();
            _serialPort = null;
        }
    }

    public void Dispose()
    {
        Close();
        _writeLock.Dispose();
    }

    private static TimeSpan CalculateTransmitDelay(int byteCount, int baudRate)
    {
        var seconds = byteCount * 10.0 / Math.Max(1, baudRate);
        return TimeSpan.FromMilliseconds(Math.Max(5, seconds * 1000 + 3));
    }
}
