using System.ComponentModel.DataAnnotations;

namespace Phoney.Generation;

/// <summary>
/// Asks an object that implements <see cref="IValidatableObject"/> whether it passes its own <c>Validate</c>, so a generated object passes the validation its
/// type declares. It draws nothing; a run makes a failing object again like one a <c>Where</c> condition rejects (see <see cref="ObjectGenerator{T}.ValidateObjects"/>).
/// </summary>
internal static class ObjectValidation
{
    /// <summary>How often an object may be made again for its own <c>Validate</c> when no condition sets a smaller limit.</summary>
    public const int MaxAttempts = 1000;

    /// <summary>Whether objects of <paramref name="type"/> have a <c>Validate</c> of their own.</summary>
    public static bool Applies(Type type) => typeof(IValidatableObject).IsAssignableFrom(type);

    /// <summary>The first error <paramref name="item"/>'s <c>Validate</c> returns, or <see langword="null"/> when it returns none (or the type has no <c>Validate</c>).</summary>
    public static string? Problem(object item)
    {
        if (item is not IValidatableObject validatable)
            return null;

        // The constructor with a display name is the trim-safe one: the others read the display name through reflection.
        var context = new ValidationContext(item, item.GetType().Name, serviceProvider: null, items: null);
        if (validatable.Validate(context).FirstOrDefault(result => result is not null) is not { } error)
            return null;
        return error.ErrorMessage is { Length: > 0 } message ? message : "Validate returned an error without a message.";
    }

    /// <summary>The error when no attempt in <paramref name="attempts"/> gave an object that passes its own <c>Validate</c>.</summary>
    public static InvalidOperationException Exhausted(Type type, string problem, int attempts) =>
        new($"Could not generate a {type.Name} that passes its own Validate after {attempts} attempts: {problem} "
            + "Give the members values that pass with .With(…), or turn the check off with .ValidateObjects(false) when the data is meant to be invalid.");
}
