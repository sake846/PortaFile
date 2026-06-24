using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortaFile.Services;

public sealed class ApplicationLastState
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string? PortName { get; set; }
    public int BaudRate { get; set; } = 115200;
    public Parity Parity { get; set; } = Parity.None;
    public DuplexMode DuplexMode { get; set; } = DuplexMode.HalfDuplex;
    public HalfDuplexControl HalfDuplexControl { get; set; } = HalfDuplexControl.DriverManaged;
    public TransferReliabilityMode ReliabilityMode { get; set; } = TransferReliabilityMode.Arq;
    public string? SendFileDirectory { get; set; }

    public static string FilePath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "last-state.json");

    public static ApplicationLastState Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new ApplicationLastState();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<ApplicationLastState>(json, JsonOptions) ?? new ApplicationLastState();
        }
        catch
        {
            return new ApplicationLastState();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
