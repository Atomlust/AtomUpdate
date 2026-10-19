using System.Security.Cryptography;
using System.Text;
using AtomUpdate.Updater;
using AtomUpdate.Updater.Configuration;
using AtomUpdate.Updater.Elevation;
using AtomUpdate.Updater.Logging;
using AtomUpdate.Updater.SelfUpdate;
using AtomUpdate.Updater.Verification;

namespace AtomUpdate.Tests.Updater;

public class PackageVerifierTests
{
    private static readonly UpdaterLog Silent = UpdaterLog.ConsoleOnly(UpdaterLogLevel.None);

    [Fact]
    public void ComputeSha256_KnownContent()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("file.bin", "hello");
        var expected = Convert.ToHexStringLower(SHA256.HashData("hello"u8.ToArray()));

        Assert.Equal(expected, PackageVerifier.ComputeSha256(workspace.Resolve("file.bin")));
    }

    [Fact]
    public void Verify_Sha256Match_Passes()
    {
        using var workspace = new TestWorkspace();
        var task = TaskWithPackage(workspace, "payload");

        PackageVerifier.Verify(task, Silent); // 不抛异常
    }

    [Fact]
    public void Verify_Sha256Mismatch_ThrowsVerificationFailed()
    {
        using var workspace = new TestWorkspace();
        var task = TaskWithPackage(workspace, "payload");
        task.Sha256 = new string('0', 64);

        var exception = Assert.Throws<UpdaterFailureException>(() => PackageVerifier.Verify(task, Silent));

        Assert.Equal(ExitCodes.VerificationFailed, exception.ExitCode);
    }

    [Fact]
    public void Verify_NoSha256_OnlyWarns()
    {
        using var workspace = new TestWorkspace();
        var task = TaskWithPackage(workspace, "payload");
        task.Sha256 = null;

        PackageVerifier.Verify(task, Silent); // 不抛异常
    }

    [Fact]
    public void Verify_SignatureRoundtrip_Passes()
    {
        using var workspace = new TestWorkspace();
        var task = TaskWithPackage(workspace, "payload");
        using var rsa = RSA.Create(2048);
        task.Verification = new VerificationSection
        {
            RequireSignature = true,
            PublicKeys = { ["k1"] = rsa.ExportSubjectPublicKeyInfoPem() }
        };
        task.KeyId = "k1";
        var content = Encoding.UTF8.GetBytes(PackageVerifier.SignatureContent(task));
        task.Signature = Convert.ToBase64String(
            rsa.SignData(content, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        PackageVerifier.Verify(task, Silent); // 不抛异常
    }

    [Fact]
    public void Verify_SignatureTampered_Throws()
    {
        using var workspace = new TestWorkspace();
        var task = TaskWithPackage(workspace, "payload");
        using var rsa = RSA.Create(2048);
        task.Verification = new VerificationSection
        {
            RequireSignature = true,
            PublicKeys = { ["k1"] = rsa.ExportSubjectPublicKeyInfoPem() }
        };
        task.KeyId = "k1";
        task.Signature = Convert.ToBase64String(new byte[256]);

        var exception = Assert.Throws<UpdaterFailureException>(() => PackageVerifier.Verify(task, Silent));

        Assert.Equal(ExitCodes.VerificationFailed, exception.ExitCode);
    }

    [Fact]
    public void Verify_RequireSignatureButUnsigned_Throws()
    {
        using var workspace = new TestWorkspace();
        var task = TaskWithPackage(workspace, "payload");
        task.Verification = new VerificationSection { RequireSignature = true };

        var exception = Assert.Throws<UpdaterFailureException>(() => PackageVerifier.Verify(task, Silent));

        Assert.Equal(ExitCodes.VerificationFailed, exception.ExitCode);
    }

    [Fact]
    public void Verify_RequireSignatureWithUnknownKeyId_Throws()
    {
        using var workspace = new TestWorkspace();
        var task = TaskWithPackage(workspace, "payload");
        task.Verification = new VerificationSection { RequireSignature = true };
        task.KeyId = "unknown";
        task.Signature = Convert.ToBase64String(new byte[256]);

        var exception = Assert.Throws<UpdaterFailureException>(() => PackageVerifier.Verify(task, Silent));

        Assert.Equal(ExitCodes.VerificationFailed, exception.ExitCode);
        Assert.Contains("未找到受信公钥", exception.Message);
    }

    [Fact]
    public void SignatureContent_LowercaseFields()
    {
        var task = new UpdaterTaskFile
        {
            Sha256 = "ABCDEF",
            ToVersion = "1.2.3",
            Platform = "Windows",
            Arch = "X64"
        };

        Assert.Equal("abcdef|1.2.3|windows|x64", PackageVerifier.SignatureContent(task));
    }

    private static UpdaterTaskFile TaskWithPackage(TestWorkspace workspace, string content)
    {
        workspace.WriteFile("package.zip", content);
        var path = workspace.Resolve("package.zip");
        return new UpdaterTaskFile
        {
            PackagePath = path,
            Sha256 = PackageVerifier.ComputeSha256(path),
            ToVersion = "1.0.1",
            Platform = "windows",
            Arch = "x64"
        };
    }
}

public class SelfUpdateVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0+build5", "1.0.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", 0)]
    public void CompareVersions_Sign(string left, string right, int expectedSign) =>
        Assert.Equal(expectedSign, Math.Sign(SelfUpdater.CompareVersions(left, right)));
}

public class UpdaterLogSanitizeTests
{
    [Theory]
    [InlineData("apikey=abc123 rest", "apikey=*** rest")]
    [InlineData("api_key=xyz&x=1", "api_key=***&x=1")]
    [InlineData("token=zzz", "token=***")]
    [InlineData("password=p@ss", "password=***")]
    [InlineData("no secrets here", "no secrets here")]
    public void Sanitize_MasksSensitiveValues(string input, string expected) =>
        Assert.Equal(expected, UpdaterLog.Sanitize(input));
}

public class ElevationServiceTests
{
    [Fact]
    public void NeedsElevation_ModeNever_ReturnsFalse()
    {
        using var workspace = new TestWorkspace();
        var task = new UpdaterTaskFile { Elevation = new ElevationSection { Mode = ElevationModes.Never } };

        Assert.False(ElevationService.NeedsElevation(task, workspace.Root, UpdaterLog.ConsoleOnly(UpdaterLogLevel.None)));
    }
}
