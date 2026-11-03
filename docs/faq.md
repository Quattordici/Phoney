# Questions and answers

The surprises people run into, and what to do about them. Each answer links to the part of the [README](../README.md) that has the details.

## Values

**A member that is an interface or abstract class stays `null`.**
Phoney needs to know which type to create. Give it one with `.Subtype<IShape, Circle, Square>()` on the generator, `Fake.Subtypes.Add<IShape, Circle>()` for the whole app, or `[JsonDerivedType]` on the base type, which works out of the box. `Fake.One<IShape>()` throws with the same advice. ([Interfaces and subtypes](../README.md#interfaces-and-subtypes))

**`order.Customer` is `null` even though the order sits inside a customer.**
That is a cycle (`Customer → Order → Customer`), and by default it stops with `null` rather than generating a fresh customer per order. `.WithBackReferences()` sets it to the customer the order belongs to, and `.WithParentKey(...)` copies keys into foreign keys. ([Back-references](../README.md#back-references))

**My `CultureInfo` or `TimeZoneInfo` member is `null`.**
On purpose: their values depend on the machine, and a seed must give the same data everywhere. Set them with `.With(...)`.

**A `false` or `0` I set before `Populate` was replaced.**
`Populate` fills members that hold their default, and a deliberate `false` or `0` looks exactly like one. Protect it with `.Ignore(x => x.Member)`. ([Filling in existing objects](../README.md#filling-in-existing-objects))

**Are the national ids, IBANs and card numbers real?**
They have valid check digits, so validators accept them, but they are not issued and can coincide with real ones. Use them as test data only.

## Rules

**A path rule such as `.With(x => x.Lines.Each().Sku, …)` seems to do nothing.**
Common causes: the lists were empty (`CollectionSize(0, …)`), or another rule creates the whole object (`.With(x => x.Lines, …)`, `WithGeneratorFor<OrderLine>`), which brings its own rules. Add `.VerifyRules()` and Phoney throws, listing every rule that never applied. ([Checking that rules apply](../README.md#checking-that-rules-apply))

**`Where(...)` throws after 1000 attempts.**
`Where` is for conditions most objects already meet. When few do, set the members directly with `.With(...)` so the condition holds by construction, or filter one member with `Where(x => x.Member, condition)`, which draws only that member again. ([Conditions with `Where`](../README.md#conditions-with-where))

**`Where(x => x.Email, …)` throws when the generator is first used.**
Names, email, address, birthdate and national id come from the object's one identity and have one value per object, so drawing just that member again would give the same value. Filter the whole object (`Where(o => …)`) instead. `Unique(x => x.Email)` works, because on a duplicate it generates the whole object again.

**The analyzer reports PHONEY005 on a line that calls `.With(...)`.**
Generators are immutable, so `generator.With(...);` on its own line creates a new generator and throws it away. Assign it (`generator = generator.With(...)`) or keep the chain going; the IDE offers the fix. ([Diagnostics](../README.md#source-generator-and-native-aot))

## Seeds and test data

**My `[FakeData]` test gets the same values on every run.**
That is the point of `[FakeData]`: stable test cases with stable names. For new data on every run that you can still replay, use `[FakeSeed]` with `Fake.*` in the body, or `[FakeCheck]` to try a hundred rows and shrink a failure. `PHONEY_SEED=123 dotnet test` moves every `[FakeData]` test to other data at once. ([Choosing a seed](seeds.md))

**Seeded dates are all before 2025.**
Seeded fakers and generators measure "past", "future" and ages from `Faker.DefaultSeededReferenceDate` (2025-01-01 UTC), so seeded data doesn't change from one day to the next. Set `.ReferenceDate(...)` or a `TimeProvider` to measure from another date. ([Choosing a seed](seeds.md#fixed-data))

**Adding a rule changed the values of other members.**
Members draw from one random stream in order, so a member that draws more or less shifts the members after it. [What moves seeded data](seeds.md#what-moves-seeded-data) lists what does and doesn't.

**A custom convention or subtype isn't used for `[FakeData]` parameters.**
Test data is created while the framework discovers tests, which is before most setup hooks run. Register in a module initializer (`[ModuleInitializer]`), or in TUnit a `[Before(TestDiscovery)]` hook.

**`[FakeSeed]` doesn't seem to change my generator.**
It seeds the `Fake` facade around the test. A generator with its own `.Seed(...)`, or one created earlier and kept in a `static` field, keeps its own sequence. Create the generator inside the test, or leave out its `Seed`.

## Performance and deployment

**The compiler warns about `RequiresUnreferencedCode` on `Fake.For<T>()` in a trimmed or Native AOT app.**
`Fake.For`, `One`, `Many` and `Populate` fall back to reflection when a type has no generated model. Add `[FakeFor<T>]` to a partial class and call `Fake.ForGenerated<T>()`, which never uses reflection. The analyzer (**PHONEY012**, a suggestion) points at the calls that would. ([Source generator and Native AOT](../README.md#source-generator-and-native-aot))

**Is a `Faker` thread-safe?**
`Fake.*` is: every thread has its own faker. A `Faker` you create is not, so give each thread its own, or `faker.Fork(i)` per work item, which also keeps the data independent of thread timing. Generators are immutable and safe to share, and `GenerateParallel(n)` makes the same objects as `Generate(n)`.

**The first call takes noticeably longer than the rest.**
Most of it is JIT compilation, paid once per process. The package ships a ReadyToRun build that the .NET host picks up by itself, and a Native AOT app with source-generated models starts in a few milliseconds. ([Performance](../README.md#performance))
