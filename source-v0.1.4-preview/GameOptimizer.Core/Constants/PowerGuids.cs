namespace GameOptimizer.Core.Constants;

public static class PowerGuids
{
    // 子组 GUID
    public static readonly Guid ProcessorSubgroup = new("54533251-82BE-4824-96C1-47B60B740D00");
    public static readonly Guid HardDiskSubgroup = new("0012EE47-9041-4B5D-9B77-535FBA8B1442");
    public static readonly Guid SleepSubgroup = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
    public static readonly Guid UsbSubgroup = new("2A737441-1930-4402-8D77-B2BEBBA308A3");
    public static readonly Guid DisplaySubgroup = new("7516B95F-F776-4464-8C53-06167F40CC99");
    public static readonly Guid PciExpressSubgroup = new("501a4d13-42af-4429-9fd1-a8218c268e20");

    // 处理器设置
    public static readonly Guid ProcessorMinState = new("893DEE8E-2BEF-41E0-89C6-B55D0929964C");
    public static readonly Guid ProcessorMaxState = new("BC5038F7-23E0-4960-96DA-33ABAF5935EC");
    public static readonly Guid CoreParkingMin = new("0CC5B647-C1DF-4637-891A-DEC35C318583");
    public static readonly Guid CoreParkingMax = new("EA062031-0E34-4FF1-9B6D-EB1059334028");
    public static readonly Guid ProcessorBoostMode = new("BE337238-0D82-4146-A960-4F3749D470C7");
    public static readonly Guid ProcessorBoostPolicy = new("45BCC044-D885-43E2-8605-EE0EC6E96B59");

    // 高级处理器设置 (延迟优化关键参数)
    public static readonly Guid ProcessorPerfIncreaseThreshold = new("06cadf0e-64ed-448a-8927-ce7bf90eb35d");
    public static readonly Guid ProcessorIdleDemoteThreshold = new("4b92d758-5a24-4851-a470-815d78aee119");
    public static readonly Guid ProcessorPerfTimeCheck = new("4d2b0152-7d5c-498b-88e2-34345392a2c5");
    public static readonly Guid ProcessorPerfIncreaseTime = new("984cf492-3bed-4488-a8f9-11563ad23cdd");
    public static readonly Guid ProcessorPerfDecreaseTime = new("d8edeb9b-95cf-4f95-a73c-b061973693c8");
    public static readonly Guid ProcessorThrottlePolicy = new("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb");

    // 异类线程调度策略 (Intel 大小核)
    public static readonly Guid HeteroThreadSchedulingPolicy = new("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
    public static readonly Guid HeteroShortThreadSchedulingPolicy = new("bae08b81-2d5e-4688-ad6a-13243356654b");

    // 第一类处理器设置 (大核/P-Core)
    public static readonly Guid ProcessorClass1EfficiencyThreshold = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    public static readonly Guid ProcessorClass1PerfLevel = new("619b7505-003b-4e82-b7a6-4dd29c300971");

    // PCIe 设置
    public static readonly Guid PcieLinkStatePowerManagement = new("ee12f906-d166-11d0-87cf-00a0c9110001");

    // 硬盘设置
    public static readonly Guid HardDiskTurnOff = new("6738E2C4-E8A5-4A42-B16A-E040E769756E");

    // 睡眠设置
    public static readonly Guid SleepAfter = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
    public static readonly Guid HibernateAfter = new("9D7815A6-7EE4-497E-8888-515A05F02364");

    // USB 设置
    public static readonly Guid UsbSelectiveSuspend = new("48E6B7A6-50F5-4782-A5D4-53BB8F07E226"); // USB Selective Suspend Setting

    // 显示设置
    public static readonly Guid DisplayTurnOff = new("3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E");
}
