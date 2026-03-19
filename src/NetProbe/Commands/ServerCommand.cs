using System.ComponentModel;
using System.Net;
using NetProbe.Shared.Net;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NetProbe.Commands;

public sealed class ServerSettings : CommandSettings
{
    [CommandOption("--port <PORT>")]
    [Description("Port to listen on")]
    [DefaultValue(5555)]
    public int Port { get; set; } = 5555;

    [CommandOption("--bind <ADDRESS>")]
    [Description("Address to bind to")]
    [DefaultValue("0.0.0.0")]
    public string Bind { get; set; } = "0.0.0.0";

    [CommandOption("--protocol <PROTOCOL>")]
    [Description("Protocol to use (udp or tcp)")]
    [DefaultValue("udp")]
    public string Protocol { get; set; } = "udp";
}

public sealed class ServerCommand : AsyncCommand<ServerSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ServerSettings settings)
    {
        if (!IPAddress.TryParse(settings.Bind, out var bindAddress))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Invalid bind address: {0}", settings.Bind);
            return 1;
        }

        var protocol = settings.Protocol.ToLowerInvariant();
        if (protocol is not ("udp" or "tcp"))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Protocol must be 'udp' or 'tcp'.");
            return 1;
        }

        // Config panel
        var panel = new Panel(
            new Rows(
                new Markup($"[bold]Bind:[/] {settings.Bind}"),
                new Markup($"[bold]Port:[/] {settings.Port}"),
                new Markup($"[bold]Protocol:[/] {protocol.ToUpperInvariant()}")))
        {
            Header = new PanelHeader("[bold blue]NetProbe Server[/]"),
            Border = BoxBorder.Rounded,
        };
        AnsiConsole.Write(panel);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (protocol == "udp")
        {
            await using var server = new UdpProbeServer(bindAddress, settings.Port);
            var actualPort = server.Start();
            AnsiConsole.MarkupLine("[green]Listening on UDP port {0}[/]", actualPort);
            AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop.[/]");

            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            catch (OperationCanceledException) { }
        }
        else
        {
            await using var server = new TcpProbeServer(bindAddress, settings.Port);
            var actualPort = server.Start();
            AnsiConsole.MarkupLine("[green]Listening on TCP port {0}[/]", actualPort);
            AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop.[/]");

            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            catch (OperationCanceledException) { }
        }

        AnsiConsole.MarkupLine("\n[yellow]Server stopped.[/]");
        return 0;
    }
}
