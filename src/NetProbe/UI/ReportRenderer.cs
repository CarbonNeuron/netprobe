using Humanizer;
using Humanizer.Bytes;
using NetProbe.Shared.Stats;
using Spectre.Console;

namespace NetProbe.UI;

/// <summary>
/// Renders the final test report using Spectre.Console tables and charts.
/// </summary>
public static class ReportRenderer
{
    public static void Render(TestReport report)
    {
        AnsiConsole.WriteLine();

        // Summary table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold blue]Test Results[/]")
            .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

        var lossColor = report.LossPercentage switch
        {
            < 1 => "green",
            < 5 => "yellow",
            _ => "red"
        };

        table.AddRow("Packets Sent", report.TotalSent.ToString());
        table.AddRow("Packets Received", report.TotalReceived.ToString());
        table.AddRow("Loss", $"[{lossColor}]{report.LossPercentage:F2}%[/]");
        table.AddEmptyRow();
        table.AddRow("Min RTT", FormatMs(report.MinRttMs));
        table.AddRow("Avg RTT", FormatMs(report.AvgRttMs));
        table.AddRow("Max RTT", FormatMs(report.MaxRttMs));
        table.AddRow("P95 RTT", FormatMs(report.P95RttMs));
        table.AddRow("P99 RTT", FormatMs(report.P99RttMs));
        table.AddEmptyRow();
        table.AddRow("Jitter (RFC 3550)", FormatMs(report.JitterMs));
        table.AddRow("Reordered", $"{report.ReorderedCount} ({report.ReorderedPercentage:F1}%)");

        if (report.ThroughputBytesPerSec > 0)
        {
            table.AddEmptyRow();
            table.AddRow("Throughput", ByteSize.FromBytes(report.ThroughputBytesPerSec).Per(TimeSpan.FromSeconds(1)).Humanize());
        }

        AnsiConsole.Write(table);

        // MTU result
        if (report.MtuResult is { } mtu)
        {
            AnsiConsole.WriteLine();
            var mtuTable = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold blue]MTU Probe Results[/]")
                .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

            mtuTable.AddRow("Max Application Payload", $"{mtu.MaxApplicationPayload} bytes");
            mtuTable.AddRow("Est. Max UDP Payload", $"{mtu.EstimatedMaxUdpPayload} bytes");
            mtuTable.AddRow("Est. Path MTU", $"{mtu.EstimatedPathMtu} bytes");
            mtuTable.AddRow("DontFragment", mtu.DontFragmentEnabled ? "[green]Enabled[/]" : "[yellow]Not available[/]");
            mtuTable.AddRow("Caveat", $"[dim]{Markup.Escape(mtu.Caveat)}[/]");

            AnsiConsole.Write(mtuTable);
        }

        // Latency distribution bar chart
        if (report.TotalReceived > 0)
        {
            AnsiConsole.WriteLine();
            RenderLatencyChart(report);
        }
    }

    private static void RenderLatencyChart(TestReport report)
    {
        var min = report.MinRttMs;
        var max = report.MaxRttMs;
        if (max - min < 0.01) return; // no meaningful distribution

        var chart = new BarChart()
            .Label("[bold blue]Latency Distribution[/]")
            .Width(60);

        chart.AddItem("Min", (int)Math.Max(1, report.MinRttMs * 100), Color.Green);
        chart.AddItem("Avg", (int)Math.Max(1, report.AvgRttMs * 100), Color.Blue);
        chart.AddItem("P95", (int)Math.Max(1, report.P95RttMs * 100), Color.Yellow);
        chart.AddItem("P99", (int)Math.Max(1, report.P99RttMs * 100), Color.Orange1);
        chart.AddItem("Max", (int)Math.Max(1, report.MaxRttMs * 100), Color.Red);

        AnsiConsole.Write(chart);
    }

    internal static string FormatMs(double ms)
    {
        return ms switch
        {
            < 1 => $"{ms * 1000:F0} us",
            < 1000 => $"{ms:F2} ms",
            _ => TimeSpan.FromMilliseconds(ms).Humanize(precision: 2),
        };
    }
}
