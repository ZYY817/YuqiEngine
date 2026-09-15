using System;
using GameOptimizer.Core.Services;

var current = WindowsElevation.Current;
Console.WriteLine("=== 当前进程真实提权状态 (WindowsElevation) ===");
Console.WriteLine($"Level                 : {current.Level}");
Console.WriteLine($"IsElevated            : {current.IsElevated}");
Console.WriteLine($"IsAdministratorAccount: {current.IsAdministratorAccount}");
Console.WriteLine($"Detail                : {current.Detail}");
Console.WriteLine();

var elevatedSnap = new ElevationSnapshot(
    ElevationLevel.ElevatedAdministrator,
    IsElevated: true,
    IsAdministratorAccount: true,
    IsWindows: true,
    WindowsElevation.ReasonElevated,
    "进程令牌已提权（演示）");

var uacSnap = new ElevationSnapshot(
    ElevationLevel.AdministratorNotElevated,
    IsElevated: false,
    IsAdministratorAccount: true,
    IsWindows: true,
    WindowsElevation.ReasonAdministratorNotElevated,
    "UAC 筛选令牌（演示）");

void PrintEval(string title, ElevationSnapshot snap)
{
    var decision = ElevationGate.Evaluate("系统清理", operationRequiresAdministrator: true, snap);
    Console.WriteLine($"=== {title} ===");
    Console.WriteLine($"NeedsElevation : {decision.NeedsElevation}");
    Console.WriteLine($"Attribution    : {decision.Attribution}");
    Console.WriteLine($"含「以管理员身份运行」 : {decision.UserMessage.Contains("以管理员身份运行")}");
    Console.WriteLine($"含「系统拒绝」          : {decision.UserMessage.Contains("系统拒绝")}");
    Console.WriteLine("--- 文案预览 ---");
    var preview = decision.UserMessage.Length > 300
        ? decision.UserMessage.Substring(0, 300) + "…"
        : decision.UserMessage;
    Console.WriteLine(preview);
    Console.WriteLine();
}

PrintEval("场景 A：进程已提权（修复后不应再要求管理员）", elevatedSnap);
PrintEval("场景 B：UAC 筛选令牌（应正确要求提权）", uacSnap);

Console.WriteLine("=== 当前进程 DescribeWriteDenial（access denied）===");
var live = ElevationGate.DescribeWriteDenial("系统清理", "access denied");
Console.WriteLine($"含「以管理员身份运行」 : {live.Contains("以管理员身份运行")}");
Console.WriteLine($"含「系统拒绝」          : {live.Contains("系统拒绝")}");
Console.WriteLine(live.Length > 260 ? live.Substring(0, 260) + "…" : live);
