using Humanizer;
using NetProbe.Shared.Stats;
using Spectre.Console;

namespace NetProbe.UI;

/// <summary>
/// Displays a live-updating dashboard during a probe test run.
/// </summary>
public static class LiveDashboard
{
    public static async Task<StatsCollector> RunWithDashboardAsync(
        Func<Task<StatsCollector>> runFunc, int totalPackets)
    {
        StatsCollector? collector = null;

        await AnsiConsole.Live(new Table()
            .AddColumn("Metric")
            .AddColumn("Value")
            .Border(TableBorder.Rounded)
            .Title("[bold blue]Live Stats[/]"))
            .StartAsync(async ctx =>
            {
                var task = runFunc();

                // Poll until complete
                while (!task.IsCompleted)
                {
                    await Task.Delay(100);
                    ctx.UpdateTarget(BuildTable(null, totalPackets));
                }

                collector = await task;
                ctx.UpdateTarget(BuildTable(collector, totalPackets));
            });

        return collector!;
    }

    private static Table BuildTable(StatsCollector? collector, int totalPackets)
    {
        var table = new Table()
            .AddColumn("Metric")
            .AddColumn("Value")
            .Border(TableBorder.Rounded)
            .Title("[bold blue]Live Stats[/]");

        if (collector is null)
        {
            table.AddRow("Status", "[yellow]Running...[/]");
            table.AddRow("Total to send", totalPackets.ToString());
            return table;
        }

        var lossColor = collector.LossPercentage switch
        {
            < 1 => "green",
            < 5 => "yellow",
            _ => "red"
        };

        table.AddRow("Sent", collector.TotalSent.ToString());
        table.AddRow("Received", collector.ReceivedCount.ToString());
        table.AddRow("Loss", $"[{lossColor}]{collector.LossPercentage:F1}%[/]");
        table.AddRow("Reordered", collector.ReorderedCount.ToString());
        table.AddRow("Jitter (RFC 3550)", $"{collector.CurrentJitter:F3} ms");

        return table;
    }
}
