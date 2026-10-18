# Phoney.WireMock

[Phoney](https://github.com/Quattordici/Phoney) fake data for [WireMock.Net](https://github.com/wiremock/WireMock.Net) mocks: realistic JSON bodies that belong to their URL, without writing or storing a single fixture.

```csharp
using Phoney.WireMock;

var server = WireMockServer.Start();

server.Given(Request.Create().WithPath("/customers/*").UsingGet())
      .RespondWith(Response.Create().WithFakeJson(Fake.For<Customer>()));

server.Given(Request.Create().WithPath("/customers").UsingGet())
      .RespondWith(Response.Create().WithFakeJsonList(Fake.For<Customer>(), 5, 20));
```

`GET /customers/42` is the same customer in every request, test run and machine, `/customers/43` is another one, and `/customers?page=2` always has the same 5 to 20 customers. The bodies are computed from the URL each time (`ObjectGenerator<T>.GenerateFor`), so nothing is kept in memory and parallel requests agree.

| Method | Body |
|---|---|
| `WithFakeJson(generator)` | the object of the request's key |
| `WithFakeJsonList(generator, count)` / `(generator, minCount, maxCount)` | an array; the count is drawn from the key, item `i` is the object of `{key}#{i}` |
| `WithFakeJson((request, faker) => …)` | any shape, from the request and a `Faker` keyed to it |

- **Keys.** The default key is the path and query string (`FakeRequestKey.PathAndQuery`); `FakeRequestKey.Path` ignores the query, and any `Func<IRequestMessage, string>` works: `WithFakeJson(customers, r => "customer:" + r.PathSegments[^1])` makes `/v1/customers/42` and `/v2/customers/42` the same customer, and the same as `customers.GenerateFor("customer:42")` in your assertions.
- **Rules and seeds.** The generator's rules apply (`Fake.For<Customer>().With(c => c.Country, "SE")`), and `.Seed(7)` gives another, equally stable, set of customers.
- **JSON.** Bodies are written with `System.Text.Json` and `JsonSerializerDefaults.Web` (camelCase) and sent as `application/json; charset=utf-8`; pass `JsonSerializerOptions`, or a `JsonTypeInfo<T>` from a `JsonSerializerContext` together with a `[FakeFor<T>]` model to stay free of reflection. Status codes, headers, delays and faults compose as usual.
- **Fails early.** The generator makes one object when the response is built, so a generator that can't make keyed objects (such as one with a `Unique` rule) throws there, not as a 500 in the middle of a run.

Requires WireMock.Net 2.12.0 or newer.
