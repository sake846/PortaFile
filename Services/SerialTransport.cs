using System.IO.Ports;
using System.Diagnostics;
using PortaFile.Protocol;

namespace PortaFile.Services;

public sealed class SerialTransport : ISerialTransport, IDisposable
{
    private const int DefaultDataBits = 8;
    private const int HalfDuplexRtsEnableDelayMs = 2;
    private const int MinTransmitDelayMs = 5;
    private const int TransmitDelayBufferMs = 3;
    private const double BitsPerByteWithFraming = 10.0;

    private SerialPort? _serialPort;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsOpen => _serialPort?.IsOpen == true;
    public Stream Stream => _serialPort?.BaseStream ?? throw new InvalidOperationException("Serial port is not open.");

    public void Open(SerialSettings settings)
    {
        Close();

        _serialPort = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, DefaultDataBits, StopBits.One)
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
            var port = _serialPort ?? throw new InvalidOperationException("Serial port is not open.");

            var useRts = settings.DuplexMode == DuplexMode.HalfDuplex &&
                         settings.HalfDuplexControl == HalfDuplexControl.Rts;

            try
            {
                if (useRts)
                {
                    port.RtsEnable = true;
                    await Task.Delay(HalfDuplexRtsEnableDelayMs, cancellationToken);
                }

                var transmissionStarted = useRts ? Stopwatch.GetTimestamp() : 0;
                await port.BaseStream.WriteAsync(frame, cancellationToken);
                await port.BaseStream.FlushAsync(cancellationToken);

                if (useRts)
                {
                    // Flush usually waits for transmission; retain any remaining wire-time margin for buffered adapters.
                    var remaining = CalculateTransmitDelay(frame.Length, settings.BaudRate)
                        - Stopwatch.GetElapsedTime(transmissionStarted);
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, cancellationToken);
                    }
                }
            }
            finally
            {
                if (useRts)
                {
                    port.RtsEnable = false;
                }
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
        var seconds = byteCount * BitsPerByteWithFraming / Math.Max(1, baudRate);
        return TimeSpan.FromMilliseconds(Math.Max(MinTransmitDelayMs, seconds * 1000 + TransmitDelayBufferMs));
    }
}
