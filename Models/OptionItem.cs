namespace PortaFile.Models;

public sealed record OptionItem<T>(string DisplayName, T Value);
