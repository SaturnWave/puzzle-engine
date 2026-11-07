// netstandard2.1 lacks this marker type, which the C# 9 compiler needs for
// init-only setters and records. Each Unity-bound assembly compiles its own
// internal copy, so nothing leaks into the public surface.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
