using System.Globalization;

namespace Phoney.Templates;

// Built-in template functions for the modules not registered in TemplateFunctions.cs.
public static partial class TemplateFunctions
{
    /// <summary>Accepts faker.js issuer names (<c>american_express</c>) and .NET names (<c>AmericanExpress</c>).</summary>
    private static Modules.CardIssuer ParseIssuer(string issuer) =>
        Enum.Parse<Modules.CardIssuer>(issuer.Replace("_", "", StringComparison.Ordinal), ignoreCase: true);
}
