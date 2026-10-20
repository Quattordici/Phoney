// Lets the generator (netstandard2.0) use records and init accessors; the compiler only needs the type to exist.
namespace System.Runtime.CompilerServices;

#pragma warning disable S2094 // polyfill: the compiler only needs the type to exist
internal static class IsExternalInit;
#pragma warning restore S2094
