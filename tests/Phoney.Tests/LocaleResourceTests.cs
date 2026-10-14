using Phoney.Data;

namespace Phoney.Tests;

/// <summary>The embedded locale files are opened without decoding anything; an entry is decoded the first time it is asked for.</summary>
public sealed class LocaleResourceTests
{
    [Test]
    public void Opening_a_locale_decodes_no_entry_until_it_is_asked_for()
    {
        var resource = LocaleResource.Open("en").ShouldNotBeNull();

        resource.DecodedEntries.ShouldBe(0);

        var first = resource.Get(DataKeys.PersonFirstNameMale).ShouldBeAssignableTo<StringsEntry>().ShouldNotBeNull();

        resource.DecodedEntries.ShouldBe(1);
        first.Values.ShouldContain("James");
        resource.Get(DataKeys.PersonFirstNameMale).ShouldBeSameAs(first);
        resource.DecodedEntries.ShouldBe(1);
    }

    [Test]
    public void A_key_the_locale_does_not_have_is_missing_without_decoding_anything()
    {
        var resource = LocaleResource.Open("base").ShouldNotBeNull();

        resource.Has(DataKeys.PersonFirstNameMale).ShouldBeFalse();
        resource.Get(DataKeys.PersonFirstNameMale).ShouldBeNull();
        resource.Get(-1).ShouldBeNull();
        resource.Get(DataKeys.Count + 5).ShouldBeNull();
        resource.DecodedEntries.ShouldBe(0);
    }

    [Test]
    [Arguments("en")]
    [Arguments("base")]
    [Arguments("de")]
    [Arguments("ja")]
    [Arguments("sv")]
    public void Has_tells_what_Get_returns_for_every_key(string code)
    {
        var resource = LocaleResource.Open(code).ShouldNotBeNull();
        var present = Enumerable.Range(0, DataKeys.Count).Where(resource.Has).ToList();

        present.ShouldNotBeEmpty();
        for (var key = 0; key < DataKeys.Count; key++)
            (resource.Get(key) is not null).ShouldBe(resource.Has(key), $"key {key} ({DataKeys.Paths[key]}) of {code}");
        resource.DecodedEntries.ShouldBe(present.Count);
    }

    [Test]
    public void Every_embedded_locale_decodes_completely_and_consistently()
    {
        foreach (var info in LocaleCatalog.Embedded)
        {
            var resource = LocaleResource.Open(info.Code).ShouldNotBeNull();

            var entries = resource.DecodeAll(); // throws when an entry doesn't end where the scan said it does

            entries.Length.ShouldBe(DataKeys.Count);
            for (var key = 0; key < entries.Length; key++)
                (entries[key] is not null).ShouldBe(resource.Has(key), $"key {key} of {info.Code}");
        }
    }

    [Test]
    public void A_locale_without_a_resource_is_not_found()
    {
        LocaleResource.Open("no-such-locale").ShouldBeNull();
        LocaleResource.Empty.Has(0).ShouldBeFalse();
        LocaleResource.Empty.Get(0).ShouldBeNull();
    }

    [Test]
    public async Task Threads_asking_for_the_same_entry_get_the_same_instance()
    {
        var resource = LocaleResource.Open("en").ShouldNotBeNull();

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => resource.Get(DataKeys.InternetFreeEmail))));

        results[0].ShouldNotBeNull();
        results.ShouldAllBe(e => ReferenceEquals(e, results[0]));
        resource.DecodedEntries.ShouldBe(1);
    }

    [Test]
    public void Merged_locale_data_returns_the_same_entry_every_time()
    {
        var data = LocaleStore.Get("de");

        var entry = data.Get(DataKeys.PersonFirstNameMale).ShouldBeAssignableTo<StringsEntry>().ShouldNotBeNull();

        data.Get(DataKeys.PersonFirstNameMale).ShouldBeSameAs(entry);
        entry.Values.ShouldContain("Lukas");
    }
}
