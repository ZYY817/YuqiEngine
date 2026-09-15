// File: GameOptimizer.Core/Diagnostics/SolutionProvider.cs
using System.Collections.Generic;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Diagnostics;

public class SolutionProvider
{
    private static LocalizationService Loc => LocalizationService.Instance;

    public List<SolutionStep> GetSolutions(ErrorCode errorCode, DiagnosticContext context)
    {
        try
        {
            return errorCode.Category switch
            {
                ErrorCategory.Permission => GetPermissionSolutions(errorCode, context),
                ErrorCategory.FileSystem => GetFileSystemSolutions(errorCode, context),
                ErrorCategory.Service => GetServiceSolutions(errorCode, context),
                ErrorCategory.Power => GetPowerSolutions(errorCode, context),
                ErrorCategory.Network => GetNetworkSolutions(errorCode, context),
                ErrorCategory.Security => GetSecuritySolutions(errorCode, context),
                ErrorCategory.System => GetSystemSolutions(errorCode, context),
                ErrorCategory.Configuration => GetConfigurationSolutions(errorCode, context),
                ErrorCategory.Hardware => GetHardwareSolutions(errorCode, context),
                _ => GetGenericSolutions(errorCode, context)
            };
        }
        catch
        {
            return new List<SolutionStep>();
        }
    }

    private List<SolutionStep> GetPermissionSolutions(ErrorCode code, DiagnosticContext context)
    {
        var solutions = new List<SolutionStep>();

        if (!context.IsAdminMode)
        {
            solutions.Add(new SolutionStep
            {
                Order = 1,
                Description = Loc.Get("Solution.RunAsAdmin"),
                RequiresAdmin = true
            });
            return solutions;
        }

        switch (code.Number)
        {
            case 1001:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CheckSecuritySoftwareRegistry") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.CheckGroupPolicyRegistry"), RequiresAdmin = true },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.CheckRegistryPermissions"), RequiresAdmin = true }
                });
                break;

            case 1002:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CloseFileOccupyingPrograms") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.CheckFileReadOnly") },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.UseResourceMonitor"), Command = "resmon.exe" }
                });
                break;

            case 1003:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.OpenServiceManager"), Command = "services.msc" },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.CheckServiceStartType"), RequiresAdmin = true },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.CheckGroupPolicyService"), RequiresAdmin = true }
                });
                break;

            case 1005:
                solutions.Add(new SolutionStep
                {
                    Order = 1,
                    Description = Loc.Get("Solution.AcceptUacPrompt")
                });
                break;

            default:
                solutions.Add(new SolutionStep
                {
                    Order = 1,
                    Description = Loc.Get("Solution.ConfirmAdminMode"),
                    RequiresAdmin = true
                });
                break;
        }

        return solutions;
    }

    private List<SolutionStep> GetFileSystemSolutions(ErrorCode code, DiagnosticContext context)
    {
        var solutions = new List<SolutionStep>();

        switch (code.Number)
        {
            case 2001:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CloseFilePrograms") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.UseResourceMonitor"), Command = "resmon.exe" },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.RestartToReleaseLock"), RequiresReboot = true }
                });
                break;

            case 2002:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CheckFilePath") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.ConfirmFileNotDeleted") },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.RunSfc"), Command = "sfc /scannow", RequiresAdmin = true }
                });
                break;

            case 2003:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CheckPathSpelling") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.ConfirmDriveConnected") },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.CheckNetworkPath") }
                });
                break;

            case 2004:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CleanDiskSpace") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.RunDiskCleanup"), Command = "cleanmgr.exe" },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.EmptyRecycleBin") },
                    new SolutionStep { Order = 4, Description = Loc.Get("Solution.UninstallUnneededPrograms") }
                });
                break;

            default:
                solutions.Add(new SolutionStep
                {
                    Order = 1,
                    Description = Loc.Get("Solution.CheckFileSystemIntegrity"),
                    Command = "chkdsk /f",
                    RequiresAdmin = true,
                    RequiresReboot = true
                });
                break;
        }

        return solutions;
    }

    private List<SolutionStep> GetServiceSolutions(ErrorCode code, DiagnosticContext context)
    {
        var solutions = new List<SolutionStep>();

        switch (code.Number)
        {
            case 3001:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.ConfirmWindowsVersion") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.CheckServiceName") },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.ServiceMayBeRemoved") }
                });
                break;

            case 3002:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.OpenServiceManagerCheck"), Command = "services.msc" },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.SetServiceAutoStart"), RequiresAdmin = true },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.CheckServiceDependencies") },
                    new SolutionStep { Order = 4, Description = Loc.Get("Solution.CheckEventViewer"), Command = "eventvwr.msc" }
                });
                break;

            case 3003:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.OpenServiceManager"), Command = "services.msc" },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.SetServiceAutoStartTarget"), RequiresAdmin = true },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.StartService"), RequiresAdmin = true }
                });
                break;

            default:
                solutions.Add(new SolutionStep
                {
                    Order = 1,
                    Description = Loc.Get("Solution.OpenServiceManagerCheckStatus"),
                    Command = "services.msc"
                });
                break;
        }

        return solutions;
    }

    private List<SolutionStep> GetPowerSolutions(ErrorCode code, DiagnosticContext context)
    {
        var solutions = new List<SolutionStep>();

        switch (code.Number)
        {
            case 4001:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CheckPowerService"), Command = "services.msc" },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.StartPowerService"), Command = "net start Power", RequiresAdmin = true },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.ResetPowerSchemes"), Command = "powercfg /restoredefaultschemes", RequiresAdmin = true }
                });
                break;

            case 4002:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.ListPowerPlans"), Command = "powercfg /list" },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.RestoreDefaultPowerPlans"), Command = "powercfg /restoredefaultschemes", RequiresAdmin = true },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.CreateNewPowerPlan"), RequiresAdmin = true }
                });
                break;

            default:
                solutions.Add(new SolutionStep
                {
                    Order = 1,
                    Description = Loc.Get("Solution.ResetPowerDefaults"),
                    Command = "powercfg /restoredefaultschemes",
                    RequiresAdmin = true
                });
                break;
        }

        return solutions;
    }

    private List<SolutionStep> GetNetworkSolutions(ErrorCode code, DiagnosticContext context)
    {
        var solutions = new List<SolutionStep>();

        switch (code.Number)
        {
            case 5001:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.OpenDeviceManager"), Command = "devmgmt.msc" },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.CheckNetworkDriver") },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.ReinstallNetworkDriver"), RequiresAdmin = true }
                });
                break;

            case 5003:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CheckNetworkConnection") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.RestartRouter") },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.CheckFirewall"), Command = "firewall.cpl" },
                    new SolutionStep
                    {
                        Order = 4,
                        Description = Loc.Get("Solution.ResetNetworkSettings") + "（仅通过受控 Network Reset Owner 执行）",
                        RequiresAdmin = true,
                        RequiresReboot = true
                    }
                });
                break;

            default:
                solutions.Add(new SolutionStep
                {
                    Order = 1,
                    Description = Loc.Get("Solution.CheckNetworkStatus"),
                    Command = "ncpa.cpl"
                });
                break;
        }

        return solutions;
    }

    private List<SolutionStep> GetSecuritySolutions(ErrorCode code, DiagnosticContext context)
    {
        return new List<SolutionStep>
        {
            new() { Order = 1, Description = Loc.Get("Solution.DisableSecuritySoftwareTemp") },
            new() { Order = 2, Description = Loc.Get("Solution.AddToWhitelist") },
            new() { Order = 3, Description = Loc.Get("Solution.CheckGroupPolicy"), Command = "gpedit.msc", RequiresAdmin = true },
            new() { Order = 4, Description = Loc.Get("Solution.ContactSysAdmin") }
        };
    }

    private List<SolutionStep> GetSystemSolutions(ErrorCode code, DiagnosticContext context)
    {
        var solutions = new List<SolutionStep>();

        switch (code.Number)
        {
            case 7002:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.CloseUnneededPrograms") },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.OpenTaskManager"), Command = "taskmgr.exe" },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.IncreaseVirtualMemory"), RequiresAdmin = true },
                    new SolutionStep { Order = 4, Description = Loc.Get("Solution.UpgradePhysicalMemory") }
                });
                break;

            case 7003:
                solutions.AddRange(new[]
                {
                    new SolutionStep { Order = 1, Description = Loc.Get("Solution.RestartWmi"), Command = "net stop winmgmt && net start winmgmt", RequiresAdmin = true },
                    new SolutionStep { Order = 2, Description = Loc.Get("Solution.CheckSystemResources"), Command = "taskmgr.exe" },
                    new SolutionStep { Order = 3, Description = Loc.Get("Solution.RunWmiDiag"), Command = "wmidiag.vbs", RequiresAdmin = true }
                });
                break;

            default:
                solutions.Add(new SolutionStep
                {
                    Order = 1,
                    Description = Loc.Get("Solution.RunSfc"),
                    Command = "sfc /scannow",
                    RequiresAdmin = true
                });
                break;
        }

        return solutions;
    }

    private List<SolutionStep> GetConfigurationSolutions(ErrorCode code, DiagnosticContext context)
    {
        return new List<SolutionStep>
        {
            new() { Order = 1, Description = Loc.Get("Solution.CheckConfigParams") },
            new() { Order = 2, Description = Loc.Get("Solution.RestoreDefaultConfig") },
            new() { Order = 3, Description = Loc.Get("Solution.ReadDocumentation") }
        };
    }

    private List<SolutionStep> GetHardwareSolutions(ErrorCode code, DiagnosticContext context)
    {
        return new List<SolutionStep>
        {
            new() { Order = 1, Description = Loc.Get("Solution.OpenDeviceManager"), Command = "devmgmt.msc" },
            new() { Order = 2, Description = Loc.Get("Solution.UpdateHardwareDriver"), RequiresAdmin = true },
            new() { Order = 3, Description = Loc.Get("Solution.CheckHardwareConnection") }
        };
    }

    private List<SolutionStep> GetGenericSolutions(ErrorCode code, DiagnosticContext context)
    {
        return new List<SolutionStep>
        {
            new() { Order = 1, Description = Loc.Get("Solution.RestartAndRetry") },
            new() { Order = 2, Description = Loc.Get("Solution.RunAsAdminGeneric"), RequiresAdmin = true },
            new() { Order = 3, Description = Loc.Get("Solution.CheckLogs") },
            new() { Order = 4, Description = Loc.Get("Solution.ContactSupport") }
        };
    }
}
