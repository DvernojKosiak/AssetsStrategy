using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Клановая война (klan-voina-14): бойцы военной деревни на карте войны остаются бойцами своей казармы.
///
///  - Боец уходит из казармы на карту с её «пропиской» и всем снаряжением (WarRecruit.bound, kit): сервер помнит, чья казарма.
///  - В казарме он и дальше числится: вкладка «Войска» показывает «На карте войны» - кто где, здоровье, далеко ли от казармы;
///    место в казарме он занимает (новых сверх мест не обучить).
///  - Рядом с казармой (не дальше WarRules.BarracksReach клеток) боец может зайти в неё («Зайти в казарму») - он снова
///    обычный боец деревни со своим оружием, щитом, бронёй и именем; там ему меняют снаряжение, как дома.
///    «Снаряж.» у бойца рядом с казармой: заходит и сразу открывает его снаряжение.
///  - Зашедшие с карты ждут в казарме («Ждут приказа») и сами больше не выходят, пока не нажать «На карту».
///  - «Позвать к казарме» - все её бойцы с карты идут к ней.
/// Где стоят бойцы, игра узнаёт из кадров карты войны (WarView.ApplyUnits -> Set).
/// </summary>
public static class WarFieldSoldiers
{
    public class Field
    {
        public int id;
        public string unit;
        public UnitType type;
        public Vector2Int home;      // клетка казармы в деревне (OriginCell)
        public int hp;               // здоровье, %
        public Vector2 world;        // где стоит (мир)
        public float dist;           // клеток до края казармы
        public int squad;
        public bool Near => dist <= WarRules.BarracksReach;
    }

    /// <summary>Снаряжение и имя бойца (WarRecruit.kit).</summary>
    [System.Serializable]
    public class Kit
    {
        public SavedSoldier s;
        public List<string> name = new List<string>();
    }

    static readonly List<Field> all = new List<Field>();
    static readonly HashSet<int> busy = new HashSet<int>();   // уже заходят (ждём ответ сервера)
    /// <summary>Зашли с карты: ждут в казарме и сами на карту не выходят (WarCityMode.Tick), пока не нажать «На карту».</summary>
    public static readonly HashSet<CitizenAgent> Staying = new HashSet<CitizenAgent>();
    static Vector2 rowsScroll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); busy.Clear(); Staying.Clear(); rowsScroll = Vector2.zero; }

    public static IReadOnlyList<Field> All => all;

    /// <summary>Кадр карты войны: где сейчас свои бойцы с казармой (WarView).</summary>
    public static void Set(List<Field> now)
    {
        all.Clear();
        if (now != null) all.AddRange(now);
        busy.RemoveWhere(id => !all.Exists(f => f.id == id));
        Staying.RemoveWhere(a => a == null || a.IsDead || !a.IsSoldier);
    }

    static Vector2Int CellOf(BarracksController br)
    {
        BuildingInstance b = br != null ? br.BuildingRef : null;
        return b != null ? b.OriginCell : new Vector2Int(-9999, -9999);
    }

    public static List<Field> Of(BarracksController br)
    {
        var list = new List<Field>();
        if (!WarCityMode.Active || br == null) return list;
        Vector2Int c = CellOf(br);
        foreach (var f in all) if (f.home == c) list.Add(f);
        return list;
    }

    public static int CountOf(BarracksController br) { return WarCityMode.Active ? Of(br).Count : 0; }

    /// <summary>Сколько мест казармы занимают её бойцы на карте войны.</summary>
    public static int SpaceOf(BarracksController br)
    {
        if (!WarCityMode.Active || br == null || all.Count == 0) return 0;
        int n = 0;
        foreach (var f in Of(br)) n += f.type != null ? Mathf.Max(1, f.type.spaceOccupied) : 1;
        return n;
    }

    /// <summary>Боец стоит у своей казармы (на карте войны): можно зайти в неё и сменить снаряжение.</summary>
    public static bool NearHome(int unitId)
    {
        Field f = all.Find(x => x.id == unitId);
        return f != null && f.Near;
    }

    // ---------- Снаряжение туда и обратно ----------

    public static string PackKit(CitizenAgent a, BuildingInstance b)
    {
        if (a == null || b == null) return null;
        var s = new SavedSoldier { unit = a.UnitName, bx = b.OriginCell.x, by = b.OriginCell.y, hearty = a.IsHearty, merc = a.IsMercenary };
        Weapon w = a.EquippedWeapon;
        if (w != null)
        {
            s.hasWeapon = true;
            s.wKind = (int)w.kind; s.wQuality = (int)w.quality; s.wUpgrade = w.upgrade;
            s.wDur = w.durability; s.wMaxDur = w.maxDurability;
            s.wPoison = w.poisonCharges; s.wSlow = w.slowCharges;
            s.wForged = w.forged; s.wForgeDamage = w.forgeDamage; s.wForgeDurability = w.forgeDurability;
            s.wForgeSpeed = w.forgeSpeed; s.wStars = w.forgeStars;
            s.wSalute = w.saluteCharges; s.wSaluteMk = w.saluteMk;
        }
        Shield sh = ShieldBearer.ShieldOf(a);
        if (sh != null)
        {
            s.hasShield = true;
            s.sQuality = (int)sh.quality; s.sUpgrade = sh.upgrade;
            s.sDur = sh.durability; s.sMaxDur = sh.maxDurability;
            s.sChaosMk = sh.chaosMk; s.sChaosCharges = sh.chaosCharges;
        }
        if (a.HasChaosCore) { s.cMk = a.ChaosCoreMk; s.cCharges = a.ChaosCharges; }
        if (a.IsRamCarrier) { s.ramBroken = a.RamBroken; s.ramWear = a.RamBroken ? 0f : a.RamWear; }
        BodyGear g = a.EquippedBody;
        if (g != null)
        {
            s.hasBody = true;
            s.bKind = (int)g.kind; s.bQuality = (int)g.quality;
            s.bDur = g.durability; s.bMaxDur = g.maxDurability;
            s.bUpgrade = g.upgrade;
        }
        var kit = new Kit { s = s };
        string[] name = CitizenFavorites.PackSoldier(a);
        if (name != null) kit.name.AddRange(name);
        return JsonUtility.ToJson(kit);
    }

    /// <summary>Боец зашёл с карты: снова житель-боец этой казармы со своим снаряжением. Без снаряжения (старый боец) - просто боец.</summary>
    static CitizenAgent Unpack(WarRecruit r, BarracksController br)
    {
        var pm = PopulationManager.Instance;
        BuildingInstance home = br != null ? br.BuildingRef : null;
        if (pm == null || home == null) return null;
        Kit kit = null;
        if (!string.IsNullOrEmpty(r.kit))
        {
            try { kit = JsonUtility.FromJson<Kit>(r.kit); }
            catch (System.Exception e) { Debug.LogWarning("[Война] снаряжение бойца: " + e.Message); }
        }
        SavedSoldier s = kit != null ? kit.s : null;
        UnitType u = BarracksController.FindType(s != null && !string.IsNullOrEmpty(s.unit) ? s.unit : r.unit);
        if (u == null) return null;

        CitizenAgent a = pm.SpawnAgent(DoorOf(home), s != null && s.merc);
        Weapon w = null;
        Shield sh = null;
        if (s != null && s.hasWeapon)
        {
            w = new Weapon((WeaponKind)s.wKind, (QualityTier)s.wQuality)
            {
                upgrade = s.wUpgrade, poisonCharges = s.wPoison, slowCharges = s.wSlow,
                forged = s.wForged, forgeDamage = s.wForgeDamage, forgeDurability = s.wForgeDurability,
                forgeSpeed = s.wForgeSpeed, forgeStars = s.wStars, saluteCharges = s.wSalute, saluteMk = s.wSaluteMk,
            };
            if (s.wMaxDur > 0f) w.maxDurability = s.wMaxDur;
            w.durability = Mathf.Clamp(s.wDur, 0f, w.maxDurability);
        }
        if (s != null && s.hasShield)
        {
            sh = new Shield((QualityTier)s.sQuality) { upgrade = s.sUpgrade, chaosMk = s.sChaosMk, chaosCharges = s.sChaosCharges };
            if (s.sMaxDur > 0f) sh.maxDurability = s.sMaxDur;
            sh.durability = Mathf.Clamp(s.sDur, 0f, sh.maxDurability);
        }
        br.AdoptSoldier(a, u, s != null && s.hearty, w, sh);
        if (s != null)
        {
            if (a.IsRamCarrier) a.RestoreRam(s.ramBroken, s.ramWear);
            if (s.hasBody)
            {
                var g = new BodyGear((BodyKind)s.bKind, (QualityTier)Mathf.Clamp(s.bQuality, 0, 2));
                if (g.IsArmor) g.upgrade = Mathf.Clamp(s.bUpgrade, 0, BodyGear.MaxUpgrade);
                if (s.bMaxDur > 0f) g.maxDurability = s.bMaxDur;
                g.durability = Mathf.Clamp(s.bDur, 0f, g.maxDurability);
                if (!a.EquipBody(g) && !ArmoryController.StashBody(g)) WarManager.Notify($"{g.Name}: некуда деть — потеряна", false);
            }
            if (s.cCharges > 0)
            {
                var core = new ModuleKit { kind = ModuleKind.ChaosCore, mk = Mathf.Clamp(s.cMk, 1, 3), charges = s.cCharges };
                if (a.IsElementalist) a.LoadChaosCore(core);
                else EngineerYard.AddToHall(core);
            }
        }
        if (kit != null && kit.name != null && kit.name.Count > 0)
        {
            try { CitizenFavorites.RestoreSoldier(a, kit.name.ToArray()); }
            catch (System.Exception e) { Debug.LogWarning("[Война] имя бойца: " + e.Message); }
        }
        a.ExpSetHp(a.MaxHp * Mathf.Clamp(r.hpMul, 0.05f, 1f));   // раны с карты остаются (лечатся, как дома)
        return a;
    }

    static Vector2 DoorOf(BuildingInstance b)
    {
        var nav = GridNavigation.Instance;
        if (nav != null && nav.TryGetDoorCell(b, nav.WorldToCell(b.Center), out Vector2Int d)) return nav.CellCenter(d);
        return b.Center;
    }

    // ---------- Приказы ----------

    /// <summary>Бойцы с карты заходят в казарму (только те, что рядом). gearFor - открыть снаряжение первого зашедшего.</summary>
    public static void Recall(BarracksController br, List<int> ids, bool openGear)
    {
        if (br == null || ids == null || ids.Count == 0 || !WarCityMode.Active || AccountManager.Backend == null) return;
        var send = new List<int>();
        foreach (int id in ids) if (!busy.Contains(id)) send.Add(id);
        if (send.Count == 0) return;
        foreach (int id in send) busy.Add(id);
        var req = new ApiRequest { warCmd = new WarCommand { kind = WarCommand.Recall, units = send } };
        AccountManager.Backend.Clan(Api.WarCmd, AccountManager.Token, req, res =>
        {
            foreach (int id in send) busy.Remove(id);
            if (!res.ok) { WarManager.Notify(res.error, false); return; }
            if (res.warFrame == null || res.warFrame.back == null) return;
            CitizenAgent first = null;
            int n = 0;
            foreach (var r in res.warFrame.back)
            {
                if (r == null) continue;
                BarracksController to = BarracksAt(new Vector2Int(r.bx, r.by)) ?? br;
                CitizenAgent a = Unpack(r, to);
                if (a == null) { WarManager.Notify($"{r.unit}: в деревне некуда вернуться — боец потерян", false); continue; }
                Staying.Add(a);
                if (first == null) first = a;
                n++;
                all.RemoveAll(f => f.id == r.id);
            }
            if (n == 0) return;
            Sfx.Ui("ui_click");
            WarManager.Notify(n == 1 ? $"{first.UnitName} зашёл в казарму — ждёт приказа" : $"В казарму зашли {n} бойцов — ждут приказа", true);
            if (openGear && first != null && br != null) br.OpenGearFor(first);
            if (AccountManager.Instance != null) AccountManager.Instance.SaveCityNow(true);
        });
    }

    static BarracksController BarracksAt(Vector2Int cell)
    {
        foreach (var b in BuildingInstance.All)
            if (b != null && b.OriginCell == cell)
            {
                BarracksController br = b.GetComponent<BarracksController>();
                if (br != null) return br;
            }
        return null;
    }

    /// <summary>Все бойцы казармы с карты идут к ней.</summary>
    public static void CallHome(BarracksController br)
    {
        BuildingInstance b = br != null ? br.BuildingRef : null;
        if (b == null) return;
        var ids = new List<int>();
        foreach (var f in Of(br)) if (!f.Near) ids.Add(f.id);
        if (ids.Count == 0) { WarManager.Notify("Все бойцы этой казармы уже рядом с ней", true); return; }
        if (WarView.OrderMoveTo(ids, DoorOf(b))) WarManager.Notify($"Бойцы идут к казарме ({ids.Count})", true);
    }

    /// <summary>Ждущие в казарме снова выходят на карту (сами, через пару секунд).</summary>
    public static void SendOut(BarracksController br)
    {
        if (br == null) return;
        int n = 0;
        foreach (var a in br.Soldiers) if (a != null && Staying.Remove(a)) n++;
        if (br.HoldFromWarMap) br.HoldFromWarMap = false;
        if (n > 0) WarManager.Notify($"Бойцы выходят на карту войны ({n})", true);
    }

    static int StayingIn(BarracksController br)
    {
        int n = 0;
        foreach (var a in br.Soldiers) if (a != null && Staying.Contains(a)) n++;
        return n;
    }

    // ---------- Вкладка «Войска»: бойцы казармы на карте войны ----------

    const float RowH = 26f, ListMax = 4;

    public static float SectionHeight(BarracksController br)
    {
        if (!WarCityMode.Active || br == null) return 0f;
        int rows = Of(br).Count;
        float h = UiKit.SectionH + 20f;                                   // заголовок и строка «сколько / рядом»
        if (rows > 0) h += Mathf.Min(rows, ListMax) * RowH + 6f + 32f;    // список и кнопки
        if (StayingIn(br) > 0) h += 32f;
        return h + 6f;
    }

    public static float DrawSection(BarracksController br, float x, float y, float w)
    {
        if (!WarCityMode.Active || br == null) return y;
        float cy = UiKit.Section(x, y, w, "На карте войны");
        var list = Of(br);
        int near = 0;
        foreach (var f in list) if (f.Near) near++;
        list.Sort((a, b) => a.dist.CompareTo(b.dist));

        string line = list.Count == 0
            ? UiKit.Col(UiKit.MutedHex, "Бойцов этой казармы на карте нет")
            : $"<b>{list.Count}</b> " + UiKit.Col(UiKit.MutedHex, $"на карте · рядом с казармой: ") + $"<b>{near}</b>";
        var small = new GUIStyle(UiKit.Small) { richText = true, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
        Rect lr = new Rect(x, cy, w, 18f);
        LocGUI.Label(lr, line, small);
        UiKit.Tip(lr, "Бойцы этой казармы на общей карте войны. Они по-прежнему её бойцы и занимают в ней место.\n" +
            $"Подойдут к казарме (не дальше {WarRules.BarracksReach:0} клеток) — могут зайти в неё: там им меняют оружие, щит и броню, как дома.");
        cy += 20f;

        if (list.Count > 0)
        {
            float viewH = Mathf.Min(list.Count, ListMax) * RowH;
            float innerW = w - (list.Count > ListMax ? 14f : 0f);
            rowsScroll = UiKit.BeginScrollView(new Rect(x, cy, w, viewH), rowsScroll, new Rect(0f, 0f, innerW, list.Count * RowH));
            for (int i = 0; i < list.Count; i++)
            {
                Field f = list[i];
                float ry = i * RowH;
                UnitIcons.Draw(new Rect(0f, ry + 2f, 22f, 22f), f.type);
                string hp = UiKit.Col(f.hp < 35 ? UiKit.BadHex : f.hp < 70 ? UiKit.WarnHex : UiKit.GoodHex, f.hp + "%");
                string where = f.Near ? UiKit.Col(UiKit.GoodHex, "у казармы") : UiKit.Col(UiKit.MutedHex, $"{Mathf.RoundToInt(f.dist)} кл.");
                bool wait = busy.Contains(f.id);
                LocGUI.Label(new Rect(26f, ry, innerW - 26f - 148f, RowH), $"{f.unit} {hp} · {where}", small);
                if (UiKit.Button(new Rect(innerW - 146f, ry + 1f, 70f, RowH - 2f), wait ? "…" : "Снаряж.", f.Near && !wait, UiKit.Kind.Normal,
                        f.Near ? "Зайти в казарму и открыть снаряжение бойца" : $"Пусть подойдёт к казарме (не дальше {WarRules.BarracksReach:0} клеток): «Позвать к казарме»"))
                    Recall(br, new List<int> { f.id }, true);
                if (UiKit.Button(new Rect(innerW - 72f, ry + 1f, 72f, RowH - 2f), "Найти", UiKit.Kind.Normal, "Показать бойца на карте"))
                    WarView.ShowUnit(f.id, f.world);
            }
            GUI.EndScrollView();
            cy += viewH + 6f;

            float hw = (w - 4f) / 2f;
            bool anyFar = near < list.Count;
            if (UiKit.Button(new Rect(x, cy, hw, UiKit.BtnH), "Позвать к казарме", anyFar, UiKit.Kind.Normal,
                    anyFar ? "Все бойцы этой казармы с карты идут к ней" : "Все уже у казармы"))
                CallHome(br);
            if (UiKit.Button(new Rect(x + hw + 4f, cy, hw, UiKit.BtnH), near > 0 ? $"Зайти в казарму ({near})" : "Зайти в казарму", near > 0, UiKit.Kind.Primary,
                    near > 0 ? "Бойцы у казармы заходят в неё и ждут приказа (снаряжение — на вкладке «Снаряжение»)" : "Рядом с казармой никого нет"))
            {
                var ids = new List<int>();
                foreach (var f in list) if (f.Near) ids.Add(f.id);
                Recall(br, ids, false);
            }
            cy += 32f;
        }

        int stay = StayingIn(br);
        if (stay > 0)
        {
            float hw = (w - 4f) / 2f;
            LocGUI.Label(new Rect(x, cy, hw, UiKit.BtnH), UiKit.Col(UiKit.InfoHex, $"Ждут приказа в казарме: {stay}"), small);
            if (UiKit.Button(new Rect(x + hw + 4f, cy, hw, UiKit.BtnH), $"На карту ({stay})", UiKit.Kind.Primary,
                    "Зашедшие с карты снова выходят на карту войны"))
                SendOut(br);
            cy += 32f;
        }
        return cy + 6f;
    }
}
