namespace Phoney;

/// <summary>Which members <see cref="ObjectGenerator{T}.Populate(T, PopulateMode)"/> fills in.</summary>
public enum PopulateMode
{
    /// <summary>
    /// Fills members that still hold their default value (<see langword="null"/>, 0, <see langword="false"/>,
    /// <see cref="Guid.Empty"/>, an empty string or collection) and keeps the others. A non-null nested object is
    /// populated the same way, member by member.
    /// </summary>
    MissingOnly,

    /// <summary>Generates every member that is not ignored, replacing what is there.</summary>
    All,
}
