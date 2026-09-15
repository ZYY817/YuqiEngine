// File: GameOptimizer.Core/Services/Snapshot/SnapshotChecksumCalculator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameOptimizer.Core.Services.Snapshot
{
    /// <summary>
    /// 快照校验和计算器 - 确保跨序列化的一致性
    /// </summary>
    public static class SnapshotChecksumCalculator
    {
        private static readonly JsonSerializerOptions NormalizeOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };

        /// <summary>
        /// 计算快照的SHA256校验和（确定性算法）
        /// </summary>
        public static string ComputeChecksum(OptimizationSnapshot snapshot)
        {
            var sb = new StringBuilder();
            
            // 1. 基础元数据（使用ISO 8601格式避免Ticks精度问题）
            sb.Append($"ID:{snapshot.Id}|");
            sb.Append($"NAME:{snapshot.Name}|");
            sb.Append($"TIME:{snapshot.CreatedAt:O}|"); // ISO 8601 round-trip format
            sb.Append($"VER:{snapshot.Version}|");
            
            // 2. 操作状态（排序后序列化）
            sb.Append("OPS:");
            foreach (var kv in snapshot.OperationStates.OrderBy(x => x.Key))
            {
                sb.Append($"{kv.Key}={kv.Value};");
            }
            sb.Append("|");
            
            // 3. 注册表设置（规范化值类型）
            sb.Append("REG:");
            foreach (var kv in snapshot.Settings.OrderBy(x => x.Key))
            {
                var normalizedValue = NormalizeValue(kv.Value);
                sb.Append($"{kv.Key}={normalizedValue};");
            }
            sb.Append("|");
            
            // 4. 服务状态
            sb.Append("SVC:");
            foreach (var kv in snapshot.ServiceStates.OrderBy(x => x.Key))
            {
                sb.Append($"{kv.Key}={kv.Value};");
            }
            sb.Append("|");
            
            // 5. BCD设置
            sb.Append("BCD:");
            foreach (var kv in snapshot.BcdSettings.OrderBy(x => x.Key))
            {
                sb.Append($"{kv.Key}={kv.Value ?? "NULL"};");
            }
            sb.Append("|");
            
            // 6. 电源计划
            sb.Append($"PWR:{snapshot.ActivePowerPlanGuid ?? "NULL"}|");

            if (snapshot.Version >= 4)
            {
                sb.Append("TYPED_DWORD:");
                foreach (var item in snapshot.TypedRegistryDwordStates.OrderBy(x => x.CapabilityId, StringComparer.Ordinal))
                {
                    sb.Append(item.CapabilityId).Append('=')
                        .Append(item.SourceKey).Append(':')
                        .Append(item.ValueExists).Append(':')
                        .Append(item.Value?.ToString() ?? "NULL").Append(';');
                }
                sb.Append('|');
            }

            if (snapshot.Version >= 5)
            {
                sb.Append("TYPED_REGISTRY:");
                foreach (var capability in snapshot.TypedRegistryCapabilityStates.OrderBy(x => x.CapabilityId, StringComparer.Ordinal))
                {
                    sb.Append(capability.CapabilityId).Append('@').Append(capability.SubKeyPath).Append('=');
                    foreach (var value in capability.Values.OrderBy(x => x.ValueName, StringComparer.Ordinal))
                    {
                        sb.Append(value.ValueName).Append(':').Append(value.Kind).Append(':')
                            .Append(value.ValueExists).Append(':')
                            .Append(value.StringValue ?? "NULL").Append(':')
                            .Append(value.DwordValue?.ToString() ?? "NULL").Append(',');
                    }
                    sb.Append(';');
                }
                sb.Append('|');
            }
            
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// 规范化值类型（处理JsonElement和其他类型）
        /// </summary>
        private static string NormalizeValue(object? value)
        {
            if (value == null) return "NULL";
            
            // Why: JSON 序列化→反序列化后，CLR 原始类型（int/string/bool）变成 JsonElement，
            // 必须统一走 JsonElement 路径，否则同一个数值会产生不同的规范化字符串导致校验和不匹配
            if (value is JsonElement jsonElement)
            {
                return NormalizeJsonElement(jsonElement);
            }
            
            // Why: 将 CLR 类型先转成 JsonElement 再规范化，确保与反序列化后的路径完全一致
            try
            {
                var json = JsonSerializer.Serialize(value, NormalizeOptions);
                var element = JsonSerializer.Deserialize<JsonElement>(json);
                return NormalizeJsonElement(element);
            }
            catch
            {
                return $"OBJ:{value}";
            }
        }

        private static string NormalizeJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => $"STR:{element.GetString()}",
                JsonValueKind.Number => $"NUM:{element.GetRawText()}",
                JsonValueKind.True => "BOOL:True",
                JsonValueKind.False => "BOOL:False",
                JsonValueKind.Null => "NULL",
                _ => $"JSON:{element.GetRawText()}"
            };
        }

        /// <summary>
        /// 验证快照完整性
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidateSnapshot(OptimizationSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Id))
                return (false, "快照ID为空");
            
            if (string.IsNullOrWhiteSpace(snapshot.Name))
                return (false, "快照名称为空");
            
            if (snapshot.CreatedAt == default)
                return (false, "创建时间无效");
            
            if (snapshot.Version < 2)
                return (false, $"快照版本过旧: {snapshot.Version}");
            
            return (true, null);
        }

        /// <summary>
        /// 比较两个快照的差异（用于诊断）
        /// </summary>
        public static List<string> CompareSnapshots(OptimizationSnapshot original, OptimizationSnapshot written)
        {
            var differences = new List<string>();
            
            if (original.Id != written.Id)
                differences.Add($"ID不匹配: {original.Id} vs {written.Id}");
            
            if (original.Name != written.Name)
                differences.Add($"名称不匹配: {original.Name} vs {written.Name}");
            
            if (Math.Abs((original.CreatedAt - written.CreatedAt).TotalMilliseconds) > 1)
                differences.Add($"时间不匹配: {original.CreatedAt:O} vs {written.CreatedAt:O}");
            
            if (original.Version != written.Version)
                differences.Add($"版本不匹配: {original.Version} vs {written.Version}");
            
            if (original.OperationStates.Count != written.OperationStates.Count)
                differences.Add($"操作数量不匹配: {original.OperationStates.Count} vs {written.OperationStates.Count}");
            
            if (original.Settings.Count != written.Settings.Count)
                differences.Add($"设置数量不匹配: {original.Settings.Count} vs {written.Settings.Count}");
            
            return differences;
        }
    }
}
