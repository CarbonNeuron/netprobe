using NetProbe.Shared.Stats;
using Spectre.Console;

namespace NetProbe.UI;

/// <summary>
/// Displays a live-updating dashboard during a probe test run.
/// </summary>
public static class LiveDashboard
{
    /// <summary>
    /// Runs the probe with a live-updating table (interactive terminal)
    /// or a simple periodic status line (non-interactive).
    /// </summary>
    public static async Task<StatsCollector> RunWithDashboardAsync(
        Func<StatsCollector, Task<StatsCollector>> runFunc,
        int totalPackets)
    {
        var collector = new StatsCollector(totalSent: totalPackets);

        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            // Non-interactive: simple periodic status lines
            var task = runFunc(collector);

            while (!task.IsCompleted)
            {
                await Task.Delay(500);
                var sent = collector.SentSoFar;
                var recv = collector.ReceivedCount;
                var loss = sent > 0 ? (sent - recv) * 100.0 / sent : 0;
                AnsiConsole.MarkupLine(
                    "[dim]{0}/{1} sent, {2} received, {3:F1}% loss[/]",
                    sent, totalPackets, recv, loss);
            }

            return await task;
        }

        // Interactive: live-updating table
        await AnsiConsole.Live(BuildTable(collector, totalPackets))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                var task = runFunc(collector);

                while (!task.IsCompleted)
                {
                    await Task.Delay(100);
                    ctx.UpdateTarget(BuildTable(collector, totalPackets));
                }

                await task;
                ctx.UpdateTarget(BuildTable(collector, totalPackets));
            });

        return collector;
    }

    private static Table BuildTable(StatsCollector collector, int totalPackets)
    {
        var table = new Table()
            .AddColumn("Metric")
            .AddColumn("Value")
            .Border(TableBorder.Rounded)
            .Title("[bold blue]Live Stats[/]");

        var sent = collector.SentSoFar;
        var recv = collector.ReceivedCount;

        table.AddRow("Progress", $"{sent} / {totalPackets}");
        table.AddRow("Received", recv.ToString());

        if (sent > 0)
        {
            var currentLoss = (sent - recv) * 100.0 / sent;
            var lossColor = currentLoss switch
            {
                < 1 => "green",
                < 5 => "yellow",
                _ => "red"
            };
            table.AddRow("Loss", $"[{lossColor}]{currentLoss:F1}%[/]");
        }
        else
        {
            table.AddRow("Loss", "[dim]—[/]");
        }

        table.AddRow("Reordered", collector.ReorderedCount.ToString());
        table.AddRow("Jitter (RFC 3550)", $"{collector.CurrentJitter:F3} ms");

        return table;
    }
}
