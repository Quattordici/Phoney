using System.Globalization;
using Phony.Data;

namespace Phony.Modules;

/// <summary>Chemical elements and units (faker.js <c>science</c>).</summary>
public sealed class ScienceModule : FakerModule
{
    internal ScienceModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a chemical element, e.g. <c>(H, Hydrogen, 1)</c>.</summary>
    public ChemicalElement ChemicalElement()
    {
        var (table, row) = Faker.PickRecord(DataKeys.ScienceChemicalElement);
        return new ChemicalElement(
            table.Get(row, "symbol") ?? "",
            table.Get(row, "name") ?? "",
            int.Parse(table.Get(row, "atomicNumber") ?? "0", CultureInfo.InvariantCulture));
    }

    /// <summary>Returns an SI unit, e.g. <c>(meter, m)</c>.</summary>
    public Unit Unit()
    {
        var (table, row) = Faker.PickRecord(DataKeys.ScienceUnit);
        return new Unit(table.Get(row, "name") ?? "", table.Get(row, "symbol") ?? "");
    }
}

/// <summary>A chemical element.</summary>
/// <param name="Symbol">Element symbol, e.g. <c>He</c>.</param>
/// <param name="Name">Element name in the locale's language.</param>
/// <param name="AtomicNumber">Atomic number.</param>
public readonly record struct ChemicalElement(string Symbol, string Name, int AtomicNumber);

/// <summary>A unit of measurement.</summary>
/// <param name="Name">Unit name, e.g. <c>meter (m)</c>.</param>
/// <param name="Symbol">Unit symbol, e.g. <c>m</c>.</param>
public readonly record struct Unit(string Name, string Symbol);
