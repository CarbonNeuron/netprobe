using NetProbe.Shared.Stats;

namespace NetProbe.UI;

public static class LiveDashboard
{
    public static async Task<StatsCollector> RunWithDashboardAsync(
        Func<Task<StatsCollector>> runFunc, int totalPackets)
    {
        // TODO: implement live dashboard in Task 10
        return await runFunc();
    }
}
