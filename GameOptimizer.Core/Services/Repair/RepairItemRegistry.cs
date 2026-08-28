using System.Diagnostics;
using GameOptimizer.Core.Services.ApplicationRepair;
using GameOptimizer.Core.Services.DeviceRepair;
using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using GameOptimizer.Core.Services.StorageSafety;
using GameOptimizer.Core.Services.Repair.RepairOperations;

namespace GameOptimizer.Core.Services.Repair;

public class RepairItemRegistry
{
    private readonly List<RepairItem> _items = [];
    private readonly IOptimizationRevertService? _revertService;
    private readonly GameOptimizer.Core.Services.NetworkReset.INetworkResetOwner? _networkResetOwner;
    private readonly IApplicationPackageRepairOwner? _applicationRepairOwner;
    private readonly IDeviceRepairOwner? _deviceRepairOwner;
    private readonly IMaintenanceDiagnosticOwner? _maintenanceDiagnosticOwner;
    private readonly IStorageFeatureRepairOwner? _storageFeatureRepairOwner;
    private bool _initialized;

    public RepairItemRegistry(
        IOptimizationRevertService? revertService = null,
        GameOptimizer.Core.Services.NetworkReset.INetworkResetOwner? networkResetOwner = null,
        IApplicationPackageRepairOwner? applicationRepairOwner = null,
        IDeviceRepairOwner? deviceRepairOwner = null,
        IMaintenanceDiagnosticOwner? maintenanceDiagnosticOwner = null,
        IStorageFeatureRepairOwner? storageFeatureRepairOwner = null)
    {
        _revertService = revertService;
        _networkResetOwner = networkResetOwner;
        _applicationRepairOwner = applicationRepairOwner;
        _deviceRepairOwner = deviceRepairOwner;
        _maintenanceDiagnosticOwner = maintenanceDiagnosticOwner;
        _storageFeatureRepairOwner = storageFeatureRepairOwner;
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            // 基础修复项
            _items.AddRange(AudioRepairs.GetAll(_deviceRepairOwner));
            _items.AddRange(NetworkRepairs.GetAll(_networkResetOwner));
            _items.AddRange(BluetoothRepairs.GetAll(_deviceRepairOwner));
            _items.AddRange(DisplayRepairs.GetAll());
            _items.AddRange(PeripheralRepairs.GetAll(_deviceRepairOwner));
            _items.AddRange(StorageRepairs.GetAll(_maintenanceDiagnosticOwner, _storageFeatureRepairOwner));
            _items.AddRange(WindowsCoreRepairs.GetAll());
            _items.AddRange(SystemComponentRepairs.GetAll().Where(item =>
                !string.Equals(item.Id, "repair.store", StringComparison.OrdinalIgnoreCase)));
            _items.AddRange(UwpRepairs.GetAll(_applicationRepairOwner));
            
            // 功能模块修复项
            _items.AddRange(GameOptimizationRepairs.GetAll(_applicationRepairOwner));
            _items.AddRange(MemoryRepairs.GetAll(_maintenanceDiagnosticOwner));
            _items.AddRange(CpuRepairs.GetAll());
            _items.AddRange(GraphicsRepairs.GetAll());
            _items.AddRange(PrivacyRepairs.GetAll(_applicationRepairOwner, _maintenanceDiagnosticOwner));
            _items.AddRange(PowerRepairs.GetAll());
            _items.AddRange(ServicesRepairs.GetAll());
            _items.AddRange(ExplorerRepairs.GetAll());
            
            // 动态加载所有优化操作的还原项
            if (_revertService != null)
            {
                var revertItems = _revertService.GetAllRevertItems();
                _items.AddRange(revertItems);
                Debug.WriteLine($"[RepairItemRegistry] Loaded {revertItems.Count} dynamic revert items");
            }
            
            Debug.WriteLine($"[RepairItemRegistry] Total {_items.Count} repair items");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RepairItemRegistry] Init error: {ex}");
        }
    }

    public bool ValidateSuggestedRepairId(string? repairId)
    {
        if (string.IsNullOrEmpty(repairId)) return true;
        var exists = _items.Any(i => i.Id == repairId);
        if (!exists)
            Debug.WriteLine($"[RepairItemRegistry] WARNING: SuggestedRepairId '{repairId}' not found!");
        return exists;
    }

    public IReadOnlyList<RepairItem> GetAll() => _items;

    public IEnumerable<RepairItem> GetByCategory(RepairCategory category) =>
        _items.Where(i => i.Category == category);

    public RepairItem? GetById(string id) =>
        _items.FirstOrDefault(i => i.Id == id);

    public RepairItem? GetByOptimization(string optimizationId) =>
        _items.FirstOrDefault(i => i.RelatedOptimization == optimizationId);
}
