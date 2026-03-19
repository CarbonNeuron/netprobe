using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using NetProbe.Shared.Net;
using NetProbe.Shared.Stats;
using NetProbe.UI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NetProbe.Commands;

public sealed class ClientSettings : CommandSettings
{
    [CommandOption("--host <HOST>")]
    [Description("Server host address (required)")]
    public string Host { get; set; } = "";

    [CommandOption("--port <PORT>")]
    [Description("Server port")]
    [DefaultValue(5555)]
    public int Port { get; set; } = 5555;

    [CommandOption("--protocol <PROTOCOL>")]
    [Description("Protocol to use (udp or tcp)")]
    [DefaultValue("udp")]
    public string Protocol { get; set; } = "udp";

    [CommandOption("--count <COUNT>")]
    [Description("Number of packets to send")]
    [DefaultValue(1000)]
    public int Count { get; set; } = 1000;

    [CommandOption("--interval <MS>")]
    [Description("Delay between packets in milliseconds")]
    [DefaultValue(10)]
    public int Interval { get; set; } = 10;

    [CommandOption("--payload-size <BYTES>")]
    [Description("Payload size in bytes")]
    [DefaultValue(64)]
    public int PayloadSize { get; set; } = 64;

    [CommandOption("--mtu-probe")]
    [Description("Enable MTU discovery mode (UDP only)")]
    [DefaultValue(false)]
    public bool MtuProbe { get; set; }

    [CommandOption("--timeout <SECONDS>")]
    [Description("Max wait for responses in seconds")]
    [DefaultValue(30)]
    public int Timeout { get; set; } = 30;

    [CommandOption("--json")]
    [Description("Output results as JSON")]
    [DefaultValue(false)]
    public bool Json { get; set; }
}

public sealed class ClientCommand : AsyncCommand<ClientSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ClientSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --host is required.");
            return 1;
        }

        var protocol = settings.Protocol.ToLowerInvariant();
        if (protocol is not ("udp" or "tcp"))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Protocol must be 'udp' or 'tcp'.");
            return 1;
        }

        if (settings.MtuProbe && protocol != "udp")
        {
            AnsiConsole.MarkupLine("[red]Error:[/] MTU probing is only supported with UDP.");
            return 1;
        }

        IPAddress serverAddress;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(settings.Host);
            serverAddress = addresses.First();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Cannot resolve host '{0}': {1}", settings.Host, ex.Message);
            return 1;
        }

        if (!settings.Json)
        {
            var panel = new Panel(
                new Rows(
                    new Markup($"[bold]Host:[/] {Markup.Escape(settings.Host)} ({serverAddress})"),
                    new Markup($"[bold]Port:[/] {settings.Port}"),
                    new Markup($"[bold]Protocol:[/] {protocol.ToUpperInvariant()}"),
                    new Markup($"[bold]Count:[/] {settings.Count}"),
                    new Markup($"[bold]Interval:[/] {settings.Interval}ms"),
                    new Markup($"[bold]Payload:[/] {settings.PayloadSize} bytes"),
                    new Markup($"[bold]Timeout:[/] {settings.Timeout}s"),
                    new Markup($"[bold]MTU Probe:[/] {(settings.MtuProbe ? "Yes" : "No")}")))
            {
                Header = new PanelHeader("[bold blue]NetProbe Client[/]"),
                Border = BoxBorder.Rounded,
            };
            AnsiConsole.Write(panel);
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        MtuProbeResult? mtuResult = null;
        if (settings.MtuProbe)
        {
            if (!settings.Json)
                AnsiConsole.MarkupLine("\n[bold]Running MTU probe...[/]");

            var prober = new MtuProber(serverAddress, settings.Port);
            mtuResult = await prober.ProbeAsync(cts.Token);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("  Max application payload: [bold]{0}[/] bytes", mtuResult.MaxApplicationPayload);
                AnsiConsole.MarkupLine("  Estimated max UDP payload: [bold]{0}[/] bytes", mtuResult.EstimatedMaxUdpPayload);
                AnsiConsole.MarkupLine("  Estimated path MTU: [bold]{0}[/] bytes", mtuResult.EstimatedPathMtu);
                AnsiConsole.MarkupLine("  [dim]{0}[/]", Markup.Escape(mtuResult.Caveat));
            }
        }

        var sw = Stopwatch.StartNew();
        StatsCollector collector;

        if (protocol == "udp")
        {
            var client = new UdpProbeClient(serverAddress, settings.Port);

            if (settings.Json)
            {
                collector = await client.RunAsync(settings.Count, settings.Interval, settings.PayloadSize, settings.Timeout, cts.Token);
            }
            else
            {
                collector = await LiveDashboard.RunWithDashboardAsync(
                    c => client.RunAsync(settings.Count, settings.Interval, settings.PayloadSize, settings.Timeout, cts.Token, c),
                    settings.Count);
            }
        }
        else
        {
            var client = new TcpProbeClient(serverAddress, settings.Port);

            if (settings.Json)
            {
                collector = await client.RunAsync(settings.Count, settings.Interval, settings.PayloadSize, settings.Timeout, cts.Token);
            }
            else
            {
                collector = await LiveDashboard.RunWithDashboardAsync(
                    c => client.RunAsync(settings.Count, settings.Interval, settings.PayloadSize, settings.Timeout, cts.Token, c),
                    settings.Count);
            }
        }

        sw.Stop();
        var report = TestReport.FromCollector(collector, sw.Elapsed.TotalSeconds, mtuResult);

        if (settings.Json)
        {
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
        }
        else
        {
            ReportRenderer.Render(report);
        }

        return 0;
    }
}
