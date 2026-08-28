// File: GameOptimizer.Shell/Constants/NavigationIconData.cs
namespace GameOptimizer.Shell.Constants;

/// <summary>
/// 项目自有的 24 x 24 导航功能图标几何。
/// 这些图标只表达通用功能，不包含第三方品牌或商标图形。
/// </summary>
public static class NavigationIconData
{
    public const string Overview = "M4,4 H10 V10 H4 Z M14,4 H20 V8 H14 Z M4,14 H10 V20 H4 Z M14,12 H20 V20 H14 Z";
    public const string Memory = "M6,5 H18 V19 H6 Z M9,8 H15 V12 H9 Z M9,15 H15 M3,8 H6 M3,12 H6 M3,16 H6 M18,8 H21 M18,12 H21 M18,16 H21";
    public const string Audio = "M4,10 H8 L13,6 V18 L8,14 H4 Z M16,9 C18,10.5 18,13.5 16,15 M18.5,6.5 C22,9.5 22,14.5 18.5,17.5";
    public const string Performance = "M5,16 C7,10 11,6 18,4 C19,11 15,16 9,18 Z M9,15 L4,20 M14,8 L18,12 M8,11 L5,10";
    public const string Cpu = "M7,5 H17 C18.1,5 19,5.9 19,7 V17 C19,18.1 18.1,19 17,19 H7 C5.9,19 5,18.1 5,17 V7 C5,5.9 5.9,5 7,5 Z M9,9 H15 V15 H9 Z M9,2 V5 M15,2 V5 M9,19 V22 M15,19 V22 M2,9 H5 M2,15 H5 M19,9 H22 M19,15 H22";
    public const string Graphics = "M4,5 H20 C21.1,5 22,5.9 22,7 V16 H2 V7 C2,5.9 2.9,5 4,5 Z M9,20 H15 M12,16 V20";
    public const string Network = "M12,4 V9 M12,15 V20 M5,8 L9,10 M15,14 L19,16 M19,8 L15,10 M9,14 L5,16 M12,9 A3,3 0 1 1 12,15 A3,3 0 1 1 12,9 M3,6 A2,2 0 1 1 3,10 A2,2 0 1 1 3,6 M21,6 A2,2 0 1 1 21,10 A2,2 0 1 1 21,6 M3,14 A2,2 0 1 1 3,18 A2,2 0 1 1 3,14 M21,14 A2,2 0 1 1 21,18 A2,2 0 1 1 21,14";
    public const string Privacy = "M12,3 L20,6 V11 C20,16 17,20 12,22 C7,20 4,16 4,11 V6 Z M9,12 L11,14 L15,10";
    public const string System = "M4,6 H14 M18,6 H20 M4,12 H7 M11,12 H20 M4,18 H12 M16,18 H20 M14,4 V8 M7,10 V14 M12,16 V20";
    public const string Folder = "M3,6 H10 L12,8 H21 V19 H3 Z M7,12 H17 M7,15 H14";
    public const string Tasks = "M5,5 H19 V20 H5 Z M8,3 V7 M16,3 V7 M5,9 H19 M12,12 V16 L15,18";
    public const string Services = "M4,4 H20 V9 H4 Z M4,15 H20 V20 H4 Z M7,6.5 H7.1 M11,6.5 H17 M7,17.5 H7.1 M11,17.5 H17 M12,9 V15";
    public const string Cleanup = "M5,7 H19 M9,7 V4 H15 V7 M7,7 L8,20 H16 L17,7 M10,11 V17 M14,11 V17";
    public const string Apps = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z";
    public const string Peripheral = "M7,3 H17 C19.2,3 21,4.8 21,7 V15 C21,18.3 18.3,21 15,21 H9 C5.7,21 3,18.3 3,15 V7 C3,4.8 4.8,3 7,3 Z M12,3 V9 M8,12 H8.1 M16,12 H16.1";
    public const string Esports = "M7,7 H17 C19,7 20,8.5 20.5,11 L22,17 C22.5,19 20.5,20.5 19,19 L16,16 H8 L5,19 C3.5,20.5 1.5,19 2,17 L3.5,11 C4,8.5 5,7 7,7 Z M7,11 V15 M5,13 H9 M15,12 H15.1 M18,14 H18.1";
    public const string Hardware = "M4,5 H20 V19 H4 Z M7,15 H17 M8,9 H16 M8,12 H13 M17,16 H17.1";
    public const string NetworkTest = "M3,9 C8,4 16,4 21,9 M6,12 C9.5,8.5 14.5,8.5 18,12 M9.5,15.5 C11,14 13,14 14.5,15.5 M12,19 H12.1";
    public const string RestorePoint = "M4,7 V3 M4,3 H8 M4,3 L7,6 M5,7 A8,8 0 1 1 4,15 M12,8 V13 L15,15";
    public const string Repair = "M14,5 C16,3 19,3 21,4 L17,8 L14,7 L13,4 C10,5 9,8 11,10 L4,17 C3,18 3,19 4,20 C5,21 6,21 7,20 L14,13 C16,15 19,14 20,11";
    public const string Backup = "M5,4 H17 L20,7 V20 H4 V4 Z M8,4 V9 H16 V4 M8,14 H16 V20 M15,6 H15.1";
    public const string CoreManagement = "M6,5 H18 V19 H6 Z M9,8 H12 V11 H9 Z M13,8 H16 V11 H13 Z M9,13 H12 V16 H9 Z M13,13 H16 V16 H13 Z M9,2 V5 M15,2 V5 M9,19 V22 M15,19 V22 M3,9 H6 M3,15 H6 M18,9 H21 M18,15 H21";
    public const string Power = "M6,6 H17 C18.7,6 20,7.3 20,9 V17 C20,18.7 18.7,20 17,20 H6 C4.3,20 3,18.7 3,17 V9 C3,7.3 4.3,6 6,6 Z M20,11 H22 V15 H20 M12,8 L9,14 H12 L11,18 L16,12 H13 L14,8 Z";
    public const string Settings = "M12,8 A4,4 0 1 1 12,16 A4,4 0 1 1 12,8 M12,3 V5 M12,19 V21 M3,12 H5 M19,12 H21 M5.6,5.6 L7,7 M17,17 L18.4,18.4 M18.4,5.6 L17,7 M7,17 L5.6,18.4";
}
