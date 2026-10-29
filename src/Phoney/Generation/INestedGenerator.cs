namespace Phoney.Generation;

/// <summary>A generator that can create objects nested inside another generator's objects.</summary>
internal interface INestedGenerator
{
    /// <summary>The type of the objects the generator creates.</summary>
    Type GeneratedType { get; }

    /// <summary>
    /// Why the generator can't create nested objects (it verifies its rules, which only happens at its own <c>Generate</c>; it numbers its objects, and every nested
    /// object is position 0), or <see langword="null"/>. Registering it anyway would silently do the wrong thing, so <c>With</c>, <c>WithGeneratorFor</c> and <c>Fake.Generators</c> refuse it.
    /// </summary>
    string? CannotBeNested { get; }

    object? CreateNested(FakeScope parent);
}
