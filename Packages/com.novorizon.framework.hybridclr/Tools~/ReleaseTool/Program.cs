using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Game.Framework.HotUpdate;

static class Program
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "keygen") Keygen(args[1], args[2]);
            else if (args.Length == 5 && args[0] == "sign") Sign(args[1], args[2], args[3], args[4]);
            else if (args.Length == 3 && args[0] == "verify") Verify(args[1], args[2]);
            else throw new ArgumentException("Usage: keygen <external-directory> <key-id> | sign <unsigned-directory> <private.pem> <key-id> <new-output-directory> | verify <release.json> <public-key.json>");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }

    private static void RequireExternalPrivateKey(string path)
    {
        path = Path.GetFullPath(path);
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Packages", "manifest.json"))) directory = directory.Parent;
        if (directory != null)
            ReleasePolicy.Require(!path.StartsWith(directory.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase), "Keep signing private keys outside the Unity project.");
    }

    private static void Keygen(string directory, string keyId)
    {
        ReleasePolicy.Require(System.Text.RegularExpressions.Regex.IsMatch(keyId, @"\A[A-Za-z0-9_-]{1,64}\z"), "Invalid key ID.");
        string privatePath = Path.Combine(Path.GetFullPath(directory), keyId + ".private.pem");
        RequireExternalPrivateKey(privatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(privatePath));
        using var rsa = RSA.Create(3072);
        using (var stream = CreatePrivateKeyFile(privatePath))
        using (var writer = new StreamWriter(stream)) writer.Write(rsa.ExportPkcs8PrivateKeyPem());
        var key = PublicKey(rsa, keyId);
        File.WriteAllText(Path.Combine(directory, keyId + ".public.json"), JsonSerializer.Serialize(key, Json), new UTF8Encoding(false));
        Console.WriteLine("Created RSA-3072 keypair with owner-only private-key permissions. Put only the public JSON fields into trustedKeys.");
    }

    private static FileStream CreatePrivateKeyFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User;
            var security = new FileSecurity();
            security.SetOwner(owner);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.None, security);
        }
        return new FileStream(path, new FileStreamOptions
        { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
    }

    private static TrustedKey PublicKey(RSA rsa, string keyId)
    {
        var p = rsa.ExportParameters(false);
        return new TrustedKey { keyId = keyId, modulus = Convert.ToBase64String(p.Modulus), exponent = Convert.ToBase64String(p.Exponent) };
    }

    private static void Sign(string unsigned, string keyPath, string keyId, string output)
    {
        RequireExternalPrivateKey(keyPath);
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(ReleasePolicy.ReadBoundedText(Path.Combine(unsigned, "manifest.json")), Json);
        ReleasePolicy.Validate(manifest);
        foreach (var file in manifest.assemblies) ReleasePolicy.VerifyFile(Path.Combine(unsigned, file.fileName), file);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(keyPath));
        ReleasePolicy.Require(rsa.KeySize >= 3072, "RSA key must be at least 3072 bits.");
        var envelope = new SignedEnvelope { keyId = keyId, payload = Convert.ToBase64String(payload),
            signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
        ReleasePolicy.VerifyEnvelope(envelope, new[] { PublicKey(rsa, keyId) });
        string text = JsonSerializer.Serialize(envelope, Json);
        ReleasePolicy.Require(Encoding.UTF8.GetByteCount(text) <= ReleasePolicy.MaxManifestBytes, "Signed envelope is too large.");
        output = Path.GetFullPath(output);
        ReleasePolicy.Require(!Directory.Exists(output) && !File.Exists(output), "Output already exists. Releases are immutable.");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        string stage = output + ".staging-" + Guid.NewGuid().ToString("N");
        string id = ReleasePolicy.Sha256(payload);
        Directory.CreateDirectory(Path.Combine(stage, id));
        foreach (var file in manifest.assemblies)
        {
            string destination = Path.Combine(stage, id, file.fileName);
            File.Copy(Path.Combine(unsigned, file.fileName), destination);
            ReleasePolicy.VerifyFile(destination, file);
        }
        File.WriteAllText(Path.Combine(stage, "release.json"), text, new UTF8Encoding(false));
        Directory.Move(stage, output);
        Console.WriteLine("SIGNED_RELEASE " + output);
        Console.WriteLine("Upload the content-hash directory first; publish release.json last. Payload ID: " + id);
    }

    private static void Verify(string path, string keyPath)
    {
        var envelope = JsonSerializer.Deserialize<SignedEnvelope>(ReleasePolicy.ReadBoundedText(path), Json);
        var key = JsonSerializer.Deserialize<TrustedKey>(ReleasePolicy.ReadBoundedText(keyPath), Json);
        byte[] payload = ReleasePolicy.VerifyEnvelope(envelope, new[] { key });
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(payload, Json);
        ReleasePolicy.Validate(manifest);
        string id = ReleasePolicy.Sha256(payload);
        foreach (var file in manifest.assemblies) ReleasePolicy.VerifyFile(Path.Combine(Path.GetDirectoryName(path), id, file.fileName), file);
        Console.WriteLine("VERIFIED version=" + manifest.version + " baseline=" + manifest.baselineId);
    }
}
