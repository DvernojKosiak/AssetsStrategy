using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Отряды казарм сохраняются вместе с городом (CityData.extra, ключи «sq.*»): кто в каком отряде, приказ отряда,
/// точка лагеря, запас еды, баррикада, патруль. Раньше после выхода из игры все бойцы оказывались дома,
/// а лагеря пропадали. Теперь после входа отряд стоит там же: лагерь на месте (с той же едой и баррикадой),
/// бойцы - там, где были. Ничего второй раз из казны не берётся.
/// Бойцы в походе (набег) - отдельно, их ведёт RaidManager_Save.
/// </summary>
public static class SquadSave
{
    const string Pre = "sq.";

    static List<string[]> pendingSquads, pendingMembers;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { pendingSquads = pendingMembers = null; }

    /// <summary>CitySave.CaptureCity.</summary>
    public static void SaveState(SaveBag bag)
    {
        if (WarCityMode.Active) return;
        var squads = new List<string[]>();
        var members = new List<string[]>();
        int index = 0;   // номер бойца - как в записи армии (CityArmy.Capture)
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null) continue;
            string bx = SaveBag.I(b.OriginCell.x), by = SaveBag.I(b.OriginCell.y);
            for (int i = 0; i < br.Squads.Count; i++)
            {
                Squad q = br.Squads[i];
                if (q == null) continue;
                squads.Add(new[] { bx, by, SaveBag.I(i), SaveBag.I((int)q.Order), SaveBag.F(q.Point.x), SaveBag.F(q.Point.y),
                                   SaveBag.I(q.Food), SaveBag.B(q.HasBarricade), SaveBag.B(q.PatrolAtCamp), SaveBag.B(q.OnExpedition),
                                   SaveBag.B(q.HasCamp), SaveBag.F(q.CampPoint.x), SaveBag.F(q.CampPoint.y) });
            }
            foreach (var a in br.Soldiers)
            {
                if (a == null || a.IsDead || !a.IsSoldier) continue;
                int sq = a.Squad != null ? IndexOf(br, a.Squad) : -1;
                // где стоит боец отряда в поле (дома, в казарме - место не нужно)
                bool field = a.Squad != null && a.Squad.Order == SoldierOrder.Move && !a.IsInsideBuilding;
                Vector2 at = a.transform.position;
                if (sq >= 0) members.Add(new[] { SaveBag.I(index), bx, by, SaveBag.I(sq), SaveBag.B(field), SaveBag.F(at.x), SaveBag.F(at.y) });
                index++;
            }
        }
        bag.SetList(Pre + "list", squads);
        bag.SetList(Pre + "members", members);
    }

    static int IndexOf(BarracksController br, Squad q)
    {
        for (int i = 0; i < br.Squads.Count; i++) if (br.Squads[i] == q) return i;
        return -1;
    }

    /// <summary>CitySave.ApplyCity: запомнить, бойцы встанут в казармы позже (CityArmy.Restore).</summary>
    public static void LoadState(SaveBag bag)
    {
        pendingSquads = bag.List(Pre + "list");
        pendingMembers = bag.List(Pre + "members");
    }

    /// <summary>CityArmy.Restore: бойцы уже в казармах (restored - по порядку записи армии).</summary>
    public static void Resume(List<CitizenAgent> restored)
    {
        var squads = pendingSquads;
        var members = pendingMembers;
        pendingSquads = pendingMembers = null;
        if (squads == null || squads.Count == 0 || restored == null) return;

        // Кто в каком отряде
        var placed = new List<KeyValuePair<CitizenAgent, Vector2>>();
        if (members != null)
            foreach (var m in members)
            {
                int i = SaveBag.At(m, 0, -1);
                if (i < 0 || i >= restored.Count) continue;
                CitizenAgent a = restored[i];
                BarracksController br = Barracks(SaveBag.At(m, 1), SaveBag.At(m, 2));
                if (a == null || a.IsDead || br == null || a.Squad == null || a.Squad.OnExpedition) continue;
                if (!Contains(br, a)) continue;   // встал в другую казарму - остаётся в её отряде
                br.PutInSquad(a, br.SquadForRestore(SaveBag.At(m, 3)));
                if (SaveBag.At(m, 4) == 1) placed.Add(new KeyValuePair<CitizenAgent, Vector2>(a, new Vector2(SaveBag.AtF(m, 5), SaveBag.AtF(m, 6))));
            }

        // Приказы отрядов
        var rm = ResourceManager.Instance;
        foreach (var r in squads)
        {
            BarracksController br = Barracks(SaveBag.At(r, 0), SaveBag.At(r, 1));
            int i = SaveBag.At(r, 2, -1);
            if (br == null || i < 0 || i >= br.Squads.Count) continue;
            Squad q = br.Squads[i];
            if (q == null || q.OnExpedition || q.Members.Count == 0 || SaveBag.At(r, 9) == 1) continue;   // в походе - ведёт RaidManager
            var order = (SoldierOrder)SaveBag.At(r, 3);
            q.SetPatrol(SaveBag.At(r, 8, 1) == 1);
            if (order == SoldierOrder.Move)
            {
                // Отряд уже стоял в поле: лагерь сразу на месте, припасы не списываем второй раз
                q.RestoreDeploy(new Vector2(SaveBag.AtF(r, 4), SaveBag.AtF(r, 5)), SaveBag.At(r, 6), SaveBag.At(r, 7) == 1,
                                SaveBag.At(r, 10) == 1, new Vector2(SaveBag.AtF(r, 11), SaveBag.AtF(r, 12)));
            }
            else if (order != q.Order) q.GoHome(order);
        }

        // Бойцы в поле - на своих местах (после приказа: SetOrder сбрасывает путь, но не место)
        foreach (var kv in placed)
            if (kv.Key != null && kv.Key.Squad != null && kv.Key.Squad.Order == SoldierOrder.Move) kv.Key.PlaceAtSaved(kv.Value);
    }

    static bool Contains(BarracksController br, CitizenAgent a)
    {
        foreach (var s in br.Soldiers) if (s == a) return true;
        return false;
    }

    static BarracksController Barracks(int x, int y)
    {
        foreach (var b in BuildingInstance.All)
            if (b != null && b.OriginCell.x == x && b.OriginCell.y == y)
            {
                var br = b.GetComponent<BarracksController>();
                if (br != null) return br;
            }
        return null;
    }
}
