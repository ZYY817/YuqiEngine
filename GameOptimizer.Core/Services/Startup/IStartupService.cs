using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Services.Startup
{
    public interface IStartupService
    {
        Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync();
        Task<IReadOnlyList<StartupItem>> GetStartupItemsByTypeAsync(StartupItemType type);
        Task<StartupOperationResult> EnableItemAsync(string itemId);
        Task<StartupOperationResult> DisableItemAsync(string itemId);
        Task<StartupOperationResult> DeleteItemAsync(string itemId);
        Task<StartupOperationResult> AddItemAsync(string name, string path, string arguments = "");
        Task RefreshAsync();
    }
}
