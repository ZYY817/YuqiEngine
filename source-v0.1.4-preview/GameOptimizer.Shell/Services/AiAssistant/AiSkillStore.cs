// File: GameOptimizer.Shell/Services/AiAssistant/AiSkillStore.cs
using System.IO;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>
/// AI 助手提示词/Skill 文件存储。文件存放在用户配置目录（Config\Ai），
/// 首次运行从应用内置 Assets\Ai 播种——用户可以直接查看、编辑，
/// 或通过"恢复默认"回到出厂版本。
/// </summary>
public sealed class AiSkillStore
{
    /// <summary>参与系统提示词组装的文件（按注入顺序）。</summary>
    public static readonly IReadOnlyList<string> PromptFiles =
        new[] { "system-prompt.md", "SKILL.md", "ITEMS.md", "SAFETY.md" };

    /// <summary>全部可编辑文件（含脚本不在此列）。</summary>
    public static readonly IReadOnlyList<string> EditableFiles = PromptFiles;

    private readonly string _userDir;
    private readonly string _defaultsDir;

    /// <param name="userDir">用户可编辑目录；生产为 ConfigDir\Ai，测试可传临时目录。</param>
    /// <param name="defaultsDir">出厂默认文件目录；生产为 AppContext.BaseDirectory\Assets\Ai。</param>
    public AiSkillStore(string? userDir = null, string? defaultsDir = null)
    {
        _userDir = userDir ?? Path.Combine(AppPaths.ConfigDir, "Ai");
        _defaultsDir = defaultsDir ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Ai");
    }

    public string UserDirectory => _userDir;

    /// <summary>诊断脚本（collect-diagnostics.ps1）的用户目录路径。</summary>
    public string DiagnosticsScriptPath =>
        Path.Combine(_userDir, "scripts", "collect-diagnostics.ps1");

    /// <summary>确保用户目录存在且默认文件已播种（不覆盖用户已有修改）。</summary>
    public void EnsureSeeded()
    {
        Directory.CreateDirectory(_userDir);
        Directory.CreateDirectory(Path.Combine(_userDir, "scripts"));

        foreach (var file in PromptFiles)
        {
            SeedFile(file);
        }
        SeedFile(Path.Combine("scripts", "collect-diagnostics.ps1"));
        SeedFile(Path.Combine("scripts", "collect-baseline.ps1"));
    }

    private void SeedFile(string relativePath)
    {
        var target = Path.Combine(_userDir, relativePath);
        if (File.Exists(target))
            return;
        var source = Path.Combine(_defaultsDir, relativePath);
        if (File.Exists(source))
            File.Copy(source, target);
    }

    /// <summary>读取某个文件内容；不存在返回空串。</summary>
    public string Read(string fileName)
    {
        EnsureSeeded();
        var path = Path.Combine(_userDir, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>保存编辑后的内容（限 EditableFiles 白名单）。</summary>
    public void Save(string fileName, string content)
    {
        if (!EditableFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"文件 {fileName} 不在可编辑列表中", nameof(fileName));
        EnsureSeeded();
        File.WriteAllText(Path.Combine(_userDir, fileName), content);
    }

    /// <summary>从出厂默认恢复某个文件。</summary>
    public void ResetToDefault(string fileName)
    {
        if (!EditableFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"文件 {fileName} 不在可编辑列表中", nameof(fileName));
        var source = Path.Combine(_defaultsDir, fileName);
        var target = Path.Combine(_userDir, fileName);
        if (File.Exists(source))
            File.Copy(source, target, overwrite: true);
    }

    /// <summary>角色提示词文件名（人设与问诊铁律，与知识库文件分开控制）。</summary>
    public const string PersonaFile = "system-prompt.md";

    /// <summary>组装注入系统提示词的内容；可按类别选择角色提示词与 Skill 知识库。</summary>
    public string ComposeSkillContext(bool includePersona = true, bool includeSkill = true)
    {
        EnsureSeeded();
        var parts = new List<string>();
        foreach (var file in PromptFiles)
        {
            var isPersona = string.Equals(file, PersonaFile, StringComparison.OrdinalIgnoreCase);
            if (isPersona ? !includePersona : !includeSkill)
                continue;
            var content = Read(file);
            if (!string.IsNullOrWhiteSpace(content))
                parts.Add($"===== {file} =====\n{content.Trim()}");
        }
        return string.Join("\n\n", parts);
    }
}
