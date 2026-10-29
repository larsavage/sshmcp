using System.Text;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshMcp.Configuration;

namespace SshMcp.Services;

public sealed class SshService(IOptions<SshOptions> options, ILogger<SshService> logger)
{
    private const string SudoPromptMarker = "__SSH_MCP_SUDO_PROMPT__";
    private const string SudoPromptLiteral = "\"__SSH_MCP_\"\"SUDO_PROMPT__\"";
    private const string ExitMarker = "__SSH_MCP_EXIT__:";

    private readonly SshOptions _options = options.Value;

    public IReadOnlyList<SshServerInfo> ListServers() => _options.Servers
        .Select(pair => new SshServerInfo(pair.Key, pair.Value.Description))
        .ToArray();

    public async Task<CommandResult> ExecuteAsync(string? server, string command, string? workingDirectory, bool withSudo, CancellationToken cancellationToken)
    {
        var settings = GetSettings(server);
        ValidateCommand(command, settings);
        if (withSudo && string.IsNullOrEmpty(settings.SudoPassword))
            throw new InvalidOperationException("Ssh:SudoPassword must be configured when withSudo is true.");

        using var client = CreateClient(settings);
        await ConnectAsync(client, settings, cancellationToken);

        var baseCommand = string.IsNullOrWhiteSpace(workingDirectory)
            ? command
            : $"cd -- {ShellQuote(workingDirectory)} && {command}";
        var commandToExecute = withSudo ? BuildSudoCommand(baseCommand) : baseCommand;

        var result = withSudo
            ? await ExecuteSudoCommandAsync(client, commandToExecute, settings, cancellationToken)
            : await Task.Run(() => ExecuteCommand(client, commandToExecute, settings), cancellationToken);

        logger.LogInformation("Executed SSH command on {Host}: {Command}", settings.Host, command);
        return result;
    }

    public async Task<string> UploadAsync(string? server, string localPath, string remotePath, bool withSudo, CancellationToken cancellationToken)
    {
        var settings = GetSettings(server);
        EnsureTransfersEnabled(settings);
        ValidateTransferPaths(localPath, remotePath);
        if (withSudo && string.IsNullOrEmpty(settings.SudoPassword))
            throw new InvalidOperationException("Ssh:SudoPassword must be configured when withSudo is true.");

        var source = Path.GetFullPath(localPath);
        if (!File.Exists(source))
            throw new FileNotFoundException($"Local file not found: {source}", source);

        using var client = CreateClient(settings);
        await ConnectAsync(client, settings, cancellationToken);

        var uploadPath = withSudo ? CreateRemoteTempPath() : remotePath;
        try
        {
            await Task.Run(() =>
            {
                using var sftp = new SftpClient(client.ConnectionInfo);
                sftp.Connect();
                using var stream = File.OpenRead(source);
                sftp.UploadFile(stream, uploadPath, true);
            }, cancellationToken);

            if (withSudo)
            {
                var move = $"mv -f -- {ShellQuote(uploadPath)} {ShellQuote(remotePath)}";
                var result = await ExecuteSudoCommandAsync(client, BuildSudoCommand(move), settings, cancellationToken);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"Remote move to '{remotePath}' failed (exit {result.ExitCode}): {result.Stderr}".Trim());
            }
        }
        finally
        {
            if (withSudo)
                await TryCleanupRemoteTempAsync(client, settings, uploadPath, cancellationToken);
        }

        var size = new FileInfo(source).Length;
        logger.LogInformation("Uploaded {LocalPath} to {Host}:{RemotePath}", source, settings.Host, remotePath);
        return $"Uploaded '{source}' to '{remotePath}' ({size} bytes).";
    }

    public async Task<string> DownloadAsync(string? server, string remotePath, string localPath, bool withSudo, CancellationToken cancellationToken)
    {
        var settings = GetSettings(server);
        EnsureTransfersEnabled(settings);
        ValidateTransferPaths(localPath, remotePath);
        if (withSudo && string.IsNullOrEmpty(settings.SudoPassword))
            throw new InvalidOperationException("Ssh:SudoPassword must be configured when withSudo is true.");

        var target = Path.GetFullPath(localPath);
        var targetDirectory = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        using var client = CreateClient(settings);
        await ConnectAsync(client, settings, cancellationToken);

        var downloadPath = withSudo ? CreateRemoteTempPath() : remotePath;
        try
        {
            if (withSudo)
            {
                var copy = $"cp -f -- {ShellQuote(remotePath)} {ShellQuote(downloadPath)}"
                    + $" && chown {ShellQuote(settings.Username)} {ShellQuote(downloadPath)}"
                    + $" && chmod 600 {ShellQuote(downloadPath)}";
                var result = await ExecuteSudoCommandAsync(client, BuildSudoCommand(copy), settings, cancellationToken);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"Remote copy of '{remotePath}' failed (exit {result.ExitCode}): {result.Stderr}".Trim());
            }

            await Task.Run(() =>
            {
                using var sftp = new SftpClient(client.ConnectionInfo);
                sftp.Connect();
                using var stream = File.Create(target);
                sftp.DownloadFile(downloadPath, stream);
            }, cancellationToken);
        }
        finally
        {
            if (withSudo)
                await TryCleanupRemoteTempAsync(client, settings, downloadPath, cancellationToken);
        }

        var size = new FileInfo(target).Length;
        logger.LogInformation("Downloaded {Host}:{RemotePath} to {LocalPath}", settings.Host, remotePath, target);
        return $"Downloaded '{remotePath}' to '{target}' ({size} bytes).";
    }

    private SshOptions GetSettings(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return _options;
        if (!_options.Servers.TryGetValue(server, out var profile))
            throw new InvalidOperationException($"Unknown SSH server profile '{server}'. Call list_ssh_servers to see available profiles.");
        return new SshOptions
        {
            Host = profile.Host, Port = profile.Port, Username = profile.Username,
            Password = profile.Password, PrivateKeyPath = profile.PrivateKeyPath,
            PrivateKeyPassphrase = profile.PrivateKeyPassphrase, SudoPassword = profile.SudoPassword,
            ConnectionTimeoutSeconds = profile.ConnectionTimeoutSeconds ?? _options.ConnectionTimeoutSeconds,
            CommandTimeoutSeconds = profile.CommandTimeoutSeconds ?? _options.CommandTimeoutSeconds,
            MaxOutputCharacters = profile.MaxOutputCharacters ?? _options.MaxOutputCharacters,
            AllowFileTransfer = profile.AllowFileTransfer ?? _options.AllowFileTransfer,
            AllowedCommandPrefixes = profile.AllowedCommandPrefixes ?? _options.AllowedCommandPrefixes
        };
    }

    private SshClient CreateClient(SshOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username))
            throw new InvalidOperationException("Ssh:Host and Ssh:Username must be configured.");

        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(settings.Password))
            methods.Add(new PasswordAuthenticationMethod(settings.Username, settings.Password));
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            var key = string.IsNullOrWhiteSpace(settings.PrivateKeyPassphrase)
                ? new PrivateKeyFile(settings.PrivateKeyPath)
                : new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(settings.Username, key));
        }
        if (methods.Count == 0) throw new InvalidOperationException("Configure Ssh:Password or Ssh:PrivateKeyPath.");

        var info = new Renci.SshNet.ConnectionInfo(settings.Host, settings.Port, settings.Username, methods.ToArray())
        { Timeout = TimeSpan.FromSeconds(settings.ConnectionTimeoutSeconds) };
        return new SshClient(info);
    }

    private async Task ConnectAsync(SshClient client, SshOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(client.Connect, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SshConnectionException(
                $"SSH connection to {settings.Host}:{settings.Port} as {settings.Username} failed: {exception.Message}",
                exception);
        }

        if (!client.IsConnected)
            throw new SshConnectionException($"SSH connection to {settings.Host}:{settings.Port} was not established.");
    }

    private CommandResult ExecuteCommand(SshClient client, string commandText, SshOptions settings)
    {
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = TimeSpan.FromSeconds(settings.CommandTimeoutSeconds);
        var stdout = command.Execute();
        var stderr = command.Error;
        return new CommandResult(Trim(stdout, settings), Trim(stderr, settings), command.ExitStatus ?? -1);
    }

    private static string BuildSudoCommand(string command) =>
        $"stty -echo 2>/dev/null; sudo -S -p {SudoPromptLiteral} -- sh -c {ShellQuote(command)}; sudoStatus=$?; stty echo 2>/dev/null; (exit $sudoStatus)";

    private async Task<CommandResult> ExecuteSudoCommandAsync(SshClient client, string commandText, SshOptions settings, CancellationToken cancellationToken)
    {
        using var shell = client.CreateShellStream("xterm", 120, 40, 800, 600, 4096);
        shell.WriteLine($"{commandText}; printf '{ExitMarker}%s\\n' $?");

        var output = new StringBuilder();
        var buffer = new byte[4096];
        var deadline = DateTime.UtcNow.AddSeconds(settings.CommandTimeoutSeconds);
        var passwordSent = false;
        var exitCode = -1;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (shell.DataAvailable)
            {
                var count = shell.Read(buffer, 0, buffer.Length);
                if (count <= 0) break;

                output.Append(Encoding.UTF8.GetString(buffer, 0, count));
                var text = output.ToString();

                if (!passwordSent && text.Contains(SudoPromptMarker, StringComparison.Ordinal))
                {
                    shell.WriteLine(settings.SudoPassword ?? "");
                    passwordSent = true;
                }

                var markerIndex = text.LastIndexOf(ExitMarker, StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    var codeStart = markerIndex + ExitMarker.Length;
                    var codeEnd = text.IndexOf('\n', codeStart);
                    if (codeEnd >= 0 && int.TryParse(text[codeStart..codeEnd].Trim(), out exitCode))
                    {
                        var cleanOutput = text[..markerIndex];
                        var stderr = cleanOutput.Contains("Sorry, try again", StringComparison.OrdinalIgnoreCase)
                            || cleanOutput.Contains("sudo:", StringComparison.OrdinalIgnoreCase)
                            ? cleanOutput
                            : "";
                        return new CommandResult(Trim(cleanOutput, settings), Trim(stderr, settings), exitCode);
                    }
                }
            }
            await Task.Delay(50, cancellationToken);
        }

        var diagnostic = output.Length == 0 ? "No output was received from the remote SSH shell." : Trim(output.ToString(), settings);
        throw new TimeoutException($"Sudo command exceeded the {settings.CommandTimeoutSeconds}-second timeout. Remote output: {diagnostic}");
    }

    private async Task TryCleanupRemoteTempAsync(SshClient client, SshOptions settings, string remotePath, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteSudoCommandAsync(client, BuildSudoCommand($"rm -f -- {ShellQuote(remotePath)}"), settings, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to remove temporary remote file {RemotePath}", remotePath);
        }
    }

    private static string CreateRemoteTempPath() => $"/tmp/ssh-mcp-{Guid.NewGuid():N}";

    private string Trim(string value, SshOptions settings) => value.Length <= settings.MaxOutputCharacters
        ? value
        : value[..settings.MaxOutputCharacters] + "\n[output truncated]";

    private static void ValidateCommand(string command, SshOptions settings)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("Command cannot be empty.", nameof(command));
        if (settings.AllowedCommandPrefixes.Length > 0 &&
            !settings.AllowedCommandPrefixes.Any(prefix => command.StartsWith(prefix, StringComparison.Ordinal)))
            throw new InvalidOperationException("This command is not allowed by Ssh:AllowedCommandPrefixes.");
    }

    private static void ValidateTransferPaths(string localPath, string remotePath)
    {
        if (string.IsNullOrWhiteSpace(localPath)) throw new ArgumentException("Local path is required.", nameof(localPath));
        if (string.IsNullOrWhiteSpace(remotePath)) throw new ArgumentException("Remote path is required.", nameof(remotePath));
    }

    private static void EnsureTransfersEnabled(SshOptions settings)
    {
        if (!settings.AllowFileTransfer) throw new InvalidOperationException("File transfer is disabled by configuration.");
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

public sealed record CommandResult(string Stdout, string Stderr, int ExitCode);
public sealed record SshServerInfo(string Name, string Description);
