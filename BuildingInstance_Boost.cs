using UnityEngine;

/// <summary>
/// Финал: ускорение мастерских за марки. Не мгновенное: работа идёт в Balance.BoostMul раза быстрее
/// Balance.BoostMinutes минут (докупить - время добавляется, но не больше Balance.BoostMaxMinutes).
/// Действует на изготовление: Мастерская, Литейная, Зельеварня, Магическая оружейная, Инженерный двор, Кухня,
/// Оружейная (изготовление и починка), Кузня мастера, Конюшня. Добыча (лесопилка, шахта, ферма) не ускоряется.
/// Время ускорения идёт по часам сервера: вышли из игры - ускорение тоже идёт и заканчивается само.
/// </summary>
public partial class BuildingInstance
{
    System.DateTime boostUntil = System.DateTime.MinValue;

    /// <summary>Множитель скорости изготовления (1 - ускорения нет).</summary>
    public float CraftBoost => BoostLeft > 0f ? Balance.BoostMul : 1f;

    public float BoostLeft
    {
        get
        {
            if (boostUntil == System.DateTime.MinValue) return 0f;
            double s = (boostUntil - RuinClockUtc).TotalSeconds;
            return s > 0.0 ? (float)s : 0f;
        }
    }

    /// <summary>Здание, у которого есть изготовление (его можно ускорить).</summary>
    public bool CanBoost
    {
        get
        {
            if (type == null) return false;
            switch (type.name)
            {
                case "Мастерская":
                case FoundryController.TypeName:
                case PotionHouse.TypeName:
                case MagicArmory.TypeName:
                case EngineerYard.TypeName:
                case KitchenController.TypeName:
                case ArmoryController.TypeName:
                case WeaponForge.TypeName:
                case StableController.TypeName:
                    return true;
            }
            return false;
        }
    }

    public int BoostCost => Balance.BoostMarks(level);

    public string BoostProblem()
    {
        if (!CanBoost) return "Это здание не ускорить";
        if (UnderConstruction) return "Сначала достройте";
        if (IsRuined) return "Сначала восстановите";
        if (WarCityMode.Active) return "На войне ускорять нельзя";
        if (BoostLeft >= Balance.BoostMaxMinutes * 60f - 30f) return $"Уже ускорено на {Balance.BoostMaxMinutes:F0} мин — больше нельзя";
        var rm = ResourceManager.Instance;
        if (rm == null || rm.marks < BoostCost) return $"Нужно {BoostCost} марок, у вас {(rm != null ? rm.marks : 0)}";
        return "";
    }

    public bool BuyBoost()
    {
        if (BoostProblem() != "") return false;
        var rm = ResourceManager.Instance;
        if (!rm.SpendMarks(BoostCost)) return false;
        System.DateTime now = RuinClockUtc;
        System.DateTime from = boostUntil > now ? boostUntil : now;
        System.DateTime until = from.AddMinutes(Balance.BoostMinutes);
        System.DateTime cap = now.AddMinutes(Balance.BoostMaxMinutes);
        boostUntil = until > cap ? cap : until;
        CombatFx.Ring(Center, Mathf.Max(type.size.x, type.size.y) * 0.7f, new Color(0.6f, 0.85f, 1f, 0.85f), 0.6f);
        Sfx.Ui("ui_coins");
        WarManager.Notify($"{type.name}: работает вдвое быстрее ещё {Balance.Dur(BoostLeft)}", true);
        return true;
    }

    // Сохраняется вместе со зданием (BuildingInstance_Construction.SaveState)
    void SaveBoost(SaveBag bag)
    {
        bag.Set("bld.boost", BoostLeft > 0f ? boostUntil.ToString("o") : null);
    }

    void LoadBoost(SaveBag bag)
    {
        string s = bag.Get("bld.boost");
        if (!string.IsNullOrEmpty(s) && System.DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime t))
            boostUntil = t.ToUniversalTime();
    }
}
