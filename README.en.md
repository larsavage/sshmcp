# SshMcp - MCP Server for Computer Control over SSH

[Русская версия](README.md)

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![MCP Server](https://img.shields.io/badge/MCP-Server-_active-brightgreen.svg)](https://modelcontextprotocol.io/)
[![SSH.NET](https://img.shields.io/badge/SSH.NET-2026.0.0-blue.svg)](https://github.com/sshnet/SSH.NET)

## Project Description

**SshMcp** is a Model Context Protocol (MCP) server that allows an MCP client, such as LM Studio, to control a remote computer through SSH and SFTP.

The service can run as a local console application using the `stdio` transport or as an ASP.NET Core application using the HTTP transport. The remote connection is configured on the server side, so the model receives MCP tools without direct access to SSH secrets.

## Key Features

- **SSH command execution** — run shell commands on a remote computer
- **Working directory support** — optionally change the remote working directory before execution
- **File upload over SFTP** — read a file from the local disk by path and send it to the server
- **File download over SFTP** — save a remote file to a local disk path
- **Password authentication** — SSH password authentication support
- **Private-key authentication** — private keys and passphrases are supported
- **Two MCP transports** — `stdio` for LM Studio and `HTTP` for remote clients
- **Multiple SSH servers** — preconfigured profiles selected with the `server` parameter
- **Command restrictions** — configure allowed command prefixes
- **Output size limits** — prevent excessively large model responses
- **Timeouts** — separate SSH connection and command execution timeouts
- **File transfer control** — disable SFTP operations when needed
- **JSON and environment configuration** — keep secrets outside configuration files
- **Self-contained publishing** — run without installing the .NET Runtime

## Architecture

```text
SshMcp/
├── Configuration/
│   └── SshOptions.cs        # SSH connection and limit settings
├── Services/
│   └── SshService.cs        # SSH/SFTP connection and operations
├── Tools/
│   └── SshTools.cs          # MCP tools
├── sshmcp.settings.json.example # Configuration example
├── Program.cs               # Transport selection, DI, and startup
├── SshMcp.csproj
└── README.md / README.en.md
```

## MCP Server Tools

### SSH Tools

| Tool name | Description | Parameters |
|---|---|---|
| `execute_command` | Execute a shell command on the remote computer. When `withSudo=true`, the command is automatically run with elevated privileges — do not add `sudo` to `command` | `command`, optional `workingDirectory`, optional `withSudo` |
| `list_ssh_servers` | List available profiles without secrets or connection details | none |
| `upload_file` | Upload a file from the local disk to the remote computer over SFTP | `localPath`, `remotePath`, optional `withSudo`, optional `server` |
| `download_file` | Download a file from the remote computer to a local disk path over SFTP | `remotePath`, `localPath`, optional `withSudo`, optional `server` |

`execute_command` returns:

```json
{
  "stdout": "...",
  "stderr": "...",
  "exitCode": 0
}
```

File contents are never passed through MCP: the model provides only local and remote paths, and the server reads and writes the files itself. Large files are streamed over SFTP.

### File Transfers

Upload a file to the server:

```json
{
  "name": "upload_file",
  "arguments": {
    "localPath": "C:\\temp\\nginx.conf",
    "remotePath": "/etc/nginx/nginx.conf",
    "withSudo": true,
    "server": "production"
  }
}
```

Download a file from the server:

```json
{
  "name": "download_file",
  "arguments": {
    "remotePath": "/var/log/nginx/error.log",
    "localPath": "C:\\temp\\error.log",
    "withSudo": true,
    "server": "production"
  }
}
```

With `withSudo: true` a temporary file in `/tmp` is used:

- upload: SFTP writes the file to `/tmp/ssh-mcp-<id>`, then `sudo mv` moves it to `remotePath`;
- download: `sudo cp` creates a copy in `/tmp/ssh-mcp-<id>` and changes its owner to the SSH user, then SFTP downloads it and the temporary file is removed with `sudo rm`.

Do not add `sudo` to paths or commands — the tool elevates privileges itself.

## Security

1. **SSH secrets stay on the server** — passwords and private keys are not MCP tool parameters.
2. **Command restrictions** — when `AllowedCommandPrefixes` is configured, a command must start with one of the listed prefixes.
3. **Execution time limits** — long-running commands are stopped after `CommandTimeoutSeconds`.
4. **Output size limits** — responses are truncated to `MaxOutputCharacters`.
5. **File transfer control** — `AllowFileTransfer: false` disables `upload_file` and `download_file`.
6. **Working-directory quoting** — the working directory is escaped before being inserted into the command.
7. **Local HTTP by default** — LM Studio deployments should listen on `127.0.0.1` instead of exposing the service publicly.
8. **TLS and authentication for public HTTP** — use an HTTPS reverse proxy and additional authentication when exposing HTTP over a network.
9. **The sudo password is not exposed to the model** — with `withSudo: true`, the password is read only from `Ssh:SudoPassword` and sent to the SSH command through stdin; it is not inserted into the shell command text.
10. **Profiles do not expose secrets** — `list_ssh_servers` returns only profile names and descriptions.
11. **Local filesystem access** — `upload_file` can read any file accessible on the MCP host, and `download_file` can write to any local path it can reach. Run the service under a dedicated low-privilege account and do not expose it to untrusted clients.

> `execute_command` effectively grants the model the permissions of the configured SSH account. Use a dedicated account with the minimum required permissions and configure `AllowedCommandPrefixes` when unrestricted shell access is not needed.

## Usage

### Configure the server

Copy the example configuration:

```powershell
Copy-Item sshmcp.settings.json.example sshmcp.settings.json
```

Fill in the `Ssh` section or provide the values through environment variables.

### Run through stdio for LM Studio

```powershell
$env:SSH_MCP_TRANSPORT = "stdio"
$env:Ssh__Host = "192.168.1.10"
$env:Ssh__Port = "22"
$env:Ssh__Username = "remote-user"
$env:Ssh__PrivateKeyPath = "C:\Users\me\.ssh\id_ed25519"

dotnet run -c Release -- --stdio
```

To run the published application:

```powershell
$env:SSH_MCP_TRANSPORT = "stdio"
& ".\bin\Release\net10.0\win-x64\publish\SshMcp.exe" --stdio
```

Configure the MCP client with the executable path and the `--stdio` argument. Example configuration:

```json
{
  "mcpServers": {
    "ssh-remote": {
      "command": ".\\bin\\Release\\net10.0\\win-x64\\publish\\SshMcp.exe",
      "args": ["--stdio"],
      "env": {
        "SSH_MCP_TRANSPORT": "stdio",
        "Ssh__Host": "192.168.1.10",
        "Ssh__Port": "22",
        "Ssh__Username": "remote-user",
        "Ssh__PrivateKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519"
      }
    }
  }
}
```

MCP JSON-RPC messages are exchanged through `stdin/stdout`. Application diagnostics are written to `stderr`.

### Run through HTTP

```powershell
$env:SSH_MCP_TRANSPORT = "http"
$env:Ssh__Host = "192.168.1.10"
$env:Ssh__Username = "remote-user"
$env:Ssh__PrivateKeyPath = "C:\Users\me\.ssh\id_ed25519"

dotnet run -c Release -- --urls http://127.0.0.1:6258
```

MCP endpoint:

```text
http://127.0.0.1:6258/mcp
```

Example HTTP client configuration:

```json
{
  "mcpServers": {
    "ssh-remote": {
      "url": "http://127.0.0.1:6258/mcp"
    }
  }
}
```

## Command-Line Arguments and Modes

| Argument/variable | Description |
|---|---|
| `--stdio` | Starts the MCP stdio transport when `SSH_MCP_TRANSPORT` is not set |
| `SSH_MCP_TRANSPORT=stdio` | Explicitly enables the stdio transport |
| `SSH_MCP_TRANSPORT=http` | Enables the HTTP transport |
| `--urls http://127.0.0.1:6258` | ASP.NET Core address and port for HTTP mode |

When `SSH_MCP_TRANSPORT` is not set and `--stdio` is not specified, HTTP mode is used.

## Configuration

The service reads the `Ssh` section from `sshmcp.settings.json`, environment variables, and the standard .NET configuration sources. The `sshmcp.settings.json` file is loaded from the directory containing `SshMcp.exe` and takes precedence over `appsettings.json`; environment variables override the file.

```json
{
  "Ssh": {
    "Host": "192.168.1.10",
    "Port": 22,
    "Username": "remote-user",
    "Password": "",
    "PrivateKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519",
    "PrivateKeyPassphrase": "",
    "SudoPassword": "",
    "ConnectionTimeoutSeconds": 15,
    "CommandTimeoutSeconds": 60,
    "MaxOutputCharacters": 100000,
    "AllowFileTransfer": true,
    "AllowedCommandPrefixes": [
      "uname",
      "whoami",
      "systemctl status"
    ],
    "Servers": {
      "production": {
        "Description": "Production Linux server",
        "Host": "192.168.1.20",
        "Port": 22,
        "Username": "deploy",
        "PrivateKeyPath": "C:\\Users\\me\\.ssh\\id_ed25519",
        "SudoPassword": ""
      }
    }
  }
}
```

Parameters:

- `Host` — remote SSH server name or IP address
- `Port` — SSH port, defaults to `22`
- `Username` — SSH username
- `Password` — SSH password; environment variables are recommended
- `PrivateKeyPath` — path to the private key
- `PrivateKeyPassphrase` — private-key passphrase
- `SudoPassword` — sudo password; used only when `withSudo: true`, preferably provided through ENV or Secret Manager
- `ConnectionTimeoutSeconds` — connection timeout, defaults to `15`
- `CommandTimeoutSeconds` — command execution timeout, defaults to `60`
- `MaxOutputCharacters` — maximum `stdout` and `stderr` size, defaults to `100000`
- `AllowFileTransfer` — enables or disables SFTP tools
- `AllowedCommandPrefixes` — allowed command prefixes; an empty array permits commands without prefix filtering
- `Servers` — dictionary of preconfigured profiles. The key (`production`) is passed through the `server` parameter of MCP tools
- `Servers.<name>.Description` — safe description returned by `list_ssh_servers`
- Profile parameters (`Host`, `Port`, `Username`, `Password`, `PrivateKeyPath`, `SudoPassword`, and limits) override the corresponding root `Ssh` values

### Selecting an SSH Server

The model first calls:

```json
{
  "name": "list_ssh_servers",
  "arguments": {}
}
```

It then passes the selected profile name to each tool call:

```json
{
  "name": "execute_command",
  "arguments": {
    "server": "production",
    "command": "systemctl status nginx",
    "withSudo": true
  }
}
```

### Environment Variables

Double underscores `__` are used as .NET configuration section separators:

| JSON parameter | Environment variable |
|---|---|
| `Ssh:Host` | `Ssh__Host` |
| `Ssh:Port` | `Ssh__Port` |
| `Ssh:Username` | `Ssh__Username` |
| `Ssh:Password` | `Ssh__Password` |
| `Ssh:PrivateKeyPath` | `Ssh__PrivateKeyPath` |
| `Ssh:PrivateKeyPassphrase` | `Ssh__PrivateKeyPassphrase` |
| `Ssh:SudoPassword` | `Ssh__SudoPassword` |
| `Ssh:AllowFileTransfer` | `Ssh__AllowFileTransfer` |

Environment variables take precedence over `sshmcp.settings.json`. Do not commit real passwords or passphrases to Git.

## Build and Publish

### Regular build

```powershell
dotnet restore
dotnet build -c Release
```

### Self-contained Windows x64 publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The output will be placed in:

```text
bin\Release\net10.0\win-x64\publish\
```

### Other platforms

The project includes the following Runtime Identifiers:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `linux-musl-x64`
- `osx-arm64`

Example Linux publish:

```bash
dotnet publish -c Release -r linux-x64 --self-contained true
```

## Testing

Build verification:

```powershell
dotnet build -c Release
```

stdio startup verification without an SSH connection:

```powershell
$env:SSH_MCP_TRANSPORT = "stdio"
dotnet run -c Release -- --stdio
```

The process should start and wait for MCP messages on standard input. To verify SSH functionality, configure a working connection and invoke `execute_command` through an MCP client.

## License

This project is distributed under the [MIT License](LICENSE.txt).

The complete license text is available in [`LICENSE.txt`](LICENSE.txt). The license permits using, copying, modifying, publishing, and distributing the project, provided that the copyright notice and license text are retained.

## Technologies

- [.NET 10](https://dotnet.microsoft.com/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)
- [SSH.NET](https://github.com/sshnet/SSH.NET)
- [ASP.NET Core](https://learn.microsoft.com/aspnet/core/)

## Links

- [Model Context Protocol](https://modelcontextprotocol.io/) — official MCP website
- [C# MCP SDK](https://csharp.sdk.modelcontextprotocol.io/) — MCP SDK documentation for .NET
- [LM Studio](https://lmstudio.ai/) — local model client and runtime
- [SSH.NET](https://github.com/sshnet/SSH.NET) — SSH/SFTP library for .NET

---

**Version:** 0.1.0-beta  
**Last updated:** 2026
