using Spectre.Console.Cli;

namespace NetProbe.Commands;

public sealed class ClientSettings : CommandSettings
{
    [CommandOption("--host <HOST>")]
    public string Host { get; set; } = "";
}

public sealed class ClientCommand : AsyncCommand<ClientSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ClientSettings settings)
    {
        await Task.CompletedTask;
        return 0;
    }
}
