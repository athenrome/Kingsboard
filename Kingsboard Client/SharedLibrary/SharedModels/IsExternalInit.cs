namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Lets positional records (which use init-only setters) compile on netstandard2.1 and in Unity,
    /// neither of which ships this type. The compiler only needs it to exist.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
