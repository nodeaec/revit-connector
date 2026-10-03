#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Minimal polyfill allowing <c>record</c> / <c>record struct</c> on
    /// .NET Framework 4.8 (Revit 2023 and 2024), where the compiler requires this type
    /// to generate the <c>init</c> accessors and it does not exist in the BCL.
    /// On .NET 8/10 the runtime's real type is used and this file is skipped
    /// entirely by the <c>#if</c>.
    /// </summary>
    internal sealed class IsExternalInit
    {
    }
}
#endif
