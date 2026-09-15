using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRuntimeStateStore
{
	private static readonly string BaselineDir = Path.Combine(AppPaths.BackupsDir, "Baseline");

	private static readonly string RuntimeStatePath = Path.Combine(BaselineDir, "runtime_state.json");

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

	public async Task<BaselineRuntimeState?> LoadAsync(CancellationToken ct = default(CancellationToken))
	{
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			Directory.CreateDirectory(BaselineDir);
			if (!File.Exists(RuntimeStatePath))
			{
				return null;
			}
			string json = await File.ReadAllTextAsync(RuntimeStatePath, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(json))
			{
				return null;
			}
			return JsonSerializer.Deserialize<BaselineRuntimeState>(json, JsonOptions);
		}
		catch
		{
			return null;
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task SaveAsync(BaselineRuntimeState state, CancellationToken ct = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(state, "state");
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			Directory.CreateDirectory(BaselineDir);
			state.LastUpdatedUtc = DateTime.UtcNow;
			string tempFile = $"{RuntimeStatePath}.{Guid.NewGuid():N}.tmp";
			string json = JsonSerializer.Serialize(state, JsonOptions);
			await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(continueOnCapturedContext: false);
			File.Move(tempFile, RuntimeStatePath, overwrite: true);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task ClearAsync(CancellationToken ct = default(CancellationToken))
	{
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (File.Exists(RuntimeStatePath))
			{
				File.Delete(RuntimeStatePath);
			}
		}
		finally
		{
			_lock.Release();
		}
	}
}
