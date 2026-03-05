using System.Security.Cryptography;

namespace UvA.Workflow.Infrastructure;

public class EncryptionServiceConfig
{
    public static string SectionName = "Encryption";
    public required string Secret { get; set; }

    public string Salt { get; set; } =
        "9zoOLRzN0z"; // for extra security, the salt can be configured. For convenience it is set to a default value.
}

public interface IEncryptionService
{
    byte[] EncryptAes(byte[] plainBytes);
    byte[] DecryptAes(byte[] cipherBytes);
}

public class EncryptionService(IOptions<EncryptionServiceConfig> options) : IEncryptionService
{
    private const int Pbkdf2Iterations = 50_000;
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private static byte[]? _key;
    private byte[] Key => _key ??= DeriveKey();
    private readonly EncryptionServiceConfig config = options.Value;

    public byte[] EncryptAes(byte[] plainBytes)
    {
        Span<byte> nonce = stackalloc byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plainBytes.Length];
        byte[] tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(Key, tagSizeInBytes: TagSizeBytes);
        aesGcm.Encrypt(nonce, plainBytes, ciphertext, tag);

        byte[] output = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce.ToArray(), 0, output, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, output, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, output, NonceSizeBytes + TagSizeBytes, ciphertext.Length);
        return output;
    }

    public byte[] DecryptAes(byte[] cipherBytes)
    {
        if (cipherBytes.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Ciphertext too short");

        Span<byte> nonce = cipherBytes.AsSpan(0, NonceSizeBytes);
        Span<byte> tag = cipherBytes.AsSpan(NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> ciphertext = cipherBytes.AsSpan(NonceSizeBytes + TagSizeBytes);

        byte[] plaintext = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(Key, tagSizeInBytes: TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private byte[] DeriveKey()
    {
        if (string.IsNullOrEmpty(config.Secret))
            throw new InvalidOperationException("Encryption passphrase not set");
        if (string.IsNullOrEmpty(config.Salt))
            throw new InvalidOperationException("Encryption salt not set");

        byte[] salt = System.Text.Encoding.UTF8.GetBytes(config.Salt);
        return Rfc2898DeriveBytes.Pbkdf2(
            password: config.Secret,
            salt: salt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySizeBytes);
    }
}