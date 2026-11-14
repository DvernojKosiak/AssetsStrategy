using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Набеги на наш город сохраняются вместе с городом (CityData.extra, ключи «wm.*»):
/// враги на карте (вид, номер набега, место, здоровье, мешок с награбленным), таймер следующего набега и
/// сколько набегов уже было (от этого растёт их сила).
/// Пока игрока нет, набеги идут дальше (время - по часам сервера, CityData.savedUtc):
///  - не было меньше AwayGrace - враги просто снова на карте, бой продолжается;
///  - дольше - бой с ними досчитывается сразу после входа (ResolveWave), и так же - набеги, которые пришли бы
///    за это время (не больше MaxAwayWaves за одно отсутствие).
/// Бой без игрока: сила бойцов дома и башен (RivalManager.PlayerPower) против силы врагов. Отбились - враги разбиты,
/// их мешки - в казну; нет - уносят часть казны. В обоих случаях гибнет часть бойцов, которые были дома.
/// Прорвались - уносят часть казны и разрушают несколько зданий (руины потом отстраиваются как обычно).
/// Бойцы соседей-соперников сохраняются так же (после входа - без командира). Войска игроков (живой набег) сюда
/// не входят: если защитник вышел, армия нападающего уходит с тем, что уже несёт (RaidManager.DefenderLost).
/// </summary>
public partial class WarManager
{
    const string WmPre = "wm.";
    const float AwayGrace = 60f;         // так недолго не было - враги просто снова на карте
    const int MaxAwayWaves = 4;          // больше набегов за одно отсутствие не досчитываем
    const float AwayPlunderShare = 0.3f; // прорвавшийся набег уносит не больше этой доли каждого ресурса казны

    /// <summary>CitySave.CaptureCity: враги на карте и счётчики набегов.</summary>
    public static void SaveState(SaveBag bag)
    {
        WarManager w = Instance;
        if (w == null || WarCityMode.Active) return;
        bag.Set(WmPre + "timer", w.raidTimer);
        bag.Set(WmPre + "warned", w.raidWarned);
        bag.Set(WmPre + "side", w.pendingSide);
        bag.Set(WmPre + "done", w.autoRaidsDone);
        bag.Set(WmPre + "waves", w.wavesSpawned);

        var list = new List<string[]>();
        foreach (var e in EnemyAgent.All)
        {
            // войско соседа-соперника тоже: после входа оно бьётся дальше уже без командира (или досчитывается)
            if (!EnemyAgent.Alive(e) || e.FromPlayer || e.IsSpy || e.Unit == null || string.IsNullOrEmpty(e.Unit.unitName)) continue;
            var a = new List<string> { e.Unit.unitName.Replace(",", " ").Replace(";", " "), SaveBag.I(e.Wave), SaveBag.F(e.Position.x), SaveBag.F(e.Position.y), SaveBag.F(e.Hp) };
            a.AddRange(LootSave.Pack(e.Carried));
            list.Add(a.ToArray());
        }
        bag.SetList(WmPre + "enemies", list);
    }

    /// <summary>CitySave.ApplyCity: вернуть врагов на карту и счётчики набегов; игрока не было долго - досчитать набеги.</summary>
    public static void LoadState(CityData c, SaveBag bag)
    {
        WarManager w = Instance;
        if (w == null || WarCityMode.Active || !bag.Has(WmPre + "timer")) return;

        w.raidTimer = bag.Float(WmPre + "timer", w.raidTimer);
        w.autoRaidsDone = Mathf.Max(0, bag.Int(WmPre + "done"));
        w.wavesSpawned = Mathf.Max(0, bag.Int(WmPre + "waves"));
        w.pendingSide = bag.Int(WmPre + "side", -1);
        bool warned = bag.Bool(WmPre + "warned");
        w.raidWarned = false;
        List<string[]> enemies = bag.List(WmPre + "enemies");
        // Обновление 12: город под щитом (после набега игрока) - войско соседей, что было у стен при выходе, ушло.
        // Раньше оно снова появлялось при входе, и под щитом город добивали (жалоба тестеров)
        if (enemies.Count > 0 && CityShield.Active)
        {
            int gone = enemies.Count;
            enemies.Clear();
            JobSave.AfterLoad(() => Notify($"Город под щитом: враги у стен ({gone}) ушли, пока вас не было", true));
        }

        float away = RaidManager.SecondsSince(c.savedUtc);
        if (away >= AwayGrace) { w.StartCoroutine(w.ResolveAway(enemies, away)); return; }

        // Ненадолго: враги снова на карте, таймер идёт дальше
        w.raidTimer -= away;
        if (warned || w.raidTimer < w.raidWarnSeconds + 5f)   // предупредим ещё раз
        {
            w.pendingSide = -1;
            w.raidTimer = Mathf.Max(w.raidTimer, w.raidWarnSeconds + 5f);
        }
        int n = w.SpawnSaved(enemies);
        if (n > 0) Notify($"Набег не закончился: на город снова идут враги ({n})", false);
    }

    int SpawnSaved(List<string[]> enemies)
    {
        var nav = GridNavigation.Instance;
        int n = 0;
        foreach (var a in enemies)
        {
            UnitType u = SavedUnit(a);
            if (u == null) continue;
            Vector2 p = new Vector2(SaveBag.AtF(a, 2), SaveBag.AtF(a, 3));
            if (nav != null)
            {
                Vector2Int c = nav.WorldToCell(p);
                if (!nav.InBounds(c)) continue;
                if (nav.IsBlocked(c)) p = nav.CellCenter(nav.NearestFree(c));
            }
            EnemyAgent e = EnemyAgent.Spawn(u, p, Mathf.Max(0, SaveBag.At(a, 1)), HallHpMul, HallDmgMul);
            e.RestoreSaved(SaveBag.AtF(a, 4, e.MaxHp), LootSave.Unpack(a, 5));
            n++;
        }
        return n;
    }

    UnitType SavedUnit(string[] a)
    {
        if (a == null || a.Length < 5) return null;
        string name = a[0];
        UnitType u = enemyUnits != null ? enemyUnits.Find(x => x != null && x.unitName == name) : null;
        return u ?? BotVillage.FindUnit(name);
    }

    // ---------- Набеги, пока игрока не было ----------

    IEnumerator ResolveAway(List<string[]> enemies, float away)
    {
        // Ждём, пока бойцы встанут в казармы (CityArmy.Restore): от них зависит исход
        for (int i = 0; i < 4; i++) yield return null;
        while (CityArmy.Restoring || BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) yield return null;

        float left = away;
        int waves = 0;

        // Набег, который шёл при выходе
        if (enemies.Count > 0)
        {
            float power = 0f;
            int count = 0, wave = 0;
            Loot carried = new Loot();
            foreach (var a in enemies)
            {
                UnitType u = SavedUnit(a);
                if (u == null) continue;
                float hpFrac = u.maxHp > 0f ? Mathf.Clamp01(SaveBag.AtF(a, 4, u.maxHp) / u.maxHp) : 1f;
                power += BotVillage.UnitPower(u) * Mathf.Max(0.2f, hpFrac);
                carried.Add(LootSave.Unpack(a, 5));
                wave = Mathf.Max(wave, SaveBag.At(a, 1));
                count++;
            }
            if (count > 0) { ResolveWave(wave + 1, count, power, carried); waves++; }
        }

        // Набеги, которые пришли бы за это время (таймер идёт так же, как в игре)
        var pm = PopulationManager.Instance;
        bool raidsOn = autoRaids && !RivalManager.Active && pm != null && pm.Hall != null;
        if (raidsOn)
        {
            while (left >= raidTimer && waves < MaxAwayWaves)
            {
                left -= Mathf.Max(0f, raidTimer);
                int size = AutoRaidSize();
                int level = wavesSpawned++;
                float power = 0f;
                for (int i = 0; i < size; i++) power += BotVillage.UnitPower(PickUnit(level + HallWaveBonus));
                ResolveWave(level + 1, size, power * (HallHpMul + HallDmgMul) * 0.5f, null);
                autoRaidsDone++;
                waves++;
                raidTimer = NextRaidInterval();
            }
            if (left >= raidTimer) raidTimer = NextRaidInterval();   // лимит набегов за отсутствие
            else raidTimer -= left;
        }
        raidWarned = false;
        pendingSide = -1;
        raidTimer = Mathf.Max(raidTimer, raidWarnSeconds + 5f);

        if (waves > 0) AccountManager.SaveSoon();
    }

    // Бой без игрока: исход по силе, потери бойцов дома, добыча
    void ResolveWave(int number, int count, float enemyPower, Loot carried)
    {
        float def = RivalManager.PlayerPower();
        float r = def / Mathf.Max(0.01f, def + enemyPower);
        bool win = Random.value < Mathf.Clamp01((r - 0.25f) / 0.5f);   // втрое сильнее - отбились наверняка, втрое слабее - нет

        int dead = KillHomeSoldiers(win ? 0.35f * (1f - r) : 0.15f + 0.5f * (1f - r));
        string losses = dead > 0 ? $", погибло бойцов: {dead}" : "";

        if (win)
        {
            string back = "";
            if (carried != null && !carried.IsEmpty)
            {
                GiveBack(carried);
                back = $". Их добыча вернулась в казну: {carried.ShortText}";
            }
            Notify($"Пока вас не было, набег №{number} ({count} врагов) отбит{losses}{back}", true);
        }
        else
        {
            int through = Mathf.Max(1, Mathf.RoundToInt(count * (1f - r)));
            Loot taken = Plunder(through * EnemyAgent.BaseCarry);
            string what = taken.IsEmpty ? "в казне было пусто" : "унесли " + taken.ShortText;
            int ruined = RuinSome(Mathf.Clamp(through / 2, 1, 4));
            string broke = ruined > 0 ? $", разрушено зданий: {ruined}" : "";
            Notify($"Пока вас не было, набег №{number} ({count} врагов) прорвался: {what}{broke}{losses}", false);
        }
    }

    // Прорвавшийся набег разрушает несколько зданий (не Цитадель, не стены и не дороги): они станут руинами
    static int RuinSome(int n)
    {
        var can = new List<BuildingInstance>();
        foreach (var b in BuildingInstance.All)
            if (b != null && b.IsDamageable && !b.IsRuined && !b.UnderConstruction && !b.IsTownHall && !b.IsBarrier) can.Add(b);
        int done = 0;
        while (done < n && can.Count > 0)
        {
            int i = Random.Range(0, can.Count);
            can[i].RuinFromReport(null);
            can.RemoveAt(i);
            done++;
        }
        return done;
    }

    static int KillHomeSoldiers(float share)
    {
        var home = new List<CitizenAgent>();
        foreach (var a in CitizenAgent.All)
            if (a != null && !a.IsDead && a.IsSoldier && !a.IsOnExpedition) home.Add(a);
        int dead = 0;
        foreach (var a in home)
            if (Random.value < share) { a.KillOutright(); dead++; }
        return dead;
    }

    static void GiveBack(Loot l)
    {
        var rm = ResourceManager.Instance;
        if (rm != null) rm.Add(l.gold, l.wood, l.stone, l.food, l.iron, l.oats, l.berries, l.herbs, l.potions, l.resin);
        TownHallStorage st = TownHallStorage.Instance;
        if (st == null) return;
        for (int q = 0; q < 3; q++)
        {
            if (l.planks[q] > 0) st.Add(RefinedKind.Planks, (QualityTier)q, l.planks[q]);
            if (l.plates[q] > 0) st.Add(RefinedKind.StonePlates, (QualityTier)q, l.plates[q]);
            if (l.steel[q] > 0) st.Add(RefinedKind.Steel, (QualityTier)q, l.steel[q]);
            if (l.reinforced[q] > 0) st.Add(RefinedKind.ReinforcedPlates, (QualityTier)q, l.reinforced[q]);
        }
    }

    // Прорвались: сначала золото, потом еда, дерево, камень, руда; каждого - не больше AwayPlunderShare казны
    static Loot Plunder(int slots)
    {
        var l = new Loot();
        var rm = ResourceManager.Instance;
        if (rm == null || slots <= 0) return l;
        int coins = Mathf.Min(Mathf.FloorToInt(Mathf.Max(0, rm.gold) * AwayPlunderShare), slots * Loot.GoldPerSlot);
        rm.gold -= coins;
        l.gold = coins;
        slots -= Loot.GoldSlots(coins);
        int Take(ref int stock)
        {
            int n = Mathf.Min(Mathf.FloorToInt(Mathf.Max(0, stock) * AwayPlunderShare), slots);
            if (n <= 0) return 0;
            stock -= n;
            slots -= n;
            return n;
        }
        l.food = Take(ref rm.food);
        l.wood = Take(ref rm.wood);
        l.stone = Take(ref rm.stone);
        l.iron = Take(ref rm.iron);
        return l;
    }
}
