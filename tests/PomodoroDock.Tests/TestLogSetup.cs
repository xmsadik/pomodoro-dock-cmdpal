using System.Runtime.CompilerServices;
using PomodoroDock.Core;

namespace PomodoroDock.Tests;

internal static class TestLogSetup
{
    [ModuleInitializer]
    internal static void RedirectDiagLog() =>
        DiagLog.LogPath = Path.Combine(Path.GetTempPath(), "PomodoroDock.Tests", "diag.log");
}
