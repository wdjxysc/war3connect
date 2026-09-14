using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using War3Connect.Core;

namespace War3Connect.Server;

public sealed record Account(string Id, string Name, string Salt, string Hash);
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed partial class Accounts
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Account> _accounts;
    public Accounts(IConfiguration config)
    {
        var directory = Path.GetFullPath(config["DataDirectory"] ?? "data");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "accounts.json");
        _accounts = File.Exists(_path) ? JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("账号文件损坏。") : [];
    }
    [GeneratedRegex("^[a-zA-Z0-9_\\p{IsCJKUnifiedIdeographs}]{2,20}$")]
    private static partial Regex NamePattern();

    public async Task<Account> Authenticate(Credentials c, bool register)
    {
        if (c.Username is null || !NamePattern().IsMatch(c.Username) || c.Password is null || c.Password.Length is < 8 or > 128)
            throw new ApiException(400, "用户名需为 2～20 位中英文、数字或下划线，密码需为 8～128 位。");
        await _gate.WaitAsync();
        try
        {
            var existing = _accounts.Find(a => a.Name.Equals(c.Username, StringComparison.OrdinalIgnoreCase));
            if (register)
            {
                if (existing != null) throw new ApiException(409, "用户名已被使用。");
                if (_accounts.Count >= 10000) throw new ApiException(503, "内测账号容量已满。");
                var salt = RandomNumberGenerator.GetBytes(16);
                var account = new Account(Guid.NewGuid().ToString("N"), c.Username,
                    Convert.ToBase64String(salt), Convert.ToBase64String(Hash(c.Password, salt)));
                var next = _accounts.Append(account).ToList();
                await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(next));
                File.Move(_path + ".tmp", _path, true);
                _accounts.Add(account);
                return account;
            }
            byte[] candidate = Hash(c.Password, existing == null ? new byte[16] : Convert.FromBase64String(existing.Salt));
            if (existing == null || !CryptographicOperations.FixedTimeEquals(candidate, Convert.FromBase64String(existing.Hash)))
                throw new ApiException(401, "用户名或密码错误。");
            return existing;
        }
        finally { _gate.Release(); }
    }
    private static byte[] Hash(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
}
