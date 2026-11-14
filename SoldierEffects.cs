using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обновление 14: эффекты на снаряжение бойца, который стоит в казарме с оружием (раньше - только на вещи на складе Оружейной).
///  - Яд, замедление и салют - на лук бойца; ядро хаоса - на щит бойца.
///  - Только вооружённому бойцу и только пока он в казарме (внутри или у двери): ушёл в бой - эффект не наложить.
///  - Житель несёт зелье / Фейерверочный лук / ядро в казарму, накладывается, когда принесли (как в Оружейной).
/// </summary>
public static class SoldierEffects
{
    static readonly HashSet<Weapon> poisonPending = new HashSet<Weapon>();
    static readonly HashSet<Weapon> slowPending = new HashSet<Weapon>();
    static readonly HashSet<Weapon> salutePending = new HashSet<Weapon>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { poisonPending.Clear(); slowPending.Clear(); salutePending.Clear(); }

    public static Weapon BowOf(CitizenAgent s)
    {
        Weapon w = s != null ? s.EquippedWeapon : null;
        return w != null && w.kind == WeaponKind.Bow ? w : null;
    }

    /// <summary>Есть ли что показать этому бойцу (лук или щит, и эффект доступен пути / есть нужная вещь).</summary>
    public static bool AnyFor(CitizenAgent s)
    {
        Weapon bow = BowOf(s);
        Shield sh = ShieldBearer.ShieldOf(s);
        return (bow != null && (ArmoryController.ShowPoison(bow) || ArmoryController.ShowSlow(bow) || ArmoryController.ShowSalute(bow)))
            || (sh != null && ArmoryController.ShowCore(sh));
    }

    static string Where(CitizenAgent s, BarracksController br)
    {
        if (s == null || s.IsDead) return "Бойца нет";
        if (s.IsOnExpedition) return "Боец в походе — эффекты накладывают только в казарме";
        if (br == null || !br.AtHome(s)) return "Боец не в казарме — пусть вернётся в казарму";
        return "";
    }

    public static string PoisonProblem(CitizenAgent s, BarracksController br)
    {
        Weapon bow = BowOf(s);
        if (bow == null) return "У бойца нет лука";
        string w = Where(s, br); if (w != "") return w;
        if (poisonPending.Contains(bow)) return "Зелье уже несут";
        if (bow.poisonCharges > Weapon.PoisonChargesPerPotion / 3) return "Яда на луке ещё много";
        if (PotionHouse.TotalPotions <= 0) return "Нет зелий отравления (Зельеварня или рынок)";
        return "";
    }

    public static string SlowProblem(CitizenAgent s, BarracksController br)
    {
        Weapon bow = BowOf(s);
        if (bow == null) return "У бойца нет лука";
        string w = Where(s, br); if (w != "") return w;
        if (slowPending.Contains(bow)) return "Зелье уже несут";
        if (bow.slowCharges > Weapon.SlowChargesPerPotion / 3) return "Зелья на луке ещё много";
        if (ResourceManager.Instance == null || ResourceManager.Instance.slowPotions <= 0) return "Нет зелий замедления (Зельеварня или рынок)";
        return "";
    }

    public static string SaluteProblem(CitizenAgent s, BarracksController br)
    {
        Weapon bow = BowOf(s);
        if (bow == null) return "У бойца нет лука";
        string w = Where(s, br); if (w != "") return w;
        if (salutePending.Contains(bow)) return "Фейерверочный лук уже несут";
        if (bow.saluteCharges > Weapon.SaluteChargesOf(1) / 3) return "Салюта на луке ещё много";
        if (EngineerYard.CountKits(ModuleKind.FireworkBow) <= 0) return "Нет Фейерверочного лука в Цитадели";
        return "";
    }

    public static string CoreProblem(CitizenAgent s, BarracksController br)
    {
        Shield sh = ShieldBearer.ShieldOf(s);
        if (sh == null) return "У бойца нет щита";
        string w = Where(s, br); if (w != "") return w;
        return MagicArmory.CoreProblemFor(sh);
    }

    static BuildingInstance Hall() { var pm = PopulationManager.Instance; return pm != null ? pm.Hall : null; }

    public static void Poison(CitizenAgent s, BarracksController br)
    {
        if (PoisonProblem(s, br) != "") return;
        Weapon bow = BowOf(s);
        if (!PotionHouse.TakePotions(1, out BuildingInstance src)) return;
        poisonPending.Add(bow);
        int carry = CarryLedger.AddPotions(1, false);
        BuildingInstance dest = br.BuildingRef;
        WarManager.Notify($"{s.UnitName} {s.CitizenName}: несут зелье отравления в казарму", true);
        Logistics.Request(new HaulJob
        {
            from = src != null ? src : Hall(),
            to = dest,
            prefer = dest,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                poisonPending.Remove(bow);
                bow.poisonCharges += Weapon.PoisonChargesPerPotion;
                WarManager.Notify($"Отравлен лук бойца: {bow.Name}", true);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                poisonPending.Remove(bow);
                PotionHouse.ReturnPotions(1, src);
            }
        });
    }

    public static void Slow(CitizenAgent s, BarracksController br)
    {
        if (SlowProblem(s, br) != "") return;
        Weapon bow = BowOf(s);
        if (!ResourceManager.Instance.SpendSlowPotions(1)) return;
        slowPending.Add(bow);
        int carry = CarryLedger.AddPotions(1, true);
        BuildingInstance dest = br.BuildingRef;
        WarManager.Notify($"{s.UnitName} {s.CitizenName}: несут зелье замедления в казарму", true);
        Logistics.Request(new HaulJob
        {
            from = Hall(),
            to = dest,
            prefer = dest,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                slowPending.Remove(bow);
                bow.slowCharges += Weapon.SlowChargesPerPotion;
                WarManager.Notify($"Лук бойца смазан зельем замедления: {bow.Name}", true);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                slowPending.Remove(bow);
                if (ResourceManager.Instance != null) ResourceManager.Instance.AddSlowPotions(1);
            }
        });
    }

    public static void Salute(CitizenAgent s, BarracksController br)
    {
        if (SaluteProblem(s, br) != "") return;
        Weapon bow = BowOf(s);
        if (!ArmoryController.BestFireworkKit(out ModuleKit kit) || !EngineerYard.TakeKit(kit, out BuildingInstance src)) return;
        salutePending.Add(bow);
        int carry = CarryLedger.Add(kit);
        BuildingInstance dest = br.BuildingRef;
        WarManager.Notify($"{s.UnitName} {s.CitizenName}: несут Фейерверочный лук в казарму", true);
        Logistics.Request(new HaulJob
        {
            from = src != null ? src : Hall(),
            to = dest,
            prefer = dest,
            onDeliver = () =>
            {
                CarryLedger.Done(carry);
                salutePending.Remove(bow);
                int mk = Mathf.Clamp(kit.mk, 1, 3);
                bow.saluteMk = Mathf.Max(bow.saluteMk, mk);
                bow.saluteCharges += Weapon.SaluteChargesOf(mk);
                WarManager.Notify($"На лук бойца наложен салют: {bow.Name}", true);
            },
            onAbort = loaded =>
            {
                CarryLedger.Done(carry);
                salutePending.Remove(bow);
                EngineerYard.AddToHall(kit);
            }
        });
    }

    public static void Core(CitizenAgent s, BarracksController br)
    {
        if (CoreProblem(s, br) != "") return;
        MagicArmory.ApplyCoreToShield(ShieldBearer.ShieldOf(s), br.BuildingRef);
    }
}
