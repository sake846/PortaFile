using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortaFile.Services
{
    public sealed class LastStateService : ILastStateService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly string FilePath = Path.Combine(
            AppContext.BaseDirectory,
            "last-state.json");

        public ApplicationLastState Load()
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

        public void Save(ApplicationLastState state)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                File.WriteAllText(FilePath, JsonSerializer.Serialize(state, JsonOptions));
            }
            catch
            {
                // Ignore
            }
        }
    }
}
