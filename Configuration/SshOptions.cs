namespace SshMcp.Configuration;

public sealed class SshOptions
{
    public const string SectionName = "Ssh";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string? SudoPassword { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 60;
    public int MaxOutputCharacters { get; set; } = 100_000;
    public bool AllowFileTransfer { get; set; } = true;
    public string[] AllowedCommandPrefixes { get; set; } = [];
    public Dictionary<string, SshServerProfile> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SshServerProfile
{
    public string Description { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string? SudoPassword { get; set; }
    public int? ConnectionTimeoutSeconds { get; set; }
    public int? CommandTimeoutSeconds { get; set; }
    public int? MaxOutputCharacters { get; set; }
    public bool? AllowFileTransfer { get; set; }
    public string[]? AllowedCommandPrefixes { get; set; }
}
