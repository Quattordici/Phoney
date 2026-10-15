namespace Phoney.Tests;

/// <summary>Methods faker.js has that Phoney added later: <c>git.commitDate/commitEntry</c>, <c>image.avatarGitHub</c> and <c>internet.color</c>.</summary>
public sealed class FakerJsGapTests
{
    [Test]
    public void Git_commit_date_looks_like_git_log_and_is_reproducible()
    {
        var date = new Faker("en", seed: 5).Git.CommitDate();

        date.ShouldMatch(@"^(Mon|Tue|Wed|Thu|Fri|Sat|Sun) (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) [ \d]\d \d{2}:\d{2}:\d{2} \d{4} \+0000$");
        new Faker("en", seed: 5).Git.CommitDate().ShouldBe(date);
    }

    [Test]
    public void Git_commit_entry_has_the_lines_of_git_log()
    {
        var entry = new Faker("en", seed: 5).Git.CommitEntry();
        var lines = entry.Split('\n');

        lines.Length.ShouldBe(6);
        lines[0].ShouldMatch("^commit [0-9a-f]{40}$");
        lines[1].ShouldMatch("^Author: .+ <.+@.+>$");
        lines[2].ShouldStartWith("Date:   ");
        lines[3].ShouldBe("");
        lines[4].ShouldStartWith("    ");
        lines[5].ShouldBe("");
        new Faker("en", seed: 5).Git.CommitEntry().ShouldBe(entry);
    }

    [Test]
    public void Git_commit_entry_can_use_windows_line_endings()
    {
        var entry = new Faker("en", seed: 5).Git.CommitEntry(lineEnding: "\r\n");

        entry.Split("\r\n").Length.ShouldBe(6);
        entry.Replace("\r\n", "").ShouldNotContain('\n');
    }

    [Test]
    public void Git_commit_entry_rejects_other_line_endings() =>
        Should.Throw<ArgumentException>(() => new Faker("en", seed: 5).Git.CommitEntry(lineEnding: "<br>")).ParamName.ShouldBe("lineEnding");

    [Test]
    public void Image_github_avatar_points_at_a_numeric_user_id()
    {
        var url = new Faker("en", seed: 5).Image.AvatarGitHub();

        url.ShouldMatch(@"^https://avatars\.githubusercontent\.com/u/\d{1,8}$");
        new Faker("en", seed: 5).Image.AvatarGitHub().ShouldBe(url);
    }

    [Test]
    public void Internet_color_is_a_hex_color_and_mixes_with_the_base()
    {
        var faker = new Faker("en", seed: 5);

        faker.Internet.Color().ShouldMatch("^#[0-9a-f]{6}$");

        // Mixing with white (255) can only make a channel at least 127: (random + 255) / 2.
        for (var i = 0; i < 50; i++)
        {
            var color = faker.Internet.Color(255, 255, 255);
            Convert.ToInt32(color[1..3], 16).ShouldBeGreaterThanOrEqualTo(127);
            Convert.ToInt32(color[3..5], 16).ShouldBeGreaterThanOrEqualTo(127);
            Convert.ToInt32(color[5..7], 16).ShouldBeGreaterThanOrEqualTo(127);
        }
    }

    [Test]
    [Arguments(-1, 0, 0, "redBase")]
    [Arguments(0, 256, 0, "greenBase")]
    [Arguments(0, 0, 300, "blueBase")]
    public void Internet_color_validates_the_bases(int red, int green, int blue, string parameter) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new Faker("en", seed: 5).Internet.Color(red, green, blue)).ParamName.ShouldBe(parameter);

    [Test]
    [Arguments("git.commitDate")]
    [Arguments("git.commitEntry")]
    [Arguments("image.avatarGitHub")]
    [Arguments("internet.color")]
    public void The_new_methods_are_template_functions(string name) =>
        new Faker("en", seed: 5).Parse("{{" + name + "}}").ShouldNotBeNullOrWhiteSpace();
}
