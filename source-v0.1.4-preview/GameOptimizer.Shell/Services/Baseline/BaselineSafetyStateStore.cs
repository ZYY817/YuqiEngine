using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineSafetyStateStore
{
	private static readonly string BaselineDir = Path.Combine(AppPaths.BackupsDir, "Baseline");

	private static readonly string BaselineStatePath = Path.Combine(BaselineDir, "baseline_state.json");

	private static readonly string BaselineCatalogPath = Path.Combine(BaselineDir, "catalog.json");

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);

	public async Task<BaselineSafetyState> LoadAsync(CancellationToken ct = default(CancellationToken))
	{
		await _syncLock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			EnsureDirectory();
			if (!File.Exists(BaselineStatePath))
			{
				BaselineSafetyState initialState = CreateInitialState();
				await WriteStateCoreAsync(initialState, ct).ConfigureAwait(continueOnCapturedContext: false);
				return initialState;
			}
			BaselineSafetyState? state = JsonSerializer.Deserialize<BaselineSafetyState>(await File.ReadAllTextAsync(BaselineStatePath, ct).ConfigureAwait(continueOnCapturedContext: false), JsonOptions);
			if (state == null)
			{
				return await RecoverCorruptedStateAsync("Baseline state payload is empty.", ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			return await NormalizeLoadedStateAsync(state, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (JsonException ex)
		{
			return await RecoverCorruptedStateAsync(ex.Message, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex2)
		{
			return await RecoverCorruptedStateAsync(ex2.Message, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_syncLock.Release();
		}
	}

	public async Task SaveAsync(BaselineSafetyState state, CancellationToken ct = default(CancellationToken))
	{
		if (state == null)
		{
			throw new ArgumentNullException("state");
		}
		await _syncLock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			EnsureDirectory();
			state.LastUpdatedUtc = DateTime.UtcNow;
			await WriteStateCoreAsync(state, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_syncLock.Release();
		}
	}

	private static void EnsureDirectory()
	{
		Directory.CreateDirectory(BaselineDir);
	}

	private static BaselineSafetyState CreateInitialState()
	{
		if (HasVerifiedBaselineInCatalog())
		{
			return new BaselineSafetyState
			{
				FirstRunState = FirstRunBaselineState.CompletedVerified,
				IsReadonlyMode = false,
				CompletedUtc = DateTime.UtcNow,
				LastEntryPoint = "baseline_catalog_migration"
			};
		}
		if (File.Exists(Path.Combine(AppPaths.FirstRunBackupDir, "backup_meta.json")) && File.Exists(Path.Combine(AppPaths.FirstRunBackupDir, "system_state.json")))
		{
			return new BaselineSafetyState
			{
				FirstRunState = FirstRunBaselineState.NotStarted,
				IsReadonlyMode = false,
				LastEntryPoint = "legacy_first_run_backup_detected",
				LastError = "legacy_first_run_backup_requires_baseline_v2"
			};
		}
		return new BaselineSafetyState
		{
			FirstRunState = FirstRunBaselineState.NotStarted,
			IsReadonlyMode = false,
			LastEntryPoint = "initial_state"
		};
	}

	private static bool HasVerifiedBaselineInCatalog()
	{
		try
		{
			if (!File.Exists(BaselineCatalogPath))
			{
				return false;
			}
			using FileStream utf8Json = File.OpenRead(BaselineCatalogPath);
			using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
			if (!jsonDocument.RootElement.TryGetProperty("entries", out var value) || value.ValueKind != JsonValueKind.Array)
			{
				return false;
			}
			foreach (JsonElement item in value.EnumerateArray())
			{
				JsonElement value2;
				string? a = (item.TryGetProperty("packageType", out value2) ? value2.GetString() : null);
				JsonElement value3;
				string? text = (item.TryGetProperty("packageId", out value3) ? value3.GetString() : null);
				JsonElement value4;
				string? a2 = (item.TryGetProperty("integrityState", out value4) ? value4.GetString() : null);
				JsonElement value5;
				int value6;
				int num = ((item.TryGetProperty("baselineState", out value5) && value5.ValueKind == JsonValueKind.Number && value5.TryGetInt32(out value6)) ? value6 : (-1));
				if (string.Equals(a, "BaselineFirstRun", StringComparison.OrdinalIgnoreCase) && string.Equals(a2, "Verified", StringComparison.OrdinalIgnoreCase) && num == 5 && !string.IsNullOrWhiteSpace(text))
				{
					string text2 = Path.Combine(BaselineDir, "packages", text.Trim());
					string path = Path.Combine(text2, "manifest.json");
					if (Directory.Exists(text2) && File.Exists(path))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static async Task<BaselineSafetyState> NormalizeLoadedStateAsync(BaselineSafetyState state, CancellationToken ct)
	{
		if (state.FirstRunState == FirstRunBaselineState.CompletedVerified && state.IsReadonlyMode)
		{
			state.IsReadonlyMode = false;
			state.LastError = "completed_state_autofix_readonly_cleared";
			state.LastEntryPoint = "completed_state_autofix";
			state.LastUpdatedUtc = DateTime.UtcNow;
			await WriteStateCoreAsync(state, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (state.FirstRunState != FirstRunBaselineState.CompletedVerified)
		{
			return state;
		}
		if (!string.Equals(state.LastEntryPoint, "legacy_first_run_backup_migration", StringComparison.OrdinalIgnoreCase))
		{
			return state;
		}
		if (HasVerifiedBaselineInCatalog())
		{
			return state;
		}
		state.FirstRunState = FirstRunBaselineState.NotStarted;
		state.IsReadonlyMode = false;
		state.CompletedUtc = null;
		state.LastError = "legacy_completion_downgraded_requires_baseline_v2";
		state.LastEntryPoint = "legacy_state_downgraded_to_not_started";
		state.LastUpdatedUtc = DateTime.UtcNow;
		await WriteStateCoreAsync(state, ct).ConfigureAwait(continueOnCapturedContext: false);
		return state;
	}

	private async Task<BaselineSafetyState> RecoverCorruptedStateAsync(string reason, CancellationToken ct)
	{
		BaselineSafetyState corruptedState = new BaselineSafetyState
		{
			FirstRunState = FirstRunBaselineState.Corrupted,
			IsReadonlyMode = true,
			LastError = reason,
			LastEntryPoint = "baseline_state_recover"
		};
		await WriteStateCoreAsync(corruptedState, ct).ConfigureAwait(continueOnCapturedContext: false);
		return corruptedState;
	}

	private static async Task WriteStateCoreAsync(BaselineSafetyState state, CancellationToken ct)
	{
		string tempFile = $"{BaselineStatePath}.{Guid.NewGuid():N}.tmp";
		string json = JsonSerializer.Serialize(state, JsonOptions);
		await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(continueOnCapturedContext: false);
		File.Move(tempFile, BaselineStatePath, overwrite: true);
	}
}
