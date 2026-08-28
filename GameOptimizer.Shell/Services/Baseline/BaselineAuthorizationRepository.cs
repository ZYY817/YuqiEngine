using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineSafetyStateAuthorizationRepository : IBaselineAuthorizationRepository
{
	private readonly BaselineSafetyStateStore _store;

	public BaselineSafetyStateAuthorizationRepository(BaselineSafetyStateStore store) =>
		_store = store ?? throw new ArgumentNullException(nameof(store));

	public Task<BaselineSafetyState> LoadAsync(CancellationToken ct = default) => _store.LoadAsync(ct);

	public Task SaveAsync(BaselineSafetyState state, CancellationToken ct = default) => _store.SaveAsync(state, ct);
}
