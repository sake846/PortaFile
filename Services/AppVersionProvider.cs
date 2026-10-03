using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace PortaFile.Services;

/// <summary>
/// Provides application version information embedded in the executable assembly.
/// </summary>
public static class AppVersionProvider
{
    private static string? _cachedVersion;

    /// <summary>
    /// Gets the current version of the application from the executable.
    /// </summary>
    /// <param name="fallback">Fallback string to return if version cannot be determined.</param>
    /// <returns>Version string (e.g., "1.0.3").</returns>
    public static string GetVersion(string fallback)
    {
        if (_cachedVersion != null)
        {
            return _cachedVersion;
        }

        try
        {
            var assembly = typeof(AppVersionProvider).Assembly;

            // 1. AssemblyInformationalVersion (e.g. 1.0.3 or 1.0.3+commit)
            var infoVerAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (!string.IsNullOrWhiteSpace(infoVerAttr?.InformationalVersion))
            {
                var ver = infoVerAttr.InformationalVersion.Split('+')[0].Trim();
                if (!string.IsNullOrEmpty(ver))
                {
                    _cachedVersion = ver;
                    return _cachedVersion;
                }
            }

            // 2. FileVersionInfo (ProductVersion / FileVersion)
            if (!string.IsNullOrEmpty(assembly.Location))
            {
                var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    var ver = fvi.ProductVersion.Split('+')[0].Trim();
                    if (!string.IsNullOrEmpty(ver))
                    {
                        _cachedVersion = ver;
                        return _cachedVersion;
                    }
                }
                if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                {
                    _cachedVersion = fvi.FileVersion.Trim();
                    return _cachedVersion;
                }
            }

            // 3. AssemblyName Version
            var asmVersion = assembly.GetName().Version;
            if (asmVersion != null)
            {
                _cachedVersion = $"{asmVersion.Major}.{asmVersion.Minor}.{Math.Max(0, asmVersion.Build)}";
                return _cachedVersion;
            }

            // 4. Fallback to version.txt if it exists (backward compatibility)
            string? dir = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(dir))
            {
                string file = Path.Combine(dir, "version.txt");
                if (File.Exists(file))
                {
                    string raw = File.ReadAllText(file).Trim();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        _cachedVersion = raw;
                        return _cachedVersion;
                    }
                }
            }
        }
        catch
        {
            // Ignore error and return fallback
        }

        return fallback;
    }
}
