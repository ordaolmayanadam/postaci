using System.Globalization;
using System.Runtime.CompilerServices;

namespace ExchangeMail.Core.Tests;

internal static class TestCulture
{
    /// <summary>Tests assert Turkish texts; pin the resources so results don't depend on the machine's language.</summary>
    [ModuleInitializer]
    internal static void PinTurkish() => Strings.Culture = CultureInfo.GetCultureInfo(Loc.Turkish);
}
