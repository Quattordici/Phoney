using System.Globalization;
using Phony.Data;

namespace Phony.Modules;

/// <summary>Dishes, ingredients and descriptions (faker.js <c>food</c>).</summary>
public sealed class FoodModule : FakerModule
{
    internal FoodModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a food adjective, e.g. <c>crispy</c>.</summary>
    public string Adjective() => Faker.Pick(DataKeys.FoodAdjective);

    /// <summary>Returns a description of a dish.</summary>
    public string Description() => Faker.Pick(DataKeys.FoodDescriptionPattern);

    /// <summary>Returns a dish name in title case, e.g. <c>Chicken Teriyaki</c>.</summary>
    public string Dish() => TitleCase(Random.Bool() ? Faker.Pick(DataKeys.FoodDishPattern) : Faker.Pick(DataKeys.FoodDish));

    /// <summary>Returns a cuisine, e.g. <c>Italian</c>.</summary>
    public string EthnicCategory() => Faker.Pick(DataKeys.FoodEthnicCategory);

    /// <summary>Returns a fruit.</summary>
    public string Fruit() => Faker.Pick(DataKeys.FoodFruit);

    /// <summary>Returns an ingredient.</summary>
    public string Ingredient() => Faker.Pick(DataKeys.FoodIngredient);

    /// <summary>Returns a type of meat.</summary>
    public string Meat() => Faker.Pick(DataKeys.FoodMeat);

    /// <summary>Returns a spice.</summary>
    public string Spice() => Faker.Pick(DataKeys.FoodSpice);

    /// <summary>Returns a vegetable.</summary>
    public string Vegetable() => Faker.Pick(DataKeys.FoodVegetable);

    /// <summary>Upper-cases the first letter of each space-separated word (other letters unchanged), as faker.js does.</summary>
    private static string TitleCase(string text) => string.Create(text.Length, text, static (span, t) =>
    {
        t.AsSpan().CopyTo(span);
        for (var i = 0; i < span.Length; i++)
        {
            if (i == 0 || span[i - 1] == ' ')
                span[i] = char.ToUpper(span[i], CultureInfo.InvariantCulture);
        }
    });
}
