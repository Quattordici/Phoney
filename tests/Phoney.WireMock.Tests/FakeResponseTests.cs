using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phoney.Generation;
using Phoney.WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Phoney.WireMock.Tests;

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

[FakeFor<Customer>]
public static partial class TestData;

[JsonSerializable(typeof(Customer))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
public sealed partial class TestJson : JsonSerializerContext;

public sealed class FakeResponseTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Answers_with_the_object_of_the_url_as_json()
    {
        using var server = Server(Response.Create().WithFakeJson(Fake.For<Customer>()));

        using var response = await Get(server, "/customers/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe(JsonSerializer.Serialize(Fake.For<Customer>().GenerateFor("/customers/42"), Web));
    }

    [Test]
    public async Task The_same_url_gives_the_same_object_and_another_url_another()
    {
        using var server = Server(Response.Create().WithFakeJson(Fake.For<Customer>()));

        var first = await Body<Customer>(server, "/customers/42");
        var again = await Body<Customer>(server, "/customers/42");
        var other = await Body<Customer>(server, "/customers/43");

        again.Email.ShouldBe(first.Email);
        again.CreatedAt.ShouldBe(first.CreatedAt);
        other.Email.ShouldNotBe(first.Email);
    }

    [Test]
    public async Task The_query_string_is_part_of_the_key()
    {
        using var server = Server(Response.Create().WithFakeJson(Fake.For<Customer>()));

        var plain = await Body<Customer>(server, "/customers/42");
        var query = await Body<Customer>(server, "/customers/42?expand=orders");

        query.Email.ShouldNotBe(plain.Email);
        (await Body<Customer>(server, "/customers/42?expand=orders")).Email.ShouldBe(query.Email);
    }

    [Test]
    public async Task Rules_and_the_seed_of_the_generator_apply()
    {
        var generator = Fake.For<Customer>().Seed(7).With(c => c.FirstName, "Ada");
        using var server = Server(Response.Create().WithFakeJson(generator));

        var customer = await Body<Customer>(server, "/customers/42");

        customer.FirstName.ShouldBe("Ada");
        customer.Email.ShouldBe(generator.GenerateFor("/customers/42").Email);
        customer.Email.ShouldNotBe(Fake.For<Customer>().GenerateFor("/customers/42").Email);
    }

    [Test]
    public async Task A_key_selector_decides_which_requests_share_an_object()
    {
        using var server = Server(Response.Create().WithFakeJson(Fake.For<Customer>(), request => "customer:" + request.PathSegments[^1]));

        var v1 = await Body<Customer>(server, "/v1/customers/42");
        var v2 = await Body<Customer>(server, "/v2/customers/42");

        v2.Email.ShouldBe(v1.Email);
        v1.Email.ShouldBe(Fake.For<Customer>().GenerateFor("customer:42").Email);
    }

    [Test]
    public async Task Fails_with_a_clear_message_when_the_key_selector_returns_null()
    {
        var builder = Response.Create().WithFakeJson(Fake.For<Customer>(), _ => null!);
        using var server = Server(builder);

        using var response = await Get(server, "/customers/42");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldContain("key selector returned null");
    }

    [Test]
    public async Task A_source_generated_model_and_json_context_need_no_reflection()
    {
        using var server = Server(Response.Create().WithFakeJson(TestData.Customer, TestJson.Default.Customer));

        var body = await (await Get(server, "/customers/42")).Content.ReadAsStringAsync();

        body.ShouldBe(JsonSerializer.Serialize(TestData.Customer.GenerateFor("/customers/42"), TestJson.Default.Customer));
    }

    [Test]
    public async Task A_list_has_the_count_and_its_items_belong_to_the_url_and_position()
    {
        using var server = Server(Response.Create().WithFakeJsonList(Fake.For<Customer>(), 3));

        var customers = await Body<List<Customer>>(server, "/customers");

        customers.Count.ShouldBe(3);
        customers.Select(c => c.Email).ShouldBe(Enumerable.Range(0, 3).Select(i => Fake.For<Customer>().GenerateFor($"/customers#{i}").Email));
        (await Body<List<Customer>>(server, "/customers")).Select(c => c.Email).ShouldBe(customers.Select(c => c.Email));
    }

    [Test]
    public async Task A_list_with_a_count_range_has_a_stable_count_per_url_within_the_range()
    {
        using var server = Server(Response.Create().WithFakeJsonList(Fake.For<Customer>(), 2, 6));

        var counts = new List<int>();
        for (var page = 1; page <= 20; page++)
        {
            var first = await Body<List<Customer>>(server, $"/customers?page={page}");
            (await Body<List<Customer>>(server, $"/customers?page={page}")).Count.ShouldBe(first.Count);
            counts.Add(first.Count);
        }

        counts.ShouldAllBe(c => c >= 2 && c <= 6);
        counts.Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Test]
    public async Task An_empty_list_is_allowed()
    {
        using var server = Server(Response.Create().WithFakeJsonList(Fake.For<Customer>(), 0));

        (await (await Get(server, "/customers")).Content.ReadAsStringAsync()).ShouldBe("[]");
    }

    [Test]
    public async Task A_list_with_a_json_context_matches_the_reflection_list()
    {
        using var reflection = Server(Response.Create().WithFakeJsonList(TestData.Customer, 3));
        using var generated = Server(Response.Create().WithFakeJsonList(TestData.Customer, 3, TestJson.Default.Customer));

        var expected = await (await Get(reflection, "/customers")).Content.ReadAsStringAsync();
        (await (await Get(generated, "/customers")).Content.ReadAsStringAsync()).ShouldBe(expected);
    }

    [Test]
    public async Task A_body_factory_gets_a_faker_that_belongs_to_the_url()
    {
        using var server = Server(Response.Create().WithFakeJson((request, faker) => new { id = request.PathSegments[^1], name = faker.Person.FullName() }));

        using var document = JsonDocument.Parse(await (await Get(server, "/people/7")).Content.ReadAsStringAsync());

        document.RootElement.GetProperty("id").GetString().ShouldBe("7");
        document.RootElement.GetProperty("name").GetString().ShouldBe(Fake.Keyed("/people/7").Person.FullName());
    }

    [Test]
    public async Task Concurrent_requests_for_a_url_get_the_same_body()
    {
        using var server = Server(Response.Create().WithFakeJsonList(Fake.For<Customer>(), 1, 10));

        var bodies = await Task.WhenAll(Enumerable.Range(0, 32).Select(async i =>
        {
            using var response = await Get(server, $"/customers?page={i % 4}");
            return (Page: i % 4, Body: await response.Content.ReadAsStringAsync());
        }));

        bodies.GroupBy(b => b.Page).ShouldAllBe(page => page.Select(b => b.Body).Distinct().Count() == 1);
    }

    [Test]
    public async Task Status_code_and_headers_still_compose()
    {
        using var server = Server(Response.Create().WithStatusCode(HttpStatusCode.Created).WithFakeJson(Fake.For<Customer>()).WithHeader("X-Fake", "yes"));

        using var response = await Get(server, "/customers/42");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.GetValues("X-Fake").ShouldBe(["yes"]);
    }

    [Test]
    public void Arguments_are_validated_when_the_response_is_built()
    {
        var response = Response.Create();
        Should.Throw<ArgumentNullException>(() => response.WithFakeJson<Customer>(null!));
        Should.Throw<ArgumentNullException>(() => ((IResponseBuilder)null!).WithFakeJson(Fake.For<Customer>()));
        Should.Throw<ArgumentNullException>(() => response.WithFakeJson(TestData.Customer, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<Customer>)null!));
        Should.Throw<ArgumentNullException>(() => response.WithFakeJson((Func<global::WireMock.IRequestMessage, Faker, object?>)null!));
        Should.Throw<ArgumentOutOfRangeException>(() => response.WithFakeJsonList(Fake.For<Customer>(), -1));
        Should.Throw<ArgumentException>(() => response.WithFakeJsonList(Fake.For<Customer>(), 5, 2)).Message.ShouldContain("maxCount");
    }

    [Test]
    public void A_generator_with_a_unique_rule_is_refused_when_the_response_is_built()
    {
        Should.Throw<InvalidOperationException>(() => Response.Create().WithFakeJson(Fake.For<Customer>().Unique(c => c.Email)))
            .Message.ShouldContain("Unique");
    }

    private static WireMockServer Server(IResponseBuilder response)
    {
        var server = WireMockServer.Start();
        server.Given(Request.Create().UsingGet()).RespondWith(response);
        return server;
    }

    private static async Task<HttpResponseMessage> Get(WireMockServer server, string pathAndQuery)
    {
        using var client = server.CreateClient();
        return await client.GetAsync(new Uri(pathAndQuery, UriKind.Relative));
    }

    private static async Task<T> Body<T>(WireMockServer server, string pathAndQuery)
    {
        using var response = await Get(server, pathAndQuery);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), Web)!;
    }
}
