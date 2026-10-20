using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Lesson
{
    public string @class { get; set; } = "";
    public int @event;
    public string @namespace { get; init; } = "";
    public List<string> @params { get; } = [];
}

public sealed record Course(string @operator, int @fixed);

[FakeFor<Lesson>, FakeFor<Course>]
public static partial class KeywordFakes;

/// <summary>Members whose names are C# keywords (<c>@class</c>), which generated code must write with their <c>@</c>.</summary>
public sealed class KeywordMemberTests
{
    private static string Describe(Lesson l) => $"{l.@class}|{l.@event}|{l.@namespace}|{string.Join(",", l.@params)}";

    [Test]
    public void Generated_and_reflection_models_agree_on_members_named_like_keywords()
    {
        var generated = KeywordFakes.Lesson.Seed(4).With(l => l.@class, "Math").Generate(10);
        var reflected = new ObjectGenerator<Lesson>(new ReflectionModel<Lesson>()).Seed(4).With(l => l.@class, "Math").Generate(10);

        generated.ShouldAllBe(l => l.@class == "Math" && l.@namespace.Length > 0 && l.@params.Count > 0);
        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
        KeywordFakes.Course.Seed(4).Generate(5).ShouldBe(new ObjectGenerator<Course>(new ReflectionModel<Course>()).Seed(4).Generate(5));
    }

    [Test]
    public void Members_named_like_keywords_are_populated()
    {
        var lesson = KeywordFakes.Lesson.Seed(4).Populate(new Lesson { @class = "Kept" });

        lesson.@class.ShouldBe("Kept");
        lesson.@namespace.ShouldNotBeEmpty();
        lesson.@params.ShouldNotBeEmpty();
    }
}
