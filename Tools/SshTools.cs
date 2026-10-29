using System.ComponentModel;
using ModelContextProtocol.Server;
using SshMcp.Services;

namespace SshMcp.Tools;

[McpServerToolType]
public sealed class SshTools(SshService ssh, ILogger<SshTools> logger)
{
    [McpServerTool, Description("Lists available SSH server profile names and descriptions.")]
    public string ListSshServers() => System.Text.Json.JsonSerializer.Serialize(ssh.ListServers());

    [McpServerTool, Description("Executes SSH command on remote server. Do NOT write 'sudo' prefix manually.")]
    public async Task<CommandResult> ExecuteCommand(
        [Description("Command to run.")] string command,
        [Description("Working directory.")] string? workingDirectory = null,
        [Description("Execute via server-side sudo with saved password.")] bool withSudo = false,
        [Description("SSH profile name (omit for default).")] string? server = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ssh.ExecuteAsync(server, command, workingDirectory, withSudo, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "SSH command failed: {Command}", command);
            return new CommandResult("", exception.Message, -1);
        }
    }

    [McpServerTool, Description("Uploads a file from the local machine to the remote server over SFTP. The model specifies paths; file contents are never passed through MCP.")]
    public async Task<string> UploadFile(
        [Description("Source path on local machine.")] string localPath,
        [Description("Destination path on remote server.")] string remotePath,
        [Description("Execute via server-side sudo with saved password.")] bool withSudo = false,
        [Description("SSH profile name (omit for default).")] string? server = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ssh.UploadAsync(server, localPath, remotePath, withSudo, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "SFTP upload failed: {LocalPath} -> {RemotePath}", localPath, remotePath);
            return $"Upload failed: {exception.Message}";
        }
    }

    [McpServerTool, Description("Downloads a file from the remote server to a local destination path over SFTP. The file is saved on the local machine; contents are never returned through MCP.")]
    public async Task<string> DownloadFile(
        [Description("Remote file path.")] string remotePath,
        [Description("Local destination path to save the file.")] string localPath,
        [Description("Execute via server-side sudo with saved password.")] bool withSudo = false,
        [Description("SSH profile name (omit for default).")] string? server = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ssh.DownloadAsync(server, remotePath, localPath, withSudo, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "SFTP download failed: {RemotePath} -> {LocalPath}", remotePath, localPath);
            return $"Download failed: {exception.Message}";
        }
    }
}
