using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GameOptimizer.Modules;

public static class ModuleManifestProvider
{
    private static readonly string ManifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modules.json");

    public static ModuleManifest Load()
    {
        if (!File.Exists(ManifestPath))
        {
            Debug.WriteLine($"[ModuleManifest] Manifest not found: {ManifestPath}");
            return new ModuleManifest();
        }

        try
        {
            var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(ManifestPath);
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(content))
            {
                Debug.WriteLine($"[ModuleManifest] Failed to read manifest: {error ?? "empty content"}");
                return new ModuleManifest();
            }
            
            var manifest = JsonSerializer.Deserialize<ModuleManifest>(content) ?? new ModuleManifest();
            Debug.WriteLine($"[ModuleManifest] Loaded {manifest.Modules.Count} modules from {ManifestPath}");
            return manifest;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModuleManifest] Failed to load manifest: {ex.Message}");
            return new ModuleManifest();
        }
    }
}
