using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SshMcp.Configuration;
using SshMcp.Services;
using SshMcp.Tools;

var mode = Environment.GetEnvironmentVariable("SSH_MCP_TRANSPORT")?.Trim().ToLowerInvariant();
mode ??= args.Contains("--stdio", StringComparer.OrdinalIgnoreCase) ? "stdio" : "http";

if (mode is not ("stdio" or "http"))
{
    Console.Error.WriteLine("SSH_MCP_TRANSPORT must be 'stdio' or 'http'.");
    return 2;
}

if (mode == "stdio")
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    AddSettingsFile(builder.Configuration);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    ConfigureServices(builder.Services, builder.Configuration);
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<SshTools>();
    await builder.Build().RunAsync();
    return 0;
}

var webBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
AddSettingsFile(webBuilder.Configuration);
ConfigureServices(webBuilder.Services, webBuilder.Configuration);
webBuilder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<SshTools>();

var app = webBuilder.Build();
app.MapMcp();
await app.RunAsync();
return 0;

static void AddSettingsFile(IConfigurationBuilder configuration)
{
    var index = configuration.Sources.Count;
    configuration.AddJsonFile("sshmcp.settings.json", optional: true, reloadOnChange: true);

    var source = configuration.Sources[index];
    configuration.Sources.RemoveAt(index);

    var environmentIndex = -1;
    for (var i = 0; i < configuration.Sources.Count; i++)
    {
        if (configuration.Sources[i] is EnvironmentVariablesConfigurationSource)
        {
            environmentIndex = i;
            break;
        }
    }

    if (environmentIndex >= 0)
        configuration.Sources.Insert(environmentIndex, source);
    else
        configuration.Sources.Add(source);
}

static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<SshOptions>(configuration.GetSection(SshOptions.SectionName));
    services.AddSingleton<SshService>();
}
