using UnityEngine;
using System.Collections.Generic;

/// <summary>Боец, ушедший в лагерь наёмников гильдии (продаётся или куплен): вид войск и всё, что при нём.</summary>
public class SoldierLot
{
    public UnitType unit;
    public bool hearty;          // обучен на похлёбке (★)
    public Weapon weapon;
    public Shield shield;
    public BodyGear body;        // броня или плащ (с усилением и износом)
    public int chaosMk, chaosCharges;   // ядро хаоса у Мага стихий (chaosCharges 0 - нет)
    public string name;          // имя бойца (для своих) или прозвище наёмника
    public bool ramBroken;       // носильщик тарана: таран сломан
    public float ramWear;        // ... или побит (доля выбитой прочности)
}

public enum ItemKind { Weapon, Shield, Module, Soldier, Trap, Barricade, Body, Squad }   // новые виды - только в конец

/// <summary>
/// Штучный товар рынка: оружие, щит, броня или плащ, модуль для башни (и ядро хаоса), ловушка или боец.
/// Вещь уходит на рынок со всем, что на неё наложено: улучшения, доводка, заряды, ядро хаоса на щите, усиление брони;
/// боец - со своим оружием, щитом, бронёй, конём, похлёбкой и ядром хаоса. В отличие от сырья, каждый - единственный в своём роде
/// (качество, улучшения, доводка, заряды яда/замедления, марка модуля, снаряжение бойца).
///  - Продажа: вещь берут из Оружейной / двора / Цитадели и несут в Торговый дом.
///    Боец уходит в лагерь наёмников гильдии сразу (вместе с оружием, щитом и конём).
///  - Ловушки (обычные и боевые) лежат в запасе Цитадели (TrapStock): так обычные ловушки инженеров
///    попадают к игроку Военного дела, который переделывает их в боевые.
///  - Покупка: купец привозит вещь в Торговый дом; оружие и щиты несут в Оружейную, модули и ловушки - в Цитадель
///    (поставить их на башню может любой город), боец сходит с повозки и идёт в казарму.
/// Цена - по ценности вещи (Value) и рыночному спросу.
/// </summary>
public class MarketItem
{
    public ItemKind kind;
    public Weapon weapon;
    public Shield shield;
    public BodyGear body;
    public ModuleKit kit;
    public TrapKit trap;
    public SoldierLot soldier;
    public List<SoldierLot> squadLots;   // отряд целиком: все бойцы со своим снаряжением
    public FieldGear.Item gear;   // заслон, пункт, полевая башня из запаса Цитадели (рынок игроков)

    // Пока вещь ещё в городе (кандидат на продажу) - откуда её брать
    public ArmoryController armory;
    public CitizenAgent agent;
    public BarracksController barracks;
    public Squad squad;          // отряд (пока он в казарме)

    /// <summary>Войска: один боец или отряд целиком (уходят в лагерь наёмников, приезжают на повозке).</summary>
    public bool IsTroops => kind == ItemKind.Soldier || kind == ItemKind.Squad;

    /// <summary>Вкладка рынка: 3 - оружие, щиты и броня, 4 - модули и ловушки, 5 - войска, 6 - заслоны.</summary>
    public int Category => kind == ItemKind.Barricade ? 6 : kind == ItemKind.Module || kind == ItemKind.Trap ? 4 : (IsTroops ? 5 : 3);

    static readonly float[] QMul = { 1f, 1.7f, 2.8f };
    static readonly int[] ModuleValue = { 45, 75, 110 };

    // ---------- Название и описание ----------

    public string Name
    {
        get
        {
            switch (kind)
            {
                case ItemKind.Weapon: return Cap(weapon.Name);
                case ItemKind.Shield: return Cap(shield.Name);
                case ItemKind.Body: return body != null ? body.Name : "Броня";
                case ItemKind.Module: return TowerModules.KitName(kit);
                case ItemKind.Trap: return trap != null ? trap.Name : "Ловушка";
                case ItemKind.Barricade: return gear != null ? FieldPost.Specs[(int)gear.kind].name + $" (сила {gear.strength * 100f:F0}%)" : "Заслон";
                case ItemKind.Squad:
                {
                    int n = SquadCount;
                    return (squad != null ? squad.Name : "Отряд") + $" ({n} {Fighters(n)})";
                }
                default:
                    UnitType u = SoldierUnit;
                    string star = SoldierHearty ? " ★" : "";
                    return (u != null ? u.unitName : "Боец") + star + (SoldierName != "" ? " " + SoldierName : "");
            }
        }
    }

    public string Details
    {
        get
        {
            switch (kind)
            {
                case ItemKind.Weapon: return weapon.StatsText;
                case ItemKind.Shield: return shield.StatsText;
                case ItemKind.Body: return body != null ? body.StatsText : "";
                case ItemKind.Module: return TowerModules.Stats(kit.kind, kit.mk);
                case ItemKind.Trap: return trap != null ? Traps.Details(trap) + (trap.combat ? " (ставится в бою)" : " (ставится в мирное время)") : "";
                case ItemKind.Barricade:
                    if (gear == null) return "";
                    FieldPost.Spec sp = FieldPost.Specs[(int)gear.kind];
                    return sp.info + $". Прочность {sp.hp * gear.strength:F0}" + (gear.MaxPool > 0f ? $", лечебный запас {gear.pool:F0}/{gear.MaxPool:F0}" : "");
                case ItemKind.Squad: return SquadDetails;
                default:
                    UnitType u = SoldierUnit;
                    if (u == null) return "";
                    var parts = new List<string> { $"здоровье {u.maxHp:F0}, урон {u.damage:F0}, мест {u.spaceOccupied}" };
                    Weapon w = SoldierWeapon;
                    Shield sh = SoldierShield;
                    if (w != null) parts.Add(w.Name);
                    if (sh != null) parts.Add(sh.Name);
                    BodyGear bg = SoldierBody;
                    if (bg != null) parts.Add(bg.Name);
                    if (SoldierChaos > 0) parts.Add("ядро хаоса");
                    if (u.requiresHorse) parts.Add("с конём");
                    if (u.IsRam) parts.Add(SoldierRamBroken ? "таран сломан" : "с тараном");
                    return string.Join(", ", parts);
            }
        }
    }

    static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

    UnitType SoldierUnit => soldier != null ? soldier.unit : (agent != null ? agent.ExpUnit : null);
    bool SoldierHearty => soldier != null && soldier.hearty;
    string SoldierName => soldier != null ? (soldier.name ?? "") : (agent != null ? agent.CitizenName : "");
    Weapon SoldierWeapon => soldier != null ? soldier.weapon : (agent != null ? agent.EquippedWeapon : null);
    Shield SoldierShield => soldier != null ? soldier.shield : (agent != null ? ShieldBearer.ShieldOf(agent) : null);
    BodyGear SoldierBody => soldier != null ? soldier.body : (agent != null ? agent.EquippedBody : null);
    int SoldierChaos => soldier != null ? soldier.chaosCharges : (agent != null && agent.HasChaosCore ? agent.ChaosCharges : 0);
    bool SoldierRamBroken => soldier != null ? soldier.ramBroken : (agent != null && agent.RamBroken);

    /// <summary>Сколько места займёт боец (отряд - все его бойцы) в казарме.</summary>
    public int Space
    {
        get
        {
            if (kind != ItemKind.Squad) return SoldierUnit != null ? SoldierUnit.spaceOccupied : 1;
            int sp = 0;
            if (squadLots != null) foreach (var l in squadLots) sp += l != null && l.unit != null ? l.unit.spaceOccupied : 1;
            else foreach (var a in SquadMembers()) sp += a.ExpUnit != null ? a.ExpUnit.spaceOccupied : 1;
            return Mathf.Max(1, sp);
        }
    }

    /// <summary>Сколько людей приедет (жильё для наёмников).</summary>
    public int People => kind == ItemKind.Squad ? Mathf.Max(1, SquadCount) : 1;

    // ---------- Отряд ----------

    static string Fighters(int n)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return "боец";
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "бойца";
        return "бойцов";
    }

    // Бойцы отряда, пока он в городе (живые, свои для этой казармы)
    List<CitizenAgent> SquadMembers()
    {
        var list = new List<CitizenAgent>();
        if (squad == null) return list;
        foreach (var a in squad.Members) if (a != null && !a.IsDead && a.ExpUnit != null) list.Add(a);
        return list;
    }

    /// <summary>Первый боец отряда (для картинки).</summary>
    public UnitType LeadUnit
    {
        get
        {
            if (kind == ItemKind.Soldier) return SoldierUnit;
            if (squadLots != null) { foreach (var l in squadLots) if (l != null && l.unit != null) return l.unit; return null; }
            var m = SquadMembers();
            return m.Count > 0 ? m[0].ExpUnit : null;
        }
    }

    int SquadCount => squadLots != null ? squadLots.Count : SquadMembers().Count;

    // Состав: «2 Мечник ★, Лучник (меч, броня)…» - кратко, с главным снаряжением
    string SquadDetails
    {
        get
        {
            var rows = new List<string>();
            if (squadLots != null)
                foreach (var l in squadLots)
                {
                    if (l == null || l.unit == null) continue;
                    rows.Add(SoldierLine(l.unit, l.hearty, l.weapon, l.shield, l.body, l.chaosCharges > 0, l.ramBroken));
                }
            else
                foreach (var a in SquadMembers())
                    rows.Add(SoldierLine(a.ExpUnit, a.IsHearty, a.EquippedWeapon, ShieldBearer.ShieldOf(a), a.EquippedBody, a.HasChaosCore, a.RamBroken));
            return string.Join("; ", rows);
        }
    }

    static string SoldierLine(UnitType u, bool hearty, Weapon w, Shield sh, BodyGear bg, bool chaos, bool ramBroken)
    {
        var gear = new List<string>();
        if (w != null) gear.Add(w.Name);
        if (sh != null) gear.Add(sh.Name);
        if (bg != null) gear.Add(bg.Name);
        if (chaos) gear.Add("ядро хаоса");
        if (u.requiresHorse) gear.Add("конь");
        if (u.IsRam) gear.Add(ramBroken ? "таран сломан" : "таран");
        return u.unitName + (hearty ? " ★" : "") + (gear.Count > 0 ? " (" + string.Join(", ", gear) + ")" : "");
    }

    static float SoldierValue(UnitType u, bool hearty, bool chaos, Weapon w, Shield sh, BodyGear bg)
    {
        if (u == null) return 10f;
        float v = u.costGold * 0.22f + u.costFood * 0.1f + u.maxHp * 0.05f
                + (u.requiresHorse ? 20f : 0f) + (hearty ? 8f : 0f)
                + u.resinCost * 2f + u.potionCost * 6f;
        if (u.IsRam) v = v / u.Crew + u.planksCost * 1.5f / u.Crew;   // цена тарана - на двоих
        if (chaos) v += 40f;
        return v + WeaponValue(w) + ShieldValue(sh) + BodyValue(bg);
    }

    // ---------- Ценность (базовая цена в марках) ----------

    static float WeaponValue(Weapon w)
    {
        if (w == null) return 0f;
        float v = 10f * QMul[(int)w.quality] + w.upgrade * 12f + (w.forged ? w.forgeStars * 6f : 0f)
                + w.poisonCharges / 6f + w.slowCharges / 5f + w.saluteCharges * (0.3f + 0.1f * w.saluteMk);
        return v * (0.4f + 0.6f * w.Frac);
    }

    static float ShieldValue(Shield s)
    {
        if (s == null) return 0f;
        return (9f * QMul[(int)s.quality] + s.upgrade * 10f + s.chaosMk * 15f) * (0.4f + 0.6f * s.Frac);
    }

    static float BodyValue(BodyGear g)
    {
        if (g == null) return 0f;
        float v = g.IsArmor ? 14f * QMul[(int)g.quality] + g.upgrade * 16f : 8f * QMul[(int)g.quality];
        return v * (0.4f + 0.6f * g.Frac);
    }

    static float KitValue(ModuleKit k)
    {
        float v = ModuleValue[Mathf.Clamp(k.mk, 1, 3) - 1];
        if (k.kind != ModuleKind.ChaosCore) return v;
        // Ядро хаоса - редкость школы Хаоса; начатое стоит меньше
        int full = ChaosMagic.Charges(Mathf.Clamp(k.mk, 1, 3));
        float left = k.charges > 0 && full > 0 ? Mathf.Clamp01(k.charges / (float)full) : 1f;
        return v * 1.6f * (0.3f + 0.7f * left);
    }

    public float Value
    {
        get
        {
            switch (kind)
            {
                case ItemKind.Weapon: return WeaponValue(weapon);
                case ItemKind.Shield: return ShieldValue(shield);
                case ItemKind.Body: return BodyValue(body);
                case ItemKind.Module: return KitValue(kit);
                case ItemKind.Trap: return trap != null ? Traps.Value(trap) : 10f;
                case ItemKind.Barricade:
                {
                    if (gear == null) return 10f;
                    FieldPost.Spec sp = FieldPost.Specs[(int)gear.kind];
                    return (sp.wood * 0.5f + sp.planks * 3f + sp.steel * 6f) * gear.strength;
                }
                case ItemKind.Squad:
                {
                    float sum = 0f;
                    if (squadLots != null)
                        foreach (var l in squadLots) { if (l != null) sum += SoldierValue(l.unit, l.hearty, l.chaosCharges > 0, l.weapon, l.shield, l.body); }
                    else
                        foreach (var a in SquadMembers())
                            sum += SoldierValue(a.ExpUnit, a.IsHearty, a.HasChaosCore, a.EquippedWeapon, ShieldBearer.ShieldOf(a), a.EquippedBody);
                    return Mathf.Max(10f, sum);
                }
                default:
                    return SoldierValue(SoldierUnit, SoldierHearty, SoldierChaos > 0, SoldierWeapon, SoldierShield, SoldierBody);
            }
        }
    }

    // ---------- Что в городе можно продать ----------

    public static List<MarketItem> SellableNow(int category)
    {
        var list = new List<MarketItem>();
        if (category == 3)
        {
            foreach (var a in ArmoryController.All)
            {
                if (a == null || !a.Works) continue;
                foreach (var w in a.Stock) list.Add(new MarketItem { kind = ItemKind.Weapon, weapon = w, armory = a });
                foreach (var s in a.Shields) list.Add(new MarketItem { kind = ItemKind.Shield, shield = s, armory = a });
                foreach (var g in a.Bodies) list.Add(new MarketItem { kind = ItemKind.Body, body = g, armory = a });
            }
        }
        else if (category == 4)
        {
            foreach (var k in EngineerYard.AllKits()) list.Add(new MarketItem { kind = ItemKind.Module, kit = k });
            foreach (var t in TrapStock.Hall) list.Add(new MarketItem { kind = ItemKind.Trap, trap = t });
        }
        else if (category == 6)
        {
            foreach (var g in FieldGear.All()) list.Add(new MarketItem { kind = ItemKind.Barricade, gear = g });
        }
        else if (category == 5)
        {
            foreach (var b in BuildingInstance.All)
            {
                if (b == null) continue;
                BarracksController br = b.GetComponent<BarracksController>();
                if (br == null) continue;
                // Отряды целиком (от двух бойцов): со всем снаряжением, конями, похлёбкой и тараном
                foreach (var sq in br.Squads)
                {
                    if (sq == null || sq.OnExpedition) continue;
                    var it = new MarketItem { kind = ItemKind.Squad, squad = sq, barracks = br };
                    if (it.SquadCount >= 2 && it.SellProblem() == "") list.Add(it);
                }
                foreach (var s in br.Soldiers)
                {
                    if (s == null || s.IsDead || s.IsOnExpedition || s.InCombat || s.ExpUnit == null) continue;
                    list.Add(new MarketItem { kind = ItemKind.Soldier, agent = s, barracks = br });
                }
            }
        }
        return list;
    }

    /// <summary>Вещь ещё на месте и её можно забрать на продажу.</summary>
    public string SellProblem()
    {
        switch (kind)
        {
            case ItemKind.Weapon:
            case ItemKind.Shield:
            case ItemKind.Body:
                if (armory == null || !armory.Works) return "Оружейная недоступна";
                return "";
            case ItemKind.Soldier:
                if (agent == null || agent.IsDead || agent.IsOnExpedition) return "Боец недоступен";
                if (agent.InCombat) return "Боец в бою";
                return "";
            case ItemKind.Squad:
            {
                if (squad == null || barracks == null) return "Отряд недоступен";
                if (squad.OnExpedition) return "Отряд в походе";
                var m = SquadMembers();
                if (m.Count == 0) return "В отряде никого нет";
                foreach (var a in m)
                {
                    if (a.IsOnExpedition) return "Кто-то из отряда в походе";
                    if (a.InCombat) return "Отряд в бою";
                }
                return "";
            }
            case ItemKind.Trap:
                return trap != null && TrapStock.Hall.Contains(trap) ? "" : "Ловушки уже нет в запасе";
            case ItemKind.Barricade:
                return gear != null && FieldGear.Has(gear) ? "" : "Этого уже нет в запасе Цитадели";
            default: return "";
        }
    }

    /// <summary>Забрать вещь из города под продажу. source - откуда её несут (null - нести не нужно: боец ушёл сам).</summary>
    public bool TakeFromTown(out BuildingInstance source)
    {
        source = null;
        switch (kind)
        {
            case ItemKind.Weapon:
                if (armory == null || !armory.RemoveFromStock(weapon)) return false;
                source = armory.Building;
                armory = null;
                return true;
            case ItemKind.Shield:
                if (armory == null || !armory.RemoveShield(shield)) return false;
                source = armory.Building;
                armory = null;
                return true;
            case ItemKind.Body:
                if (armory == null || !armory.RemoveBody(body)) return false;
                source = armory.Building;
                armory = null;
                return true;
            case ItemKind.Module:
                return EngineerYard.TakeKit(kit, out source);
            case ItemKind.Barricade:
            {
                if (!FieldGear.Remove(gear)) return false;
                var pmb = PopulationManager.Instance;
                source = pmb != null ? pmb.Hall : null;
                return true;
            }
            case ItemKind.Trap:
            {
                if (!TrapStock.Remove(trap)) return false;
                var pm = PopulationManager.Instance;
                source = pm != null ? pm.Hall : null;
                return true;
            }
            case ItemKind.Squad:
            {
                if (barracks == null || squad == null || SellProblem() != "") return false;
                var lots = new List<SoldierLot>();
                foreach (var a in SquadMembers())
                {
                    BarracksController br = a.Barracks != null ? a.Barracks.GetComponent<BarracksController>() : barracks;
                    if (TakeSoldier(br != null ? br : barracks, a, out SoldierLot l)) lots.Add(l);
                }
                if (lots.Count == 0) return false;
                squadLots = lots;
                squad = null;
                barracks = null;
                return true;
            }
            default:
            {
                if (!TakeSoldier(barracks, agent, out SoldierLot lot)) return false;
                soldier = lot;
                agent = null;
                barracks = null;
                return true;
            }
        }
    }

    // Боец уходит в лагерь наёмников со всем, что при нём: оружие, щит, конь, броня, ядро хаоса, таран
    static bool TakeSoldier(BarracksController br, CitizenAgent a, out SoldierLot lot)
    {
        lot = null;
        if (br == null || a == null) return false;
        // Броня и ядро хаоса уходят вместе с бойцом (иначе казарма вернула бы их в город)
        BodyGear bg = a.UnequipBody();
        bool core = a.TakeChaosCoreForSale(out int cMk, out int cCharges);
        bool ramBroken = a.RamBroken;
        float ramWear = a.RamWear;
        if (!br.ReleaseForSale(a, out lot))
        {
            if (bg != null && !a.EquipBody(bg) && !ArmoryController.StashBody(bg))
                WarManager.Notify($"В Оружейных нет места: {bg.Name} потеряна", false);
            if (core) a.LoadChaosCore(new ModuleKit { kind = ModuleKind.ChaosCore, mk = cMk, charges = cCharges });
            lot = null;
            return false;
        }
        lot.body = bg;
        if (core) { lot.chaosMk = cMk; lot.chaosCharges = cCharges; }
        lot.ramBroken = ramBroken;
        lot.ramWear = ramBroken ? 0f : ramWear;
        return true;
    }

    // ---------- Получение вещи городом ----------

    /// <summary>Можно ли принять эту вещь (купить / вернуть): pendingGear, pendingSpace - что уже везут.</summary>
    public static string ReceiveProblem(ItemKind kind, int space, int pendingGear, int pendingSpace, int pendingPeople, int people = 1)
    {
        switch (kind)
        {
            case ItemKind.Weapon:
            case ItemKind.Shield:
            case ItemKind.Body:
                if (ArmoryController.All.Count == 0) return "Нужна Оружейная";
                if (ArmoryController.FreeSlotsTotal - pendingGear <= 0) return "В Оружейных нет места";
                return "";
            case ItemKind.Squad:
            case ItemKind.Soldier:
                var pm = PopulationManager.Instance;
                if (BarracksController.WithRoomFor(space + pendingSpace) == null)
                    return kind == ItemKind.Squad ? $"Нет казармы, где поместится весь отряд ({space} мест)" : "Нет места в казармах";
                // Наёмники живут в Гостевом доме; нет в нём мест - в обычных домах, если там есть место
                if (pm != null && pm.MercenaryRoom - pendingPeople < Mathf.Max(1, people))
                    return pm.GuestCapacity > 0 ? "Нет мест: Гостевые дома полны" : "Нет жилья для наёмника — постройте Гостевой дом";
                return "";
            default: return "";
        }
    }

    /// <summary>
    /// Вещь доставлена к зданию at (Торговый дом или Цитадель). Оружие и щиты несут в Оружейную, модуль - в Цитадель,
    /// боец сходит с повозки и становится бойцом казармы. onDone - когда вещь окончательно на месте.
    /// </summary>
    public void Receive(BuildingInstance at, System.Action onDone)
    {
        var pm = PopulationManager.Instance;
        BuildingInstance hall = pm != null ? pm.Hall : null;

        if (kind == ItemKind.Soldier)
        {
            ArriveSoldier(at);
            if (onDone != null) onDone();
            return;
        }
        if (kind == ItemKind.Squad)
        {
            ArriveSquad(at);
            if (onDone != null) onDone();
            return;
        }

        if (kind == ItemKind.Module || kind == ItemKind.Trap || kind == ItemKind.Barricade)
        {
            System.Action store = () =>
            {
                if (kind == ItemKind.Module) EngineerYard.AddToHall(kit);
                else if (kind == ItemKind.Barricade) FieldGear.Add(gear);
                else TrapStock.Add(trap);
            };
            if (at == null || hall == null || at == hall) { store(); if (onDone != null) onDone(); return; }
            // Выход из игры в пути - модуль или ловушка после входа в Цитадели (final-1)
            int carryK = kind == ItemKind.Module ? CarryLedger.Add(kit) : (kind == ItemKind.Trap ? CarryLedger.Add(trap) : 0);
            Logistics.Request(new HaulJob
            {
                from = at, to = hall, prefer = at,
                onDeliver = () => { CarryLedger.Done(carryK); store(); if (onDone != null) onDone(); },
                onAbort = loaded => { CarryLedger.Done(carryK); store(); if (onDone != null) onDone(); }
            });
            return;
        }

        // Оружие, щиты и броня - в Оружейную, где есть место
        ArmoryController dest = null;
        foreach (var a in ArmoryController.All)
            if (a != null && a.Works && a.ItemCount < a.Capacity) { dest = a; break; }

        if (dest == null || at == null)
        {
            StashOrHold();
            if (onDone != null) onDone();
            return;
        }
        string carryKey = kind == ItemKind.Weapon ? (weapon != null ? Spoils.Of(weapon) : null)
                        : kind == ItemKind.Body ? (body != null ? Spoils.Of(body) : null)
                        : (shield != null ? Spoils.Of(shield) : null);
        int carryW = CarryLedger.Add(carryKey, 1);
        Logistics.Request(new HaulJob
        {
            from = at, to = dest.Building, prefer = at,
            onDeliver = () => { CarryLedger.Done(carryW); StashOrHold(); if (onDone != null) onDone(); },
            onAbort = loaded => { CarryLedger.Done(carryW); StashOrHold(); if (onDone != null) onDone(); }
        });
    }

    // Положить в Оружейную; нет места - вещь ждёт в Торговом доме (гильдия попробует позже)
    void StashOrHold()
    {
        bool ok = TryStash();
        if (ok) WarManager.Notify($"В Оружейную принесли: {Name}", true);
        else if (TradeGuild.Instance != null) TradeGuild.Instance.HoldGear(this);
    }

    /// <summary>Повторная попытка положить вещь в Оружейную (место освободилось).</summary>
    public bool TryStash()
    {
        if (kind == ItemKind.Body) return ArmoryController.StashBody(body);
        return kind == ItemKind.Weapon ? ArmoryController.Stash(weapon) : ArmoryController.StashShield(shield);
    }

    // Отряд сходит с повозки: все - в одну казарму, новым отрядом (нет такой казармы - каждый ищет место сам)
    void ArriveSquad(BuildingInstance at)
    {
        var pm = PopulationManager.Instance;
        if (pm == null || squadLots == null || squadLots.Count == 0) return;
        var lots = squadLots.FindAll(l => l != null && l.unit != null);
        if (lots.Count == 0) return;
        Vector2 pos = at != null ? DoorOf(at) : (pm.Hall != null ? pm.Hall.Center : Vector2.zero);
        int space = 0;
        foreach (var l in lots) space += l.unit.spaceOccupied;
        BarracksController br = BarracksController.WithRoomFor(space);
        if (br == null)
        {
            WarManager.Notify($"Отряд прибыл, но ни в одной казарме нет {space} мест — бойцы расходятся по свободным", false);
            foreach (var l in lots) ArriveSoldier(at, l);
            return;
        }
        Squad sq = br.NewSquadForArrivals();
        foreach (var l in lots)
        {
            CitizenAgent a = pm.SpawnAgent(pos + Random.insideUnitCircle * 0.4f, true);
            br.AdoptSoldier(a, l.unit, l.hearty, l.weapon, l.shield, sq);
            DressArrival(a, l);
        }
        WarManager.Notify($"Отряд прибыл: {lots.Count} {Fighters(lots.Count)} — идут в казарму ({sq.Name})", true);
    }

    // Броня, ядро хаоса и таран прибывшего бойца
    static void DressArrival(CitizenAgent a, SoldierLot l)
    {
        if (l.body != null && !a.EquipBody(l.body) && !ArmoryController.StashBody(l.body))
            WarManager.Notify($"{l.body.Name} наёмника: некуда деть — потеряна", false);
        if (l.chaosCharges > 0)
        {
            var core = new ModuleKit { kind = ModuleKind.ChaosCore, mk = Mathf.Clamp(l.chaosMk, 1, 3), charges = l.chaosCharges };
            if (a.IsElementalist) a.LoadChaosCore(core);
            else EngineerYard.AddToHall(core);
        }
        if (a.IsRamCarrier) a.RestoreRam(l.ramBroken, l.ramWear);
    }

    // Наёмник сходит с повозки у здания at и идёт в казарму
    void ArriveSoldier(BuildingInstance at) { ArriveSoldier(at, soldier); }

    static void ArriveSoldier(BuildingInstance at, SoldierLot soldier)
    {
        var pm = PopulationManager.Instance;
        if (pm == null || soldier == null || soldier.unit == null) return;
        Vector2 pos = at != null ? DoorOf(at) : (pm.Hall != null ? pm.Hall.Center : Vector2.zero);

        CitizenAgent a = pm.SpawnAgent(pos, true);   // наёмник: живёт в Гостевом доме
        BarracksController br = BarracksController.WithRoomFor(soldier.unit.spaceOccupied);
        if (br == null)
        {
            // Казарм нет / все полны: наёмник остаётся простым жителем, снаряжение - на склад
            if (soldier.weapon != null && !ArmoryController.Stash(soldier.weapon)) WarManager.Notify("Оружие наёмника некуда деть — потеряно", false);
            if (soldier.shield != null && !ArmoryController.StashShield(soldier.shield)) WarManager.Notify("Щит наёмника некуда деть — потерян", false);
            if (soldier.body != null && !ArmoryController.StashBody(soldier.body)) WarManager.Notify($"{soldier.body.Name} наёмника: некуда деть — потеряна", false);
            if (soldier.chaosCharges > 0) EngineerYard.AddToHall(new ModuleKit { kind = ModuleKind.ChaosCore, mk = Mathf.Clamp(soldier.chaosMk, 1, 3), charges = soldier.chaosCharges });
            WarManager.Notify($"{soldier.unit.unitName}: в казармах нет места — наёмник остался в городе простым жителем", false);
            return;
        }
        br.AdoptSoldier(a, soldier.unit, soldier.hearty, soldier.weapon, soldier.shield);
        DressArrival(a, soldier);
        WarManager.Notify($"Наёмник прибыл: {soldier.unit.unitName}{(soldier.hearty ? " ★" : "")} — идёт в казарму", true);
    }

    static Vector2 DoorOf(BuildingInstance b)
    {
        var nav = GridNavigation.Instance;
        if (nav != null && nav.TryGetDoorCell(b, nav.WorldToCell(b.Center), out Vector2Int d)) return nav.CellCenter(d);
        return b.Center;
    }

    // ---------- Товар купцов ----------

    static QualityTier RollTier(float good, float best)
    {
        float r = Random.value;
        return r < best ? QualityTier.Best : (r < best + good ? QualityTier.Good : QualityTier.Medium);
    }

    static int RollUpgrade()
    {
        float r = Random.value;
        return r < 0.05f ? 2 : (r < 0.2f ? 1 : 0);
    }

    static Weapon RandomWeapon(WeaponKind k)
    {
        Weapon w = new Weapon(k, RollTier(0.3f, 0.1f));
        for (int i = RollUpgrade(); i > 0; i--) w.ApplyUpgrade();
        if (Random.value < 0.15f) w.ApplyForge(Random.Range(0.4f, 1f), Random.Range(0.4f, 1f), Random.Range(0.4f, 1f), 1f);
        if (k == WeaponKind.Bow && Random.value < 0.12f) w.poisonCharges = Weapon.PoisonChargesPerPotion;
        if (k == WeaponKind.Bow && Random.value < 0.12f) w.slowCharges = Weapon.SlowChargesPerPotion;
        if (k == WeaponKind.Bow && Random.value < 0.06f) { w.saluteMk = 1; w.saluteCharges = Weapon.SaluteChargesOf(1); }   // редкий лук пиротехников
        w.durability = w.maxDurability * Random.Range(0.6f, 1f);
        return w;
    }

    static Shield RandomShield()
    {
        Shield s = new Shield(RollTier(0.3f, 0.1f));
        for (int i = RollUpgrade(); i > 0; i--) s.ApplyUpgrade();
        s.durability = s.maxDurability * Random.Range(0.6f, 1f);
        return s;
    }

    static BodyGear RandomArmor()
    {
        var g = new BodyGear(BodyKind.Armor, RollTier(0.3f, 0.1f));
        for (int i = RollUpgrade(); i > 0; i--) g.ApplyUpgrade();
        g.durability = g.maxDurability * Random.Range(0.6f, 1f);
        return g;
    }

    static readonly string[] Nicknames = { "Хромой", "Рыжий", "Молчун", "Ворон", "Седой", "Бывалый", "Кривой", "Удалой", "Тихий", "Лютый" };

    // Наёмников можно купить любых, в том числе чужого пути
    static readonly string[] MercPool = { "Мечник", "Мечник", "Лучник", "Лучник", "Рыцарь", "Всадник", "Конный лучник", "Полевой маг",
                                          BarracksController.PoisonerName, BarracksController.FireBomberName };

    /// <summary>Случайный штучный товар для купца: category 3 - оружие/щит, 4 - модуль или ловушка, 5 - наёмник. null - сейчас нечего.</summary>
    public static MarketItem RandomBotItem(int category)
    {
        if (category == 3)
        {
            float r = Random.value;
            if (r < 0.4f) return new MarketItem { kind = ItemKind.Weapon, weapon = RandomWeapon(WeaponKind.Sword) };
            if (r < 0.72f) return new MarketItem { kind = ItemKind.Weapon, weapon = RandomWeapon(WeaponKind.Bow) };
            if (r < 0.8f) return new MarketItem { kind = ItemKind.Weapon, weapon = RandomWeapon(WeaponKind.Shot) };   // бронебойное ядро для полевой пушки
            if (r < 0.9f) return new MarketItem { kind = ItemKind.Body, body = RandomArmor() };
            return new MarketItem { kind = ItemKind.Shield, shield = RandomShield() };
        }
        if (category == 4)
        {
            // Купцы возят и обычные ловушки инженеров (резина у них своя) - так ловушки достаются и другим путям
            if (Random.value < 0.45f) return new MarketItem { kind = ItemKind.Trap, trap = Traps.RandomKit() };
            float r = Random.value;
            int mk = r < 0.6f ? 1 : (r < 0.9f ? 2 : 3);
            float pick = Random.value;
            ModuleKind mkind = Rivers.Any && pick < 0.25f ? ModuleKind.Drawbridge : ModuleKind.SteamRepeller;
            if (pick >= 0.5f && pick < 0.65f) mkind = ModuleKind.CannonLift;
            else if (pick >= 0.65f && pick < 0.8f) mkind = MagicShells.Kinds[Random.Range(0, MagicShells.Kinds.Length)];   // пачка снарядов (полная)
            else if (pick >= 0.8f && pick < 0.87f) mkind = ModuleKind.FireworkBow;   // редкий товар пиротехников
            else if (pick >= 0.87f && pick < 0.92f) mkind = ModuleKind.ChaosCore;    // редкое ядро хаоса (школа Хаоса)
            return new MarketItem { kind = ItemKind.Module, kit = new ModuleKit { kind = mkind, mk = mk } };
        }

        // Наёмник: вид войск берём из казармы игрока (нет казарм - наёмники не предлагаются)
        UnitType u = BarracksController.FindType(MercPool[Random.Range(0, MercPool.Length)]);
        if (u == null) return null;
        var lot = new SoldierLot
        {
            unit = u,
            hearty = u.UsesPottage && Random.value < 0.25f,
            name = "«" + Nicknames[Random.Range(0, Nicknames.Length)] + "»"
        };
        if (u.usesWeapon && Random.value < 0.6f) lot.weapon = RandomWeapon(u.weaponKind);
        if (u.usesShield && Random.value < 0.4f) lot.shield = RandomShield();
        if (Random.value < 0.15f) lot.body = RandomArmor();
        return new MarketItem { kind = ItemKind.Soldier, soldier = lot };
    }
}
