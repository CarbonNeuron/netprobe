using Spectre.Console.Cli;

namespace NetProbe.Commands;

public sealed class ServerSettings : CommandSettings
{
}

public sealed class ServerCommand : AsyncCommand<ServerSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ServerSettings settings)
    {
        await Task.CompletedTask;
        return 0;
    }
}
