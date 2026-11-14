using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Разведка переживает выход из игры (final-9). Раньше найденная деревня, разведчик в пути и его карта жили только
/// в памяти: отправили разведчика, вышли, вернулись - ни деревни, ни карты. Теперь всё это едет вместе с городом
/// (CityData.extra, ключи «scout.*»):
///  - найденная деревня игрока (её город и номер набега) - пока сервер держит её за нами (Api.RaidLockMinutes);
///  - карта разведчика - тем же числом (ScoutReport.RngSeed), поэтому после входа это та же самая карта;
///  - разведчик в пути: пока игрока не было, время шло. Вернулся бы - карта уже готова; ещё в пути - снова уходит
///    за край и приходит, когда осталось.
/// Боты-соседи (игра без аккаунта) не сохраняются вовсе, им это не нужно.
/// </summary>
public partial class RaidManager
{
    const string ScoutPre = "scout.";
    string foundUtc;                 // когда сервер нашёл нам эту деревню
    float scoutLeft = -1f;           // разведчик был в пути: столько секунд ему ещё идти (после входа)
    Vector2 scoutEdge;
    RaidTarget scoutTarget;          // кого он разведывает

    RaidTarget FoundPlayerTarget()
    {
        foreach (var t in targets) if (t != null && t.IsPlayer && t != current && t.City != null && !string.IsNullOrEmpty(t.RaidId)) return t;
        return null;
    }

    void SaveScouting(SaveBag bag)
    {
        if (!AccountManager.LoggedIn || State != Phase.Idle) return;   // в походе цель и так в записи набега
        RaidTarget t = FoundPlayerTarget();
        if (t == null) return;
        if (string.IsNullOrEmpty(foundUtc)) foundUtc = WorldClock.ServerUtc.ToString("o");
        CityData copy = JsonUtility.FromJson<CityData>(JsonUtility.ToJson(t.City));
        copy.extra = new List<string>();
        copy.income = new List<float>();
        bag.Set(ScoutPre + "v", 1);
        bag.Set(ScoutPre + "id", t.RaidId);
        bag.Set(ScoutPre + "found", foundUtc);
        bag.Set(ScoutPre + "city", JsonUtility.ToJson(copy));
        ScoutReport rep = ReportFor(t);
        if (rep != null) bag.Set(ScoutPre + "rep", rep.RngSeed);
        ScoutMission m = MissionFor(t);
        if (m != null)
        {
            float left = m.away ? m.timer : t.TravelSeconds + ScoutSeconds + 10f;
            bag.Set(ScoutPre + "left", Mathf.Max(0f, left));
            bag.Set(ScoutPre + "edge", SaveBag.F(m.edge.x) + "," + SaveBag.F(m.edge.y));
        }
        else if (scoutTarget == t && scoutLeft >= 0f)
        {
            bag.Set(ScoutPre + "left", scoutLeft);   // ещё не успели снова отправить (бойцы встают в казармы)
            bag.Set(ScoutPre + "edge", SaveBag.F(scoutEdge.x) + "," + SaveBag.F(scoutEdge.y));
        }
    }

    void LoadScouting(CityData c, SaveBag bag)
    {
        scoutLeft = -1f;
        scoutTarget = null;
        if (AccountManager.IsOffline || bag.Int(ScoutPre + "v") <= 0) return;
        string id = bag.Get(ScoutPre + "id");
        string found = bag.Get(ScoutPre + "found");
        float since = SecondsSince(found);
        float away = SecondsSince(c.savedUtc);
        if (string.IsNullOrEmpty(id) || since <= 0f || since > Api.RaidLockMinutes * 60f - 60f) return;   // сервер её уже отпустил
        CityData city = null;
        try { city = JsonUtility.FromJson<CityData>(bag.Get(ScoutPre + "city") ?? ""); }
        catch (System.Exception) { city = null; }
        if (city == null || city.buildings == null || city.buildings.Count == 0) return;

        targets.RemoveAll(x => x != null && x.IsPlayer && x != current);
        RaidTarget t = RaidTarget.FromCity(city, id);
        targets.Insert(0, t);
        pickTarget = 0;
        foundUtc = found;

        int rs = bag.Int(ScoutPre + "rep");
        if (rs > 0) reports[t] = ScoutReport.Make(t, rs);

        if (bag.Has(ScoutPre + "left"))
        {
            float left = bag.Float(ScoutPre + "left") - away;
            string[] e = (bag.Get(ScoutPre + "edge") ?? "").Split(',');
            scoutEdge = e.Length == 2 ? new Vector2(SaveBag.ParseFloat(e[0], 0f), SaveBag.ParseFloat(e[1], 0f)) : Vector2.zero;
            if (left <= 0f)
            {
                // Вернулся, пока игрока не было: карта уже в окне «В бой!»
                ScoutReport rep = ScoutReport.Make(t);
                reports[t] = rep;
                WarManager.Notify($"Пока вас не было, разведчик вернулся с картой {t.Name} (точность ~{rep.ShownAccuracy}%) — смотрите в окне «В бой!»", true);
            }
            else
            {
                scoutLeft = left;   // уйдёт снова, когда бойцы встанут в казармы (ResumeScouting)
                scoutTarget = t;
            }
        }
    }

    /// <summary>Бойцы встали в казармы: разведчик, который был в пути, снова уходит за край и вернётся, когда осталось.</summary>
    void ResumeScouting()
    {
        if (scoutTarget == null || scoutLeft < 0f) return;
        RaidTarget t = scoutTarget;
        float left = scoutLeft;
        scoutTarget = null;
        scoutLeft = -1f;
        if (!targets.Contains(t) || MissionFor(t) != null) return;
        var scouts = FreeScouts();
        CitizenAgent a = scouts.Count > 0 ? scouts[0] : null;
        Vector2 edge = scoutEdge != Vector2.zero ? scoutEdge : (a != null ? ExitEdgePoint(a.transform.position, t.ApproachSide) : Vector2.zero);
        if (a == null || !a.BeginExpedition(JitterHome(edge), t.ApproachSide))
        {
            // Разведчика нет (погиб, занят) - карта всё равно пришла: он успел передать её с дороги
            ScoutReport rep = ScoutReport.Make(t);
            reports[t] = rep;
            return;
        }
        a.ForceAway();
        missions.Add(new ScoutMission { agent = a, target = t, edge = edge, away = true, timer = left });
        WarManager.Notify($"Разведчик {a.CitizenName} всё ещё в пути к {t.Name}: вернётся через {Mathf.CeilToInt(left)} с", true);
    }
}
