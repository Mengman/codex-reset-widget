using CodexResetWidget.Application;
using CodexResetWidget.Domain;
using static TestAssert;

internal static class StartupTests
{
    public static (int Passed, int Failed) Run()
    {
        var tests = new TestSuite("Startup");
        const string path = @"C:\Widget folder\CodexResetWidget.exe";
        tests.Check("First load defaults off and never registers startup", () =>
        {
            var store = new MemoryRegistration(); var service = new StartupService(store, path);
            service.Refresh(); Assert(!service.IsEnabled); Equal(0, store.Writes);
        });
        tests.Check("Explicit enable quotes the portable executable and survives reload", () =>
        {
            var store = new MemoryRegistration(); var service = new StartupService(store, path);
            Assert(service.SetEnabled(true)); Equal('"' + path + '"', store.Command);
            var reloaded = new StartupService(store, path); reloaded.Refresh(); Assert(reloaded.IsEnabled); Equal(1, store.Writes);
        });
        tests.Check("Explicit disable removes the registration", () =>
        {
            var store = new MemoryRegistration { Command = '"' + path + '"' }; var service = new StartupService(store, path);
            Assert(service.SetEnabled(false)); Assert(store.Command is null && !service.IsEnabled);
        });
        tests.Check("Windows startup opt-out is respected rather than silently re-enabled", () =>
        {
            var store = new MemoryRegistration { Command = '"' + path + '"', IsDisabledByWindows = true };
            var service = new StartupService(store, path); service.Refresh(); Assert(!service.IsEnabled);
            Assert(!service.SetEnabled(true)); Equal("Startup.DisabledByWindows", service.Warning); Equal(0, store.Writes);
        });
        tests.Check("Read failure does not create a startup entry", () =>
        {
            var store = new MemoryRegistration { FailRead = true }; var service = new StartupService(store, path);
            service.Refresh(); Assert(!service.IsEnabled); Equal("Startup.ReadFailed", service.Warning); Equal(0, store.Writes);
        });
        tests.Check("Write failure preserves the real disabled state", () =>
        {
            var store = new MemoryRegistration { FailWrite = true }; var service = new StartupService(store, path);
            Assert(!service.SetEnabled(true)); Assert(!service.IsEnabled); Equal("Startup.WriteFailed", service.Warning);
        });
        tests.Check("A moved portable copy does not update startup without consent", () =>
        {
            var store = new MemoryRegistration { Command = @"""C:\Old folder\CodexResetWidget.exe""" };
            var service = new StartupService(store, path); service.Refresh(); Assert(service.IsEnabled);
            Equal("Startup.OtherCopy", service.Warning); Equal(0, store.Writes);
            Assert(service.SetEnabled(true)); Equal('"' + path + '"', store.Command);
        });
        tests.Check("A failed disable keeps the registered startup state visible", () =>
        {
            var store = new MemoryRegistration { Command = '"' + path + '"', FailWrite = true };
            var service = new StartupService(store, path); service.Refresh();
            Assert(!service.SetEnabled(false)); Assert(service.IsEnabled); Equal("Startup.WriteFailed", service.Warning);
        });
        tests.Check("Invalid or overlong startup paths are rejected before writing", () =>
        {
            foreach (var invalid in new[] { "relative.exe", @"C:\app.dll", "C:\\bad\"path.exe", @"C:\" + new string('x', 260) + ".exe" })
            {
                var store = new MemoryRegistration(); var service = new StartupService(store, invalid);
                Assert(!service.SetEnabled(true)); Equal(0, store.Writes); Equal("Startup.WriteFailed", service.Warning);
            }
        });
        return tests.Result;
    }
    private sealed class MemoryRegistration : IStartupRegistration
    {
        public string? Command { get; set; }
        public bool IsDisabledByWindows { get; set; }
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }
        public int Writes { get; private set; }
        public string? ReadCommand() => FailRead ? throw new IOException("Read denied") : Command;
        public void WriteCommand(string? command)
        {
            if (FailWrite) throw new UnauthorizedAccessException("Write denied");
            Writes++; Command = command;
        }
    }
}
