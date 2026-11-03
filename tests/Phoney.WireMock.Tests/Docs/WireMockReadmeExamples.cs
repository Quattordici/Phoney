using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Phoney.WireMock.Tests.Docs;

// Inside the namespace, so the docs' shop model wins over the test models of this project.
using Phoney.Docs.Shop;

/// <summary>The WireMock.Net example of the main README and the package README, against a running server.</summary>
public sealed class WireMockReadmeExamples
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Bodies_belong_to_their_url()
    {
        // snippet: wiremock-customers
        var server = WireMockServer.Start();

        server.Given(Request.Create().WithPath("/customers/*").UsingGet())
              .RespondWith(Response.Create().WithFakeJson(Fake.For<Customer>()));            // /customers/42 is always the same customer

        server.Given(Request.Create().WithPath("/customers").UsingGet())
              .RespondWith(Response.Create().WithFakeJsonList(Fake.For<Customer>(), 5, 20)); // a stable 5 to 20 per URL
        // end snippet

        using (server)
        {
            using var client = server.CreateClient();
            var first = await client.GetStringAsync(new Uri("/customers/42", UriKind.Relative));

            (await client.GetStringAsync(new Uri("/customers/42", UriKind.Relative))).ShouldBe(first);
            (await client.GetStringAsync(new Uri("/customers/43", UriKind.Relative))).ShouldNotBe(first);
            JsonSerializer.Deserialize<Customer>(first, Web)!.Email.ShouldNotBeEmpty();

            var page = JsonSerializer.Deserialize<List<Customer>>(
                await client.GetStringAsync(new Uri("/customers?page=2", UriKind.Relative)), Web)!;
            page.Count.ShouldBeInRange(5, 20);
        }
    }
}
