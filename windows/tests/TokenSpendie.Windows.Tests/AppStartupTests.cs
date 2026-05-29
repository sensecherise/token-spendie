using FluentAssertions;
using NSubstitute;
using TokenSpendie.Windows;
using TokenSpendie.Windows.Services.StartupAtLogin;
using Xunit;

namespace TokenSpendie.Windows.Tests;

public class AppStartupTests
{
    [Fact]
    public void ReconcileEnablesWhenPrefsWantItButRegistryDoesnt()
    {
        var startup = Substitute.For<IStartupAtLoginService>();
        startup.IsEnabled().Returns(false);

        StartupReconciler.Reconcile(preferenceWantsEnabled: true, startup);

        startup.Received(1).Enable();
        startup.DidNotReceive().Disable();
    }

    [Fact]
    public void ReconcileDisablesWhenPrefsDontButRegistryDoes()
    {
        var startup = Substitute.For<IStartupAtLoginService>();
        startup.IsEnabled().Returns(true);

        StartupReconciler.Reconcile(preferenceWantsEnabled: false, startup);

        startup.Received(1).Disable();
        startup.DidNotReceive().Enable();
    }

    [Fact]
    public void ReconcileDoesNothingWhenAlreadyAligned()
    {
        var trueAligned = Substitute.For<IStartupAtLoginService>();
        trueAligned.IsEnabled().Returns(true);
        StartupReconciler.Reconcile(preferenceWantsEnabled: true, trueAligned);
        trueAligned.DidNotReceive().Enable();
        trueAligned.DidNotReceive().Disable();

        var falseAligned = Substitute.For<IStartupAtLoginService>();
        falseAligned.IsEnabled().Returns(false);
        StartupReconciler.Reconcile(preferenceWantsEnabled: false, falseAligned);
        falseAligned.DidNotReceive().Enable();
        falseAligned.DidNotReceive().Disable();
    }
}
