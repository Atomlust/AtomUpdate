using System.Text.Json;
using AtomUpdate.Core.Enums;
using AtomUpdate.Core.Models;
using AtomUpdate.Core.Serialization;

namespace AtomUpdate.Tests.Core;

public class CheckContractJsonTests
{
    private static readonly JsonSerializerOptions Options = AtomUpdateJson.Create();

    [Fact]
    public void CheckRequest_serializes_with_camel_case_wire_names()
    {
        var request = new CheckRequest
        {
            ProjectKey = "myapp",
            AppId = "com.company.myapp",
            CurrentVersion = "1.2.3",
            BuildNumber = 100,
            Platform = UpdatePlatform.Windows,
            Arch = UpdateArch.X64,
            Channel = UpdateChannel.Stable,
            InstallationId = "2f1c9a3e-0b7a-4f5c-9d21-6a4b8c0d1e2f",
            MachineFingerprint = "sha256-hash",
            PolicyVersion = 12
        };

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"projectKey\":\"myapp\"", json);
        Assert.Contains("\"appId\":\"com.company.myapp\"", json);
        Assert.Contains("\"currentVersion\":\"1.2.3\"", json);
        Assert.Contains("\"buildNumber\":100", json);
        Assert.Contains("\"platform\":\"windows\"", json);
        Assert.Contains("\"arch\":\"x64\"", json);
        Assert.Contains("\"installationId\":\"2f1c9a3e-0b7a-4f5c-9d21-6a4b8c0d1e2f\"", json);
        Assert.Contains("\"policyVersion\":12", json);
    }

    [Fact]
    public void No_update_response_omits_null_members()
    {
        var response = new CheckResponse
        {
            HasUpdate = false,
            LatestVersion = "1.3.0",
            PolicyUnchanged = true
        };

        var json = JsonSerializer.Serialize(response, Options);

        Assert.Contains("\"hasUpdate\":false", json);
        Assert.Contains("\"latestVersion\":\"1.3.0\"", json);
        Assert.Contains("\"policyUnchanged\":true", json);
        Assert.DoesNotContain("\"forceUpdate\"", json);
        Assert.DoesNotContain("\"minSupportedVersion\"", json);
        Assert.DoesNotContain("\"policy\":", json);
        Assert.DoesNotContain("\"package\":", json);
        Assert.DoesNotContain("\"fullPackage\":", json);
        Assert.DoesNotContain("\"releaseNotes\"", json);
    }

    [Fact]
    public void Full_update_response_round_trips()
    {
        const string json = """
        {
          "hasUpdate": true,
          "latestVersion": "1.3.0",
          "forceUpdate": false,
          "minSupportedVersion": "1.0.0",
          "policyUnchanged": false,
          "policy": {
            "version": 13,
            "pollingEnabled": true,
            "pollingIntervalMinutes": 360,
            "pollingJitterMinutes": 10,
            "downloadOnStartup": true,
            "downloadOnPolling": false,
            "downloadOnManualCheck": true,
            "downloadOnForceUpdate": true,
            "downloadOnNetworkRecovery": false,
            "downloadOnResume": false,
            "installMode": "onRestart",
            "allowPostpone": true,
            "maxPostponeCount": 3,
            "allowMeteredNetwork": false,
            "maxDownloadSpeedKBps": 0,
            "updatedAt": "2026-10-06T00:00:00+00:00"
          },
          "package": {
            "type": "delta",
            "packageType": "zip",
            "arch": "x64",
            "fromVersion": "1.2.3",
            "url": "https://example.com/1.2.3-1.3.0.zip",
            "size": 1024,
            "sha256": "abc123"
          },
          "fullPackage": {
            "packageType": "exe",
            "arch": "x64",
            "url": "https://example.com/setup-1.3.0.exe",
            "sha256": "def456",
            "silentArgs": "/VERYSILENT /NORESTART"
          },
          "releaseNotes": { "new": ["新功能"], "improvements": [], "fixes": [], "notes": [] }
        }
        """;

        var response = JsonSerializer.Deserialize<CheckResponse>(json, Options);

        Assert.NotNull(response);
        Assert.True(response.HasUpdate);
        Assert.Equal("1.3.0", response.LatestVersion);
        Assert.False(response.ForceUpdate);
        Assert.Equal("1.0.0", response.MinSupportedVersion);
        Assert.False(response.PolicyUnchanged);

        Assert.NotNull(response.Policy);
        Assert.Equal(13, response.Policy.Version);
        Assert.Equal(InstallMode.OnRestart, response.Policy.InstallMode);
        Assert.Equal(360, response.Policy.PollingIntervalMinutes);
        Assert.True(response.Policy.AllowPostpone);

        Assert.NotNull(response.Package);
        Assert.Equal(AssetKind.Delta, response.Package.Type);
        Assert.Equal(PackageType.Zip, response.Package.PackageType);
        Assert.Equal(UpdateArch.X64, response.Package.Arch);
        Assert.Equal("1.2.3", response.Package.FromVersion);
        Assert.Null(response.Package.Signature);
        Assert.Null(response.Package.KeyId);

        Assert.NotNull(response.FullPackage);
        Assert.Null(response.FullPackage.Type);
        Assert.Equal(PackageType.Exe, response.FullPackage.PackageType);
        Assert.Equal(UpdateArch.X64, response.FullPackage.Arch);
        Assert.Equal("/VERYSILENT /NORESTART", response.FullPackage.SilentArgs);

        Assert.NotNull(response.ReleaseNotes);
        Assert.Equal(new[] { "新功能" }, response.ReleaseNotes.New);
        Assert.Empty(response.ReleaseNotes.Fixes);
    }

    [Fact]
    public void AppImage_package_type_uses_lowercase_member_name()
    {
        var json = JsonSerializer.Serialize(PackageType.AppImage, Options);

        Assert.Equal("\"appimage\"", json);
    }
}
