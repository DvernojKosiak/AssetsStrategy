using UnityEngine;

/// <summary>
/// Раньше - окно обороны со схемой боя. Теперь, если защитник в игре, бой идёт прямо в его деревне (HomeSiege),
/// а схема не нужна. Остались только проверки для старых мест.
/// </summary>
public static class DefenseWindow
{
    public const int BarricadeWood = 20;

    /// <summary>На деревню сейчас идёт набег другого игрока.</summary>
    public static bool UnderRaid => HomeSiege.UnderRaid;

    /// <summary>Курсор над полосой тревоги (клики не уходят на карту города).</summary>
    public static bool IsPointerOver(Vector2 p) { return HomeSiege.IsPointerOver(p); }
}
