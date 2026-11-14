using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Идущий набег сохраняется вместе с городом (CityData.extra, ключи «raid.*»): вышли из игры посреди похода -
/// после входа армия снова в походе с того же места.
///  - Что пишем: фазу и таймеры, припасы, кто из армии в походе (номер бойца в записи армии, см. CityArmy.Capture),
///    их здоровье и мешки, голема, снаряжение похода, деревню-цель (её город, без его «extra»).
///  - Бой у копии деревни: здания (прочность, руины, что осталось на складах), погибшие защитники, бойцы на поле
///    (место, мешок, приказ), кто уже ушёл с поля с добычей, сколько осталось времени боя, трофеи.
///  - Живой бой (защитник был в игре, бой считала его игра) продолжить нельзя: бойцы уходят из его деревни
///    с тем, что несли, итог уходит на сервер, армия идёт домой.
///  - Пока игрока нет, поход идёт дальше (время берём по часам сервера, CityData.savedUtc): армия доходит до деревни
///    и ждёт приказа (время боя идёт), бой у копии стоит на месте, а время боя идёт; вышло - уходит с тем, что несёт;
///    дорога домой тоже идёт - дошли, пока игрока не было, значит после входа они уже дома.
///  - Сервер держит набег Api.RaidLockMinutes с начала похода. Прошло больше - армия возвращается (не дошли -
///    припасы в казну), с поля - с тем, что несла.
/// Бойцы встают в казармы как обычно (CityArmy.Restore), потом ResumeSaved снова отправляет их в поход.
/// </summary>
public partial class RaidManager
{
    bool restoring;          // восстанавливаем бой из сохранения: руины без счёта и без раздачи складов
    string raidStartUtc;     // когда начался поход (время сервера)
    List<string> pending;    // запись набега из сохранения: ждём, пока бойцы встанут в казармы
    int pendingArmy;         // сколько бойцов в записи армии
    float pendingAway;       // сколько секунд игрока не было
    float arriveSpent;       // армия дошла до деревни, пока игрока не было: столько времени боя уже прошло

    float TakeArriveSpent() { float s = arriveSpent; arriveSpent = 0f; return Mathf.Clamp(s, 0f, Api.RaidBattleSeconds - 30f); }

    /// <summary>Сколько секунд прошло с момента utc (часы сервера); 0 - не разобрать.</summary>
    public static float SecondsSince(string utc)
    {
        if (string.IsNullOrEmpty(utc) || !System.DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime t)) return 0f;
        return (float)System.Math.Max(0.0, System.Math.Min(14 * 86400.0, (WorldClock.ServerUtc - t.ToUniversalTime()).TotalSeconds));
    }

    // Деревня-цель одной строкой (пересчитываем только при смене цели)
    RaidTarget cityFor;
    string cityJson;

    const string Pre = "raid.";
    const float MarchAway = 30f;   // столько не было, пока армия шла к краю - она уже за краем
    const float HomeWalkAway = 20f;   // армия пришла домой раньше на столько - она уже у казарм (обновление 14)
    const float RetreatAway = 20f; // столько не было, пока армия отступала с поля - она уже ушла (обновление 14)

    // ---------- Сохранение ----------

    /// <summary>CitySave.CaptureCity: записать идущий набег.</summary>
    public static void SaveState(SaveBag bag)
    {
        RaidManager rm = Instance;
        if (rm == null || WarCityMode.Active) return;
        rm.SaveScouting(bag);   // найденная деревня, разведчик в пути и его карта (final-9)
        if (rm.pending != null) { foreach (var s in rm.pending) bag.Set(EntryKey(s), EntryValue(s)); return; }   // ещё не восстановили - как было
        rm.Capture(bag);
    }

    static string EntryKey(string entry) { int i = entry.IndexOf('='); return i < 0 ? entry : entry.Substring(0, i); }
    static string EntryValue(string entry) { int i = entry.IndexOf('='); return i < 0 ? "" : entry.Substring(i + 1); }

    // Бойцы в том же порядке, что и в записи армии (CityArmy.Capture)
    static List<CitizenAgent> ArmyOrder()
    {
        var list = new List<CitizenAgent>();
        foreach (var b in BuildingInstance.All)
        {
            if (b == null) continue;
            BarracksController br = b.GetComponent<BarracksController>();
            if (br == null) continue;
            foreach (var a in br.Soldiers)
                if (a != null && !a.IsDead && a.IsSoldier) list.Add(a);
        }
        return list;
    }

    void Capture(SaveBag bag)
    {
        if (State == Phase.Idle || current == null || !current.IsPlayer || string.IsNullOrEmpty(current.RaidId)) return;
        if (string.IsNullOrEmpty(raidStartUtc)) raidStartUtc = WorldClock.ServerUtc.ToString("o");

        bag.Set(Pre + "v", 1);
        bag.Set(Pre + "state", (int)State);
        bag.Set(Pre + "id", current.RaidId);
        bag.Set(Pre + "name", (current.Name ?? "").Replace(";", " ").Replace(",", " "));
        bag.Set(Pre + "level", current.Level);
        bag.Set(Pre + "start", raidStartUtc);
        if (State != Phase.TravelHome && current.City != null)
        {
            if (cityFor != current || cityJson == null)
            {
                // Без его «extra»: там может лежать и его собственный набег с чужим городом
                CityData copy = JsonUtility.FromJson<CityData>(JsonUtility.ToJson(current.City));
                copy.extra = new List<string>();
                copy.income = new List<float>();
                cityJson = JsonUtility.ToJson(copy);
                cityFor = current;
            }
            bag.Set(Pre + "city", cityJson);
        }

        bag.Set(Pre + "timer", timer);
        bag.Set(Pre + "food", food);
        if (State == Phase.Scouting || State == Phase.Battle || State == Phase.Leaving) bag.Set(Pre + "fog", FogOfWar.SaveRaidExplored());
        bag.Set(Pre + "side", landSide);   // сторона подхода (выбрана при выходе, final-4)
        bag.Set(Pre + "wood", woodPaid);
        bag.Set(Pre + "camp", campPaid);
        bag.Set(Pre + "count", startCount);
        bag.Set(Pre + "edge", SaveBag.F(homeEdge.x) + "," + SaveBag.F(homeEdge.y));
        bag.Set(Pre + "golem", golemOut);
        bag.Set(Pre + "golemLost", golemLost);
        bag.Set(Pre + "golemBack", golemHpBack);
        bag.Set(Pre + "reported", reported);
        bag.Set(Pre + "live", live);
        bag.Set(Pre + "fought", fought);
        bag.Set(Pre + "stats", string.Join(",", new[] { SaveBag.I(lost), SaveBag.I(kills), SaveBag.I(ruinedCount), SaveBag.I(defendersTotal), SaveBag.I(towersTotal),
                                                       SaveBag.B(hallSacked), SaveBag.B(resistanceBroken), SaveBag.I(ruinedMain) }));
        bag.Set(Pre + "left", Mathf.Max(0f, battleDeadline - Time.time));
        bag.Set(Pre + "eat", eatTimer);
        bag.Set(Pre + "spoilBudget", spoilBudget);
        bag.Set(Pre + "liveStolen", liveStolen.Encode());
        bag.Set(Pre + "liveCopies", liveCopies.Encode());
        SaveBounty(bag);

        var killed = new List<string[]>();
        foreach (var n in killedNames) if (!string.IsNullOrEmpty(n)) killed.Add(new[] { n });
        bag.SetList(Pre + "killed", killed);
        bag.SetList(Pre + "trophyW", SaveBag.PackAll(trophyWeapons, SaveBag.Pack));
        bag.SetList(Pre + "trophyS", SaveBag.PackAll(trophyShields, SaveBag.Pack));

        // Снаряжение похода: что в наборе и что стоит на поле боя (вернётся в набор)
        var kitList = new List<string[]>();
        for (int k = 0; k < FieldGear.Kinds; k++)
            foreach (var it in kit[k]) kitList.Add(new[] { SaveBag.I(k), SaveBag.F(it.strength), SaveBag.F(it.pool) });
        foreach (var p in FieldPost.All)
            if (p != null && p.Alive && p.Raid && !p.Puppet && p.S.crafted)
                kitList.Add(new[] { SaveBag.I((int)p.Kind), SaveBag.F(p.Strength), SaveBag.F(p.Pool) });
        bag.SetList(Pre + "kit", kitList);
        var traps = SaveBag.PackAll(TrapStock.RaidKit, SaveBag.Pack);
        foreach (var t in Trap.All)
        {
            if (t == null || !t.Alive || !t.Raid || t.Puppet || t.Kit == null) continue;
            TrapKit kt = t.Kit.Clone();
            kt.charges = t.Charges;
            traps.Add(SaveBag.Pack(kt));
        }
        bag.SetList("trapsRaid", traps);

        // Бойцы: номер в записи армии, здоровье, где он (0 - в пути, 1 - на поле, 2 - ушёл с поля), место, приказ, мешок
        var units = new Dictionary<CitizenAgent, RaidUnit>();
        foreach (var u in RaidUnit.All)
            if (u != null && u.Attacker && u.Alive && u.Source != null) units[u.Source] = u;
        foreach (var u in RaidUnit.Riders)   // носильщики внутри тарана - тоже на поле
            if (u != null && u.Alive && u.Source != null) units[u.Source] = u;
        List<CitizenAgent> order = ArmyOrder();
        var list = new List<string[]>();
        foreach (var m in members)
        {
            if (m == null || m.IsDead) continue;
            int idx = order.IndexOf(m);
            if (idx < 0) continue;
            int where = 0;
            Vector2 pos = Vector2.zero, post = Vector2.zero;
            int mode = 0;
            Vector2Int focus = new Vector2Int(int.MinValue, 0);
            Loot bagLoot = null;
            if (survivors.TryGetValue(m, out Loot gone)) { where = 2; bagLoot = gone; }
            else if (units.TryGetValue(m, out RaidUnit u))
            {
                where = 1;
                pos = NetPos(u.Position);
                post = NetPos(u.Post);
                mode = (int)u.Mode;
                if (u.Focus != null) focus = u.Focus.OriginCell;
                bagLoot = u.Carried;
            }
            var e = new List<string>
            {
                SaveBag.I(idx), SaveBag.F(m.Hp), SaveBag.I(where), SaveBag.F(pos.x), SaveBag.F(pos.y), SaveBag.I(mode),
                SaveBag.F(post.x), SaveBag.F(post.y), SaveBag.I(focus.x), SaveBag.I(focus.y),
            };
            e.AddRange(LootSave.Pack(bagLoot));
            list.Add(e.ToArray());
        }
        bag.SetList(Pre + "members", list);

        // Живой бой: номер каждого бойца в бою (по нему сервер скажет, кто погиб, пока игра была закрыта - final-8)
        if (live)
        {
            var ids = new List<string[]>();
            foreach (var kv in armyIds)
            {
                int idx = order.IndexOf(kv.Value);
                if (idx >= 0 && kv.Value != null && !kv.Value.IsDead) ids.Add(new[] { SaveBag.I(idx), SaveBag.I(kv.Key) });
            }
            bag.SetList(Pre + "liveIds", ids);
        }

        // Здания копии деревни: вид, клетка, прочность, руины, склад
        if (!live && (State == Phase.Scouting || State == Phase.Battle || State == Phase.Leaving))
        {
            var bl = new List<string[]>();
            foreach (var b in RaidBuilding.All)
            {
                if (b == null) continue;
                var e = new List<string>
                {
                    SaveBag.I((int)b.Kind), SaveBag.I(b.OriginCell.x), SaveBag.I(b.OriginCell.y),
                    SaveBag.F(b.MaxHp > 0f ? b.Hp / b.MaxHp : 1f), SaveBag.B(b.IsRuined),
                };
                e.AddRange(LootSave.Pack(b.Storage));
                bl.Add(e.ToArray());
            }
            bag.SetList(Pre + "buildings", bl);
        }
    }

    // ---------- Загрузка ----------

    /// <summary>CitySave.ApplyCity: запомнить набег из сохранения (бойцов ещё нет - продолжим в ResumeSaved).</summary>
    public static void LoadState(CityData c, SaveBag bag)
    {
        RaidManager rm = Instance;
        if (rm == null || WarCityMode.Active) return;
        rm.LoadScouting(c, bag);
        rm.pending = null;
        if (bag.Int(Pre + "v") <= 0)
        {
            TrapStock.UnloadRaidKit(true);   // набега нет - боевые ловушки похода лежат в запасе Цитадели
            return;
        }
        var raw = new List<string>();
        foreach (var s in c.extra) if (s != null && s.StartsWith(Pre, System.StringComparison.Ordinal)) raw.Add(s);
        rm.pending = raw;
        rm.pendingAway = SecondsSince(c.savedUtc);
        rm.pendingArmy = c.army != null ? c.army.Count : 0;
        if (rm.pendingArmy == 0) rm.DropSaved("бойцов в казармах нет");
    }

    /// <summary>Бойцы встали в казармы (CityArmy.Restore): кто был в походе - снова уходит.</summary>
    public static void ResumeSaved(List<CitizenAgent> restored)
    {
        RaidManager rm = Instance;
        if (rm == null) return;
        try { rm.ResumeScouting(); }
        catch (System.Exception e) { Debug.LogWarning("[Загрузка] разведка: " + e); }
        if (rm.pending == null) return;
        try { rm.Resume(restored); }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Загрузка] набег: " + e);
            if (rm.pending != null) rm.DropSaved("ошибка загрузки");
        }
    }

    // Набег не продолжить: снаряжение - в запас, деревню на сервере освобождаем
    void DropSaved(string why)
    {
        var bag = new SaveBag(pending);
        pending = null;
        foreach (var a in bag.List(Pre + "kit")) FieldGear.Add(UnpackKitItem(a));
        TrapStock.UnloadRaidKit(true);
        Phase st = (Phase)bag.Int(Pre + "state");
        string id = bag.Get(Pre + "id");
        if (!string.IsNullOrEmpty(id) && !bag.Bool(Pre + "reported") && st != Phase.TravelHome)
            AccountManager.FinishRaid(new ApiRequest { raidId = id, loot = new RaidLoot(), killed = new List<string>(), live = bag.Bool(Pre + "live") });
        Debug.LogWarning("[Загрузка] набег не продолжен: " + why);
        WarManager.Notify("Набег из прошлой игры не продолжить (" + why + ") — армия дома, снаряжение в запасе", false);
    }

    static FieldGear.Item UnpackKitItem(string[] a)
    {
        int k = Mathf.Clamp(SaveBag.At(a, 0), 0, FieldGear.Kinds - 1);
        var it = new FieldGear.Item((PostKind)k, SaveBag.AtF(a, 1, 1f));
        it.pool = Mathf.Clamp(SaveBag.AtF(a, 2, it.MaxPool), 0f, it.MaxPool);
        return it;
    }

    void Resume(List<CitizenAgent> restored)
    {
        var bag = new SaveBag(pending);
        Phase st = (Phase)bag.Int(Pre + "state");
        string id = bag.Get(Pre + "id");
        if (st == Phase.Idle || st > Phase.TravelHome || string.IsNullOrEmpty(id)) { DropSaved("запись повреждена"); return; }
        if (State != Phase.Idle) { DropSaved("армия уже в походе"); return; }

        // Деревня-цель
        RaidTarget t;
        if (st == Phase.TravelHome)
            t = new RaidTarget { IsPlayer = true, RaidId = id, Name = bag.Get(Pre + "name") ?? "Игрок", Level = Mathf.Clamp(bag.Int(Pre + "level", 1), 1, 3) };
        else
        {
            CityData city = null;
            try { city = JsonUtility.FromJson<CityData>(bag.Get(Pre + "city") ?? ""); }
            catch (System.Exception) { city = null; }
            if (city == null || city.buildings == null || city.buildings.Count == 0) { DropSaved("нет города противника"); return; }
            t = RaidTarget.FromCity(city, id);
        }

        // Бойцы
        var found = new List<KeyValuePair<CitizenAgent, string[]>>();
        foreach (var a in bag.List(Pre + "members"))
        {
            int idx = SaveBag.At(a, 0, -1);
            if (restored == null || idx < 0 || idx >= restored.Count) continue;
            CitizenAgent m = restored[idx];
            if (m == null || m.IsDead || !m.IsSoldier || m.IsOnExpedition) continue;
            found.Add(new KeyValuePair<CitizenAgent, string[]>(m, a));
        }
        bool golem = bag.Bool(Pre + "golem");
        if (found.Count == 0 && !golem) { DropSaved("бойцы не вернулись в казармы"); return; }

        float away = pendingAway;
        pending = null;
        arriveSpent = 0f;
        current = t;
        cityFor = null;
        cityJson = null;
        ResetStats();
        ClearLive();
        squads.Clear();
        members.Clear();
        raidStartUtc = bag.Get(Pre + "start");
        food = bag.Int(Pre + "food");
        woodPaid = bag.Int(Pre + "wood");
        campPaid = bag.Bool(Pre + "camp");
        startCount = Mathf.Max(found.Count, bag.Int(Pre + "count"));
        reported = bag.Bool(Pre + "reported");
        fought = bag.Bool(Pre + "fought");
        eatTimer = bag.Float(Pre + "eat");
        if (bag.Has(Pre + "spoilBudget")) spoilBudget = Mathf.Max(0, bag.Int(Pre + "spoilBudget"));
        liveStolen.Add(SpoilBundle.Decode(bag.Get(Pre + "liveStolen")));
        liveCopies.Add(SpoilBundle.Decode(bag.Get(Pre + "liveCopies")));
        string[] edge = (bag.Get(Pre + "edge") ?? "").Split(',');
        homeEdge = new Vector2(SaveBag.AtF(edge, 0), SaveBag.AtF(edge, 1));
        if (edge.Length < 2) homeEdge = HomeEdgePoint(Vector2.zero);
        landSide = Mathf.Clamp(bag.Int(Pre + "side", t.ApproachSide), 0, 3);   // сторона подхода - та, что была при выходе

        string[] stats = (bag.Get(Pre + "stats") ?? "").Split(',');
        lost = SaveBag.At(stats, 0);
        kills = SaveBag.At(stats, 1);
        ruinedCount = SaveBag.At(stats, 2);
        int savedDefenders = SaveBag.At(stats, 3), savedTowers = SaveBag.At(stats, 4);
        hallSacked = SaveBag.AtB(stats, 5);
        resistanceBroken = SaveBag.AtB(stats, 6);
        ruinedMain = SaveBag.At(stats, 7);
        LoadBounty(bag);
        foreach (var a in bag.List(Pre + "killed")) if (a.Length > 0 && a[0].Length > 0) killedNames.Add(a[0]);
        foreach (var a in bag.List(Pre + "trophyW")) { Weapon w = SaveBag.UnpackWeapon(a); if (w != null) trophyWeapons.Add(w); }
        foreach (var a in bag.List(Pre + "trophyS")) { Shield s = SaveBag.UnpackShield(a); if (s != null) trophyShields.Add(s); }

        for (int k = 0; k < FieldGear.Kinds; k++) kit[k].Clear();
        foreach (var a in bag.List(Pre + "kit")) { FieldGear.Item it = UnpackKitItem(a); kit[(int)it.kind].Add(it); }

        // Снова в поход: Marching - идут к краю заново, дальше - уже за краем карты
        foreach (var kv in found)
        {
            CitizenAgent m = kv.Key;
            m.ExpSetHp(SaveBag.AtF(kv.Value, 1, m.MaxHp));
            m.BeginExpedition(JitterHome(homeEdge), landSide);
            if (st != Phase.Marching || away >= MarchAway) m.ForceAway();
            members.Add(m);
        }
        var inRaid = new HashSet<CitizenAgent>(members);
        var loose = new Dictionary<BarracksController, Squad>();   // бойцы похода без своего отряда - в новый отряд их казармы
        foreach (var m in members)
        {
            Squad sq = m.Squad;
            if (sq != null && squads.Contains(sq)) continue;
            bool whole = sq != null;   // отряд целиком в походе (после входа бойцы могли разойтись по другим отрядам)
            if (sq != null) foreach (var o in sq.Members) if (o != null && !o.IsDead && !inRaid.Contains(o)) { whole = false; break; }
            if (whole)
            {
                sq.OnExpedition = true;
                squads.Add(sq);
                continue;
            }
            // Обновление 14: раньше такой боец оставался без отряда - и внизу не было карточек, чтобы им управлять
            BarracksController br = m.ExpBarracks;
            if (br == null) continue;
            if (!loose.TryGetValue(br, out Squad own))
            {
                own = br.NewSquadForArrivals();
                own.OnExpedition = true;
                loose[br] = own;
                squads.Add(own);
            }
            br.PutInSquad(m, own);
        }

        golemOut = golem && LifeAltar.TakeForRaid();
        golemLost = bag.Bool(Pre + "golemLost") || (golem && !golemOut);
        golemHpBack = golemOut ? bag.Float(Pre + "golemBack", -1f) : -1f;
        golemUnit = null;

        // Сервер держит набег Api.RaidLockMinutes: прошло больше - деревня уже свободна
        bool expired = false;
        if (System.DateTime.TryParse(raidStartUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime started))
            expired = (WorldClock.ServerUtc - started.ToUniversalTime()).TotalMinutes > Api.RaidLockMinutes - 1f;

        bool savedLive = bag.Bool(Pre + "live");
        string name = t.Name;
        string awayText = away >= 60f ? $" (вас не было {AwayText(away)})" : "";
        switch (st)
        {
            case Phase.Marching:
            case Phase.TravelOut:
                if (expired)
                {
                    State = Phase.TravelOut;   // Recall работает только в пути
                    reported = true;           // сервер уже отпустил деревню - итог не нужен
                    Recall();
                    WarManager.Notify($"Поход на {name} сорван: вас не было слишком долго. Армия вернулась, припасы в казне", false);
                    return;
                }
                if (st == Phase.Marching && away < MarchAway)
                {
                    State = Phase.Marching;
                    timer = 0f;
                    WarManager.Notify($"Набег на {name} продолжается: армия снова выходит к краю карты", true);
                    break;
                }
                State = Phase.TravelOut;
                timer = (st == Phase.Marching ? t.TravelSeconds : Mathf.Max(0f, bag.Float(Pre + "timer"))) - away;
                if (timer > 0f)
                {
                    WarManager.Notify($"Набег на {name} продолжается: армия в пути, до деревни {timer:F0} с{awayText}", true);
                    break;
                }

                // Дошли, пока игрока не было: стоят у деревни и ждут приказа, время боя идёт
                float waited = -timer;
                timer = 0f;
                if (waited >= Api.RaidBattleSeconds - 30f)
                {
                    SendResult(current, true);   // боя не было - деревня свободна
                    foreach (var kv in found) survivors[kv.Key] = new Loot();
                    GolemLeavesWithArmy();
                    State = Phase.TravelHome;
                    timer = t.TravelSeconds;
                    WarManager.Notify($"Армия дошла до {name}, пока вас не было, ждала приказа всё время набега и ушла домой без боя", false);
                    FastForwardHome(waited - Api.RaidBattleSeconds);
                    break;
                }
                arriveSpent = waited;
                EatAway(waited);
                WarManager.Notify($"Армия дошла до {name}, пока вас не было, и ждёт приказа у деревни. До конца набега {(Api.RaidBattleSeconds - waited) / 60f:F0} мин", true);
                break;   // Update сам начнёт прибытие (timer <= 0)

            case Phase.Scouting:
            case Phase.Battle:
            case Phase.Leaving:
                float left = bag.Float(Pre + "left", Api.RaidBattleSeconds);
                if (savedLive && !expired && online && !string.IsNullOrEmpty(AccountManager.Token) && t.IsPlayer)
                {
                    // Живой бой шёл без нас (final-8): сначала спросим сервер, кто из наших погиб у защитника.
                    // Раньше все сохранённые бойцы «воскресали» и возвращались домой - бой расходился с тем, что видел защитник.
                    BeginLiveResume(bag, found, name, away);
                    return;
                }
                // Обновление 14: отступление идёт и без игрока. Нажали «Отступить» и вышли - за это время бойцы
                // уже ушли с поля с тем, что несли (раньше стояли на месте, пока игрок не вернётся).
                bool retreated = !savedLive && st == Phase.Leaving && away >= RetreatAway;
                if (!savedLive && !retreated && away >= RetreatAway)
                    foreach (var kv in found)
                        if (SaveBag.At(kv.Value, 2) == 1 && SaveBag.At(kv.Value, 5) == (int)RaidUnit.Order.Retreat && kv.Value.Length > 2)
                            kv.Value[2] = SaveBag.I(2);   // отступал один отряд - он уже ушёл с поля
                if (savedLive || expired || away >= left || retreated)
                {
                    // Бой без нас не продолжить или время вышло: кто был в деревне - уходит с тем, что нёс
                    Loot taken = new Loot();
                    foreach (var kv in found)
                    {
                        Loot l = LootSave.Unpack(kv.Value, 10);
                        survivors[kv.Key] = l;
                        taken.Add(l);
                    }
                    GolemLeavesWithArmy();
                    if (!savedLive && !expired) SendResult(current, false, taken);   // бой на копии: унесли то, что в мешках
                    live = savedLive;
                    State = Phase.Leaving;
                    WarManager.Notify(savedLive
                        ? $"Бой в деревне {name} шёл без вас — армия ушла оттуда с тем, что несла"
                        : retreated && away < left ? $"Армия отступила из деревни {name}, пока вас не было, и идёт домой с тем, что несла"
                        : $"Время набега на {name} вышло, пока вас не было — армия ушла с тем, что несла", false);
                    if (!savedLive) EatAway(Mathf.Min(away, left));
                    Finish();
                    FastForwardHome(savedLive ? away - 60f : retreated && away < left ? away - RetreatAway : away - left);
                    return;
                }
                EatAway(away);
                ResumeBattle(bag, st, found, savedDefenders, savedTowers, left - away);
                if (away >= 60f) WarManager.Notify($"Пока вас не было, армия держала позиции у {name}{awayText}", true);
                break;

            case Phase.TravelHome:
                foreach (var kv in found) if (SaveBag.At(kv.Value, 2) == 2) survivors[kv.Key] = LootSave.Unpack(kv.Value, 10);
                State = Phase.TravelHome;
                timer = Mathf.Max(0.1f, bag.Float(Pre + "timer"));
                if (timer > away) WarManager.Notify($"Армия возвращается из набега на {name}: в пути {timer - away:F0} с", true);
                FastForwardHome(away);
                break;
        }
    }

    // ---------- Возврат к живому бою после перезахода (final-8) ----------

    bool resumeWait;
    float resumeTimer;

    void BeginLiveResume(SaveBag bag, List<KeyValuePair<CitizenAgent, string[]>> found, string name, float away)
    {
        // Кто под каким номером был в бою
        var idOfIdx = new Dictionary<int, int>();
        foreach (var a in bag.List(Pre + "liveIds")) idOfIdx[SaveBag.At(a, 0, -1)] = SaveBag.At(a, 1, -1);
        var byId = new Dictionary<int, CitizenAgent>();
        var savedLoot = new Dictionary<CitizenAgent, Loot>();
        foreach (var kv in found)
        {
            savedLoot[kv.Key] = LootSave.Unpack(kv.Value, 10);
            if (idOfIdx.TryGetValue(SaveBag.At(kv.Value, 0, -1), out int id) && id > 0) byId[id] = kv.Key;
        }

        live = true;
        State = Phase.Leaving;
        resumeWait = true;
        resumeTimer = 10f;   // сервер молчит - как раньше: уходят с тем, что несли
        RaidTarget t = current;
        WarManager.Notify($"Бой в деревне {name} шёл без вас — узнаём у сервера, чем он кончился…", false);

        void Complete(SiegeFrame f)
        {
            if (!resumeWait || current != t) return;
            resumeWait = false;
            int dead = 0;
            var goneAlive = new Dictionary<int, SiegeGone>();
            var goneDead = new HashSet<int>();
            if (f != null && f.gone != null)
                foreach (var g in f.gone) if (g != null) { if (g.dead) goneDead.Add(g.id); else goneAlive[g.id] = g; }
            foreach (var kv in byId)
            {
                CitizenAgent m = kv.Value;
                if (m == null || m.IsDead) continue;
                if (goneDead.Contains(kv.Key))
                {
                    lost++;
                    dead++;
                    savedLoot.Remove(m);
                    m.KillOutright();   // погиб у защитника - погиб и у нас
                }
                else if (goneAlive.TryGetValue(kv.Key, out SiegeGone g) && g.loot != null) savedLoot[m] = g.loot.ToLoot();
            }
            Loot taken = new Loot();
            foreach (var kv in savedLoot)
            {
                if (kv.Key == null || kv.Key.IsDead) continue;
                survivors[kv.Key] = kv.Value;
                taken.Add(kv.Value);
            }
            members.RemoveAll(m => m == null || m.IsDead);
            GolemLeavesWithArmy();
            WarManager.Notify(f == null
                ? $"Бой в деревне {name} шёл без вас — сервер не ответил, армия ушла с тем, что несла"
                : dead > 0 ? $"Бой в деревне {name} шёл без вас: погибло бойцов — {dead}, остальные ушли с добычей"
                           : $"Бой в деревне {name} шёл без вас — все бойцы ушли оттуда с тем, что несли", dead > 0 ? false : true);
            Finish();
            FastForwardHome(away - 60f);
        }

        resumeComplete = Complete;
        AccountManager.Backend.Call(Api.SiegePull, AccountManager.Token, new ApiRequest { raidId = t.RaidId }, res =>
        {
            if (this == null) return;
            Complete(res != null && res.ok && res.siegeLive ? res.siegeFrame : null);
        });
    }

    System.Action<SiegeFrame> resumeComplete;

    /// <summary>Ждём ответа сервера о живом бое (Update ничего другого не делает). true - ждём.</summary>
    bool ResumeTick(float dt)
    {
        if (!resumeWait) return false;
        resumeTimer -= dt;
        if (resumeTimer <= 0f && resumeComplete != null) resumeComplete(null);
        return resumeWait;
    }

    // Голем уходит с поля вместе с армией (с теми ранами, что у него дома)
    void GolemLeavesWithArmy()
    {
        if (golemOut && golemHpBack <= 0f) golemHpBack = Mathf.Max(1f, LifeAltar.RaidHp);
    }

    // Армия шла домой, пока игрока не было: дошла - уже дома (отчёт - как обычно)
    void FastForwardHome(float spent)
    {
        if (State != Phase.TravelHome || spent <= 0f) return;
        timer -= spent;
        if (timer <= 0f) ReturnHome(-timer >= HomeWalkAway);
    }

    // Еда похода за время у деревни (каждый боец ест раз в EatInterval)
    void EatAway(float seconds)
    {
        if (seconds <= 0f) return;
        int eaters = 0;
        foreach (var m in members) if (m != null && !m.IsDead) eaters++;
        food = Mathf.Max(0, food - eaters * Mathf.FloorToInt(seconds / EatInterval));
    }

    static string AwayText(float s)
    {
        if (s < 3600f) return $"{Mathf.Max(1, Mathf.RoundToInt(s / 60f))} мин";
        int h = Mathf.FloorToInt(s / 3600f);
        return h < 48 ? $"{h} ч {Mathf.RoundToInt((s - h * 3600f) / 60f)} мин" : $"{h / 24} дн";
    }

    // Бой у копии деревни: деревня как была при выходе
    void ResumeBattle(SaveBag bag, Phase st, List<KeyValuePair<CitizenAgent, string[]>> found, int savedDefenders, int savedTowers, float left)
    {
        // Погибшие защитники больше не выходят: убираем их из плана (сначала с поля, потом из подкреплений)
        BotVillage.Plan plan = current.GetPlan();
        foreach (var n in killedNames)
        {
            int i = plan.Defenders.FindIndex(d => d != null && d.unitName == n);
            if (i >= 0) { plan.Defenders.RemoveAt(i); continue; }
            i = plan.Reserve.IndexOf(n);
            if (i >= 0) plan.Reserve.RemoveAt(i);
        }

        restoring = true;
        landSide = Mathf.Clamp(bag.Int(Pre + "side", 0), 0, 3);   // старое сохранение - слева, как было
        try
        {
            Arrive();
            FogOfWar.LoadRaidExplored(bag.Get(Pre + "fog"));   // разведанная зона не спадает после входа (final-1)

            // Здания: руины, прочность, склады
            var saved = new Dictionary<long, string[]>();
            foreach (var a in bag.List(Pre + "buildings"))
                saved[CellKey(SaveBag.At(a, 0), SaveBag.At(a, 1), SaveBag.At(a, 2))] = a;
            foreach (var b in new List<RaidBuilding>(RaidBuilding.All))
            {
                if (b == null || !saved.TryGetValue(CellKey((int)b.Kind, b.OriginCell.x, b.OriginCell.y), out string[] a)) continue;
                b.SetNet(SaveBag.AtF(a, 3, 1f), SaveBag.AtB(a, 4));
                b.Storage.Clear();
                b.Storage.Add(LootSave.Unpack(a, 5));
            }
        }
        finally { restoring = false; }

        // Голем уже ушёл с поля - второй раз на поле не выходит
        if (golemUnit != null && golemHpBack > 0f)
        {
            golemUnit.gameObject.SetActive(false);
            Destroy(golemUnit.gameObject);
            golemUnit = null;
        }

        defendersTotal = Mathf.Max(defendersTotal, savedDefenders);
        towersTotal = Mathf.Max(towersTotal, savedTowers);
        battleDeadline = Time.time + Mathf.Max(30f, left);

        // Бойцы: ушедшие с поля - уже с добычей; на поле - на своих местах, с мешками и приказами
        var units = new Dictionary<CitizenAgent, RaidUnit>();
        foreach (var u in RaidUnit.All)
            if (u != null && u.Attacker && u.Source != null) units[u.Source] = u;
        foreach (var u in new List<RaidUnit>(RaidUnit.Riders))
            if (u != null && u.Source != null) units[u.Source] = u;
        State = st;
        // Бой уже шёл до выхода: штурм начат. Без этого после входа бойцы стояли без дела, пока не выйдет время,
        // а потом просто уходили к краю карты (final-7)
        if (st == Phase.Battle || st == Phase.Leaving) fought = true;
        foreach (var kv in found)
        {
            string[] a = kv.Value;
            if (!units.TryGetValue(kv.Key, out RaidUnit u)) continue;
            int where = SaveBag.At(a, 2);
            if (where == 2)
            {
                survivors[kv.Key] = LootSave.Unpack(a, 10);
                u.gameObject.SetActive(false);
                Destroy(u.gameObject);
                continue;
            }
            if (where != 1) continue;
            Vector2 pos = LivePos(SaveBag.AtF(a, 3), SaveBag.AtF(a, 4));
            if (RaidMap.Instance != null)
            {
                Vector2Int cell = RaidMap.Instance.WorldToCell(pos);
                if (RaidMap.Instance.IsBlocked(cell)) pos = RaidMap.Instance.CellCenter(RaidMap.Instance.NearestFree(cell));
            }
            u.transform.position = new Vector3(pos.x, pos.y, u.transform.position.z);
            u.Carried.Add(LootSave.Unpack(a, 10));
            switch ((RaidUnit.Order)SaveBag.At(a, 5))
            {
                case RaidUnit.Order.Auto: u.OrderAuto(); break;
                case RaidUnit.Order.Hold: u.OrderHold(LivePos(SaveBag.AtF(a, 6), SaveBag.AtF(a, 7))); break;
                case RaidUnit.Order.Retreat: u.OrderRetreat(ExitFor(u)); break;
                case RaidUnit.Order.Focus:
                    RaidBuilding fb = RaidBuilding.All.Find(b => b != null && !b.IsRuined && b.OriginCell.x == SaveBag.At(a, 8) && b.OriginCell.y == SaveBag.At(a, 9));
                    if (fb != null) u.OrderFocus(fb); else u.OrderAuto();
                    break;
                default:
                    if (st != Phase.Scouting) u.OrderHold(pos);
                    break;
            }
        }

        string name = current.Name;
        WarManager.Notify($"Набег на {name} продолжается: армия у деревни, до конца боя {BattleTimeLeft / 60f:F0} мин", true);
    }

    static long CellKey(int kind, int x, int y) { return ((long)kind << 40) ^ ((long)(x + 100000) << 20) ^ (long)(y + 100000); }
}

/// <summary>Добыча строкой полей (сохранение набегов): 10 простых ресурсов и по 3 сорта досок, плит, стали, укреплённых плит.</summary>
public static class LootSave
{
    public const int Fields = 22;

    public static string[] Pack(Loot l)
    {
        var a = new string[Fields];
        if (l == null) { for (int i = 0; i < Fields; i++) a[i] = "0"; return a; }
        int[] v = { l.gold, l.wood, l.stone, l.food, l.iron, l.oats, l.berries, l.herbs, l.potions, l.resin };
        for (int i = 0; i < 10; i++) a[i] = SaveBag.I(v[i]);
        for (int q = 0; q < 3; q++)
        {
            a[10 + q] = SaveBag.I(l.planks[q]);
            a[13 + q] = SaveBag.I(l.plates[q]);
            a[16 + q] = SaveBag.I(l.steel[q]);
            a[19 + q] = SaveBag.I(l.reinforced[q]);
        }
        if (!l.HasItems) return a;
        // Трофеи (обновление 23) - после всех полей, по штуке на запись: «i» - отнятое, «c» - копии, дальше «ключ*кол-во».
        // Добыча в записи всегда последняя, поэтому старые поля не сдвигаются
        var all = new List<string>(a);
        for (int i = 0; i < l.items.Kinds; i++) all.Add("i" + l.items.KeyAt(i) + "*" + SaveBag.I(l.items.CountAt(i)));
        for (int i = 0; i < l.copies.Kinds; i++) all.Add("c" + l.copies.KeyAt(i) + "*" + SaveBag.I(l.copies.CountAt(i)));
        return all.ToArray();
    }

    /// <summary>Добыча из полей записи, начиная с from.</summary>
    public static Loot Unpack(string[] a, int from)
    {
        var l = new Loot();
        int F(int i) { return Mathf.Max(0, SaveBag.At(a, from + i)); }
        l.gold = F(0); l.wood = F(1); l.stone = F(2); l.food = F(3); l.iron = F(4);
        l.oats = F(5); l.berries = F(6); l.herbs = F(7); l.potions = F(8); l.resin = F(9);
        for (int q = 0; q < 3; q++)
        {
            l.planks[q] = F(10 + q);
            l.plates[q] = F(13 + q);
            l.steel[q] = F(16 + q);
            l.reinforced[q] = F(19 + q);
        }
        if (a == null) return l;
        for (int i = from + Fields; i < a.Length; i++)
        {
            string f = a[i];
            if (string.IsNullOrEmpty(f) || f.Length < 3 || (f[0] != 'i' && f[0] != 'c')) continue;
            int star = f.LastIndexOf('*');
            if (star <= 1) continue;
            (f[0] == 'i' ? l.items : l.copies).Add(f.Substring(1, star - 1), SaveBag.ParseInt(f.Substring(star + 1)));
        }
        return l;
    }
}
