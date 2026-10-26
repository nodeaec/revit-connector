using System;
using System.Collections.Generic;
using System.IO;
using NodeAec.Connector.Diagnostics;

namespace NodeAec.Connector.Plugins;

/// <summary>
/// Lists the Autodesk bundles installed on this computer and reads their Node.aec product
/// file (<see cref="PluginInventory.ProductFileName"/>) and <c>PackageContents.xml</c>.
/// Reads only; run it off the UI thread. Never throws: unreadable folders and files are
/// skipped and logged.
/// </summary>
internal static class InstalledBundleScanner
{
    /// <summary>Largest metadata file read; anything bigger is not a real bundle file.</summary>
    private const long MaxMetadataBytes = 256 * 1024;

    /// <summary>
    /// The folders Revit loads bundles from: per machine (<c>%ProgramData%</c>, where the
    /// Node.aec installers write) and per user (<c>%AppData%</c>).
    /// </summary>
    /// <returns>Absolute folder paths, which may not exist.</returns>
    internal static IReadOnlyList<string> DefaultRoots()
    {
        return new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "ApplicationPlugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins"),
        };
    }

    /// <summary>Scans <see cref="DefaultRoots"/>.</summary>
    /// <returns>Every bundle found, with whatever metadata could be read.</returns>
    internal static IReadOnlyList<InstalledBundle> Scan() => Scan(DefaultRoots());

    /// <summary>
    /// Scans the <c>*.bundle</c> folders directly under each root. A bundle with a valid
    /// product file takes its slug and version from it; otherwise the version comes from
    /// <c>PackageContents.xml</c> and the slug stays <c>null</c>.
    /// </summary>
    /// <param name="roots">Folders to scan.</param>
    /// <returns>Every bundle found.</returns>
    internal static IReadOnlyList<InstalledBundle> Scan(IEnumerable<string> roots)
    {
        var found = new List<InstalledBundle>();
        foreach (string root in roots)
        {
            string[] bundleDirs;
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                bundleDirs = Directory.GetDirectories(root, "*.bundle");
            }
            catch (Exception ex)
            {
                ConnectorLog.Write("WARN", $"Não foi possível listar {root}: {ex.GetType().Name}.");
                continue;
            }

            foreach (string dir in bundleDirs)
            {
                var bundle = ReadBundle(dir);
                if (bundle != null)
                {
                    found.Add(bundle);
                }
            }
        }

        return found;
    }

    private static InstalledBundle? ReadBundle(string dir)
    {
        try
        {
            var bundle = new InstalledBundle { BundlePath = dir };

            if (PluginInventory.TryParsePackageContents(ReadSmallFile(Path.Combine(dir, "PackageContents.xml")),
                    out string name, out string appVersion, out _))
            {
                bundle.DisplayName = name;
                bundle.Version = appVersion;
            }

            if (PluginInventory.TryParseProductFile(ReadSmallFile(Path.Combine(dir, PluginInventory.ProductFileName)),
                    out string slug, out string version))
            {
                bundle.ProductSlug = slug;
                bundle.Version = version;
            }

            return bundle;
        }
        catch (Exception ex)
        {
            ConnectorLog.Write("WARN", $"Não foi possível ler o bundle {Path.GetFileName(dir)}: {ex.GetType().Name}.");
            return null;
        }
    }

    private static string? ReadSmallFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxMetadataBytes)
        {
            return null;
        }

        return File.ReadAllText(path);
    }
}
