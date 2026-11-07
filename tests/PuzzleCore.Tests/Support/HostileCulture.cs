using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// Runs the whole test assembly under a culture that exposes culture bugs: Turkish by
    /// default (dotted and dotless i), or the culture named in PUZZLE_TEST_CULTURE (CI also
    /// uses sv-SE, whose minus sign is U+2212). Code that accidentally formats, parses or
    /// compares with the device culture then fails here instead of on a player's phone.
    /// </summary>
    internal static class HostileCulture
    {
        public const string EnvironmentVariable = "PUZZLE_TEST_CULTURE";
        public const string DefaultCultureName = "tr-TR";

        public static string CultureName { get; private set; } = DefaultCultureName;

        [ModuleInitializer]
        [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
            Justification = "The test assembly must switch culture before any test runs; xUnit 2 has no assembly fixture.")]
        internal static void Apply()
        {
            string? requested = Environment.GetEnvironmentVariable(EnvironmentVariable);
            CultureName = string.IsNullOrWhiteSpace(requested) ? DefaultCultureName : requested.Trim();
            CultureInfo culture = CultureInfo.GetCultureInfo(CultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
