using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WireMock;
using WireMock.ResponseBuilders;

namespace Phoney.WireMock;

/// <summary>
/// Fake JSON bodies for WireMock.Net responses. Every body belongs to its request: by default the key is the path and query string, so
/// <c>GET /customers/42</c> returns the same customer in every request, run and machine, and <c>/customers/43</c> another one, without storing anything.
/// </summary>
/// <example>
/// <code>
/// server.Given(Request.Create().WithPath("/customers/*").UsingGet())
///       .RespondWith(Response.Create().WithFakeJson(Fake.For&lt;Customer&gt;()));
/// server.Given(Request.Create().WithPath("/customers").UsingGet())
///       .RespondWith(Response.Create().WithFakeJsonList(Fake.For&lt;Customer&gt;(), 5, 20));
/// </code>
/// </example>
/// <remarks>
/// Objects come from <see cref="ObjectGenerator{T}.GenerateFor"/>, so the generator's rules apply and its <see cref="ObjectGenerator{T}.Seed"/> gives another mapping.
/// The generator makes one object when the response is built, so a generator that can't produce one (or has a <c>Unique</c> rule, which keyed objects can't keep)
/// fails there and not in the middle of a test run. Bodies are serialized with <see cref="JsonSerializerDefaults.Web"/> (camelCase) unless other options are given.
/// </remarks>
public static class FakeResponseExtensions
{
    private const string ReflectionJson = "JSON serialization with JsonSerializerOptions may need types that trimming removes. Pass a JsonTypeInfo<T> from a JsonSerializerContext instead.";
    private const string ReflectionJsonCode = "JSON serialization with JsonSerializerOptions may need runtime code generation. Pass a JsonTypeInfo<T> from a JsonSerializerContext instead.";
    private const string JsonContentType = "application/json; charset=utf-8";

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Answers with the object of <paramref name="generator"/> that belongs to the request, as JSON.</summary>
    /// <param name="response">The response builder.</param>
    /// <param name="generator">Makes the object; its rules and seed apply.</param>
    /// <param name="key">The key of the request (<see cref="FakeRequestKey.PathAndQuery"/> when <see langword="null"/>): requests with the same key get the same object.</param>
    /// <param name="options">Serializer options; <see cref="JsonSerializerDefaults.Web"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> or <paramref name="generator"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The generator can't make keyed objects, such as one with a <c>Unique</c> rule.</exception>
    [RequiresUnreferencedCode(ReflectionJson)]
    [RequiresDynamicCode(ReflectionJsonCode)]
    public static IResponseBuilder WithFakeJson<T>(this IResponseBuilder response, ObjectGenerator<T> generator, Func<IRequestMessage, string>? key = null, JsonSerializerOptions? options = null)
        => response.WithFakeJson(generator, TypeInfo<T>(options), key);

    /// <summary>Answers with the object of <paramref name="generator"/> that belongs to the request, as JSON written by <paramref name="typeInfo"/> (no reflection).</summary>
    /// <param name="response">The response builder.</param>
    /// <param name="generator">Makes the object; its rules and seed apply.</param>
    /// <param name="typeInfo">The serializer metadata of <typeparamref name="T"/>, e.g. <c>AppJson.Default.Customer</c>.</param>
    /// <param name="key">The key of the request (<see cref="FakeRequestKey.PathAndQuery"/> when <see langword="null"/>): requests with the same key get the same object.</param>
    /// <exception cref="ArgumentNullException"><paramref name="response"/>, <paramref name="generator"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The generator can't make keyed objects, such as one with a <c>Unique</c> rule.</exception>
    public static IResponseBuilder WithFakeJson<T>(this IResponseBuilder response, ObjectGenerator<T> generator, JsonTypeInfo<T> typeInfo, Func<IRequestMessage, string>? key = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(typeInfo);
        Probe(generator);
        var keyOf = key ?? FakeRequestKey.PathAndQuery;
        return Json(response, request => JsonSerializer.Serialize(generator.GenerateFor(KeyOf(keyOf, request)), typeInfo));
    }

    /// <summary>Answers with a JSON array of <paramref name="count"/> objects that belong to the request: item <c>i</c> is the object of <c>{key}#{i}</c>.</summary>
    /// <param name="response">The response builder.</param>
    /// <param name="generator">Makes the objects; its rules and seed apply.</param>
    /// <param name="count">The number of objects.</param>
    /// <param name="key">The key of the request (<see cref="FakeRequestKey.PathAndQuery"/> when <see langword="null"/>).</param>
    /// <param name="options">Serializer options; <see cref="JsonSerializerDefaults.Web"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionJson)]
    [RequiresDynamicCode(ReflectionJsonCode)]
    public static IResponseBuilder WithFakeJsonList<T>(this IResponseBuilder response, ObjectGenerator<T> generator, int count, Func<IRequestMessage, string>? key = null, JsonSerializerOptions? options = null)
        => response.WithFakeJsonList(generator, count, count, key, options);

    /// <summary>
    /// Answers with a JSON array of <paramref name="minCount"/> to <paramref name="maxCount"/> objects (both inclusive) that belong to the request: the count is drawn
    /// from the key only (<see cref="Fake.Keyed"/>, not the generator's seed), so a URL always has the same number of items, and item <c>i</c> is the object of <c>{key}#{i}</c>.
    /// </summary>
    /// <param name="response">The response builder.</param>
    /// <param name="generator">Makes the objects; its rules and seed apply.</param>
    /// <param name="minCount">The smallest number of objects.</param>
    /// <param name="maxCount">The largest number of objects.</param>
    /// <param name="key">The key of the request (<see cref="FakeRequestKey.PathAndQuery"/> when <see langword="null"/>).</param>
    /// <param name="options">Serializer options; <see cref="JsonSerializerDefaults.Web"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minCount"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxCount"/> is smaller than <paramref name="minCount"/>.</exception>
    [RequiresUnreferencedCode(ReflectionJson)]
    [RequiresDynamicCode(ReflectionJsonCode)]
    public static IResponseBuilder WithFakeJsonList<T>(
        this IResponseBuilder response, ObjectGenerator<T> generator, int minCount, int maxCount, Func<IRequestMessage, string>? key = null, JsonSerializerOptions? options = null)
        => response.WithFakeJsonList(generator, minCount, maxCount, TypeInfo<T>(options), key);

    /// <summary>Like <see cref="WithFakeJsonList{T}(IResponseBuilder, ObjectGenerator{T}, int, Func{IRequestMessage, string}?, JsonSerializerOptions?)"/>, written by <paramref name="typeInfo"/> (no reflection).</summary>
    /// <param name="response">The response builder.</param>
    /// <param name="generator">Makes the objects; its rules and seed apply.</param>
    /// <param name="count">The number of objects.</param>
    /// <param name="typeInfo">The serializer metadata of <typeparamref name="T"/>, e.g. <c>AppJson.Default.Customer</c>.</param>
    /// <param name="key">The key of the request (<see cref="FakeRequestKey.PathAndQuery"/> when <see langword="null"/>).</param>
    public static IResponseBuilder WithFakeJsonList<T>(this IResponseBuilder response, ObjectGenerator<T> generator, int count, JsonTypeInfo<T> typeInfo, Func<IRequestMessage, string>? key = null)
        => response.WithFakeJsonList(generator, count, count, typeInfo, key);

    /// <summary>Like <see cref="WithFakeJsonList{T}(IResponseBuilder, ObjectGenerator{T}, int, int, Func{IRequestMessage, string}?, JsonSerializerOptions?)"/>, written by <paramref name="typeInfo"/> (no reflection).</summary>
    /// <param name="response">The response builder.</param>
    /// <param name="generator">Makes the objects; its rules and seed apply.</param>
    /// <param name="minCount">The smallest number of objects.</param>
    /// <param name="maxCount">The largest number of objects.</param>
    /// <param name="typeInfo">The serializer metadata of <typeparamref name="T"/>, e.g. <c>AppJson.Default.Customer</c>.</param>
    /// <param name="key">The key of the request (<see cref="FakeRequestKey.PathAndQuery"/> when <see langword="null"/>).</param>
    public static IResponseBuilder WithFakeJsonList<T>(
        this IResponseBuilder response, ObjectGenerator<T> generator, int minCount, int maxCount, JsonTypeInfo<T> typeInfo, Func<IRequestMessage, string>? key = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);
        if (maxCount < minCount)
            throw new ArgumentException($"maxCount ({maxCount}) must be greater than or equal to minCount ({minCount}).", nameof(maxCount));
        Probe(generator);
        var keyOf = key ?? FakeRequestKey.PathAndQuery;
        var json = typeInfo.Options;
        var writerOptions = new JsonWriterOptions { Encoder = json.Encoder, Indented = json.WriteIndented, MaxDepth = json.MaxDepth };
        return Json(response, request =>
        {
            var itemKey = KeyOf(keyOf, request);
            var count = minCount == maxCount ? minCount : Fake.Keyed(itemKey).Random.Int(minCount, maxCount);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, writerOptions))
            {
                writer.WriteStartArray();
                for (var i = 0; i < count; i++)
                    JsonSerializer.Serialize(writer, generator.GenerateFor(string.Create(CultureInfo.InvariantCulture, $"{itemKey}#{i}")), typeInfo);
                writer.WriteEndArray();
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        });
    }

    /// <summary>
    /// Answers with the JSON of <paramref name="body"/>, which gets the request and a faker that belongs to it (<see cref="Faker.Keyed"/> of the key), for bodies
    /// that aren't one generated type: <c>WithFakeJson((request, faker) =&gt; new { id = request.PathSegments[^1], name = faker.Person.FullName() })</c>.
    /// </summary>
    /// <param name="response">The response builder.</param>
    /// <param name="body">Makes the body from the request and its faker.</param>
    /// <param name="key">The key of the request (<see cref="FakeRequestKey.PathAndQuery"/> when <see langword="null"/>).</param>
    /// <param name="options">Serializer options; <see cref="JsonSerializerDefaults.Web"/> when <see langword="null"/>.</param>
    /// <param name="locale">The locale of the faker; <see cref="Fake.Locale"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> or <paramref name="body"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(ReflectionJson)]
    [RequiresDynamicCode(ReflectionJsonCode)]
    public static IResponseBuilder WithFakeJson(
        this IResponseBuilder response, Func<IRequestMessage, Faker, object?> body, Func<IRequestMessage, string>? key = null, JsonSerializerOptions? options = null, string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(body);
        var faker = new Faker(locale ?? Fake.Locale);
        var keyOf = key ?? FakeRequestKey.PathAndQuery;
        var serializer = options ?? WebOptions;
        serializer.MakeReadOnly(populateMissingResolver: true);
        return Json(response, request => JsonSerializer.Serialize(body(request, faker.Keyed(KeyOf(keyOf, request))), serializer));
    }

    // Freezes the options with the reflection resolver when they have none, as the first JsonSerializer.Serialize call with them would.
    [RequiresUnreferencedCode(ReflectionJson)]
    [RequiresDynamicCode(ReflectionJsonCode)]
    private static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions? options)
    {
        options ??= WebOptions;
        options.MakeReadOnly(populateMissingResolver: true);
        return (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
    }

    private static IResponseBuilder Json(IResponseBuilder response, Func<IRequestMessage, string> body)
        => response.WithBody(body, encoding: Encoding.UTF8).WithHeader("Content-Type", JsonContentType);

    // One object up front: a generator that can't make keyed objects (Unique, a strict rule, a missing model) fails where the response is built, not as a 500 mid-run.
    private static void Probe<T>(ObjectGenerator<T> generator) => generator.GenerateFor("");

    private static string KeyOf(Func<IRequestMessage, string> key, IRequestMessage request)
        => key(request) ?? throw new InvalidOperationException(
            $"The key selector returned null for {request.Method} {request.Path}. Return a string such as the path, an id from it, or \"customer:\" + id.");
}
