// File: GameOptimizer.Core/Diagnostics/BackupDiagnosticService.cs
using System;
using System.Collections.Generic;
using System.IO;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Diagnostics;

public class BackupDiagnosticService
{
    public DiagnosticResult DiagnoseBackupFailure(Exception exception, string operationName)
    {
        var loc = LocalizationService.Instance;
        var result = new DiagnosticResult
        {
            Category = ErrorCategory.FileSystem,
            Severity = ErrorSeverity.High,
            Title = loc.Get("Backup.DiagnosticTitle"),
            OriginalException = exception,
            Timestamp = DateTime.Now
        };

        if (exception is DirectoryNotFoundException || exception.Message.Contains("Could not find"))
        {
            result.Code = new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.High, 1001);
            result.UserMessage = loc.Get("Backup.Error.DirectoryNotFound");
            result.TechnicalDetails = $"Directory: {AppPaths.SnapshotsDir}\nException: {exception.Message}";
            result.PossibleCauses.Add(loc.Get("Backup.Cause.DirectoryNotCreated"));
            result.PossibleCauses.Add(loc.Get("Backup.Cause.ParentDirMissing"));
            
            result.Solutions.Add(new SolutionStep
            {
                Order = 1,
                Description = loc.Get("Backup.Solution.CreateDirectory"),
                RequiresAdmin = false,
                RequiresReboot = false
            });
        }
        else if (exception is UnauthorizedAccessException)
        {
            result.Code = new ErrorCode(ErrorCategory.Permission, ErrorSeverity.High, 1002);
            result.UserMessage = loc.Get("Backup.Error.NoPermission");
            result.TechnicalDetails = $"Path: {AppPaths.SnapshotsDir}\nException: {exception.Message}";
            result.PossibleCauses.Add(loc.Get("Backup.Cause.NoWritePermission"));
            result.PossibleCauses.Add(loc.Get("Backup.Cause.ReadOnlyFolder"));
            
            result.Solutions.Add(new SolutionStep
            {
                Order = 1,
                Description = loc.Get("Backup.Solution.RunAsAdmin"),
                RequiresAdmin = true,
                RequiresReboot = false
            });
            result.Solutions.Add(new SolutionStep
            {
                Order = 2,
                Description = "目录 ACL 修复当前不可自动执行；请保留诊断信息并检查目录所有权来源。",
                RequiresAdmin = true,
                RequiresReboot = false
            });
        }
        else if (exception is IOException && exception.Message.Contains("space"))
        {
            result.Code = new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Critical, 1003);
            result.UserMessage = loc.Get("Backup.Error.DiskFull");
            result.TechnicalDetails = $"Exception: {exception.Message}";
            result.PossibleCauses.Add(loc.Get("Backup.Cause.DiskFull"));
            
            result.Solutions.Add(new SolutionStep
            {
                Order = 1,
                Description = loc.Get("Backup.Solution.CleanupDisk"),
                RequiresAdmin = false,
                RequiresReboot = false
            });
            result.Solutions.Add(new SolutionStep
            {
                Order = 2,
                Description = loc.Get("Backup.Solution.DeleteOldBackups"),
                RequiresAdmin = false,
                RequiresReboot = false
            });
        }
        else if (exception is ArgumentException && exception.Message.Contains("name"))
        {
            result.Code = new ErrorCode(ErrorCategory.Configuration, ErrorSeverity.Medium, 1004);
            result.UserMessage = loc.Get("Backup.Error.InvalidName");
            result.TechnicalDetails = $"Exception: {exception.Message}";
            result.PossibleCauses.Add(loc.Get("Backup.Cause.InvalidCharacters"));
            result.PossibleCauses.Add(loc.Get("Backup.Cause.NameTooLong"));
            
            result.Solutions.Add(new SolutionStep
            {
                Order = 1,
                Description = loc.Get("Backup.Solution.UseValidName"),
                RequiresAdmin = false,
                RequiresReboot = false
            });
        }
        else
        {
            result.Code = new ErrorCode(ErrorCategory.Unknown, ErrorSeverity.High, 1099);
            result.UserMessage = loc.Get("Backup.Error.Unknown", exception.Message);
            result.TechnicalDetails = $"Operation: {operationName}\nException: {exception}";
            result.PossibleCauses.Add(loc.Get("Backup.Cause.UnknownError"));
            
            result.Solutions.Add(new SolutionStep
            {
                Order = 1,
                Description = loc.Get("Backup.Solution.CheckLogs"),
                RequiresAdmin = false,
                RequiresReboot = false
            });
            result.Solutions.Add(new SolutionStep
            {
                Order = 2,
                Description = loc.Get("Backup.Solution.RestartApp"),
                RequiresAdmin = false,
                RequiresReboot = false
            });
        }

        return result;
    }

    public List<AutoRepairOption> GetRepairOptions(DiagnosticResult diagnostic)
    {
        var options = new List<AutoRepairOption>();

        if (diagnostic.Code.Category == ErrorCategory.FileSystem && 
            diagnostic.Code.Number == 1001)
        {
            options.Add(new AutoRepairOption
            {
                Id = "repair_backup_create_directory",
                Description = LocalizationService.Instance.Get("Backup.Repair.CreateDirectory"),
                RiskLevel = RepairRiskLevel.Safe,
                RequiresReboot = false,
                RequiresAdmin = false,
                EstimatedSeconds = 2,
                SuccessRate = 98,
                TechnicalDetails = $"Create directory: {AppPaths.SnapshotsDir}"
            });
        }

        if (diagnostic.Code.Category == ErrorCategory.Permission)
        {
            options.Add(new AutoRepairOption
            {
                Id = "repair_backup_request_permission",
                Description = LocalizationService.Instance.Get("Backup.Repair.FixPermissions"),
                RiskLevel = RepairRiskLevel.Low,
                RequiresReboot = false,
                RequiresAdmin = true,
                EstimatedSeconds = 5,
                SuccessRate = 85,
                TechnicalDetails = $"Grant write permission to: {AppPaths.AppDataRoot}"
            });
        }

        return options;
    }
}
