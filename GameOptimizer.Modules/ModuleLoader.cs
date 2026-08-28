// File: GameOptimizer.Modules/ModuleLoader.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq; 
using System.Reflection;
using GameOptimizer.Core.Extensions;
using GameOptimizer.Core.Modules;
using GameOptimizer.ModuleContracts.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Modules;

public sealed class ModuleLoader
{
    public static void RegisterConfiguredModules(IServiceCollection services)
    {
        var manifest = ModuleManifestProvider.Load();
        foreach (var descriptor in manifest.Modules.Where(m => m.Enabled))
        {
            RegisterModule(services, descriptor);
        }
    }

    private static void RegisterModule(IServiceCollection services, ModuleDescriptor descriptor)
    {
        if (descriptor.AssemblyPath is null)
        {
            Debug.WriteLine($"[ModuleLoader] Skipping module: AssemblyPath is null");
            return;
        }

        try
        {
            // P1-027: 支持相对路径，基于应用程序目录解析
            var assemblyPath = descriptor.AssemblyPath;
            if (!Path.IsPathRooted(assemblyPath))
            {
                assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assemblyPath);
            }
            
            if (!File.Exists(assemblyPath))
            {
                Debug.WriteLine($"[ModuleLoader] Assembly not found: {assemblyPath}");
                return;
            }
            
            var assembly = Assembly.LoadFrom(assemblyPath);
            var moduleType = string.IsNullOrWhiteSpace(descriptor.TypeName)
                ? assembly.GetTypes().FirstOrDefault(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract)
                : assembly.GetType(descriptor.TypeName);
            
            if (moduleType is null)
            {
                Debug.WriteLine($"[ModuleLoader] Module type not found in {assemblyPath}, TypeName={descriptor.TypeName}");
                return;
            }

            var method = typeof(ServiceCollectionExtensions)
                .GetMethod(nameof(ServiceCollectionExtensions.AddModule))
                ?.MakeGenericMethod(moduleType);
            method?.Invoke(null, new object[] { services });
            
            Debug.WriteLine($"[ModuleLoader] Registered module: {moduleType.Name}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModuleLoader] Failed to load module from {descriptor.AssemblyPath}: {ex.Message}");
        }
    }
}
