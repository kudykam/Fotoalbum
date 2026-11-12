using System.Security.Cryptography;
using System.Text;
using Fotoalbum.Core;
using Fotoalbum.Core.Ai;

namespace Fotoalbum.Desktop;

/// <summary>Klíč AI uložený šifrovaně (Windows DPAPI, jen pro přihlášeného uživatele tohoto počítače).</summary>
internal sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Fotoalbum-klic-v1");
    private readonly string _dir = new AppPaths().DataDir;

    private string FileOf(string name) => Path.Combine(_dir, "tajne-" + name + ".bin");

    public string? Get(string name)
    {
        try
        {
            var f = FileOf(name);
            if (!File.Exists(f)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(f), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException e) { Log.Error(e); return null; }
    }

    public void Set(string name, string? value)
    {
        var f = FileOf(name);
        if (string.IsNullOrWhiteSpace(value)) { if (File.Exists(f)) File.Delete(f); return; }
        File.WriteAllBytes(f, ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), Entropy, DataProtectionScope.CurrentUser));
    }
}
