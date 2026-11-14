using System;
using System.Collections.Generic;
using UnityEngine;

// ---------- Обмен игры с сервером ----------
// Игра шлёт POST-запрос с JSON (ApiRequest) на адрес сервера + путь (см. Api),
// сервер отвечает JSON (ApiResponse). Одни и те же классы у игры и у сервера.

public static class Api
{
    public const int Version = 1;
    /// <summary>Номер сборки игры (final-8 = 8). Растёт с каждым выпуском; сервер может требовать не ниже (принудительное обновление).</summary>
    public const int Build = 15;   // final-13
    public const int DefaultPort = 7777;
    public const string DefaultAddress = "http://localhost:7777";

    public const string Ping = "/api/ping";
    public const string Register = "/api/register";
    public const string Login = "/api/login";
    public const string Resume = "/api/resume";
    public const string Logout = "/api/logout";
    public const string SaveProfile = "/api/profile/save";
    public const string ChangePassword = "/api/password";
    public const string LoadCity = "/api/city/load";
    public const string SaveCity = "/api/city/save";
    public const string FindOpponent = "/api/raid/find";
    public const string StartRaid = "/api/raid/start";
    public const string FinishRaid = "/api/raid/finish";
    public const string MarketList = "/api/market/list";
    public const string MarketPost = "/api/market/post";
    public const string MarketBuy = "/api/market/buy";
    public const string MarketCancel = "/api/market/cancel";
    public const int MaxLotsPerPlayer = 10;                 // больше мест под лоты не бывает (сколько открыто - AccountProfile.lotSlots, см. LotRules)
    public const string MarketSlotBuy = "/api/market/slot";   // открыть ещё одно место под лот (slot - сколько мест было); ответ - profile
    public const string ShieldDrop = "/api/shield/drop";      // снять щит своего города раньше срока
    public const string ShopClaim = "/api/shop/claim";        // купили марки в Google Play: productId, receipt (чек Unity IAP); ответ - grants, shopMarks
    public const string ClanInfo = "/api/clan/info";       // мой клан и чат (или список кланов, если клана нет)
    public const string ClanCreate = "/api/clan/create";
    public const string ClanJoin = "/api/clan/join";
    public const string ClanLeave = "/api/clan/leave";
    public const string ClanKick = "/api/clan/kick";
    public const string ClanRole = "/api/clan/role";       // назначить роль (memberId, role: ClanRoles)
    public const string ClanSay = "/api/clan/say";
    public const int MaxClanMembers = 30;
    public const int ClanChatKeep = 200;      // сколько последних сообщений хранит сервер
    public const int ClanMessageMax = 200;    // длина сообщения
    public const string BattlePush = "/api/battle/push";     // нападающий: снимок боя, в ответ - приказы защитника
    public const string BattleReplay = "/api/battle/replay"; // защитник: запись набега, который шёл без него (raidId - id отчёта RaidReport; final-9)
    public const string BattleWatch = "/api/battle/watch";   // защитник: «я дома» (home), в ответ - идёт ли на него набег и армия живого набега
    // Живой набег (см. Siege*): защитник в игре - бой идёт прямо в его деревне, его игра считает бой
    public const string SiegeArrive = "/api/siege/arrive";   // нападающий: армия у деревни (siegeArmy); ответ - бой вживую или нет (siegeLive)
    public const string SiegePush = "/api/siege/push";       // защитник: что сейчас в деревне (siegeFrame); ответ - приказы нападающего
    public const string SiegePull = "/api/siege/pull";       // нападающий: свои приказы (commands); ответ - что сейчас в деревне защитника
    public const float SiegeHomeSeconds = 8f;                // защитник «дома», если спрашивал сервер не позже стольких секунд назад
    public const float SiegeSpawnSeconds = 15f;              // столько ждём, пока игра защитника выставит армию; нет - бой без него
    public const float SiegeLostSeconds = 20f;               // столько нет вестей от защитника - бой окончен, армия уходит с тем, что несёт
    // Клановая война (см. WarProtocol)
    public const string WarInfo = "/api/war/info";           // состояние войны моего клана
    public const string WarStart = "/api/war/start";         // глава: искать соперника
    public const string WarCancel = "/api/war/cancel";       // глава: перестать искать
    public const string WarScheme = "/api/war/scheme";       // глава: поставить город игрока memberId на место slot
    public const string WarStatic = "/api/war/static";       // карта войны: города и известные здания
    public const string WarFrame = "/api/war/frame";         // карта войны: бойцы, здоровье, участки (since - номер события)
    public const string WarCmd = "/api/war/cmd";             // приказ (warCmd)
    public const string WarAdmin = "/api/war/admin";         // админ: перейти на день войны (day; 8 - закончить)
    public const string WarCityLoad = "/api/war/city/load";  // военный город игрока (city): полная копия его города
    public const string WarCitySave = "/api/war/city/save";  // сохранить военный город (city); ответ - доход с захваченных земель (warIncome)
    public const string WarDeploy = "/api/war/deploy";       // вывести бойцов из казарм военного города на карту (recruits)
    // Рынок клана на войне: только свои, платят золотом военного города (как рынок игроков: lot / lotId)
    public const string WarMarketList = "/api/war/market/list";
    public const string WarMarketPost = "/api/war/market/post";
    public const string WarMarketBuy = "/api/war/market/buy";
    public const string WarMarketCancel = "/api/war/market/cancel";
    // Лиги: мировой рейтинг кланов и игроков (league - показать только эту лигу, -1 - все)
    public const string LeagueTop = "/api/league/top";
    public const int LeagueTopSize = 100;      // сколько мест в таблице
    // Google Play: удаление аккаунта, жалобы и блокировка в чате, вход через Google Play Games, push-уведомления (см. ServerSafety)
    public const string DeleteAccount = "/api/account/delete";   // удалить свой аккаунт навсегда (text - слово-подтверждение, AccountRules.DeleteWord)
    public const string ChatReport = "/api/chat/report";         // пожаловаться на игрока (memberId, text - причина, since - номер сообщения или 0)
    public const string ChatBlock = "/api/chat/block";           // заблокировать (role = 1) или разблокировать (role = 0) игрока memberId; ответ - profile
    public const string GoogleLogin = "/api/google/login";       // вход через Google Play Games: authCode (или googleTicket); create - завести новый аккаунт
    public const string GoogleLink = "/api/google/link";         // привязать Google Play Games к своему аккаунту (authCode или googleTicket)
    public const string PushToken = "/api/push/token";           // устройство для push (pushToken, lang, pushOff); text = "remove" - забыть устройство
    public const string PushPlan = "/api/push/plan";             // когда закончатся стройки (pushPlan) - сервер пришлёт уведомление
    public const int ReportsPerDay = 20;                         // больше жалоб в сутки от одного игрока не принимаем
    // Страницы сервера для браузера (их адреса указываются в Google Play Console)
    public const string WebPrivacy = "/privacy";
    public const string WebTerms = "/terms";
    public const string WebDelete = "/delete-account";
    public const string WebAdmin = "/admin";

    // Обновление 14: набег короче - 12 минут на бой (было 30). С дорогой туда и обратно весь набег укладывается в 15 минут
    public const float RaidBattleSeconds = 12f * 60f;   // на бой у чужой деревни - 12 минут
    public const float RaidLockMinutes = 22f;           // пока идёт набег, на эту деревню не нападёт никто другой (было 45)
    public const float OldRaidLockMinutes = 45f;        // сборки до ShortRaidBuild: у них бой 30 минут - сервер держит набег по-старому
    public const int ShortRaidBuild = 14;

    public const int MaxCityJson = 2 * 1024 * 1024;   // больше 2 МБ город быть не может
}

[Serializable]
public class ApiRequest
{
    public int version = Api.Version;
    public int build = Api.Build;    // сборка игры (старые - 0): сервер может потребовать обновиться
    public string token;
    public string login;
    public string displayName;
    public string password;
    public string newPassword;
    public AccountProfile profile;
    public string city;   // CityData в JSON
    public string raidId;
    public RaidLoot loot;            // итог набега: что унесли
    public List<string> killed;      // итог набега: какие защитники погибли (по виду войск)
    public List<string> ruinedAt;    // итог набега: какие здания разрушены («x,y» - левая нижняя клетка в его городе)
    public bool hallSacked;
    public int ruined;
    public List<string> acks;        // id отчётов о набегах, которые игра уже учла в городе
    public BattleSnapshot battle;            // нападающий: как сейчас выглядит бой
    public List<BattleCommand> commands;     // защитник: его приказы
    public bool live;                        // защитник: окно боя открыто (он командует сам); итог набега: бой шёл вживую
    public bool home;                        // защитник: его деревня загружена и он может принять бой у себя
    public List<SiegeUnit> siegeArmy;        // нападающий: армия живого набега
    public bool camp;                        // нападающий: взяли дерево на лагерь
    public int siegeSide;                    // нападающий: с какой стороны деревни защитника подходит армия (0 - запад, 1 - восток, 2 - юг, 3 - север; final-8)
    public SiegeFrame siegeFrame;            // защитник: что сейчас в деревне
    public MarketLot lot;                    // рынок: новый лот
    public string lotId;                     // рынок: купить / снять этот лот
    public string clanId;                    // клан: вступить
    public string clanName;                  // клан: название (создать) или поиск
    public string text;                      // клан: описание (создать) или сообщение в чат
    public string memberId;                  // клан: кого исключить / кому сменить роль
    public int role;                         // клан: новая роль (ClanRoles)
    public int since;                        // клан: номер последнего полученного сообщения чата
    public int slot;                         // война: место в схеме
    public WarCommand warCmd;                // война: приказ
    public int day;                          // война, админ: на какой день перейти (1..7, 8 - закончить)
    public List<WarRecruit> recruits;        // война: бойцы, которых выводят из военного города на карту
    public bool fought;                      // итог набега: бой был (без этого рейтинг за набег не меняется)
    public int damage = -1;                  // итог набега: сколько процентов его построек (без стен и ворот) разрушено (-1 - старая игра)
    public int league = -1;                  // лиги: показать только эту лигу (-1 - все)
    public string productId;                 // магазин марок: какой набор купили
    public string receipt;                   // магазин марок: чек Unity IAP (product.receipt)
    public string authCode;                  // Google Play Games: одноразовый код для сервера (RequestServerSideAccess)
    public string googleTicket;              // Google Play Games: билет из прошлого ответа (код второй раз не годится)
    public bool create;                      // Google Play Games: аккаунта ещё нет - завести новый
    public string pushToken;                 // push: токен устройства (Firebase Cloud Messaging)
    public string lang;                      // push: язык уведомлений (ru, en, lt)
    public int pushOff = -1;                 // push: какие уведомления выключены (PushKinds, биты); -1 - не менять
    public List<PushPlanItem> pushPlan;      // push: стройки и улучшения, о которых сообщить
}

/// <summary>Что унесли из деревни (простые ресурсы и материалы по качеству: индекс = QualityTier).</summary>
[Serializable]
public class RaidLoot
{
    public int gold, wood, stone, food, iron, oats, berries, herbs, potions, resin;
    public int[] planks = new int[3], plates = new int[3], steel = new int[3], reinforced = new int[3];

    public int Total
    {
        get
        {
            int n = gold + wood + stone + food + iron + oats + berries + herbs + potions + resin;
            for (int i = 0; i < 3; i++) n += At(planks, i) + At(plates, i) + At(steel, i) + At(reinforced, i);
            return n;
        }
    }

    public static int At(int[] a, int i) { return a != null && i < a.Length ? Mathf.Max(0, a[i]) : 0; }

#if UNITY_5_3_OR_NEWER   // Loot - класс игры, отдельному серверу не нужен
    public static RaidLoot From(Loot l)
    {
        var r = new RaidLoot();
        if (l == null) return r;
        r.gold = l.gold; r.wood = l.wood; r.stone = l.stone; r.food = l.food; r.iron = l.iron;
        r.oats = l.oats; r.berries = l.berries; r.herbs = l.herbs; r.potions = l.potions; r.resin = l.resin;
        for (int i = 0; i < 3; i++) { r.planks[i] = l.planks[i]; r.plates[i] = l.plates[i]; r.steel[i] = l.steel[i]; r.reinforced[i] = l.reinforced[i]; }
        return r;
    }

    public Loot ToLoot()
    {
        var l = new Loot();
        l.gold = Mathf.Max(0, gold); l.wood = Mathf.Max(0, wood); l.stone = Mathf.Max(0, stone); l.food = Mathf.Max(0, food);
        l.iron = Mathf.Max(0, iron); l.oats = Mathf.Max(0, oats); l.berries = Mathf.Max(0, berries); l.herbs = Mathf.Max(0, herbs);
        l.potions = Mathf.Max(0, potions); l.resin = Mathf.Max(0, resin);
        for (int i = 0; i < 3; i++) { l.planks[i] = At(planks, i); l.plates[i] = At(plates, i); l.steel[i] = At(steel, i); l.reinforced[i] = At(reinforced, i); }
        return l;
    }
#endif

    /// <summary>Каждое число - не больше, чем в cap (чтобы унести больше, чем было в деревне, нельзя).</summary>
    public void ClampTo(RaidLoot cap)
    {
        gold = Mathf.Clamp(gold, 0, cap.gold); wood = Mathf.Clamp(wood, 0, cap.wood); stone = Mathf.Clamp(stone, 0, cap.stone);
        food = Mathf.Clamp(food, 0, cap.food); iron = Mathf.Clamp(iron, 0, cap.iron); oats = Mathf.Clamp(oats, 0, cap.oats);
        berries = Mathf.Clamp(berries, 0, cap.berries); herbs = Mathf.Clamp(herbs, 0, cap.herbs);
        potions = Mathf.Clamp(potions, 0, cap.potions); resin = Mathf.Clamp(resin, 0, cap.resin);
        planks = Clamp3(planks, cap.planks); plates = Clamp3(plates, cap.plates);
        steel = Clamp3(steel, cap.steel); reinforced = Clamp3(reinforced, cap.reinforced);
    }

    static int[] Clamp3(int[] a, int[] cap)
    {
        var r = new int[3];
        for (int i = 0; i < 3; i++) r[i] = Mathf.Min(At(a, i), At(cap, i));
        return r;
    }

    public string ShortText
    {
        get
        {
            var parts = new List<string>();
            if (gold > 0) parts.Add(gold + " зол");
            if (wood > 0) parts.Add(wood + " дер");
            if (stone > 0) parts.Add(stone + " кам");
            if (food > 0) parts.Add(food + " еды");
            int other = Total - gold - wood - stone - food;
            if (other > 0) parts.Add(other + " прочего");
            return parts.Count == 0 ? "ничего" : string.Join(", ", parts);
        }
    }
}

/// <summary>Отчёт защитнику: на его деревню напали.</summary>
[Serializable]
public class RaidReport
{
    public string id;
    public string attackerName;
    public string timeUtc;
    public RaidLoot loot = new RaidLoot();
    public List<string> killed = new List<string>();
    public bool hallSacked;
    public int ruined;
    public List<string> ruinedAt = new List<string>();   // разрушенные здания («x,y» - левая нижняя клетка): в городе встанут руины
    public int rating;   // как изменился рейтинг защитника (0 - не менялся)
    public bool live;    // бой шёл вживую в деревне защитника: потери уже случились у него в игре, вычитать нечего
    public int shieldMinutes;   // на сколько минут после этого набега город получил щит (0 - старый сервер)
    public bool replay;         // у сервера есть запись боя: её можно посмотреть (Api.BattleReplay; final-9)
}

[Serializable]
public class ApiResponse
{
    public bool ok;
    public string error;
    public bool updateRequired;      // эта сборка игры устарела: сервер больше не принимает её запросы (final-8)
    public string updateUrl;         // где взять новую (страница игры в Google Play)
    public string token;
    public AccountProfile profile;
    public bool hasCity;
    public string city;   // CityData в JSON
    public string serverName;
    public int players;
    public string raidId;
    public List<RaidReport> reports;     // набеги на этого игрока, которые игра ещё не учла
    public bool reportsApplied;          // true - сервер уже вычел потери из города (при загрузке)
    public string incomingName;          // на этого игрока сейчас идёт набег (имя нападающего)
    public BattleSnapshot battle;        // защитнику: снимок боя у его деревни
    public BattleReplay replay;          // защитнику: запись набега (Api.BattleReplay)
    public List<BattleCommand> commands; // нападающему: приказы защитника
    public bool defenderLive;            // нападающему: защитник сейчас командует сам
    public float raidLeft = -1f;         // final-13, защитнику: секунд до конца набега на него (-1 - неизвестно)
    public int raidPhase = -1;           // final-13, защитнику: 0 - армия у деревни (осмотр), 1 - бой, 2 - отступает, -1 - в пути / неизвестно
    public bool siegeLive;               // живой набег: бой идёт в деревне защитника (нападающему - после SiegeArrive / SiegePull)
    public List<SiegeUnit> siegeArmy;    // защитнику: армия, которую надо выставить у себя
    public bool camp;                    // защитнику: у армии есть лагерь
    public int siegeSide;                // защитнику: с какого края карты входит армия (0 - запад, 1 - восток, 2 - юг, 3 - север; final-8)
    public SiegeFrame siegeFrame;        // нападающему: что сейчас в деревне защитника (null - ещё ничего)
    public bool siegeOver;               // живой набег окончен (защитнику - нападающий ушёл; нападающему - защитник пропал)
    public List<MarketLot> lots;         // рынок: все лоты
    public MarketLot lot;                // рынок: купленный / снятый лот
    public List<MarketSale> sales;       // рынок: проданные лоты игрока, золото за которые игра ещё не получила
    public bool salesApplied;            // true - сервер уже добавил золото в город (при загрузке)
    public ClanView clan;                // мой клан (null - не в клане)
    public List<ClanBrief> clans;        // кланы, в которые можно вступить
    public List<ClanMessage> messages;   // новые сообщения чата (после since)
    public string serverUtc;             // часы сервера (в каждом ответе) - по ним идёт общее время игры
    public string worldStartUtc;         // когда начался мир: от этого момента считаются игровые сутки
    public WarInfo war;                  // клановая война: состояние
    public WarStatic warStatic;          // клановая война: карта
    public WarFrame warFrame;            // клановая война: что сейчас на карте
    public List<WarGrant> grants;        // награды за войну на домашнюю базу, которые игра ещё не получила
    public WarGrant warIncome;           // военный город: доход с захваченных земель с прошлого сохранения
    public string ruined;                // военный город: какие его здания разрушены на карте войны ("x,y;..." - клетки зданий в городе)
    public int deployed;                 // сколько бойцов вышло на карту
    public LeagueBoard league;           // лиги: мировой рейтинг
    public int shopMarks;                // магазин марок: сколько марок засчитано за эту покупку (0 - она уже была засчитана)
    public bool shieldKnown;             // в ответе есть щит города (старый сервер его не присылает)
    public string shieldUtc;             // до этого времени (часы сервера) город под щитом; пусто - щита нет
    public string googleTicket;          // Google Play Games: билет, чтобы следующим запросом завести или привязать аккаунт без нового кода
    public string googleName;            // Google Play Games: имя игрока в Google Play
    public bool googleNew;               // Google Play Games: к этому Google-аккаунту ещё не привязан ни один аккаунт игры

    public static ApiResponse Fail(string error) { return new ApiResponse { ok = false, error = error }; }
}

// ---------- Живой бой: нападающий считает бой у себя, защитник видит его и отдаёт приказы ----------

/// <summary>Приказ защитника. Координаты - клетки карты набега (локальные).</summary>
[Serializable]
public class BattleCommand
{
    public const string Move = "move", Attack = "attack", Auto = "auto", Barricade = "barricade";
    // Живой набег (приказы нападающего): units пусто - все бойцы
    public const string Assault = "assault", Retreat = "retreat", Focus = "focus";   // Focus: x, y - левая нижняя клетка здания
    public string kind;
    public List<int> units = new List<int>();
    public float x, y;
    public int target;   // Attack: id вражеского бойца
}

[Serializable]
public class SnapBuilding
{
    public int kind;          // RaidBuildingKind
    public string title;
    public int x, y, w, h;
    public float hp;          // доля 0..1
    public bool ruined, rising;
    // Обновление 12 (полный повтор боя): здание в клетках хозяина деревни и его вид
    public string type;       // тип здания ("Казарма"), "" - у ботов
    public int lv;
    public int rx, ry, rw, rh;
}

[Serializable]
public class SnapUnit
{
    public int id;
    public bool attacker;
    public string name;
    public float x, y;
    public float hp;          // доля 0..1
    public bool ranged, controlled;
    // Обновление 12 (полный повтор боя): точка в клетках хозяина деревни и вид бойца
    public float rx, ry;
    public string art;        // рисунок (GameArt.FigureKind)
    public bool horse;
    public int tint;          // цвет RGB
}

[Serializable]
public class SnapSpot { public int x, y, w, h; }

[Serializable]
public class BattleSnapshot
{
    public int size;                  // карта набега size × size, клетки от -size/2
    public int phase;                 // 0 - осмотр, 1 - бой, 2 - отступление, 3 - окончен
    public float timeLeft;            // секунд до конца набега
    public string attackerName;
    public int barricadesLeft;        // сколько ещё заслонов может поставить защитник
    public List<SnapBuilding> buildings = new List<SnapBuilding>();
    public List<SnapUnit> units = new List<SnapUnit>();
    public List<SnapSpot> spots = new List<SnapSpot>();
    public bool real;                 // обновление 12: есть rx/ry (клетки хозяина) - повтор рисуется прямо в его городе
    public List<SiegeShot> shots = new List<SiegeShot>();   // выстрелы с прошлого снимка (в клетках хозяина)
}

// ---------- Запись набега (final-9): сервер копит снимки боя, защитник потом смотрит повтор ----------

/// <summary>Боец в кадре записи. Координаты и здоровье - целые, чтобы запись была маленькой.</summary>
[Serializable]
public class ReplayUnit
{
    public int i;     // id бойца (имя и сторона - в BattleReplay.names)
    public int x, y;  // клетка × 10
    public int h;     // здоровье, проценты
}

[Serializable]
public class ReplayName
{
    public int i;
    public string n;
    public bool a;    // нападающий
    public bool r;    // стрелок
    public string k;  // рисунок бойца (обновление 12)
    public bool hr;   // верхом
    public int c;     // цвет RGB
}

/// <summary>Выстрел в записи: вид (как SiegeShot.kind) и откуда-куда, клетки × 10.</summary>
[Serializable]
public class ReplayShot { public int k, x0, y0, x1, y1; }

[Serializable]
public class ReplayFrame
{
    public int t;                                   // секунд от начала записи
    public List<int> b = new List<int>();           // здоровье зданий в процентах по порядку BattleReplay.buildings; -1 - руины
    public List<ReplayUnit> u = new List<ReplayUnit>();
    public List<ReplayShot> s = new List<ReplayShot>();   // выстрелы с прошлого кадра (обновление 12)
}

[Serializable]
public class BattleReplay
{
    public const int MaxFrames = 600;
    public string attackerName;
    public int size;
    public int step = 1;                            // секунд между кадрами
    public bool real;                               // обновление 12: координаты - клетки города защитника (повтор прямо в его городе)
    public List<SnapBuilding> buildings = new List<SnapBuilding>();
    public List<ReplayName> names = new List<ReplayName>();
    public List<ReplayFrame> frames = new List<ReplayFrame>();
}

// ---------- Живой набег: армия нападающего приходит прямо в деревню защитника ----------
// Защитник в игре - его игра считает бой: армия - враги на его карте (как набег соседей), он играет как обычно.
// Нападающий видит его деревню (план тех же клеток) и бойцов по снимкам SiegeFrame и отдаёт приказы (BattleCommand).
// Координаты - мировые точки карты защитника (клетка x,y - от x до x+1).

/// <summary>Боец нападающего: всё, чтобы игра защитника поставила его у себя таким же (здоровье, урон, конь, щит).</summary>
[Serializable]
public class SiegeUnit
{
    public int id;
    public string unit, name;
    public float hp, maxHp, damage, range, cooldown, speed, heal, healRange, scale;
    public bool mounted, golem;
    public int carry;                      // сколько добычи унесёт
    public float r = 1f, g = 1f, b = 1f;   // цвет
    public bool hasShield;
    public int sQuality, sUpgrade;
    public float sDur, sMaxDur;
    // Таран: ведущий носильщик несёт таран (ram), остальные «в нём» (inRam - номер ведущего) до поломки.
    // Пока таран цел, hp / maxHp - у тарана (ramHp, ramMaxHp), удар - ramDamage, по людям не бьёт.
    public bool ram;
    public int inRam;
    public float ramHp, ramMaxHp, ramArmor, ramDamage, ramCd, ramSpeed, bldMul, wallMul;
}

/// <summary>Боец на поле. Нападающий: carried - что несёт. Защитник: unit - вид ("" - житель).</summary>
[Serializable]
public class SiegeMark
{
    public int id;
    public string unit;
    public float x, y, hp;     // hp - доля 0..1
    public bool ranged, mounted, citizen, fighting;
    public bool ram;           // нападающий: это целый таран (hp - его прочность)
    public RaidLoot carried;
}

/// <summary>Здание защитника не целое: x, y - его левая нижняя клетка.</summary>
[Serializable]
public class SiegeHouse
{
    public int x, y;
    public float hp;      // доля 0..1
    public bool ruined;
}

/// <summary>Выстрел (только для картинки): 0 - стрела, 1 - ядро, 2 - магия.</summary>
[Serializable]
public class SiegeShot { public int kind; public float x0, y0, x1, y1; }

/// <summary>Боец нападающего ушёл с поля: погиб или унёс добычу (loot).</summary>
[Serializable]
public class SiegeGone { public int id; public bool dead; public RaidLoot loot; }

[Serializable]
public class SiegeFrame
{
    public int seq;
    public float t;                // когда снят (часы игры защитника, секунды) - нападающий по ним плавно ведёт бойцов
    public bool over;              // войско ушло целиком - бой окончен
    public bool engaged;           // защитники уже дерутся
    public string status;          // что делает войско (для нападающего)
    public List<SiegeMark> attackers = new List<SiegeMark>();
    public List<SiegeMark> defenders = new List<SiegeMark>();
    public List<SiegeHouse> houses = new List<SiegeHouse>();
    public List<SiegeShot> shots = new List<SiegeShot>();
    public List<SiegeGone> gone = new List<SiegeGone>();          // все, кто ушёл с поля с начала боя
    public List<string> killed = new List<string>();              // погибшие бойцы защитника (по виду войск), с начала боя
}

// ---------- Рынок между игроками ----------

/// <summary>Лот на рынке: товар (GoodKind + качество) и цена в золоте за весь лот.</summary>
[Serializable]
public class MarketLot
{
    public string id;
    public string sellerId;
    public string sellerName;
    public int kind;      // GoodKind
    public int tier;      // QualityTier
    public int units;
    public int price;     // марки за весь лот
    public string createdUtc;
    // Штучная вещь (оружие, щит, модуль, ловушка, боец, заслон): item = ItemKind + 1; 0 - обычный товар (kind, tier, units)
    public int item;
    public string name, info;   // как вещь называется и чем хороша (для списка)
    public MarketThing thing;
}

/// <summary>Штучная вещь на рынке игроков: всё, чтобы покупатель получил ровно её (качество, заряды, снаряжение бойца).</summary>
[Serializable]
public class MarketThing
{
    public SavedSoldier s;       // боец: вид, похлёбка, оружие, щит; у оружия и щита - только их поля
    public List<SavedSoldier> squad;   // отряд целиком: каждый боец со своим снаряжением
    public int modKind, mk, charges;                        // модуль
    public int trapType, trapMods, trapMk, trapCharges;     // ловушка
    public bool trapCombat;
    public float trapPower;
    public int post;                                        // заслон (PostKind)
    public float strength, pool;
}

/// <summary>Продажа: кто-то купил лот игрока - ему причитается золото.</summary>
[Serializable]
public class MarketSale
{
    public string id;
    public string buyerName;
    public int kind, tier, units, price;   // price - в марках
    public string timeUtc;
    public int item;          // штучная вещь (как в MarketLot)
    public string name;
}

// ---------- Кланы ----------

[Serializable]
public class ClanMessage
{
    public int n;               // номер по порядку
    public string authorId;
    public string author;
    public string text;
    public string timeUtc;
    public bool system;         // «вступил», «вышел» и т.п.
}

[Serializable]
public class ClanMember
{
    public string id;
    public string name;
    public bool leader;
    public int role;            // ClanRoles: 0 - участник, 1 - старейшина, 2 - заместитель, 3 - глава
    public bool online;         // был на сервере в последнюю минуту
    public int hall;            // уровень Цитадели
    public int rating;          // рейтинг игрока (лига)
}

/// <summary>
/// Роли в клане. Глава может всё и может передать главенство. Заместитель - начинает войну и расставляет города,
/// исключает старейшин и участников, делает участников старейшинами. Старейшина - исключает участников.
/// </summary>
public static class ClanRoles
{
    public const int Member = 0, Elder = 1, CoLeader = 2, Leader = 3;

    public static string Name(int role)
    {
        switch (role)
        {
            case Leader: return "Глава";
            case CoLeader: return "Заместитель";
            case Elder: return "Старейшина";
            default: return "Участник";
        }
    }

    /// <summary>Значок роли перед именем.</summary>
    public static string Mark(int role)
    {
        switch (role)
        {
            case Leader: return "★";
            case CoLeader: return "✦";
            case Elder: return "◆";
            default: return "";
        }
    }

    /// <summary>Может ли роль a исключить игрока с ролью b.</summary>
    public static bool CanKick(int a, int b) { return a >= Elder && a > b; }

    /// <summary>Может ли роль a поставить игроку с ролью b роль to (to = Leader - передать главенство).</summary>
    public static bool CanSet(int a, int b, int to)
    {
        if (to == b || to < Member || to > Leader) return false;
        if (to == Leader) return a == Leader;
        return a > b && a > to;
    }

    /// <summary>Может ли роль начинать войну и расставлять города.</summary>
    public static bool CanLeadWar(int role) { return role >= CoLeader; }
}

[Serializable]
public class ClanView
{
    public string id;
    public string name;
    public string description;
    public List<ClanMember> members = new List<ClanMember>();
    public int rating;          // рейтинг клана (за клановые войны)
}

[Serializable]
public class ClanBrief
{
    public string id;
    public string name;
    public string description;
    public int members;
    public int rating;
}

// ---------- Лиги ----------
// Рейтинг игрока и клана делится на лиги. Правила общие у игры и у сервера.
// Игрок получает рейтинг за набеги на игроков и за клановые войны, клан - за клановые войны.

public static class Leagues
{
    /// <summary>С какого рейтинга начинается лига.</summary>
    public static readonly int[] From = { 0, 100, 200, 300, 400, 500, 600, 750, 900, 1050, 1200, 1350, 1500, 1800, 2200 };
    static readonly string[] names =
    {
        "Бронза III", "Бронза II", "Бронза I", "Серебро III", "Серебро II", "Серебро I",
        "Золото III", "Золото II", "Золото I", "Кристалл III", "Кристалл II", "Кристалл I",
        "Мастер", "Чемпион", "Легенда",
    };
    static readonly string[] hex =
    {
        "#c08a5a", "#c08a5a", "#c08a5a", "#c9d1da", "#c9d1da", "#c9d1da",
        "#f2c94c", "#f2c94c", "#f2c94c", "#b48cf0", "#b48cf0", "#b48cf0",
        "#ff7a59", "#ff4d6d", "#ffe08a",
    };
    static readonly string[] marks = { "▮", "▮", "▮", "◆", "◆", "◆", "★", "★", "★", "✦", "✦", "✦", "♛", "♛", "♛" };

    public static int Count => From.Length;

    public static int Of(int rating)
    {
        int i = 0;
        while (i + 1 < From.Length && rating >= From[i + 1]) i++;
        return i;
    }

    static int Clamp(int i) { return i < 0 ? 0 : i >= From.Length ? From.Length - 1 : i; }
    public static string Name(int i) { return names[Clamp(i)]; }
    public static string Hex(int i) { return hex[Clamp(i)]; }
    public static string Mark(int i) { return marks[Clamp(i)]; }

    /// <summary>Рейтинг следующей лиги (-1 - выше некуда).</summary>
    public static int Next(int i) { return i + 1 < From.Length ? From[i + 1] : -1; }

    /// <summary>Подпись лиги цветом, со значком.</summary>
    public static string Label(int i) { return $"<color={Hex(i)}>{Mark(i)} {Name(i)}</color>"; }

    // Награда, когда игрок первый раз дошёл до лиги (клан - каждому участнику вполовину)
    public static int RewardGold(int i) { return 120 * Clamp(i) + (i >= 12 ? 600 : 0); }
    public static int RewardRes(int i) { return 80 * Clamp(i); }
    public static int RewardIron(int i) { return Clamp(i) >= 6 ? 10 * (Clamp(i) - 5) : 0; }

    // Набег на игрока: победа - снесли Цитадель или разрушили хотя бы столько зданий
    public const int RaidWinRuined = 3;
    // Обновление 13: исход набега - по доле разрушенного города (без стен, ворот, дорожек и мостов)
    public const float RaidWinShare = 0.5f, RaidSmallWinShare = 0.25f, RaidFailShare = 0.1f;
    public const int RaidWin = 12, RaidLossDefender = 8;     // удачный набег: нападающему +, защитнику -
    public const int RaidFail = 6, RaidHeld = 6;             // неудачный: нападающему -, защитнику +
    // Клановая война
    public const int WarWinClan = 30, WarLoseClan = 15, WarDrawClan = 5;
    public const int WarWinPlayer = 20, WarLosePlayer = 8, WarDrawPlayer = 5;

    /// <summary>Поправка за соперника: сильнее - больше дают и меньше отнимают, слабее - наоборот.</summary>
    public static int Bonus(int mine, int theirs, int max)
    {
        int b = (theirs - mine) / 25;
        return b < -max ? -max : b > max ? max : b;
    }
}

[Serializable]
public class LeagueRow
{
    public int place;
    public string id;
    public string name;
    public string clan;          // игроки: название клана
    public int rating;
    public int members;          // кланы: сколько участников
    public int hall;             // игроки: уровень Цитадели
    public bool me;              // это я (или мой клан)
}

[Serializable]
public class RatingChange
{
    public int delta;
    public int rating;           // каким стал рейтинг
    public string text;
    public string timeUtc;
}

[Serializable]
public class LeagueBoard
{
    public List<LeagueRow> clans = new List<LeagueRow>();
    public List<LeagueRow> players = new List<LeagueRow>();
    public int totalClans, totalPlayers;
    public int myRating, myPlace, myBest;              // мой рейтинг, место в мире, лучшая лига (номер)
    public string clanName;                            // пусто - не в клане
    public int clanRating, clanPlace, clanBest;
    public List<RatingChange> history = new List<RatingChange>();      // мои последние изменения рейтинга
    public List<RatingChange> clanHistory = new List<RatingChange>();  // клана
}

/// <summary>
/// Щит города после набега другого игрока: пока он стоит, город не попадается другим игрокам во вкладке «Бой».
/// Чем сильнее разорили, тем дольше щит. Набеги соседей (не игроков) щит не дают.
/// Числа общие для сервера (он ставит щит) и игры (она объясняет игроку).
/// </summary>
public static class ShieldRules
{
    public struct Tier { public float from; public int minutes; public string name; }

    // Тяжесть набега 0..1+ (см. Severity) → длительность щита. Отбились совсем - короткий щит.
    public static readonly Tier[] Tiers =
    {
        new Tier { from = 0f,    minutes = 30,      name = "Отбились: ничего не унесли и не разрушили" },
        new Tier { from = 0.01f, minutes = 2 * 60,  name = "Лёгкий урон" },
        new Tier { from = 0.15f, minutes = 4 * 60,  name = "Заметный урон" },
        new Tier { from = 0.35f, minutes = 8 * 60,  name = "Разрушена треть города или снесена Цитадель" },
        new Tier { from = 0.55f, minutes = 12 * 60, name = "Разрушена половина города" },
        new Tier { from = 0.80f, minutes = 16 * 60, name = "Город почти разрушен" },
    };

    public const float HallWeight = 0.35f;   // снесли Цитадель - сразу не меньше «трети города»
    public const float LootWeight = 0.5f;    // унесли всё из казны - как половина разрушенного города
    public const float KillWeight = 0.15f;   // перебили всех защитников

    /// <summary>
    /// Тяжесть набега. damage - доля разрушенных построек (без стен и ворот), loot - доля унесённого из того, что было,
    /// killed - доля погибших защитников. Всё 0..1.
    /// </summary>
    public static float Severity(float damage, float loot, float killed, bool hallSacked)
    {
        float s = Clamp01(damage) + LootWeight * Clamp01(loot) + KillWeight * Clamp01(killed);
        if (hallSacked) s = Math.Max(s + 0.1f, HallWeight);
        return s;
    }

    public static Tier TierFor(float severity)
    {
        Tier t = Tiers[0];
        foreach (var x in Tiers) if (severity >= x.from) t = x;
        return t;
    }

    public static int MinutesFor(float severity) { return TierFor(severity).minutes; }

    /// <summary>«4 ч», «30 мин», «1 ч 20 мин».</summary>
    public static string Duration(double seconds)
    {
        int m = (int)Math.Ceiling(Math.Max(0.0, seconds) / 60.0);
        if (m < 60) return m + " мин";
        int h = m / 60;
        m %= 60;
        return m == 0 ? h + " ч" : h + " ч " + m + " мин";
    }

    static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
}

// ---------- Google Play: push-уведомления, блокировка ----------

/// <summary>Виды push-уведомлений. AccountProfile.pushOff - биты выключенных.</summary>
public static class PushKinds
{
    public const int Attack = 1;    // на вас напали / итог набега
    public const int Build = 2;     // стройка или улучшение готовы
    public const int Shield = 4;    // щит скоро кончится
}

/// <summary>Стройка, о которой сервер пришлёт уведомление (время - часы сервера).</summary>
[Serializable]
public class PushPlanItem
{
    public string atUtc;
    public string text;      // «Казарма», «Стена: Ур. 3»
    public bool upgrade;     // улучшение (иначе стройка)
}

/// <summary>Заблокированный игрок: его сообщения в чате не видны.</summary>
[Serializable]
public class BlockedPlayer
{
    public string id;
    public string name;
}
