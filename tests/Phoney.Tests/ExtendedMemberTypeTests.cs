using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class NumbersAndTexts
{
    public Half Half { get; set; }
    public Int128 Wide { get; set; }
    public UInt128 UnsignedWide { get; set; }
    public BigInteger Big { get; set; }
    public nint Native { get; set; }
    public nuint UnsignedNative { get; set; }
    public Rune Letter { get; set; }
    public Half? MaybeHalf { get; set; }

    [System.ComponentModel.DataAnnotations.Range(10, 20)]
    public BigInteger Limited { get; set; }

    [System.ComponentModel.DataAnnotations.Range(10, 20)]
    public Int128 LimitedWide { get; set; }

    [System.ComponentModel.DataAnnotations.Range(10, 20)]
    public Half LimitedHalf { get; set; }

    [System.ComponentModel.DataAnnotations.Range(10, 20)]
    public nint LimitedNative { get; set; }

    public int Age { get; set; }
    public Int128 Quantity { get; set; }
}

public sealed class Compound
{
    public (int, string) Pair { get; set; }
    public (string, int, bool) Triple { get; set; }
    public KeyValuePair<string, int> Entry { get; set; }
    public Tuple<int, string> Reference { get; set; } = null!;
    public (int, (string, bool)) Nested { get; set; }
    public List<(int, string)> Pairs { get; set; } = [];
    public Dictionary<string, (int, int)> Ranges { get; set; } = [];
}

public sealed class BuffersAndGrids
{
    public Memory<byte> Buffer { get; set; }
    public ReadOnlyMemory<byte> ReadOnlyBuffer { get; set; }
    public int[,] Grid { get; set; } = null!;
    public string[,] Names { get; set; } = null!;
    public int[,,] Cube { get; set; } = null!;
}

public sealed class NetworkTypes
{
    public IPAddress Address { get; set; } = null!;
    public IPAddress IpAddress { get; set; } = null!;
    public IPAddress Ipv4Address { get; set; } = null!;
    public IPAddress Ipv6Address { get; set; } = null!;
    public IPAddress? Optional { get; set; }
    public Version Version { get; set; } = null!;
    public Version BuildNumber { get; set; } = null!;
    public List<IPAddress> Servers { get; set; } = [];
}

public sealed class FrameworkStructs
{
    public JsonElement Json { get; set; }
    public Range Range { get; set; }
    public CancellationToken Token { get; set; }
    public ArraySegment<int> Segment { get; set; }
    public string After { get; set; } = "";
}

[FakeFor<NumbersAndTexts>]
[FakeFor<Compound>]
[FakeFor<BuffersAndGrids>]
[FakeFor<NetworkTypes>]
[FakeFor<FrameworkStructs>]
public static partial class ExtendedMemberFakes;

/// <summary>More of the .NET types are filled as members: no member type ends in a bare exception, and both models make the same data.</summary>
public sealed class ExtendedMemberTypeTests
{
    private static ObjectGenerator<T> Reflected<T>() where T : class => new ObjectGenerator<T>(new ReflectionModel<T>()).Seed(17);

    private static readonly JsonSerializerOptions WithFields = new() { IncludeFields = true };

    private static string Json<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items, WithFields);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Numbers_and_letters_of_the_less_common_types_are_generated(bool generated)
    {
        var generator = (generated ? ExtendedMemberFakes.NumbersAndTexts.Seed(17) : Reflected<NumbersAndTexts>()).NullProbability(0);

        var items = generator.Generate(20);

        items.All(i => (double)i.Half > 0 && i.Wide > 0 && i.UnsignedWide > 0 && i.Big > 0 && i.Native > 0 && i.UnsignedNative > 0).ShouldBeTrue();
        items.ShouldAllBe(i => Rune.IsLetter(i.Letter));
        items.ShouldAllBe(i => i.MaybeHalf.HasValue);
        items.Select(i => i.Big).Distinct().Count().ShouldBeGreaterThan(5);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Annotations_and_names_apply_to_them_like_to_the_built_in_numbers(bool generated)
    {
        var items = (generated ? ExtendedMemberFakes.NumbersAndTexts.Seed(17) : Reflected<NumbersAndTexts>()).Generate(50);

        items.ShouldAllBe(i => i.Limited >= 10 && i.Limited <= 20); // [Range]
        items.ShouldAllBe(i => i.Quantity >= 1 && i.Quantity <= 100); // the "Quantity" convention
        items.ShouldAllBe(i => i.Age >= 0 && i.Age < 120);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Invalid_and_boundary_data_break_and_hit_the_range_of_the_new_numbers(bool generated)
    {
        var generator = generated ? ExtendedMemberFakes.NumbersAndTexts.Seed(17) : Reflected<NumbersAndTexts>();

        generator.Invalid(x => x.Limited).Generate(10).All(i => i.Limited < 10 || i.Limited > 20).ShouldBeTrue();
        generator.Invalid(x => x.LimitedWide).Generate(10).All(i => i.LimitedWide < 10 || i.LimitedWide > 20).ShouldBeTrue();
        generator.Invalid(x => x.LimitedHalf).Generate(10).All(i => (double)i.LimitedHalf < 10 || (double)i.LimitedHalf > 20).ShouldBeTrue();
        generator.Invalid(x => x.LimitedNative).Generate(10).All(i => i.LimitedNative < 10 || i.LimitedNative > 20).ShouldBeTrue();
        generator.Boundary(x => x.Limited).Generate(10).All(i => i.Limited == 10 || i.Limited == 20).ShouldBeTrue();
        generator.Boundary(x => x.LimitedHalf).Generate(10).All(i => (double)i.LimitedHalf is 10 or 20).ShouldBeTrue();
        generator.InvalidCases().Count.ShouldBeGreaterThanOrEqualTo(8);
    }

    [Test]
    public void The_two_models_make_the_same_numbers()
    {
        static string Text(NumbersAndTexts n) => $"{n.Half}|{n.Wide}|{n.UnsignedWide}|{n.Big}|{n.Native}|{n.UnsignedNative}|{n.Letter}|{n.MaybeHalf}|{n.Limited}|{n.LimitedWide}|{n.LimitedHalf}|{n.LimitedNative}|{n.Age}|{n.Quantity}";

        Reflected<NumbersAndTexts>().Generate(10).Select(Text).ShouldBe(ExtendedMemberFakes.NumbersAndTexts.Seed(17).Generate(10).Select(Text));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Tuples_and_key_value_pairs_get_a_value_for_every_part(bool generated)
    {
        var items = (generated ? ExtendedMemberFakes.Compound.Seed(17) : Reflected<Compound>()).NullProbability(0).Generate(10);

        foreach (var item in items)
        {
            item.Pair.Item1.ShouldBeGreaterThan(0);
            item.Pair.Item2.ShouldNotBeNullOrEmpty();
            item.Triple.Item1.ShouldNotBeNullOrEmpty();
            item.Triple.Item2.ShouldBeGreaterThan(0);
            item.Entry.Key.ShouldNotBeNullOrEmpty();
            item.Entry.Value.ShouldBeGreaterThan(0);
            item.Reference.ShouldNotBeNull();
            item.Reference.Item1.ShouldBeGreaterThan(0);
            item.Reference.Item2.ShouldNotBeNullOrEmpty();
            item.Nested.Item1.ShouldBeGreaterThan(0);
            item.Nested.Item2.Item1.ShouldNotBeNullOrEmpty();
            item.Pairs.ShouldNotBeEmpty();
            item.Pairs.ShouldAllBe(p => p.Item1 > 0 && p.Item2.Length > 0);
            item.Ranges.ShouldNotBeEmpty();
            item.Ranges.Values.ShouldAllBe(r => r.Item1 > 0 && r.Item2 > 0);
        }
    }

    [Test]
    public void The_two_models_make_the_same_tuples()
    {
        Json(ExtendedMemberFakes.Compound.Seed(17).Generate(10)).ShouldBe(Json(Reflected<Compound>().Generate(10)));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Memory_of_bytes_and_two_dimensional_arrays_are_filled(bool generated)
    {
        var items = (generated ? ExtendedMemberFakes.BuffersAndGrids.Seed(17) : Reflected<BuffersAndGrids>()).CollectionSize(2, 4).Generate(10);

        foreach (var item in items)
        {
            item.Buffer.Length.ShouldBe(16); // as a byte[] member
            item.ReadOnlyBuffer.Length.ShouldBe(16);
            item.Grid.GetLength(0).ShouldBeInRange(2, 4);
            item.Grid.GetLength(1).ShouldBeInRange(2, 4);
            item.Grid.Cast<int>().ShouldAllBe(v => v > 0);
            item.Names.Cast<string>().ShouldAllBe(v => v.Length > 0);
            item.Cube.ShouldBeNull(); // three dimensions are not generated, as for other types Phoney has no generator for
        }
    }

    [Test]
    public void The_two_models_make_the_same_buffers_and_grids()
    {
        string Text(BuffersAndGrids b) => $"{Convert.ToHexString(b.Buffer.Span)}|{Convert.ToHexString(b.ReadOnlyBuffer.Span)}|{string.Join(',', b.Grid.Cast<int>())}|{string.Join(',', b.Names.Cast<string>())}";

        var generated = ExtendedMemberFakes.BuffersAndGrids.Seed(17).Generate(6).Select(Text);
        var reflected = Reflected<BuffersAndGrids>().Generate(6).Select(Text);

        reflected.ShouldBe(generated);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void IP_addresses_and_versions_are_generated(bool generated)
    {
        var items = (generated ? ExtendedMemberFakes.NetworkTypes.Seed(17) : Reflected<NetworkTypes>()).NullProbability(0).Generate(20);

        foreach (var item in items)
        {
            item.Address.ShouldNotBeNull();
            item.IpAddress.ShouldNotBeNull();
            item.Ipv4Address.AddressFamily.ShouldBe(System.Net.Sockets.AddressFamily.InterNetwork);
            item.Ipv6Address.AddressFamily.ShouldBe(System.Net.Sockets.AddressFamily.InterNetworkV6);
            item.Optional.ShouldNotBeNull();
            item.Version.Major.ShouldBeGreaterThanOrEqualTo(0);
            item.Version.Build.ShouldBeGreaterThanOrEqualTo(0);
            item.BuildNumber.ShouldNotBeNull();
            item.Servers.ShouldNotBeEmpty();
            item.Servers.ShouldAllBe(a => a.ToString().Length > 0);
        }

        items.Select(i => i.Address.ToString()).Distinct().Count().ShouldBeGreaterThan(10);
    }

    [Test]
    public void The_two_models_make_the_same_addresses_and_versions()
    {
        string Text(NetworkTypes n) => $"{n.Address}|{n.IpAddress}|{n.Ipv4Address}|{n.Ipv6Address}|{n.Optional}|{n.Version}|{n.BuildNumber}|{string.Join(',', n.Servers)}";

        ExtendedMemberFakes.NetworkTypes.Seed(17).Generate(10).Select(Text).ShouldBe(Reflected<NetworkTypes>().Generate(10).Select(Text));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Structs_without_a_generator_keep_their_default_value_and_never_break_the_object(bool generated)
    {
        var items = (generated ? ExtendedMemberFakes.FrameworkStructs.Seed(17) : Reflected<FrameworkStructs>()).Generate(5);

        items.ShouldAllBe(i => i.After.Length > 0); // the members after them are still filled
        items.ShouldAllBe(i => i.Json.ValueKind == JsonValueKind.Undefined && i.Token == default && i.Segment.Count == 0 && i.Range.Equals(default(Range)));
    }

    [Test]
    public void Fake_One_of_such_a_type_gives_a_value_of_the_type_too()
    {
        Fake.One<Half>().ShouldNotBe(default);
        Fake.One<BigInteger>().ShouldNotBe(BigInteger.Zero);
        Fake.One<IPAddress>().ShouldNotBeNull();
        Fake.One<Version>().ShouldNotBeNull();
    }
}
