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

    /// <summary>
    /// Whether the objects the generator makes can have a history: it declares a lifecycle, or a generator it uses for a nested type does. <c>GenerateHistory</c>
    /// reads it, so a root without a lifecycle of its own still records the transitions its nested objects bring.
    /// </summary>
    bool HasLifecycle { get; }

    object? CreateNested(FakeScope parent);
}
