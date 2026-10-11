namespace Phoney.Schemes;

/// <summary>The schemes registered by default.</summary>
internal static class BuiltInSchemes
{
    public static IEnumerable<NationalIdScheme> All() =>
        NordicSchemes.All().Concat(AmericanSchemes.All()).Concat(EuropeanSchemes.All());
}
