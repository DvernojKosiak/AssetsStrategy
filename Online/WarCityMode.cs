using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Живая деревня на клановой войне (военный город, klan-voina-2): копия расстановки домашней деревни, которая живёт
/// как дома - жители добывают и носят, мастерские и кузни работают, конюшни растят лошадей, казармы учат бойцов, -
/// но строить, улучшать, сносить и переносить нельзя, а ресурсы свои (стартовый запас и то, что добыли на войне).
/// Деревня стоит на общей квадратной карте рядом с соседями по клану (WarView.Embedded); обученные бойцы сами
/// выходят из казарм на карту. С домом не связана: домой возвращается только рейтинг клана.
/// В игре всегда один город: переход между домом и военной деревней - перезапуск игры (GameRestart) с входом по токену.
/// </summary>
public static class WarCityMode
{
    /// <summary>Сейчас в игре военный город (а не домашний).</summary>
    public static bool Active { get; private set; }

    static string pendingToken;

    /// <summary>Какие здания города разрушены на карте войны: клетки "x,y" (приходят при загрузке и сохранении).</summary>
    static readonly HashSet<Vector2Int> ruined = new HashSet<Vector2Int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; pendingToken = null; ruined.Clear(); switching = false; autoTimer = 0f; autoBusy = false; lastError = null; }

    public static void Enter() { Switch(true, true); }

    /// <summary>Домой. save = false - город не сохраняем (война окончилась, сервер его уже не примет).</summary>
    public static void Leave(bool save = true) { Switch(false, save); }

    static bool switching;

    /// <summary>
    /// Перезагрузить тот же город с сервера (игру свернули надолго, см. ResumeCatchUp): при загрузке город досчитывает
    /// время, пока игрока не было, - стройки, доход, руины, походы, набеги. Город уже сохранён при сворачивании.
    /// </summary>
    public static void Reload()
    {
        if (switching || GameRestart.Running || AccountManager.Instance == null) return;
        string token = AccountManager.Token;
        if (string.IsNullOrEmpty(token)) return;
        switching = true;
        bool war = Active;
        GameRestart.Now(() => { Active = war; pendingToken = token; switching = false; });
    }

    static void Switch(bool war, bool save)
    {
        if (switching || GameRestart.Running || AccountManager.Instance == null) return;
        string token = AccountManager.Token;
        if (string.IsNullOrEmpty(token)) return;
        switching = true;
        System.Action go = () => GameRestart.Now(() => { Active = war; pendingToken = token; switching = false; });
        // Сначала сохраняем город, из которого уходим
        if (save) AccountManager.Instance.SaveCityNow(true, ok => go());
        else go();
    }

    /// <summary>Токен для входа после перезапуска (один раз).</summary>
    public static string TakeToken()
    {
        string t = pendingToken;
        pendingToken = null;
        return t;
    }

    // ---------- Разрушенные на карте войны здания ----------

    public static void SetRuined(string cells)
    {
        if (cells == null) return;
        var now = new HashSet<Vector2Int>();
        foreach (string part in cells.Split(';'))
        {
            string[] xy = part.Split(',');
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y)) now.Add(new Vector2Int(x, y));
        }
        foreach (var c in now)
            if (!ruined.Contains(c))
            {
                foreach (var b in BuildingInstance.All)
                    if (b != null && b.OriginCell == c && !b.IsRuined)
                    {
                        b.RuinByWar();
                        WarManager.Notify($"{b.type.name}: враги разрушили его на карте войны — здание не работает", false);
                    }
            }
        ruined.Clear();
        foreach (var c in now) ruined.Add(c);
    }

    /// <summary>Враги только что разрушили здание (видно на карте войны): в городе оно ломается сразу, не дожидаясь сохранения.</summary>
    public static void RuinOne(Vector2Int cell)
    {
        if (!Active || ruined.Contains(cell)) return;
        ruined.Add(cell);
        foreach (var b in BuildingInstance.All)
            if (b != null && b.OriginCell == cell && !b.IsRuined)
            {
                b.RuinByWar();
                WarManager.Notify($"{b.type.name}: враги разрушили его — здание не работает", false);
            }
    }

    /// <summary>Здание починили на карте войны: в деревне оно встаёт (восстановление уже оплачено).</summary>
    public static void RepairOne(Vector2Int cell)
    {
        if (!Active || !ruined.Remove(cell)) return;
        foreach (var b in BuildingInstance.All)
            if (b != null && b.OriginCell == cell && b.IsRuined)
            {
                b.RepairedByWar();
                WarManager.Notify($"{b.type.name}: починили — здание встаёт", true);
            }
    }

    /// <summary>Подарки от соседей по клану (кадр карты войны): ресурсы - в деревню.</summary>
    public static void TakeGifts(List<WarGrant> gifts)
    {
        if (!Active || gifts == null || gifts.Count == 0 || ResourceManager.Instance == null) return;
        foreach (var g in gifts)
        {
            if (g == null) continue;
            CityIncome.Skip();
            ResourceManager.Instance.Add(Mathf.Max(0, g.gold), Mathf.Max(0, g.wood), Mathf.Max(0, g.stone), Mathf.Max(0, g.food), Mathf.Max(0, g.iron));
            WarManager.Notify($"Подарок от {g.text}: " + WarRules.IncomeText(new[] { g.gold, g.food, g.wood, g.stone, g.iron }), true);
            Sfx.Ui("ui_coins");
        }
        if (AccountManager.Instance != null) AccountManager.Instance.SaveCityNow(true);
    }

    /// <summary>Здание разрушено на карте войны: само не восстанавливается.</summary>
    public static bool IsRuinedByWar(BuildingInstance b) { return Active && b != null && ruined.Contains(b.OriginCell); }

    // ---------- Бойцы: из казарм на карту ----------

    public class Group
    {
        public string unit;
        public readonly List<CitizenAgent> agents = new List<CitizenAgent>();
        public readonly List<BarracksController> from = new List<BarracksController>();
    }

    /// <summary>Свободные бойцы казарм военного города по видам войск.</summary>
    public static List<Group> Soldiers()
    {
        var map = new Dictionary<string, Group>();
        var list = new List<Group>();
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null) continue;
            foreach (var a in br.Soldiers)
            {
                if (a == null || a.IsDead || a.IsOnExpedition || a.InCombat || !a.IsSoldier) continue;
                string name = a.UnitName;
                if (string.IsNullOrEmpty(name)) continue;
                if (!map.TryGetValue(name, out Group g)) { g = new Group { unit = name }; map[name] = g; list.Add(g); }
                g.agents.Add(a);
                g.from.Add(br);
            }
        }
        return list;
    }

    static WarRecruit RecruitOf(CitizenAgent a, BarracksController br)
    {
        Vector2 p = a.transform.position;
        var r = new WarRecruit { unit = a.UnitName, here = true, x = p.x, y = p.y };
        // klan-voina-14: боец остаётся бойцом своей казармы - у неё он сможет зайти обратно со всем снаряжением
        BuildingInstance home = br != null ? br.BuildingRef : null;
        if (home != null)
        {
            r.bound = true;
            r.bx = home.OriginCell.x;
            r.by = home.OriginCell.y;
            r.kit = WarFieldSoldiers.PackKit(a, home);
        }
        Weapon w = a.EquippedWeapon;
        if (w != null) r.dmgMul = w.DamageMul;
        Shield s = ShieldBearer.ShieldOf(a);
        if (s != null) r.hpMul = 1f + s.BlockChance * s.ReduceFraction;
        return r;
    }

    /// <summary>Бойцов казармы послали за край города: выходят на карту войны и сразу идут к точке (мир).</summary>
    public static void SendOut(BarracksController br, List<CitizenAgent> agents, Vector2 worldPoint)
    {
        if (br == null || agents == null || agents.Count == 0) return;
        Vector2 cell = WarView.WorldToCell(worldPoint);
        var req = new ApiRequest
        {
            recruits = new List<WarRecruit>(),
            warCmd = new WarCommand { kind = WarCommand.AttackMove, x = cell.x, y = cell.y },
        };
        foreach (var a in agents) { req.recruits.Add(RecruitOf(a, br)); WarFieldSoldiers.Staying.Remove(a); }
        AccountManager.Backend.Clan(Api.WarDeploy, AccountManager.Token, req, res =>
        {
            if (!res.ok) { WarManager.Notify(res.error, false); return; }
            if (br == null) return;   // казарму снесли, пока ждали ответа
            for (int i = 0; i < res.deployed && i < agents.Count; i++) if (agents[i] != null) br.ReleaseToWar(agents[i]);
            if (AccountManager.Instance != null) AccountManager.Instance.SaveCityNow(true);
            WarManager.Notify(string.IsNullOrEmpty(res.error) ? $"Бойцы вышли на карту войны и идут к цели ({res.deployed})"
                                                              : $"Бойцы вышли на карту войны ({res.deployed}), но туда пока нельзя: {res.error}", string.IsNullOrEmpty(res.error));
        });
    }

    // ---------- Бойцы сами выходят на карту ----------

    static float autoTimer;
    static bool autoBusy;
    static string lastError;

    /// <summary>
    /// Каждые пару секунд: все обученные бойцы казарм выходят на карту войны там, где стоят
    /// (там их ведёт сервер: защищают город, идут, куда прикажут). Кроме казарм, где включено «Держать в казарме».
    /// </summary>
    public static void Tick()
    {
        if (!Active || !WarView.Embedded || autoBusy || GameRestart.Running) return;
        autoTimer -= Time.unscaledDeltaTime;
        if (autoTimer > 0f) return;
        autoTimer = 2f;
        var agents = new List<CitizenAgent>();
        var from = new List<BarracksController>();
        foreach (var g in Soldiers())
            for (int i = 0; i < g.agents.Count; i++)
            {
                if (g.from[i].HoldFromWarMap) continue;   // казарму держат дома (например, бойцов продают на рынке клана)
                if (WarFieldSoldiers.Staying.Contains(g.agents[i])) continue;   // зашёл с карты - ждёт приказа «На карту»
                agents.Add(g.agents[i]);
                from.Add(g.from[i]);
            }
        if (agents.Count == 0) return;
        var req = new ApiRequest { recruits = new List<WarRecruit>() };
        for (int i = 0; i < agents.Count; i++) req.recruits.Add(RecruitOf(agents[i], from[i]));
        autoBusy = true;
        AccountManager.Backend.Clan(Api.WarDeploy, AccountManager.Token, req, res =>
        {
            autoBusy = false;
            if (!res.ok)
            {
                if (res.error != lastError) WarManager.Notify("Бойцы не вышли на карту: " + res.error, false);
                lastError = res.error;
                autoTimer = 20f;
                return;
            }
            lastError = null;
            var names = new Dictionary<string, int>();
            for (int i = 0; i < res.deployed && i < agents.Count; i++)
            {
                if (agents[i] == null || from[i] == null) continue;
                string n = agents[i].UnitName;
                if (from[i].ReleaseToWar(agents[i])) { names.TryGetValue(n, out int k); names[n] = k + 1; }
            }
            if (names.Count == 0) return;
            if (AccountManager.Instance != null) AccountManager.Instance.SaveCityNow(true);
            var parts = new List<string>();
            foreach (var kv in names) parts.Add($"{kv.Key} ×{kv.Value}");
            WarManager.Notify("Вышли на карту войны: " + string.Join(", ", parts), true);
        });
    }
}
