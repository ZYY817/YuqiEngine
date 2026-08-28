// File: GameOptimizer.Core/Diagnostics/ErrorDiagnosticEngine.cs
using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Diagnostics;

public class ErrorDiagnosticEngine
{
    private readonly SolutionProvider _solutionProvider;
    private static LocalizationService Loc => LocalizationService.Instance;

    public ErrorDiagnosticEngine(SolutionProvider solutionProvider)
    {
        _solutionProvider = solutionProvider;
    }

    public DiagnosticResult Diagnose(Exception exception, DiagnosticContext context)
    {
        try
        {
            return exception switch
            {
                UnauthorizedAccessException uae => DiagnoseUnauthorizedAccess(uae, context),
                SecurityException se => DiagnoseSecurityException(se, context),
                Win32Exception w32 => DiagnoseWin32Exception(w32, context),
                IOException io => DiagnoseIOException(io, context),
                TimeoutException te => DiagnoseTimeout(te, context),
                InvalidOperationException ioe => DiagnoseInvalidOperation(ioe, context),
                ArgumentException ae => DiagnoseArgumentException(ae, context),
                OutOfMemoryException oom => DiagnoseOutOfMemory(oom, context),
                _ => DiagnoseGeneric(exception, context)
            };
        }
        catch
        {
            return CreateFallbackResult(exception, context);
        }
    }

    private DiagnosticResult DiagnoseUnauthorizedAccess(UnauthorizedAccessException ex, DiagnosticContext context)
    {
        var message = ex.Message.ToLowerInvariant();
        var isAdmin = context.IsAdminMode;

        if (message.Contains("registry"))
        {
            var code = new ErrorCode(ErrorCategory.Permission, isAdmin ? ErrorSeverity.High : ErrorSeverity.Critical, 1001);
            return new DiagnosticResult
            {
                Code = code,
                Category = ErrorCategory.Permission,
                Severity = code.Severity,
                Title = Loc.Get("Diag.RegistryAccessDenied.Title"),
                UserMessage = isAdmin 
                    ? Loc.Get("Diag.RegistryAccessDenied.Admin") 
                    : Loc.Get("Diag.RegistryAccessDenied.User"),
                TechnicalDetails = $"{Loc.Get("Diag.TargetResource")}: {context.TargetResource}\n{Loc.Get("Diag.Exception")}: {ex.Message}",
                PossibleCauses = isAdmin
                    ? new() { Loc.Get("Diag.Cause.RegistryProtected"), Loc.Get("Diag.Cause.SecuritySoftware"), Loc.Get("Diag.Cause.GroupPolicy"), Loc.Get("Diag.Cause.RegistryPermission") }
                    : new() { Loc.Get("Diag.Cause.NotAdmin"), Loc.Get("Diag.Cause.UacInsufficient") },
                Solutions = _solutionProvider.GetSolutions(code, context),
                Context = context,
                OriginalException = ex
            };
        }

        if (message.Contains("file") || message.Contains("path"))
        {
            var code = new ErrorCode(ErrorCategory.Permission, isAdmin ? ErrorSeverity.Medium : ErrorSeverity.High, 1002);
            return new DiagnosticResult
            {
                Code = code,
                Category = ErrorCategory.Permission,
                Severity = code.Severity,
                Title = Loc.Get("Diag.FileAccessDenied.Title"),
                UserMessage = isAdmin 
                    ? Loc.Get("Diag.FileAccessDenied.Admin") 
                    : Loc.Get("Diag.FileAccessDenied.User"),
                TechnicalDetails = $"{Loc.Get("Diag.TargetResource")}: {context.TargetResource}\n{Loc.Get("Diag.Exception")}: {ex.Message}",
                PossibleCauses = isAdmin
                    ? new() { Loc.Get("Diag.Cause.FileInUse"), Loc.Get("Diag.Cause.FilePermission"), Loc.Get("Diag.Cause.FileProtected"), Loc.Get("Diag.Cause.SecuritySoftware") }
                    : new() { Loc.Get("Diag.Cause.NotAdmin"), Loc.Get("Diag.Cause.FilePermission") },
                Solutions = _solutionProvider.GetSolutions(code, context),
                Context = context,
                OriginalException = ex
            };
        }

        if (message.Contains("service"))
        {
            var code = new ErrorCode(ErrorCategory.Permission, isAdmin ? ErrorSeverity.High : ErrorSeverity.Critical, 1003);
            return new DiagnosticResult
            {
                Code = code,
                Category = ErrorCategory.Permission,
                Severity = code.Severity,
                Title = Loc.Get("Diag.ServiceDenied.Title"),
                UserMessage = isAdmin 
                    ? Loc.Get("Diag.ServiceDenied.Admin") 
                    : Loc.Get("Diag.ServiceDenied.User"),
                TechnicalDetails = $"{Loc.Get("Diag.OperationType")}: {context.OperationType}\n{Loc.Get("Diag.Exception")}: {ex.Message}",
                PossibleCauses = isAdmin
                    ? new() { Loc.Get("Diag.Cause.ServiceNotExist"), Loc.Get("Diag.Cause.ServiceDisabled"), Loc.Get("Diag.Cause.GroupPolicy"), Loc.Get("Diag.Cause.ServiceDependency") }
                    : new() { Loc.Get("Diag.Cause.NotAdmin") },
                Solutions = _solutionProvider.GetSolutions(code, context),
                Context = context,
                OriginalException = ex
            };
        }

        if (context.OperationType.Contains("power", StringComparison.OrdinalIgnoreCase) || 
            context.OperationType.Contains("电源", StringComparison.OrdinalIgnoreCase))
        {
            var code = new ErrorCode(ErrorCategory.Power, isAdmin ? ErrorSeverity.High : ErrorSeverity.Critical, 4001);
            return new DiagnosticResult
            {
                Code = code,
                Category = ErrorCategory.Power,
                Severity = code.Severity,
                Title = Loc.Get("Diag.PowerFailed.Title"),
                UserMessage = isAdmin 
                    ? Loc.Get("Diag.PowerFailed.Admin") 
                    : Loc.Get("Diag.PowerFailed.User"),
                TechnicalDetails = $"{Loc.Get("Diag.OperationType")}: {context.OperationType}\n{Loc.Get("Diag.Exception")}: {ex.Message}",
                PossibleCauses = isAdmin
                    ? new() { Loc.Get("Diag.Cause.PowerServiceDown"), Loc.Get("Diag.Cause.PowerPlanCorrupt"), Loc.Get("Diag.Cause.SystemPolicy"), Loc.Get("Diag.Cause.PowerConfigMissing") }
                    : new() { Loc.Get("Diag.Cause.NotAdmin") },
                Solutions = _solutionProvider.GetSolutions(code, context),
                Context = context,
                OriginalException = ex
            };
        }

        var genericCode = new ErrorCode(ErrorCategory.Permission, isAdmin ? ErrorSeverity.Medium : ErrorSeverity.High, 1000);
        return new DiagnosticResult
        {
            Code = genericCode,
            Category = ErrorCategory.Permission,
            Severity = genericCode.Severity,
            Title = Loc.Get("Diag.OperationDenied.Title"),
            UserMessage = isAdmin ? Loc.Get("Diag.OperationDenied.Admin") : Loc.Get("Diag.OperationDenied.User"),
            TechnicalDetails = $"{Loc.Get("Diag.OperationType")}: {context.OperationType}\n{Loc.Get("Diag.Exception")}: {ex.Message}",
            PossibleCauses = isAdmin
                ? new() { Loc.Get("Diag.Cause.SystemProtection"), Loc.Get("Diag.Cause.SecuritySoftware"), Loc.Get("Diag.Cause.GroupPolicy") }
                : new() { Loc.Get("Diag.Cause.NotAdmin") },
            Solutions = _solutionProvider.GetSolutions(genericCode, context),
            Context = context,
            OriginalException = ex
        };
    }

    private DiagnosticResult DiagnoseSecurityException(SecurityException ex, DiagnosticContext context)
    {
        var isAdmin = context.IsAdminMode;
        var code = new ErrorCode(ErrorCategory.Security, isAdmin ? ErrorSeverity.High : ErrorSeverity.Critical, 9001);
        
        return new DiagnosticResult
        {
            Code = code,
            Category = ErrorCategory.Security,
            Severity = code.Severity,
            Title = Loc.Get("Diag.SecurityBlocked.Title"),
            UserMessage = isAdmin 
                ? Loc.Get("Diag.SecurityBlocked.Admin") 
                : Loc.Get("Diag.SecurityBlocked.User"),
            TechnicalDetails = $"{Loc.Get("Diag.OperationType")}: {context.OperationType}\n{Loc.Get("Diag.Exception")}: {ex.Message}",
            PossibleCauses = isAdmin
                ? new() { Loc.Get("Diag.Cause.GroupPolicy"), Loc.Get("Diag.Cause.SecuritySoftware"), Loc.Get("Diag.Cause.IntegrityProtection"), Loc.Get("Diag.Cause.CodeSignFailed") }
                : new() { Loc.Get("Diag.Cause.NotAdmin"), Loc.Get("Diag.Cause.SecurityPolicy") },
            Solutions = _solutionProvider.GetSolutions(code, context),
            Context = context,
            OriginalException = ex
        };
    }

    private DiagnosticResult DiagnoseWin32Exception(Win32Exception ex, DiagnosticContext context)
    {
        var isAdmin = context.IsAdminMode;

        return ex.NativeErrorCode switch
        {
            5 => CreateResult(
                new ErrorCode(ErrorCategory.Permission, isAdmin ? ErrorSeverity.Medium : ErrorSeverity.High, 1000),
                Loc.Get("Diag.AccessDenied.Title"),
                isAdmin ? Loc.Get("Diag.AccessDenied.Admin") : Loc.Get("Diag.AccessDenied.User"),
                isAdmin 
                    ? new() { Loc.Get("Diag.Cause.ResourceInUse"), Loc.Get("Diag.Cause.SystemProtection"), Loc.Get("Diag.Cause.SecuritySoftware") }
                    : new() { Loc.Get("Diag.Cause.NotAdmin") },
                ex, context),

            1223 => CreateResult(
                new ErrorCode(ErrorCategory.Permission, ErrorSeverity.Low, 1005),
                Loc.Get("Diag.UserCancelled.Title"),
                Loc.Get("Diag.UserCancelled.Message"),
                new() { Loc.Get("Diag.Cause.UacDenied"), Loc.Get("Diag.Cause.UserCancelled") },
                ex, context),

            1060 => CreateResult(
                new ErrorCode(ErrorCategory.Service, ErrorSeverity.High, 3001),
                Loc.Get("Diag.ServiceNotExist.Title"),
                Loc.Get("Diag.ServiceNotExist.Message"),
                new() { Loc.Get("Diag.Cause.ServiceNotInstalled"), Loc.Get("Diag.Cause.ServiceNameWrong"), Loc.Get("Diag.Cause.WindowsVersionNoService") },
                ex, context),

            1058 => CreateResult(
                new ErrorCode(ErrorCategory.Service, ErrorSeverity.High, 3002),
                Loc.Get("Diag.ServiceCantStart.Title"),
                Loc.Get("Diag.ServiceCantStart.Message"),
                new() { Loc.Get("Diag.Cause.ServiceDisabled"), Loc.Get("Diag.Cause.DependencyNotRunning"), Loc.Get("Diag.Cause.ServiceConfigError"), Loc.Get("Diag.Cause.ServiceExeCorrupt") },
                ex, context),

            2 => CreateResult(
                new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Medium, 2002),
                Loc.Get("Diag.FileNotExist.Title"),
                Loc.Get("Diag.FileNotExist.Message"),
                new() { Loc.Get("Diag.Cause.FilePathWrong"), Loc.Get("Diag.Cause.FileDeleted"), Loc.Get("Diag.Cause.DriveUnavailable") },
                ex, context),

            3 => CreateResult(
                new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Medium, 2003),
                Loc.Get("Diag.PathNotExist.Title"),
                Loc.Get("Diag.PathNotExist.Message"),
                new() { Loc.Get("Diag.Cause.PathNotExist"), Loc.Get("Diag.Cause.NetworkPathUnreachable"), Loc.Get("Diag.Cause.PermissionInsufficient") },
                ex, context),

            32 => CreateResult(
                new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Medium, 2001),
                Loc.Get("Diag.FileInUse.Title"),
                Loc.Get("Diag.FileInUse.Message"),
                new() { Loc.Get("Diag.Cause.FileOpenByOther"), Loc.Get("Diag.Cause.FileLocked"), Loc.Get("Diag.Cause.HandleNotReleased") },
                ex, context),

            _ => CreateResult(
                new ErrorCode(ErrorCategory.System, ErrorSeverity.Medium, 7000),
                Loc.Get("Diag.SystemError.Title", ex.NativeErrorCode),
                isAdmin ? Loc.Get("Diag.SystemError.Admin", ex.Message) : Loc.Get("Diag.SystemError.User"),
                new() { Loc.Get("Diag.Cause.Win32Error", ex.NativeErrorCode), Loc.Get("Diag.Cause.SeeTechnicalDetails") },
                ex, context)
        };
    }

    private DiagnosticResult DiagnoseIOException(IOException ex, DiagnosticContext context)
    {
        var message = ex.Message.ToLowerInvariant();

        if (message.Contains("being used by another process") || message.Contains("another process"))
        {
            var code = new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Medium, 2001);
            return CreateResult(code, Loc.Get("Diag.FileInUse.Title"), Loc.Get("Diag.FileInUse.Message"),
                new() { Loc.Get("Diag.Cause.FileOpenByOther"), Loc.Get("Diag.Cause.FileLocked"), Loc.Get("Diag.Cause.HandleNotReleased") },
                ex, context);
        }

        if (message.Contains("not find") || message.Contains("does not exist"))
        {
            var code = new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Medium, 2002);
            return CreateResult(code, Loc.Get("Diag.FileOrPathNotExist.Title"), Loc.Get("Diag.FileOrPathNotExist.Message"),
                new() { Loc.Get("Diag.Cause.FilePathWrong"), Loc.Get("Diag.Cause.FileDeleted"), Loc.Get("Diag.Cause.DriveUnavailable") },
                ex, context);
        }

        if (message.Contains("access") && message.Contains("denied"))
        {
            var code = new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Medium, 2004);
            return CreateResult(code, Loc.Get("Diag.FileAccessDenied.Title"), Loc.Get("Diag.FileAccessDenied.IOMessage"),
                new() { Loc.Get("Diag.Cause.FileInUse"), Loc.Get("Diag.Cause.PermissionInsufficient"), Loc.Get("Diag.Cause.FileProtected") },
                ex, context);
        }

        if (message.Contains("disk") && (message.Contains("full") || message.Contains("space")))
        {
            var code = new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.High, 2004);
            return CreateResult(code, Loc.Get("Diag.DiskFull.Title"), Loc.Get("Diag.DiskFull.Message"),
                new() { Loc.Get("Diag.Cause.DiskFull"), Loc.Get("Diag.Cause.TempFilesTooMany"), Loc.Get("Diag.Cause.NeedCleanup") },
                ex, context);
        }

        var genericCode = new ErrorCode(ErrorCategory.FileSystem, ErrorSeverity.Medium, 2000);
        return CreateResult(genericCode, Loc.Get("Diag.FileOpFailed.Title"), Loc.Get("Diag.FileOpFailed.Message", ex.Message),
            new() { Loc.Get("Diag.Cause.FileSystemError"), Loc.Get("Diag.Cause.SeeTechnicalDetails") },
            ex, context);
    }

    private DiagnosticResult DiagnoseTimeout(TimeoutException ex, DiagnosticContext context)
    {
        var code = new ErrorCode(ErrorCategory.Network, ErrorSeverity.Medium, 5003);
        return CreateResult(code, Loc.Get("Diag.Timeout.Title"), Loc.Get("Diag.Timeout.Message"),
            new() { Loc.Get("Diag.Cause.NetworkSlow"), Loc.Get("Diag.Cause.SystemResourceLow"), Loc.Get("Diag.Cause.TargetNoResponse"), Loc.Get("Diag.Cause.FirewallBlock") },
            ex, context);
    }

    private DiagnosticResult DiagnoseInvalidOperation(InvalidOperationException ex, DiagnosticContext context)
    {
        var code = new ErrorCode(ErrorCategory.Configuration, ErrorSeverity.Medium, 8001);
        return CreateResult(code, Loc.Get("Diag.InvalidOp.Title"), Loc.Get("Diag.InvalidOp.Message"),
            new() { Loc.Get("Diag.Cause.WrongOrder"), Loc.Get("Diag.Cause.PreconditionNotMet"), Loc.Get("Diag.Cause.ConfigInvalid"), Loc.Get("Diag.Cause.StateInconsistent") },
            ex, context);
    }

    private DiagnosticResult DiagnoseArgumentException(ArgumentException ex, DiagnosticContext context)
    {
        var code = new ErrorCode(ErrorCategory.Configuration, ErrorSeverity.Low, 8002);
        return CreateResult(code, Loc.Get("Diag.ArgumentError.Title"), Loc.Get("Diag.ArgumentError.Message"),
            new() { Loc.Get("Diag.Cause.ParamFormatWrong"), Loc.Get("Diag.Cause.ParamOutOfRange"), Loc.Get("Diag.Cause.RequiredParamMissing") },
            ex, context);
    }

    private DiagnosticResult DiagnoseOutOfMemory(OutOfMemoryException ex, DiagnosticContext context)
    {
        var code = new ErrorCode(ErrorCategory.System, ErrorSeverity.Critical, 7002);
        return CreateResult(code, Loc.Get("Diag.OutOfMemory.Title"), Loc.Get("Diag.OutOfMemory.Message"),
            new() { Loc.Get("Diag.Cause.MemoryInsufficient"), Loc.Get("Diag.Cause.MemoryLeak"), Loc.Get("Diag.Cause.CloseOtherPrograms"), Loc.Get("Diag.Cause.IncreaseVirtualMemory") },
            ex, context);
    }

    private DiagnosticResult DiagnoseGeneric(Exception ex, DiagnosticContext context)
    {
        var code = new ErrorCode(ErrorCategory.Unknown, ErrorSeverity.Medium, 0);
        return CreateResult(code, Loc.Get("Diag.UnknownError.Title"), Loc.Get("Diag.UnknownError.Message", ex.Message),
            new() { Loc.Get("Diag.Cause.ExceptionType", ex.GetType().Name), Loc.Get("Diag.Cause.SeeTechnicalDetails") },
            ex, context);
    }

    private DiagnosticResult CreateResult(
        ErrorCode code, 
        string title, 
        string userMessage, 
        System.Collections.Generic.List<string> causes,
        Exception ex, 
        DiagnosticContext context)
    {
        return new DiagnosticResult
        {
            Code = code,
            Category = code.Category,
            Severity = code.Severity,
            Title = title,
            UserMessage = userMessage,
            TechnicalDetails = $"{Loc.Get("Diag.OperationType")}: {context.OperationType}\n{Loc.Get("Diag.TargetResource")}: {context.TargetResource}\n{Loc.Get("Diag.Exception")}: {ex.Message}\n{Loc.Get("Diag.StackTrace")}: {ex.StackTrace}",
            PossibleCauses = causes,
            Solutions = _solutionProvider.GetSolutions(code, context),
            Context = context,
            OriginalException = ex
        };
    }

    private DiagnosticResult CreateFallbackResult(Exception ex, DiagnosticContext context)
    {
        var code = new ErrorCode(ErrorCategory.Unknown, ErrorSeverity.Medium, 0);
        return new DiagnosticResult
        {
            Code = code,
            Category = ErrorCategory.Unknown,
            Severity = ErrorSeverity.Medium,
            Title = Loc.Get("Diag.DiagnoseFailed.Title"),
            UserMessage = Loc.Get("Diag.DiagnoseFailed.Message", ex.Message),
            TechnicalDetails = $"{Loc.Get("Diag.Exception")}: {ex}",
            PossibleCauses = new() { Loc.Get("Diag.Cause.DiagInternalError") },
            Solutions = new(),
            Context = context,
            OriginalException = ex
        };
    }
}
