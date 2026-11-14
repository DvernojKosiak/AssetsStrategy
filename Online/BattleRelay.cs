using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Живой бой, сторона нападающего. Бой считает игра нападающего (как обычный набег), а этот объект
/// несколько раз в секунду отправляет на сервер снимок поля боя и забирает приказы защитника:
/// куда идти его бойцам, кого бить, где поставить заслон. Бойцами, которым защитник дал приказ,
/// командует он сам (RaidDefense их не трогает); «Пусть решают сами» - возвращает их под управление деревни.
/// Создаёт RaidManager, когда армия пришла к деревне игрока; убирает в конце набега.
/// </summary>
public class BattleRelay : MonoBehaviour
{
    public static BattleRelay Instance { get; private set; }

    const float PushEvery = 0.4f;
    public const int MaxBarricades = 6;   // столько заслонов защитник может поставить за бой

    RaidTarget target;
    bool inFlight;
    float timer;
    bool defenderLive;
    int barricadesLeft = MaxBarricades;

    readonly Dictionary<RaidUnit, int> ids = new Dictionary<RaidUnit, int>();
    readonly Dictionary<int, RaidUnit> byId = new Dictionary<int, RaidUnit>();
    int nextId = 1;
    readonly HashSet<RaidUnit> controlled = new HashSet<RaidUnit>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    /// <summary>Этим защитником командует сам игрок (деревня его не переставляет).</summary>
    public static bool Controlled(RaidUnit u) { return Instance != null && u != null && Instance.controlled.Contains(u); }
    /// <summary>Защитник сейчас в игре и смотрит бой.</summary>
    public static bool DefenderLive => Instance != null && Instance.defenderLive;

    public static void Begin(RaidTarget t)
    {
        End();
        if (t == null || !t.IsPlayer) return;
        var go = new GameObject("BattleRelay");
        Instance = go.AddComponent<BattleRelay>();
        Instance.target = t;
    }

    public static void End()
    {
        if (Instance != null) Destroy(Instance.gameObject);
        Instance = null;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f || inFlight) return;
        timer = PushEvery;
        Push();
    }

    int IdOf(RaidUnit u)
    {
        if (ids.TryGetValue(u, out int id)) return id;
        id = nextId++;
        ids[u] = id;
        byId[id] = u;
        return id;
    }

    static bool Alive(RaidUnit u) { return u != null && u.Alive; }

    // Выстрелы с прошлого снимка - для полного повтора боя у защитника (обновление 12)
    readonly List<SiegeShot> shots = new List<SiegeShot>();

    void OnEnable() { CombatFx.Record += RecordShot; }
    void OnDisable() { CombatFx.Record -= RecordShot; }

    void RecordShot(int kind, Vector2 a, Vector2 b)
    {
        if (shots.Count >= 60) return;
        Vector2 ra = RaidMap.RealOf(a), rb = RaidMap.RealOf(b);
        shots.Add(new SiegeShot { kind = kind, x0 = ra.x, y0 = ra.y, x1 = rb.x, y1 = rb.y });
    }

    static int Rgb(Color c)
    {
        return (Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255) << 16) | (Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255) << 8) | Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
    }

    BattleSnapshot Snapshot()
    {
        var rm = RaidManager.Instance;
        var s = new BattleSnapshot
        {
            size = RaidMap.Size,
            phase = rm == null ? 3 : (rm.State == RaidManager.Phase.Scouting ? 0 : (rm.State == RaidManager.Phase.Battle ? 1 : 2)),
            timeLeft = rm != null ? rm.BattleTimeLeft : 0f,
            barricadesLeft = barricadesLeft,
            real = true,
        };
        s.shots.AddRange(shots);
        shots.Clear();
        foreach (var b in RaidBuilding.All)
        {
            if (b == null) continue;
            s.buildings.Add(new SnapBuilding
            {
                kind = (int)b.Kind, title = b.Title,
                x = b.OriginCell.x, y = b.OriginCell.y, w = b.SizeCells.x, h = b.SizeCells.y,
                hp = b.MaxHp > 0f ? Mathf.Clamp01(b.Hp / b.MaxHp) : 0f, ruined = b.IsRuined, rising = b.Rising,
            });
            var sb = s.buildings[s.buildings.Count - 1];
            sb.type = b.TypeName ?? ""; sb.lv = b.Level;
            RaidMap.PlanRectToReal(RaidMap.PlanRot, b.OriginCell, b.SizeCells, out Vector2Int ro, out Vector2Int rs);
            sb.rx = ro.x; sb.ry = ro.y; sb.rw = rs.x; sb.rh = rs.y;
        }
        foreach (var u in RaidUnit.All)
        {
            if (!Alive(u)) continue;
            if (u.Attacker && u.IsConcealed) continue;   // под плащом - защитник их не видит
            Vector2 p = RaidMap.FrameOf(u.Position);   // защитнику - в клетках его города (сторона высадки, final-1)
            s.units.Add(new SnapUnit
            {
                id = IdOf(u), attacker = u.Attacker, name = u.Name, x = p.x, y = p.y,
                hp = u.HpFraction, ranged = u.IsRangedUnit, controlled = controlled.Contains(u),
            });
            var su = s.units[s.units.Count - 1];
            Vector2 rp = RaidMap.RealOf(u.Position);
            su.rx = rp.x; su.ry = rp.y;
            su.art = u.LookKind ?? ""; su.horse = u.LookHorse; su.tint = Rgb(u.LookTint);
        }
        if (RaidDefense.Instance != null)
            foreach (var sp in RaidDefense.Instance.FreeSpots())
                s.spots.Add(new SnapSpot { x = sp.origin.x, y = sp.origin.y, w = sp.size.x, h = sp.size.y });
        return s;
    }

    void Push()
    {
        string token = AccountManager.Token;
        if (string.IsNullOrEmpty(token) || target == null) return;
        controlled.RemoveWhere(u => !Alive(u));
        inFlight = true;
        AccountManager.Backend.BattlePush(token, target.RaidId, Snapshot(), res =>
        {
            if (this == null) return;   // бой уже закончился
            inFlight = false;
            if (!res.ok) return;

            bool was = defenderLive;
            defenderLive = res.defenderLive;
            if (defenderLive && !was) WarManager.Notify($"{target.Name} зашёл в игру и сам командует обороной!", false);
            if (!defenderLive && was)
            {
                controlled.Clear();   // защитник ушёл - деревня снова обороняется сама
                WarManager.Notify($"{target.Name} вышел из боя — деревня обороняется сама", true);
            }
            if (res.commands != null) foreach (var c in res.commands) Apply(c);
        });
    }

    // Приказ защитника
    void Apply(BattleCommand c)
    {
        if (c == null) return;
        var rm = RaidManager.Instance;
        if (c.kind == BattleCommand.Barricade)
        {
            if (barricadesLeft > 0 && RaidDefense.Instance != null &&
                RaidDefense.Instance.PlayerBuild(new Vector2Int(Mathf.RoundToInt(c.x), Mathf.RoundToInt(c.y))))
            {
                barricadesLeft--;
                WarManager.Notify($"{target.Name} ставит заслон", false);
            }
            return;
        }

        var units = new List<RaidUnit>();
        if (c.units != null)
            foreach (int id in c.units)
                if (byId.TryGetValue(id, out RaidUnit u) && Alive(u) && !u.Attacker) units.Add(u);
        if (units.Count == 0) return;

        if (c.kind == BattleCommand.Auto)
        {
            foreach (var u in units) controlled.Remove(u);
            return;
        }

        if (c.kind == BattleCommand.Attack)
        {
            if (!byId.TryGetValue(c.target, out RaidUnit prey) || !Alive(prey) || !prey.Attacker) return;
            if (rm != null && rm.State == RaidManager.Phase.Scouting) rm.DefendersStrikeFirst();   // защитник напал первым
            foreach (var u in units)
            {
                controlled.Add(u);
                u.Alert();
                u.Command(prey.Position, 8f, 10f);
                u.CommandTarget(prey);
            }
            return;
        }

        // Move: встают вокруг точки, дерутся с теми, кто подойдёт
        Vector2 at = RaidMap.Local(c.x, c.y);
        int n = units.Count, side = Mathf.CeilToInt(Mathf.Sqrt(n));
        for (int i = 0; i < n; i++)
        {
            Vector2 off = new Vector2((i % side) - (side - 1) / 2f, (i / side) - (side - 1) / 2f) * 0.9f;
            controlled.Add(units[i]);
            units[i].Alert();
            units[i].Command(at + off, 2.5f, 6f);
        }
    }
}
