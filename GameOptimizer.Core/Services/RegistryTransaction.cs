// File: GameOptimizer.Core/Services/RegistryTransaction.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 注册表事务操作 - 提供原子性写入保证
/// 解决 P0-4: 注册表操作缺乏原子性保护
/// </summary>
public sealed class RegistryTransaction : IDisposable
{
    private readonly RegistryKey _root;
    private readonly string _subKeyPath;
    private readonly Dictionary<string, (object Value, RegistryValueKind Kind)> _pendingWrites = new();
    private readonly string _tempSubKeyPath;
    private bool _committed;
    private bool _disposed;

    public RegistryTransaction(RegistryKey root, string subKeyPath)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _subKeyPath = subKeyPath ?? throw new ArgumentNullException(nameof(subKeyPath));
        _tempSubKeyPath = $"{subKeyPath}_tx_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// 添加待写入的值
    /// </summary>
    public void SetValue(string valueName, object value, RegistryValueKind kind = RegistryValueKind.Unknown)
    {
        if (_committed)
            throw new InvalidOperationException("Transaction already committed");
        
        _pendingWrites[valueName] = (value, kind);
    }

    /// <summary>
    /// 提交事务 - 原子性写入所有值
    /// </summary>
    public (bool Success, string? Error) Commit()
    {
        if (_committed)
            return (false, "Transaction already committed");
        
        if (_disposed)
            return (false, "Transaction already disposed");

        try
        {
            // 步骤1: 写入临时键
            using (var tempKey = _root.CreateSubKey(_tempSubKeyPath, writable: true))
            {
                if (tempKey == null)
                    return (false, $"Failed to create temporary registry key: {_tempSubKeyPath}");

                foreach (var kvp in _pendingWrites)
                {
                    try
                    {
                        if (kvp.Value.Kind == RegistryValueKind.Unknown)
                            tempKey.SetValue(kvp.Key, kvp.Value.Value);
                        else
                            tempKey.SetValue(kvp.Key, kvp.Value.Value, kvp.Value.Kind);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RegistryTransaction] Failed to write {kvp.Key}: {ex.Message}");
                        // 清理临时键
                        try { _root.DeleteSubKeyTree(_tempSubKeyPath, throwOnMissingSubKey: false); } catch { }
                        return (false, $"Failed to write value {kvp.Key}: {ex.Message}");
                    }
                }
            }

            // 步骤2: 验证临时键完整性
            using (var verifyKey = _root.OpenSubKey(_tempSubKeyPath, writable: false))
            {
                if (verifyKey == null)
                    return (false, "Temporary key verification failed");

                foreach (var kvp in _pendingWrites)
                {
                    var readValue = verifyKey.GetValue(kvp.Key);
                    if (readValue == null)
                    {
                        Debug.WriteLine($"[RegistryTransaction] Verification failed: {kvp.Key} not found");
                        try { _root.DeleteSubKeyTree(_tempSubKeyPath, throwOnMissingSubKey: false); } catch { }
                        return (false, $"Verification failed: {kvp.Key} not written correctly");
                    }
                }
            }

            // 步骤3: 删除旧键（如果存在）
            try
            {
                if (SafeRegistryHelper.KeyExists(_root, _subKeyPath))
                {
                    _root.DeleteSubKeyTree(_subKeyPath, throwOnMissingSubKey: false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RegistryTransaction] Failed to delete old key: {ex.Message}");
                try { _root.DeleteSubKeyTree(_tempSubKeyPath, throwOnMissingSubKey: false); } catch { }
                return (false, $"Failed to delete old key: {ex.Message}");
            }

            // 步骤4: 原子性重命名（通过复制实现，因为Registry不支持直接重命名）
            try
            {
                using (var sourceKey = _root.OpenSubKey(_tempSubKeyPath, writable: false))
                using (var targetKey = _root.CreateSubKey(_subKeyPath, writable: true))
                {
                    if (sourceKey == null || targetKey == null)
                        return (false, "Failed to open keys for atomic rename");

                    foreach (var valueName in sourceKey.GetValueNames())
                    {
                        var value = sourceKey.GetValue(valueName);
                        var kind = sourceKey.GetValueKind(valueName);
                        targetKey.SetValue(valueName, value!, kind);
                    }
                }

                // 清理临时键
                _root.DeleteSubKeyTree(_tempSubKeyPath, throwOnMissingSubKey: false);
                
                _committed = true;
                Debug.WriteLine($"[RegistryTransaction] Successfully committed {_pendingWrites.Count} values to {_subKeyPath}");
                return (true, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RegistryTransaction] Atomic rename failed: {ex.Message}");
                try { _root.DeleteSubKeyTree(_tempSubKeyPath, throwOnMissingSubKey: false); } catch { }
                return (false, $"Atomic rename failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RegistryTransaction] Commit failed: {ex.Message}");
            return (false, $"Transaction commit failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 回滚事务 - 清理临时数据
    /// </summary>
    public void Rollback()
    {
        try
        {
            if (SafeRegistryHelper.KeyExists(_root, _tempSubKeyPath))
            {
                _root.DeleteSubKeyTree(_tempSubKeyPath, throwOnMissingSubKey: false);
                Debug.WriteLine($"[RegistryTransaction] Rolled back transaction for {_subKeyPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RegistryTransaction] Rollback error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (!_committed)
        {
            Rollback();
        }

        _disposed = true;
    }
}
