using System;
using FluentAssertions;
using Microsoft.Win32;
using TokenSpendie.Windows.Services.StartupAtLogin;
using Xunit;

namespace TokenSpendie.Windows.Tests.Services.StartupAtLogin;

public class RegistryRunKeyStartupServiceTests : IDisposable
{
    private readonly string _testKey = $@"Software\TokenSpendie.Tests.{Guid.NewGuid():N}\Run";

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_testKey, throwOnMissingSubKey: false); } catch { }
    }

    private RegistryRunKeyStartupService Make(string exePath = @"C:\fake\TokenSpendie.exe") =>
        new(_testKey, "TokenSpendie", exePath);

    [Fact]
    public void IsEnabledFalseWhenKeyMissing()
    {
        Make().IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void EnableWritesRegistryValue()
    {
        var svc = Make(@"C:\Apps\TokenSpendie.exe");
        svc.Enable();
        svc.IsEnabled().Should().BeTrue();

        using var key = Registry.CurrentUser.OpenSubKey(_testKey);
        var value = key?.GetValue("TokenSpendie") as string;
        value.Should().Contain("TokenSpendie.exe");
        value.Should().Contain("--hidden");
    }

    [Fact]
    public void DisableRemovesValue()
    {
        var svc = Make();
        svc.Enable();
        svc.Disable();
        svc.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void IsEnabledFalseIfPathMismatched()
    {
        var svc = Make(@"C:\NewPath\TokenSpendie.exe");
        using (var key = Registry.CurrentUser.CreateSubKey(_testKey))
            key!.SetValue("TokenSpendie", @"""C:\OldPath\TokenSpendie.exe"" --hidden");

        svc.IsEnabled().Should().BeFalse();
    }
}
