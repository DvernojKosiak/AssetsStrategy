using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Рынок между игроками (в аккаунте вместо рынка гильдии). Открывается так же - через Торговый дом.
///  - «Купить»: лоты всех игроков. Платите марками - товар сразу в казну (вещь - в Торговый дом), марки уходят продавцу (даже если его нет в игре).
///  - «Продать»: товар (сырьё, материалы, зелья, золото) - сколько - цена в марках за весь лот; или штучная вещь
///    (оружие, щит, броня или плащ, модуль или ядро хаоса, ловушка, боец, заслон) - цена в марках. Товар сразу уходит из города.
///    Вещь уходит со всем, что на неё наложено (улучшения, доводка, заряды, ядро хаоса на щите, усиление брони),
///    боец - со своим снаряжением и ядром хаоса.
///  - «Мои лоты»: свои лоты; снять - товар вернётся. Сначала открыто одно место под лот, следующие покупаются
///    за марки и золото (LotRules.SlotCost), сколько всего - зависит от уровня Торгового дома (до Api.MaxLotsPerPlayer).
/// Доставка: покупатель платит цену лота и сбор за доставку (LotRules.DeliveryFee, ~3%). Продавцу приходит цена лота, сбор сгорает.
/// Продажи приходят в AccountManager.HandleSales (марки - один раз на продажу).
/// Рынок клана (OpenClan, только в военном городе): то же самое, но лоты видят только свои по войне, платят золотом военного города.
/// Режим «рынок клана» (OpenClan, на клановой войне из военного города): лоты только своего клана, платят золотом
/// военного города; проданное приходит продавцу с сохранением военного города.
/// Создаётся вместе с AccountManager.
/// </summary>
public class PlayerMarket : MonoBehaviour
{
    static PlayerMarket inst;
    static bool open;
    static Rect winRect;

    const float RefreshEvery = 10f;

    int tab;            // 0 - купить, 1 - продать, 2 - мои лоты
    int buyCat = -1;    // -1 - все разделы
    int sellCat;
    Vector2 scroll;
    readonly List<MarketLot> lots = new List<MarketLot>();
    bool loading, busy;
    float refreshTimer;
    string msg = "";

    bool hasGood;
    GoodId sellGood;
    MarketItem sellItem;    // выбранная штучная вещь (разделы 3..6)
    string unitsText = "10", priceText = "10";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; open = false; winRect = default; clan = false; }

    void Awake() { inst = this; }
    void OnDestroy() { if (inst == this) inst = null; }

    public static bool IsOpen => open;
    public static bool IsPointerOver(Vector2 gp) { return open && winRect.Contains(gp); }
    public static void Close() { open = false; }

    static bool clan;   // рынок клана на войне

    /// <summary>Рынок игроков (дома).</summary>
    public static void Open() { OpenAs(false); }

    /// <summary>Рынок клана (в военном городе).</summary>
    public static void OpenClan() { OpenAs(true); }

    static void OpenAs(bool clanMode)
    {
        if (inst == null) return;
        if (clan != clanMode) { inst.lots.Clear(); inst.tab = 0; }
        clan = clanMode;
        open = true;
        inst.msg = "";
        inst.Refresh();
    }

    // Тревога: свернуть окно, чтобы было видно бой
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable() { WarManager.OnAlarm -= CollapseForAlarm; }
    bool CollapseForAlarm()
    {
        if (!open) return false;
        open = false;
        return true;
    }

    void Update()
    {
        if (!open) return;
        if (!AccountManager.LoggedIn || TradeGuild.House == null) { open = false; return; }
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { open = false; return; }
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) Refresh();
    }

    // ---------- Сервер ----------

    static string Token => AccountManager.Token;

    // Валюта: дома - марки, на рынке клана - золото военного города
    static string Cur => clan ? "золота" : "марок";
    static string Sym => clan ? "зол." : "◈";
    static string SymCol => clan ? "#FFE040" : "#9FE0FF";
    static int Money { get { var rm = ResourceManager.Instance; return rm == null ? 0 : clan ? rm.gold : rm.marks; } }
    static bool Pay(int n) { var rm = ResourceManager.Instance; return rm != null && (clan ? rm.Spend(n, 0, 0, 0) : rm.SpendMarks(n)); }
    static void Refund(int n) { var rm = ResourceManager.Instance; if (rm == null) return; if (clan) { CityIncome.Skip(); rm.Add(n, 0, 0, 0); } else rm.AddMarks(n); }

    // Сколько платит покупатель: дома - цена лота и доставка (она сгорает), на рынке клана - только цена
    static int Fee(int price) { return clan ? 0 : LotRules.DeliveryFee(price); }
    static int Pays(int price) { return price + Fee(price); }

    // Места под лоты: дома - сколько открыто на сервере, на рынке клана - все
    static int Slots
    {
        get
        {
            if (clan) return Api.MaxLotsPerPlayer;
            var p = AccountManager.Profile;
            return Mathf.Clamp(p != null ? p.lotSlots : 0, LotRules.StartSlots, Api.MaxLotsPerPlayer);
        }
    }
    bool SlotsFull => MyLots().Count >= Slots;
    string FullText => Slots < Api.MaxLotsPerPlayer ? $"Все места под лоты заняты ({Slots}) — откройте новое во вкладке «Мои лоты»" : $"Больше {Api.MaxLotsPerPlayer} лотов нельзя";
    static int HouseLevel => House != null ? Mathf.Max(1, House.level) : 1;

    static void Call(string homePath, string clanPath, ApiRequest req, System.Action<ApiResponse> done)
    {
        AccountManager.Backend.Clan(clan ? clanPath : homePath, Token, req, done);
    }
    static string MyId => AccountManager.Profile != null ? AccountManager.Profile.id : "";

    void Refresh()
    {
        refreshTimer = RefreshEvery;
        if (loading || string.IsNullOrEmpty(Token)) return;
        loading = true;
        Call(Api.MarketList, Api.WarMarketList, new ApiRequest(), res =>
        {
            loading = false;
            if (!res.ok) { msg = UiKit.Col(UiKit.BadHex, res.error); return; }
            lots.Clear();
            if (res.lots != null) lots.AddRange(res.lots);
            if (!clan) AccountManager.HandleSales(res.sales);
        });
    }

    static GoodId GoodOf(MarketLot l) { return new GoodId((GoodKind)l.kind, (QualityTier)l.tier); }

    void Buy(MarketLot l)
    {
        var rm = ResourceManager.Instance;
        if (busy || rm == null) return;
        int pays = Pays(l.price);
        if (Money < pays) { msg = UiKit.Col(UiKit.BadHex, $"Не хватает {Cur}: нужно {pays}"); return; }
        if (l.item > 0)
        {
            string why = ItemProblem(l);
            if (why != "") { msg = UiKit.Col(UiKit.BadHex, why); return; }
        }
        else if (StorageLimits.RoomForGood((GoodKind)l.kind) < l.units)
        {
            // Лимит склада (StorageLimits): иначе купленное пропало бы
            msg = UiKit.Col(UiKit.BadHex, "Склад полон — столько не влезет. Улучшите Цитадель или постройте Хранилище");
            return;
        }
        // Марки (цена и доставка) списываем сразу; не вышло - вернём
        if (!Pay(pays)) return;
        busy = true;
        Call(Api.MarketBuy, Api.WarMarketBuy, new ApiRequest { lotId = l.id }, res =>
        {
            busy = false;
            if (!res.ok || res.lot == null)
            {
                Refund(pays);
                msg = UiKit.Col(UiKit.BadHex, res.error);
                Refresh();
                return;
            }
            Deliver(res.lot);
            lots.RemoveAll(x => x.id == res.lot.id);
            int fee = pays - l.price;
            msg = UiKit.Col(UiKit.GoodHex, $"Куплено: {LotName(res.lot)} за {pays} {Cur}" + (fee > 0 ? $" (доставка {fee})" : ""));
            AccountManager.SaveSoon();
        });
    }

    void Post()
    {
        if (busy) return;
        if (sellItem != null) { PostItem(); return; }
        if (!hasGood) return;
        if (!int.TryParse(unitsText, out int units) || units <= 0) { msg = UiKit.Col(UiKit.BadHex, "Сколько продаёте? Введите число"); return; }
        if (!int.TryParse(priceText, out int price) || price <= 0) { msg = UiKit.Col(UiKit.BadHex, "Цена — число больше нуля"); return; }
        if (MarketGoods.Have(sellGood) < units) { msg = UiKit.Col(UiKit.BadHex, "Столько нет в казне"); return; }
        if (SlotsFull) { msg = UiKit.Col(UiKit.BadHex, FullText); return; }

        GoodId g = sellGood;
        if (!MarketGoods.Take(g, units, out BuildingInstance _)) { msg = UiKit.Col(UiKit.BadHex, "Не удалось взять товар из казны"); return; }
        busy = true;
        var lot = new MarketLot { kind = (int)g.kind, tier = (int)g.tier, units = units, price = price };
        Call(Api.MarketPost, Api.WarMarketPost, new ApiRequest { lot = lot }, res =>
        {
            busy = false;
            if (!res.ok || res.lot == null)
            {
                MarketGoods.Give(g, units);   // товар возвращается
                msg = UiKit.Col(UiKit.BadHex, res.error);
                return;
            }
            lots.Insert(0, res.lot);
            msg = UiKit.Col(UiKit.GoodHex, $"Выставлено: {MarketGoods.UnitsText(g, units)} за {price} {Cur}");
            AccountManager.SaveSoon();
        });
    }

    void PostItem()
    {
        MarketItem it = sellItem;
        if (!int.TryParse(priceText, out int price) || price <= 0) { msg = UiKit.Col(UiKit.BadHex, "Цена — число больше нуля"); return; }
        if (SlotsFull) { msg = UiKit.Col(UiKit.BadHex, FullText); return; }
        string why = it.SellProblem();
        if (why != "") { msg = UiKit.Col(UiKit.BadHex, why); sellItem = null; return; }
        string name = it.Name, info = it.Details;   // пока вещь ещё в городе (у бойца - имя и снаряжение)
        if (!it.TakeFromTown(out BuildingInstance _)) { msg = UiKit.Col(UiKit.BadHex, "Не удалось забрать вещь"); sellItem = null; return; }
        sellItem = null;
        busy = true;
        var lot = new MarketLot { kind = 0, tier = 0, units = 1, price = price, item = (int)it.kind + 1, name = name, info = info, thing = ToThing(it) };
        Call(Api.MarketPost, Api.WarMarketPost, new ApiRequest { lot = lot }, res =>
        {
            busy = false;
            if (!res.ok || res.lot == null)
            {
                it.Receive(House, null);   // вещь возвращается
                msg = UiKit.Col(UiKit.BadHex, res.error);
                return;
            }
            lots.Insert(0, res.lot);
            msg = UiKit.Col(UiKit.GoodHex, $"Выставлено: {name} за {price} {Cur}");
            AccountManager.SaveSoon();
        });
    }

    static BuildingInstance House => TradeGuild.House != null ? TradeGuild.House.Building : null;

    // Купленный (или снятый) лот - в город: товар в казну, вещь - в Торговый дом и дальше на место
    static void Deliver(MarketLot l)
    {
        if (l.item <= 0) { MarketGoods.Give(GoodOf(l), l.units); return; }
        MarketItem it = FromThing(l);
        if (it != null) it.Receive(House, null);
    }

    // Картинка лота (вещь разбираем один раз на лот)
    static readonly Dictionary<string, string> lotIcons = new Dictionary<string, string>();
    static string LotIcon(MarketLot l)
    {
        if (l.item <= 0) return ItemIcons.Good((GoodKind)l.kind);
        string key = l.id ?? "";
        if (lotIcons.TryGetValue(key, out string id)) return id;
        if (lotIcons.Count > 500) lotIcons.Clear();
        id = ItemIcons.Of(FromThing(l));
        lotIcons[key] = id;
        return id;
    }

    static string LotName(MarketLot l) { return l.item > 0 ? (l.name ?? "вещь") : MarketGoods.UnitsText(GoodOf(l), l.units); }

    // Можно ли сейчас принять вещь (место в Оружейной, в казарме, есть ли такая казарма)
    static string ItemProblem(MarketLot l)
    {
        MarketItem it = FromThing(l);
        if (it == null)
            return l.item - 1 == (int)ItemKind.Soldier || l.item - 1 == (int)ItemKind.Squad ? "Нужна казарма, где учат таких бойцов" : "Эту вещь ваш город принять не может";
        return MarketItem.ReceiveProblem(it.kind, it.Space, 0, 0, 0, it.People);
    }

    // ---------- Вещь <-> лот ----------

    static MarketThing ToThing(MarketItem it)
    {
        var t = new MarketThing();
        switch (it.kind)
        {
            case ItemKind.Weapon: t.s = new SavedSoldier(); PutWeapon(t.s, it.weapon); break;
            case ItemKind.Shield: t.s = new SavedSoldier(); PutShield(t.s, it.shield); break;
            case ItemKind.Body: t.s = new SavedSoldier(); PutBody(t.s, it.body); break;
            case ItemKind.Soldier:
                t.s = PutSoldier(it.soldier);
                break;
            case ItemKind.Squad:
                t.s = new SavedSoldier();
                t.squad = new List<SavedSoldier>();
                if (it.squadLots != null) foreach (var l in it.squadLots) if (l != null) t.squad.Add(PutSoldier(l));
                break;
            case ItemKind.Module: t.modKind = (int)it.kit.kind; t.mk = it.kit.mk; t.charges = it.kit.charges; break;
            case ItemKind.Trap:
                if (it.trap != null)
                {
                    t.trapType = (int)it.trap.type; t.trapMods = (int)it.trap.mods; t.trapMk = it.trap.mk;
                    t.trapCharges = it.trap.charges; t.trapCombat = it.trap.combat; t.trapPower = it.trap.power;
                }
                break;
            case ItemKind.Barricade:
                if (it.gear != null) { t.post = (int)it.gear.kind; t.strength = it.gear.strength; t.pool = it.gear.pool; }
                break;
        }
        return t;
    }

    static MarketItem FromThing(MarketLot l)
    {
        MarketThing t = l.thing;
        int k = l.item - 1;
        if (t == null || k < 0 || k > (int)ItemKind.Squad) return null;
        var it = new MarketItem { kind = (ItemKind)k };
        switch (it.kind)
        {
            case ItemKind.Weapon: it.weapon = TakeWeapon(t.s); return it.weapon != null ? it : null;
            case ItemKind.Shield: it.shield = TakeShield(t.s); return it.shield != null ? it : null;
            case ItemKind.Body: it.body = TakeBody(t.s); return it.body != null ? it : null;
            case ItemKind.Soldier:
                it.soldier = TakeSoldier(t.s);
                return it.soldier != null ? it : null;
            case ItemKind.Squad:
            {
                // Отряд: каждый боец со своим снаряжением. Вид войск, которого у нас нет, - весь лот не принять
                if (t.squad == null || t.squad.Count == 0) return null;
                it.squadLots = new List<SoldierLot>();
                foreach (var s in t.squad)
                {
                    SoldierLot sl = TakeSoldier(s);
                    if (sl == null) return null;
                    it.squadLots.Add(sl);
                }
                return it;
            }
            case ItemKind.Module: it.kit = new ModuleKit { kind = (ModuleKind)t.modKind, mk = Mathf.Clamp(t.mk, 1, 3), charges = t.charges }; return it;
            case ItemKind.Trap:
                it.trap = new TrapKit { type = (TrapBase)t.trapType, mods = (TrapMod)t.trapMods, mk = Mathf.Clamp(t.trapMk, 1, 3),
                                        charges = t.trapCharges, combat = t.trapCombat, power = t.trapPower > 0f ? t.trapPower : 1f };
                return it;
            default:
                if (t.post < 0 || t.post >= FieldPost.Specs.Length) return null;
                it.gear = new FieldGear.Item((PostKind)t.post, t.strength > 0f ? t.strength : 1f);
                it.gear.pool = Mathf.Clamp(t.pool, 0f, it.gear.MaxPool);
                return it;
        }
    }

    static SavedSoldier PutSoldier(SoldierLot l)
    {
        var s = new SavedSoldier();
        if (l == null) return s;
        s.unit = l.unit != null ? l.unit.unitName : "";
        s.hearty = l.hearty;
        PutWeapon(s, l.weapon);
        PutShield(s, l.shield);
        PutBody(s, l.body);
        if (l.chaosCharges > 0) { s.cMk = l.chaosMk; s.cCharges = l.chaosCharges; }
        s.ramBroken = l.ramBroken;
        s.ramWear = l.ramWear;
        return s;
    }

    static SoldierLot TakeSoldier(SavedSoldier s)
    {
        UnitType u = s != null ? BarracksController.FindType(s.unit) : null;
        if (u == null) return null;
        return new SoldierLot { unit = u, hearty = s.hearty, weapon = TakeWeapon(s), shield = TakeShield(s), body = TakeBody(s),
                                chaosMk = s.cMk, chaosCharges = s.cCharges, name = "", ramBroken = s.ramBroken, ramWear = s.ramWear };
    }

    static void PutWeapon(SavedSoldier s, Weapon w)
    {
        if (w == null) return;
        s.hasWeapon = true;
        s.wKind = (int)w.kind; s.wQuality = (int)w.quality; s.wUpgrade = w.upgrade;
        s.wDur = w.durability; s.wMaxDur = w.maxDurability;
        s.wPoison = w.poisonCharges; s.wSlow = w.slowCharges;
        s.wForged = w.forged; s.wForgeDamage = w.forgeDamage; s.wForgeDurability = w.forgeDurability;
        s.wForgeSpeed = w.forgeSpeed; s.wStars = w.forgeStars;
        s.wSalute = w.saluteCharges; s.wSaluteMk = w.saluteMk;
    }

    static void PutShield(SavedSoldier s, Shield sh)
    {
        if (sh == null) return;
        s.hasShield = true;
        s.sQuality = (int)sh.quality; s.sUpgrade = sh.upgrade;
        s.sDur = sh.durability; s.sMaxDur = sh.maxDurability;
        s.sChaosMk = sh.chaosMk; s.sChaosCharges = sh.chaosCharges;
    }

    static void PutBody(SavedSoldier s, BodyGear g)
    {
        if (g == null) return;
        s.hasBody = true;
        s.bKind = (int)g.kind; s.bQuality = (int)g.quality; s.bUpgrade = g.upgrade;
        s.bDur = g.durability; s.bMaxDur = g.maxDurability;
    }

    static BodyGear TakeBody(SavedSoldier s)
    {
        if (s == null || !s.hasBody) return null;
        var g = new BodyGear((BodyKind)s.bKind, (QualityTier)Mathf.Clamp(s.bQuality, 0, 2));
        if (g.IsArmor) g.upgrade = Mathf.Clamp(s.bUpgrade, 0, BodyGear.MaxUpgrade);
        if (s.bMaxDur > 0f) g.maxDurability = s.bMaxDur;
        g.durability = Mathf.Clamp(s.bDur, 0f, g.maxDurability);
        return g;
    }

    static Weapon TakeWeapon(SavedSoldier s)
    {
        if (s == null || !s.hasWeapon) return null;
        var w = new Weapon((WeaponKind)s.wKind, (QualityTier)s.wQuality)
        {
            upgrade = s.wUpgrade, poisonCharges = s.wPoison, slowCharges = s.wSlow,
            forged = s.wForged, forgeDamage = s.wForgeDamage, forgeDurability = s.wForgeDurability,
            forgeSpeed = s.wForgeSpeed, forgeStars = s.wStars, saluteCharges = s.wSalute, saluteMk = s.wSaluteMk,
        };
        if (s.wMaxDur > 0f) w.maxDurability = s.wMaxDur;
        w.durability = Mathf.Clamp(s.wDur, 0f, w.maxDurability);
        return w;
    }

    static Shield TakeShield(SavedSoldier s)
    {
        if (s == null || !s.hasShield) return null;
        var sh = new Shield((QualityTier)s.sQuality) { upgrade = s.sUpgrade, chaosMk = s.sChaosMk, chaosCharges = s.sChaosCharges };
        if (s.sMaxDur > 0f) sh.maxDurability = s.sMaxDur;
        sh.durability = Mathf.Clamp(s.sDur, 0f, sh.maxDurability);
        return sh;
    }

    void Cancel(MarketLot l)
    {
        if (busy) return;
        busy = true;
        Call(Api.MarketCancel, Api.WarMarketCancel, new ApiRequest { lotId = l.id }, res =>
        {
            busy = false;
            if (!res.ok || res.lot == null) { msg = UiKit.Col(UiKit.BadHex, res.error); Refresh(); return; }
            Deliver(res.lot);
            lots.RemoveAll(x => x.id == res.lot.id);
            msg = UiKit.Col(UiKit.GoodHex, res.lot.item > 0 ? "Лот снят, вещь вернулась в город" : "Лот снят, товар вернулся в казну");
            AccountManager.SaveSoon();
        });
    }

    List<MarketLot> MyLots() { return lots.FindAll(l => l.sellerId == MyId); }

    // Средняя цена за штуку этого товара у других игроков (подсказка при продаже)
    string PriceHint(GoodId g)
    {
        int units = 0, gold = 0;
        foreach (var l in lots)
            if (l.item <= 0 && l.sellerId != MyId && GoodOf(l).Equals(g)) { units += l.units; gold += l.price; }
        if (units == 0) return "других лотов с этим товаром нет";
        return $"у других: ~{gold / (float)units:0.##} {Cur} за штуку";
    }

    // Под ценой: сколько заплатит покупатель (доставка сгорает) и сколько придёт вам
    float DrawFeeNote(float x, float y, float w)
    {
        if (clan || !int.TryParse(priceText, out int p) || p <= 0) return y;
        int fee = LotRules.DeliveryFee(p);
        LocGUI.Label(new Rect(x, y, w, 22f), UiKit.Col(UiKit.MutedHex, $"Покупатель заплатит {p + fee} ◈: {p} — вам, {fee} — доставка (сгорает)"), UiKit.Small);
        return y + 24f;
    }

    // ---------- Окно ----------

    void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        if (!open) return;
        UiKit.Apply();
        UiKit.MarkTextWindow();
        GUI.depth = -10;
        float w = Mathf.Min(760f, UiKit.W - 40f), h = Mathf.Min(560f, UiKit.H - 100f);
        winRect = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f - 10f, w, h);
        if (UiKit.Phone) { winRect = UiKit.CenterWindow(760f, 560f); w = winRect.width; h = winRect.height; }
        CameraController.BlockZoom();
        Rect c = UiKit.Window(winRect, clan ? "Рынок клана (только свои)" : "Рынок игроков", out bool close);
        if (close) { open = false; return; }

        string mineTab = clan ? $"Мои лоты ({MyLots().Count})" : $"Мои лоты ({MyLots().Count}/{Slots})";
        string[] tabs = { "Купить", "Продать", mineTab };
        if (tab >= tabs.Length) tab = 0;
        int nt = UiKit.Tabs(new Rect(c.x, c.y, c.width, UiKit.BtnH), tabs, tab);
        if (nt != tab) { tab = nt; scroll = Vector2.zero; msg = ""; }
        float y = c.y + UiKit.BtnH + 8f;

        var rm = ResourceManager.Instance;
        string gold = clan ? $"Золото города на войне: <color=#FFE040><b>{Money}</b></color>" : $"Марки: <color=#9FE0FF><b>{Money}</b></color> ◈";
        LocGUI.Label(new Rect(c.x, c.yMax - 22f, c.width * 0.5f, 22f), gold, UiKit.Body);
        LocGUI.Label(new Rect(c.x + c.width * 0.4f, c.yMax - 22f, c.width * 0.6f, 22f), msg, new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleRight });
        Rect body = new Rect(c.x, y, c.width, c.yMax - 28f - y);

        if (tab == 0) DrawBuy(body);
        else if (tab == 1) DrawSell(body);
        else DrawMine(body);
    }

    void DrawBuy(Rect r)
    {
        int nc = UiKit.Tabs(new Rect(r.x, r.y, r.width - 110f, 24f), BuyCats, buyCat + 1) - 1;
        if (nc != buyCat) { buyCat = nc; scroll = Vector2.zero; }
        if (UiKit.Button(new Rect(r.xMax - 100f, r.y, 100f, 24f), loading ? "…" : "Обновить", !loading, UiKit.Kind.Normal, "Лоты обновляются сами каждые 10 секунд")) Refresh();

        var list = lots.FindAll(l => buyCat < 0 || CatOf(l) == buyCat);
        Rect view = new Rect(r.x, r.y + 30f, r.width, r.height - 30f);
        DrawLots(view, list, false);
    }

    // Разделы: 0..2 - товары, 3 - оружие и щиты, 4 - модули и ловушки, 5 - войска, 6 - заслоны
    static readonly string[] SellCats = { "Сырьё", "Материалы", "Зелья", "Оружие", "Модули", "Войска", "Заслоны" };
    static readonly string[] BuyCats = { "Всё", "Сырьё", "Материалы", "Зелья", "Оружие", "Модули", "Войска", "Заслоны" };

    static int CatOf(MarketLot l)
    {
        if (l.item <= 0) return MarketGoods.Category((GoodKind)l.kind);
        return new MarketItem { kind = (ItemKind)(l.item - 1) }.Category;
    }

    void DrawMine(Rect r)
    {
        if (clan)
        {
            LocGUI.Label(new Rect(r.x, r.y, r.width, 22f), UiKit.Col(UiKit.MutedHex, $"Ваши лоты (не больше {Api.MaxLotsPerPlayer}). Снимете — товар вернётся в казну."), UiKit.Small);
            DrawLots(new Rect(r.x, r.y + 26f, r.width, r.height - 26f), MyLots(), true);
            return;
        }
        float top = DrawSlots(r);
        DrawLots(new Rect(r.x, r.y + top, r.width, r.height - top), MyLots(), true);
    }

    // Места под лоты: сколько открыто и кнопка «Открыть ещё одно» (марки + золото)
    float DrawSlots(Rect r)
    {
        int have = Slots, cap = Mathf.Min(Api.MaxLotsPerPlayer, LotRules.SlotsForHouse(HouseLevel));
        string head = $"Места под лоты: <b>{MyLots().Count}</b> занято из <b>{have}</b>. Снимете лот — товар вернётся в казну.";
        LocGUI.Label(new Rect(r.x, r.y, r.width, 22f), head, UiKit.Small);
        float y = r.y + 24f;
        var rm = ResourceManager.Instance;
        string why = null, label;
        bool more = LotRules.SlotCost(have, out int mk, out int gold);
        if (!more) label = $"Открыты все {Api.MaxLotsPerPlayer} мест";
        else
        {
            label = $"Открыть место №{have + 1}: <color=#9FE0FF><b>{mk}</b></color> ◈ + <color={UiKit.GoldHex}><b>{gold}</b></color> зол.";
            if (have >= cap) why = HouseLevel < 3 ? $"При Торговом доме Ур.{HouseLevel} мест не больше {cap}. Улучшите Торговый дом" : $"Мест не больше {cap}";
            else if (rm == null || rm.marks < mk) why = $"Не хватает марок: нужно {mk} ◈";
            else if (rm.gold < gold) why = $"Не хватает золота: нужно {gold}";
        }
        bool can = more && why == null && !busy;
        if (UiKit.Button(new Rect(r.x, y, Mathf.Min(r.width, 340f), 28f), label, can, UiKit.Kind.Primary,
                why ?? (more ? "Ещё один лот на рынке одновременно. Место остаётся навсегда." : null))) BuySlot(mk, gold);
        if (more && why != null) LocGUI.Label(new Rect(r.x + Mathf.Min(r.width, 340f) + 10f, y + 4f, r.width - 350f, 22f), UiKit.Col(UiKit.MutedHex, why), UiKit.Small);
        return 24f + 34f;
    }

    void BuySlot(int mk, int gold)
    {
        var rm = ResourceManager.Instance;
        if (busy || rm == null || AccountManager.Profile == null) return;
        if (rm.marks < mk || rm.gold < gold) return;
        int had = Slots;
        if (!rm.SpendMarks(mk)) return;
        CityIncome.Skip();
        if (!rm.Spend(gold, 0, 0, 0)) { rm.AddMarks(mk); return; }
        busy = true;
        AccountManager.Backend.Clan(Api.MarketSlotBuy, Token, new ApiRequest { slot = had }, res =>
        {
            busy = false;
            if (!res.ok || res.profile == null)
            {
                rm.AddMarks(mk);
                CityIncome.Skip();
                rm.Add(gold, 0, 0, 0);
                msg = UiKit.Col(UiKit.BadHex, StampPacks.ServerError(res.error));
                return;
            }
            AccountManager.Profile.lotSlots = res.profile.lotSlots;
            msg = UiKit.Col(UiKit.GoodHex, $"Открыто место под лот №{res.profile.lotSlots}");
            AccountManager.SaveSoon();
        });
    }

    void DrawLots(Rect view, List<MarketLot> list, bool mine)
    {
        const float rowH = 30f;
        if (list.Count == 0)
        {
            LocGUI.Label(new Rect(view.x, view.y, view.width, 40f), UiKit.Col(UiKit.MutedHex,
                loading ? "Загрузка…" : mine ? "У вас нет лотов. Выставьте товар во вкладке «Продать»." : "Лотов пока нет. Игроки выставляют товар во вкладке «Продать»."), UiKit.Body);
            return;
        }
        var rm = ResourceManager.Instance;
        scroll = UiKit.BeginScrollView(view, scroll, new Rect(0f, 0f, view.width - 16f, list.Count * rowH));
        float w = view.width - 16f;
        GUIStyle line = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip };
        for (int i = 0; i < list.Count; i++)
        {
            MarketLot l = list[i];
            float y = i * rowH;
            if (i % 2 == 0) { Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.04f); GUI.DrawTexture(new Rect(0f, y, w, rowH - 2f), Texture2D.whiteTexture); GUI.color = oc; }
            string who = l.sellerId == MyId ? UiKit.Col(UiKit.GoodHex, "ваш лот") : UiKit.Col(UiKit.MutedHex, l.sellerName);
            bool own = l.sellerId == MyId;
            int pays = Pays(l.price), fee = pays - l.price;
            // Чужой лот: сколько заплатите вы (с доставкой); свой - сколько придёт вам
            string price = $"<color={SymCol}><b>{(own ? l.price : pays)}</b></color> {Sym}" + (fee > 0 && !own ? UiKit.Col(UiKit.MutedHex, $" (доставка {fee})") : "");
            string text;
            if (l.item > 0) text = $"<b>{l.name}</b> — {price} · {who}";
            else
            {
                GoodId g = GoodOf(l);
                float each = (own ? l.price : pays) / (float)Mathf.Max(1, l.units);
                text = $"{MarketGoods.Colored(g, MarketGoods.UnitsText(g, l.units))} — {price} {UiKit.Col(UiKit.MutedHex, $"({each:0.##}/шт)")} · {who}";
            }
            float il = ItemIcons.Lead(new Rect(6f, y + 2f, 24f, 24f), LotIcon(l), l.item <= 0 && MarketGoods.IsRefined((GoodKind)l.kind) ? l.tier : -1);
            LocGUI.Label(new Rect(6f + il, y + 4f, w - 130f - il, 22f), Loc.Content(text, l.item > 0 ? l.info : null), line);
            Rect br = new Rect(w - 116f, y + 2f, 112f, rowH - 6f);
            if (l.sellerId == MyId)
            {
                if (UiKit.Button(br, "Снять", !busy, UiKit.Kind.Normal, l.item > 0 ? "Вещь вернётся в город" : "Товар вернётся в казну")) Cancel(l);
            }
            else
            {
                bool can = !busy && rm != null && Money >= pays;
                string feeTip = fee > 0 ? $"\n{l.price} {Sym} — продавцу, {fee} {Sym} — доставка (сгорает)" : "";
                string tip = (!can ? $"Нужно {pays} {Cur}" : l.item > 0 ? "Вещь привезут в Торговый дом" : "Товар сразу придёт в казну") + feeTip;
                if (UiKit.Button(br, "Купить", can, UiKit.Kind.Primary, tip)) Buy(l);
            }
        }
        GUI.EndScrollView();
    }

    void DrawSell(Rect r)
    {
        int nc = UiKit.Tabs(new Rect(r.x, r.y, r.width, 24f), SellCats, sellCat);
        if (nc != sellCat) { sellCat = nc; hasGood = false; sellItem = null; scroll = Vector2.zero; }
        if (sellCat >= 3) { DrawSellItems(r); return; }
        float y = r.y + 32f;

        // Товары раздела (золото тоже можно продать за марки)
        var goods = MarketGoods.AllVisible(sellCat, true);
        if (clan) goods.RemoveAll(g => g.kind == GoodKind.Gold);   // на рынке клана платят золотом - его не продают
        float leftW = Mathf.Min(320f, r.width * 0.45f);
        const float rowH = 26f;
        Rect view = new Rect(r.x, y, leftW, r.height - 32f);
        scroll = UiKit.BeginScrollView(view, scroll, new Rect(0f, 0f, leftW - 16f, goods.Count * rowH));
        for (int i = 0; i < goods.Count; i++)
        {
            GoodId g = goods[i];
            int have = MarketGoods.Have(g);
            bool on = hasGood && sellGood.Equals(g);
            string label = $"{MarketGoods.Colored(g)}  {UiKit.Col(UiKit.MutedHex, "есть " + have)}";
            float il = ItemIcons.Lead(new Rect(0f, i * rowH, 22f, rowH - 3f), ItemIcons.Good(g.kind), MarketGoods.IsRefined(g.kind) ? (int)g.tier : -1);
            if (UiKit.Button(new Rect(il, i * rowH, leftW - 18f - il, rowH - 3f), label, have > 0, on ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                    have > 0 ? null : "В казне нет этого товара"))
            {
                hasGood = true;
                sellGood = g;
                unitsText = Mathf.Min(have, Mathf.Max(1, MarketGoods.Pack(g))).ToString();
            }
        }
        GUI.EndScrollView();

        // Сколько и почём
        float x = r.x + leftW + 16f, w = r.xMax - x;
        if (!hasGood)
        {
            LocGUI.Label(new Rect(x, y, w, 60f), UiKit.Col(UiKit.MutedHex, "Выберите товар слева."), UiKit.Body);
            return;
        }
        int haveNow = MarketGoods.Have(sellGood);
        float gil = ItemIcons.Lead(new Rect(x, y - 2f, 26f, 26f), ItemIcons.Good(sellGood.kind), MarketGoods.IsRefined(sellGood.kind) ? (int)sellGood.tier : -1);
        LocGUI.Label(new Rect(x + gil, y, w - gil, 22f), $"<b>{MarketGoods.Colored(sellGood)}</b>  {UiKit.Col(UiKit.MutedHex, "в казне " + haveNow)}", UiKit.Body);
        y += 30f;
        LocGUI.Label(new Rect(x, y, 120f, 22f), "Сколько:", UiKit.Body);
        unitsText = MobileInput.Field(new Rect(x + 120f, y, 100f, 24f), "MarketUnits", unitsText, 7, null, false, TouchScreenKeyboardType.NumberPad);
        if (LocGUI.Button(new Rect(x + 226f, y, 50f, 24f), "всё")) unitsText = haveNow.ToString();
        y += 32f;
        LocGUI.Label(new Rect(x, y, 120f, 22f), clan ? "Цена (золото):" : "Цена (марки):", UiKit.Body);
        priceText = MobileInput.Field(new Rect(x + 120f, y, 100f, 24f), "MarketPrice", priceText, 7, null, false, TouchScreenKeyboardType.NumberPad);
        y += 30f;
        LocGUI.Label(new Rect(x, y, w, 22f), UiKit.Col(UiKit.MutedHex, PriceHint(sellGood)), UiKit.Small);
        y += 26f;
        if (int.TryParse(unitsText, out int u) && int.TryParse(priceText, out int p) && u > 0 && p > 0)
        {
            LocGUI.Label(new Rect(x, y, w, 22f), $"За штуку: {p / (float)u:0.##} {Cur}", UiKit.Small);
            y += 24f;
        }
        y = DrawFeeNote(x, y, w);
        y += 8f;
        bool can = !busy && !SlotsFull;
        if (UiKit.Button(new Rect(x, y, Mathf.Min(w, 260f), 32f), "Выставить на рынок", can, UiKit.Kind.Primary,
                can ? "Товар сразу уйдёт из казны. Снимете лот — вернётся." : FullText)) Post();
    }

    // Штучные вещи: оружие и щиты, модули и ловушки, бойцы, заслоны
    List<MarketItem> itemsCache = new List<MarketItem>();
    float itemsTimer;

    void DrawSellItems(Rect r)
    {
        float y = r.y + 32f;
        itemsTimer -= Time.unscaledDeltaTime;
        if (Event.current.type == EventType.Layout && itemsTimer <= 0f) { itemsCache = MarketItem.SellableNow(sellCat); itemsTimer = 0.5f; }
        var items = itemsCache;
        float leftW = Mathf.Min(340f, r.width * 0.48f);
        const float rowH = 26f;
        Rect view = new Rect(r.x, y, leftW, r.height - 32f);
        if (items.Count == 0)
            LocGUI.Label(new Rect(r.x, y, leftW, 60f), UiKit.Col(UiKit.MutedHex, sellCat == 3 ? "В Оружейных пусто." : sellCat == 5 ? (WarCityMode.Active ? "В казармах нет свободных бойцов. Обученные сами уходят на карту войны — чтобы продать, включите в казарме «Держать в казарме» (вкладка «Войска»)." : "В казармах нет свободных бойцов.") :
                sellCat == 6 ? "В запасе Цитадели нет заслонов и пунктов." : "В Цитадели нет модулей и ловушек."), UiKit.Body);
        scroll = UiKit.BeginScrollView(view, scroll, new Rect(0f, 0f, leftW - 16f, items.Count * rowH));
        for (int i = 0; i < items.Count; i++)
        {
            MarketItem it = items[i];
            bool on = sellItem != null && Same(sellItem, it);
            float il = ItemIcons.Lead(new Rect(0f, i * rowH, 22f, rowH - 3f), ItemIcons.Of(it));
            if (UiKit.Button(new Rect(il, i * rowH, leftW - 18f - il, rowH - 3f), it.Name, true, on ? UiKit.Kind.Primary : UiKit.Kind.Normal, it.Details))
            {
                sellItem = it;
                priceText = Mathf.Max(1, Mathf.RoundToInt(it.Value)).ToString();
            }
        }
        GUI.EndScrollView();

        float x = r.x + leftW + 16f, w = r.xMax - x;
        if (sellItem == null)
        {
            LocGUI.Label(new Rect(x, y, w, 60f), UiKit.Col(UiKit.MutedHex, "Выберите вещь слева."), UiKit.Body);
            return;
        }
        float sil = ItemIcons.Lead(new Rect(x, y - 2f, 26f, 26f), ItemIcons.Of(sellItem));
        LocGUI.Label(new Rect(x + sil, y, w - sil, 22f), $"<b>{sellItem.Name}</b>", UiKit.Body);
        y += 24f;
        GUIStyle wrap = new GUIStyle(UiKit.Small) { wordWrap = true };
        LocGUI.Label(new Rect(x, y, w, 44f), UiKit.Col(UiKit.MutedHex, sellItem.Details), wrap);
        y += 48f;
        LocGUI.Label(new Rect(x, y, 120f, 22f), clan ? "Цена (золото):" : "Цена (марки):", UiKit.Body);
        priceText = MobileInput.Field(new Rect(x + 120f, y, 100f, 24f), "MarketPrice", priceText, 7, null, false, TouchScreenKeyboardType.NumberPad);
        y += 30f;
        LocGUI.Label(new Rect(x, y, w, 22f), UiKit.Col(UiKit.MutedHex, $"Гильдия оценила бы примерно в {Mathf.RoundToInt(sellItem.Value)} {Sym}"), UiKit.Small);
        y += 24f;
        y = DrawFeeNote(x, y, w) + 6f;
        bool can = !busy && !SlotsFull;
        string tip = !can ? FullText : sellCat == 5 ? (sellItem.kind == ItemKind.Squad ? "Отряд сразу уйдёт из казармы целиком, со всем снаряжением. Снимете лот — вернётся отрядом наёмников." : "Боец сразу уйдёт из казармы. Снимете лот — вернётся наёмником.") : "Вещь сразу уйдёт из города. Снимете лот — вернётся.";
        if (UiKit.Button(new Rect(x, y, Mathf.Min(w, 260f), 32f), "Выставить на рынок", can, UiKit.Kind.Primary, tip)) Post();
    }

    static bool Same(MarketItem a, MarketItem b)
    {
        return a.kind == b.kind && a.weapon == b.weapon && a.shield == b.shield && a.body == b.body && a.trap == b.trap && a.gear == b.gear && a.agent == b.agent && a.squad == b.squad
            && (a.kind != ItemKind.Module || (a.kit.kind == b.kit.kind && a.kit.mk == b.kit.mk && a.kit.charges == b.kit.charges));
    }
}
