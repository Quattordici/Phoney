using Microsoft.Extensions.DependencyInjection.Extensions;
using Phoney;
using Phoney.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers Phoney in a service collection.</summary>
public static class PhoneyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFaker"/> and <see cref="Faker"/> (scoped, as a faker is not thread-safe), <see cref="IFakerFactory"/>
    /// (singleton) and <see cref="PhoneyOptions"/>. Calling it again configures the same options.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddPhoney(o =&gt; { o.Locale = "sv"; o.Seed = 42; })
    ///     .AddGenerator&lt;Customer&gt;(g =&gt; g.With(c =&gt; c.Country, "SE"));
    /// </code>
    /// </example>
    /// <exception cref="ArgumentException">The configured locale does not exist.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A generation option (<see cref="PhoneyOptions.NullProbability"/>, the collection sizes, <see cref="PhoneyOptions.MaxDepth"/>) is outside its range.</exception>
    public static PhoneyBuilder AddPhoney(this IServiceCollection services, Action<PhoneyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.FirstOrDefault(d => d.ServiceType == typeof(PhoneyOptions))?.ImplementationInstance as PhoneyOptions;
        if (options is null)
        {
            options = new PhoneyOptions();
            services.AddSingleton(options);
        }

        configure?.Invoke(options);
        options.Validate();
        if (options.Locale is { } locale)
            options.Locale = Locales.Get(locale).Code; // validates now rather than at the first resolve

        services.TryAddSingleton<IFakerFactory>(sp => new FakerFactory(sp.GetRequiredService<PhoneyOptions>(), sp.GetService<TimeProvider>()));
        services.TryAddScoped(sp => sp.GetRequiredService<IFakerFactory>().Create());
        services.TryAddScoped<IFaker>(sp => sp.GetRequiredService<Faker>());
        return new PhoneyBuilder(services);
    }
}
