using Microsoft.Win32;
using Xunit;

namespace BigClock.Tests;

public sealed class StartupRegistrationTests : IDisposable
{
    private const string KeyPath = @"Software\BigClockTests";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);

    [Fact]
    public void Enable_and_disable_round_trip()
    {
        var startup = new StartupRegistration(@"C:\Apps\Big Clock\BigClock.exe", KeyPath);
        Assert.False(startup.IsEnabled);

        startup.SetEnabled(true);
        Assert.True(startup.IsEnabled);
        using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
            Assert.Equal("\"C:\\Apps\\Big Clock\\BigClock.exe\"", key!.GetValue("BigClock"));

        startup.SetEnabled(false);
        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void Entry_for_a_moved_exe_counts_as_disabled()
    {
        new StartupRegistration(@"C:\Old\BigClock.exe", KeyPath).SetEnabled(true);

        Assert.False(new StartupRegistration(@"C:\New\BigClock.exe", KeyPath).IsEnabled);
    }
}
