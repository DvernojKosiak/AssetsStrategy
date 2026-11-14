using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Работа мастерских переживает выход из игры и сворачивание (обновление final-1).
/// Здание сохраняет, что оно делает и сколько осталось, а при загрузке продолжает с того же места.
/// Пока игрока не было, работа шла: остаток уменьшается на время отсутствия (Away).
/// Вещи, которые при сохранении были в пути (несли в Цитадель, в Оружейную...), при загрузке кладутся сразу на место,
/// но только когда весь город уже загружен (AfterLoad): склады Цитадели заполняются в CitySave.ApplyCity.
/// </summary>
public static class JobSave
{
    /// <summary>Сколько секунд игрока не было (ставится перед загрузкой зданий, 0 - если неизвестно).</summary>
    public static double Away { get; private set; }

    static readonly List<Action> after = new List<Action>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Away = 0.0; after.Clear(); }

    public static void BeginLoad(double awaySeconds)
    {
        Away = Math.Max(0.0, Math.Min(awaySeconds, 14 * 86400.0));
        after.Clear();
    }

    /// <summary>Остаток таймера с учётом отсутствия. Не меньше 0.05 с: работа закончится в первом же кадре.</summary>
    public static float Left(float saved)
    {
        if (saved <= 0f) return 0.05f;
        return Mathf.Max(0.05f, (float)(saved - Away));
    }

    /// <summary>Выполнить, когда весь город загружен (склады Цитадели и запасы уже на месте).</summary>
    public static void AfterLoad(Action a) { if (a != null) after.Add(a); }

    public static void RunAfterLoad()
    {
        var list = new List<Action>(after);
        after.Clear();
        foreach (var a in list)
        {
            try { a(); }
            catch (Exception e) { Debug.LogWarning("JobSave: " + e.Message); }
        }
        Away = 0.0;
    }
}

/// <summary>
/// Вещи в руках у жителей: модули, ядра, ловушки, оружие, зелья, которые несут из здания в здание.
/// Раньше при выходе из игры (и при сворачивании - игра перезагружает город) в этот момент вещь пропадала:
/// рейс жителя не сохраняется. Теперь всё, что в пути, сохраняется с городом, а после входа лежит на складах
/// (Цитадель, Оружейная; не влезло - в сундуке трофеев, он разложит сам, когда появится место).
/// Начали нести - Add (вернёт номер), донесли или рейс сорван - Done(номер). Ключи вещей - как у трофеев (Spoils).
/// </summary>
public static class CarryLedger
{
    static readonly Dictionary<int, KeyValuePair<string, int>> items = new Dictionary<int, KeyValuePair<string, int>>();
    static int next = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Clear(); }

    /// <summary>Новый город загружается: рейсы прошлого города уже не долетят.</summary>
    public static void Clear() { items.Clear(); hosts.Clear(); }

    public static int Add(string key, int n)
    {
        int id = next++;
        if (!string.IsNullOrEmpty(key) && n > 0) items[id] = new KeyValuePair<string, int>(key, n);
        return id;
    }
    public static int Add(ModuleKit k) { return k.kind == ModuleKind.None ? next++ : Add(Spoils.Of(k), 1); }
    public static int Add(TrapKit k) { return k == null ? next++ : Add(Spoils.Of(k), 1); }
    public static int Add(Weapon w) { return w == null ? next++ : Add(Spoils.Of(w), 1); }
    public static int AddPotions(int n, bool slow) { return Add(slow ? Spoils.SlowPotions : Spoils.Potions, n); }
    public static void Done(int id) { items.Remove(id); hosts.Remove(id); }

    // Модуль / пачку снарядов несут ставить на постройку (sohranenie-1): после входа он уже стоит там,
    // а не лежит в Цитадели. Запись: модуль (SaveBag.Pack), клетка здания, номер гнезда (у пушки их два).
    static readonly Dictionary<int, string[]> hosts = new Dictionary<int, string[]>();

    /// <summary>Рейс id несёт модуль kit на постройку host (EngineerYard.Install).</summary>
    public static void ToHost(int id, ModuleKit kit, IModuleHost host)
    {
        if (host == null || host.Building == null || !items.ContainsKey(id)) return;
        var all = host.Building.GetComponents<IModuleHost>();
        int idx = System.Array.IndexOf(all, host);
        if (idx < 0) return;
        var row = new List<string>(SaveBag.Pack(kit))
        {
            SaveBag.I(host.Building.OriginCell.x), SaveBag.I(host.Building.OriginCell.y), SaveBag.I(idx)
        };
        hosts[id] = row.ToArray();
    }

    public static void Save(SaveBag bag)
    {
        var b = new SpoilBundle();
        var to = new List<string[]>();
        foreach (var kv in items)
        {
            if (hosts.TryGetValue(kv.Key, out string[] row)) { to.Add(row); continue; }
            b.Add(kv.Value.Key, kv.Value.Value);
        }
        bag.Set("carry.box", b.IsEmpty ? "" : b.Encode());
        bag.SetList("carry.to", to);
    }

    // Донести модуль до постройки (пока игрока не было). Не вышло (постройку снесли) - на склад, как раньше.
    static void DeliverToHost(string[] a, SpoilBundle rest)
    {
        ModuleKit kit = SaveBag.UnpackKit(a);
        if (kit.kind == ModuleKind.None) return;
        BuildingInstance b = CitySave.BuildingAt(SaveBag.At(a, 3, int.MinValue), SaveBag.At(a, 4, int.MinValue));
        int idx = SaveBag.At(a, 5, -1);
        IModuleHost host = null;
        if (b != null && !b.IsRuined)
        {
            var all = b.GetComponents<IModuleHost>();
            if (idx >= 0 && idx < all.Length) host = all[idx];
        }
        if (host == null || host.ModuleBusy)
        {
            rest.Add(Spoils.Of(kit), 1);
            return;
        }
        if (host.HasModule) EngineerYard.AddToHall(host.TakeModule());   // старый - на склад
        host.SetModule(kit.kind, kit.mk);
        if (host is CannonShellSlot slot) slot.SetCharges(kit.charges);
        if (!host.HasModule) { rest.Add(Spoils.Of(kit), 1); return; }
        WarManager.Notify($"{b.type.name}: пока вас не было, поставили {TowerModules.KitName(kit)}", true);
    }

    /// <summary>После того как склады загружены: всё, что несли, - на склады; не влезло - в сундук трофеев.</summary>
    public static void Load(SaveBag bag)
    {
        Clear();
        var b = new SpoilBundle();
        foreach (var a in bag.List("carry.to"))
        {
            try { DeliverToHost(a, b); }
            catch (Exception e) { Debug.LogWarning("CarryLedger: " + e.Message); }
        }
        SpoilBundle box = SpoilBundle.Decode(bag.Get("carry.box"));
        if (box != null) for (int i = 0; i < box.Kinds; i++) b.Add(box.KeyAt(i), box.CountAt(i));
        StoreAll(b);
    }

    /// <summary>Вещи - на склады; не влезло - в сундук трофеев (он разложит сам, когда появится место).</summary>
    public static void StoreAll(SpoilBundle b)
    {
        if (b == null || b.IsEmpty) return;
        var rest = new SpoilBundle();
        for (int i = 0; i < b.Kinds; i++)
        {
            string key = b.KeyAt(i);
            int n = b.CountAt(i);
            int put = Mathf.Clamp(Spoils.Store(key, n), 0, n);
            if (put < n) rest.Add(key, n - put);
        }
        if (!rest.IsEmpty) SpoilsChest.Put(rest);
    }
}
