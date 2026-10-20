using System.Text.Json;

/// <summary>
/// v1.3.5: the one serializer setting for an Item kept as JSON in a database row. Item.LootEffects is a
/// list of value tuples, whose members are fields; System.Text.Json writes fields only with IncludeFields,
/// so the auction, the guild bank and the NPC market (default options) stored every loot effect as an
/// empty object and read it back as (None, 0): fire and frost, life steal, crit, magic resist, stat
/// effects, BossSlayer and TitanResolve were lost. Each store keeps the naming its rows already have.
/// </summary>
public static class ItemJson
{
    /// <summary>The auction listings (players and NPCs) and the guild bank: PascalCase names, as these
    /// rows were written before 1.3.5, with IncludeFields so the LootEffects tuples are written.</summary>
    public static readonly JsonSerializerOptions Store = new() { IncludeFields = true };

    /// <summary>The inheritance queue (team estates and world boss loot past a full pack): camelCase
    /// with IncludeFields, as these rows were already written before 1.3.5 (GameEngine reads them with it).</summary>
    public static readonly JsonSerializerOptions Inheritance = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true
    };

    /// <summary>An item as an auction or guild bank row.</summary>
    public static string WriteStore(Item item) => JsonSerializer.Serialize(item, Store);

    /// <summary>An auction or guild bank row as an item. A row written before 1.3.5 holds an empty
    /// object for each loot effect; those read back as (0, 0) and are dropped, not kept as None 0.</summary>
    public static Item? ReadStore(string json) => DropEmptyEffects(JsonSerializer.Deserialize<Item>(json, Store));

    private static Item? DropEmptyEffects(Item? item)
    {
        item?.LootEffects?.RemoveAll(e => e.EffectType == 0 && e.Value == 0);
        return item;
    }
}
