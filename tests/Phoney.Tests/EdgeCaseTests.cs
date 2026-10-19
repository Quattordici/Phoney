using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>Arguments at the edges: results stay correct, and mistakes fail with an exception that names the parameter instead of a silent or misleading result.</summary>
public sealed class EdgeCaseTests
{
    private static Faker Make() => new("en", seed: 7);

    [Test]
    public void Between_never_returns_a_moment_before_from()
    {
        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, 0, TimeSpan.Zero).AddTicks(5_000); // half a millisecond in
        var to = from.AddMilliseconds(3);

        for (var seed = 0; seed < 200; seed++)
            new Faker("en", seed).Date.Between(from, to).ShouldBeInRange(from, to);
    }

    [Test]
    public void Between_with_no_whole_millisecond_in_range_returns_from()
    {
        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, 0, TimeSpan.Zero).AddTicks(2_000);

        Make().Date.Between(from, from.AddTicks(1_000)).ShouldBe(from);
    }

    [Test]
    public void Between_rejects_from_after_to_within_the_same_millisecond()
    {
        var to = new DateTimeOffset(2020, 1, 1, 0, 0, 0, 0, TimeSpan.Zero).AddTicks(2_000);

        Should.Throw<ArgumentException>(() => Make().Date.Between(to.AddTicks(1), to)).ParamName.ShouldBe("from");
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(-1.0)]
    [Arguments(double.PositiveInfinity)]
    public void Nearby_coordinates_need_a_finite_radius_that_is_not_negative(double radius) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Location.NearbyGpsCoordinate((10, 10), radius)).ParamName.ShouldBe("radius");

    [Test]
    [Arguments(90.5, 0.0)]
    [Arguments(-91.0, 0.0)]
    [Arguments(0.0, 180.5)]
    [Arguments(double.NaN, 0.0)]
    [Arguments(0.0, double.NegativeInfinity)]
    public void Nearby_coordinates_need_an_origin_on_the_globe(double latitude, double longitude) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Location.NearbyGpsCoordinate((latitude, longitude))).ParamName.ShouldBe("origin");

    [Test]
    public void Nearby_coordinates_stay_on_the_globe_next_to_the_poles_and_the_date_line()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var (latitude, longitude) = new Faker("en", seed).Location.NearbyGpsCoordinate((89.99, 179.99), 50, isMetric: true);
            latitude.ShouldBeInRange(-90, 90);
            longitude.ShouldBeInRange(-180, 180);
        }
    }

    [Test]
    public void Nearby_coordinates_are_the_same_on_every_platform()
    {
        // Pinned bits: sine and cosine come from PureMath, not the platform's maths library (which may round the last bit differently), and CI runs this on every OS.
        var (latitude, longitude) = new Faker("en", 1).Location.NearbyGpsCoordinate((59.3293, 18.0686), 10, isMetric: true);

        BitConverter.DoubleToInt64Bits(latitude).ShouldBe(PinnedLatitudeBits);
        BitConverter.DoubleToInt64Bits(longitude).ShouldBe(PinnedLongitudeBits);
    }

    [Test]
    [Arguments(0.0, 180.0)]
    [Arguments(90.0, -180.0)]
    [Arguments(-90.0, 0.0)]
    public void A_zero_radius_gives_the_origin_itself(double latitude, double longitude)
    {
        for (var seed = 0; seed < 20; seed++)
            new Faker("en", seed).Location.NearbyGpsCoordinate((latitude, longitude), 0).ShouldBe((latitude, longitude));
    }

    private const long PinnedLatitudeBits = 4633540400198287834; // 59.28472211840899
    private const long PinnedLongitudeBits = 4625775252713656800; // 18.05501537265252

    [Test]
    public void Word_lengths_are_validated_before_searching()
    {
        var faker = Make();
        Func<int?, int?, string>[] pickers =
        [
            (min, max) => faker.Lorem.Word(min, max),
            (min, max) => faker.Word.Noun(min, max),
            (min, max) => faker.Word.Sample(min, max),
        ];

        foreach (var pick in pickers)
        {
            Should.Throw<ArgumentOutOfRangeException>(() => pick(-1, null)).ParamName.ShouldBe("minLength");
            Should.Throw<ArgumentOutOfRangeException>(() => pick(null, -1)).ParamName.ShouldBe("maxLength");
            Should.Throw<ArgumentException>(() => pick(6, 5)).ParamName.ShouldBe("maxLength");
        }
    }

    [Test]
    public void No_word_of_a_length_says_which_lengths_were_asked_for()
    {
        Should.Throw<PhoneyDataException>(() => Make().Word.Noun(maxLength: 0)).Message.ShouldContain("of at most 0 characters");
        Should.Throw<PhoneyDataException>(() => Make().Lorem.Word(100, 200)).Message.ShouldContain("of 100 to 200 characters");
    }

    [Test]
    public void Negative_numbers_in_another_base_keep_their_sign()
    {
        var faker = Make();

        faker.Number.Binary(-5, -5).ShouldBe("-101");
        faker.Number.Octal(-9, -9).ShouldBe("-11");
        faker.Number.Hex(-255, -255).ShouldBe("-ff");
        faker.Number.Hex(int.MinValue, int.MinValue).ShouldBe("-80000000");
        faker.Number.Hex(255, 255).ShouldBe("ff");
    }

    [Test]
    public void A_negative_element_count_is_an_argument_error() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Random.Elements([1, 2, 3], -1)).ParamName.ShouldBe("count");

    [Test]
    public void Unique_values_for_huge_counts_still_try()
    {
        var next = 0;

        Make().Helpers.UniqueValues(() => next++, 3_000_000).Count.ShouldBe(3_000_000); // 1000 × count overflowed an int and gave nothing
    }

    [Test]
    public void Time_ordered_ids_reject_timestamps_before_1970()
    {
        var timestamp = new DateTimeOffset(1969, 6, 1, 0, 0, 0, TimeSpan.Zero);

        Should.Throw<ArgumentOutOfRangeException>(() => Make().String.UuidV7(timestamp)).ParamName.ShouldBe("timestamp");
        Should.Throw<ArgumentOutOfRangeException>(() => Make().String.Ulid(timestamp)).ParamName.ShouldBe("timestamp");
    }

    [Test]
    public void Rounded_doubles_without_a_value_in_range_say_so()
    {
        var exception = Should.Throw<ArgumentException>(() => Make().Number.Double(0.11, 0.12, fractionDigits: 1));

        exception.ParamName.ShouldBe("fractionDigits");
        exception.Message.ShouldContain("No value with 1 decimals between 0.11 and 0.12");
    }

    [Test]
    public void Coordinate_precision_is_validated_under_its_own_name()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Location.Latitude(precision: 16)).ParamName.ShouldBe("precision");
        Should.Throw<ArgumentOutOfRangeException>(() => Make().Location.Longitude(precision: -1)).ParamName.ShouldBe("precision");
    }

    [Test]
    public void Excluding_every_character_names_the_exclusion()
    {
        Should.Throw<ArgumentException>(() => Make().String.Alpha(3, Casing.Upper, "ABCDEFGHIJKLMNOPQRSTUVWXYZ")).ParamName.ShouldBe("exclude");
        Should.Throw<ArgumentException>(() => Make().String.AlphaNumeric(3, Casing.Lower, "0123456789abcdefghijklmnopqrstuvwxyz")).ParamName.ShouldBe("exclude");
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public void An_email_provider_must_have_text(string provider) =>
        Should.Throw<ArgumentException>(() => Make().Internet.Email(provider: provider)).ParamName.ShouldBe("provider");
}
