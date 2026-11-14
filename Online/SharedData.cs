using System;
using System.Collections.Generic;
using UnityEngine;

// ---------- Общие данные игры и сервера ----------
// Этот файл компилируется и в Unity, и в отдельной программе сервера (папка Assets/ServerApp~),
// поэтому здесь только данные и правила, без сцены и без MonoBehaviour.

// ReinforcedPlates - укреплённые плиты: плита + сталь + камень (мастерская Ур.3)
// TarredBeams - смоляной брус: доски + смоляная пропитка (мастерская Ур.3)
// Monolith - монолитное основание: укреплённые плиты + пропитка + смоляной брус (мастерская Ур.3)
// Порядок важен: по номеру хранятся склады Хранилищ. Новые - только в конец.
public enum RefinedKind { Planks, StonePlates, Steel, ReinforcedPlates, TarredBeams, Monolith }

/// <summary>Профиль игрока - то, что игра знает об аккаунте.</summary>
[Serializable]
public class AccountProfile
{
    public string id;            // постоянный id аккаунта
    public string login;         // для входа, без учёта регистра
    public string displayName;   // имя в игре
    public string createdUtc;    // дата создания (ISO 8601)
    public string lastLoginUtc;
    public int logins;           // сколько раз входили
    public float playSeconds;    // сколько времени провели в игре
    public int rating;           // рейтинг игрока (за клановые войны; меняет только сервер)
    public int lotSlots;         // сколько мест под лоты на рынке игроков открыто (меняет только сервер, см. LotRules)
    public bool googleLinked;    // привязан Google Play Games: вход на новом телефоне без пароля (меняет только сервер)
    public bool noPassword;      // аккаунт заведён через Google Play Games, пароля нет (меняет только сервер)
    public string mutedUtc;      // до этого времени писать в чат нельзя (модерация по жалобам; меняет только сервер)
    public int pushOff;          // какие push-уведомления выключены (PushKinds; меняет игра через Api.PushToken)
    public List<BlockedPlayer> blocked = new List<BlockedPlayer>();   // заблокированные игроки (меняет только сервер)

    public DateTime Created => ParseDate(createdUtc);
    public DateTime LastLogin => ParseDate(lastLoginUtc);

    static DateTime ParseDate(string s)
    {
        return DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d) ? d : DateTime.MinValue;
    }

    public AccountProfile Copy()
    {
        var p = (AccountProfile)MemberwiseClone();
        p.blocked = blocked != null ? new List<BlockedPlayer>(blocked) : new List<BlockedPlayer>();
        return p;
    }

    public bool Blocks(string accountId) { return blocked != null && !string.IsNullOrEmpty(accountId) && blocked.Exists(b => b != null && b.id == accountId); }

    /// <summary>Сейчас писать в чат нельзя (до какого времени - MutedUntil).</summary>
    public bool Muted => MutedUntil > DateTime.UtcNow;
    public DateTime MutedUntil { get { DateTime d = ParseDate(mutedUtc); return d == DateTime.MinValue ? d : d.ToUniversalTime(); } }
}

/// <summary>Ответ: успех, профиль и токен сессии, либо текст ошибки для игрока.</summary>
public class AccountResult
{
    public bool ok;
    public string error;
    public AccountProfile profile;
    public string token;   // токен сессии: по нему входят без пароля («Запомнить меня»)

    public static AccountResult Fail(string error) { return new AccountResult { ok = false, error = error }; }
    public static AccountResult Ok(AccountProfile p, string token) { return new AccountResult { ok = true, profile = p, token = token }; }
}

/// <summary>Правила логина, пароля и имени - одни и те же у игры и у сервера.</summary>
public static class AccountRules
{
    public const int MinLogin = 3, MaxLogin = 16, MinPassword = 4, MaxName = 20;

    public static string CheckLogin(string login)
    {
        login = (login ?? "").Trim();
        if (login.Length < MinLogin) return $"Логин — от {MinLogin} символов";
        if (login.Length > MaxLogin) return $"Логин — не длиннее {MaxLogin} символов";
        foreach (char ch in login)
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-') return "В логине только буквы, цифры, _ и -";
        return null;
    }

    public static string CheckPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPassword) return $"Пароль — от {MinPassword} символов";
        return null;
    }

    /// <summary>Слово, которое нужно ввести, чтобы удалить аккаунт (сервер принимает любое из них).</summary>
    public const string DeleteWord = "УДАЛИТЬ", DeleteWordEn = "DELETE";

    public static bool IsDeleteWord(string s)
    {
        s = (s ?? "").Trim().ToUpperInvariant();
        return s == DeleteWord || s == DeleteWordEn;
    }

    public static string CheckName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "Укажите имя в игре";
        if (name.Length > MaxName) return $"Имя — не длиннее {MaxName} символов";
        return null;
    }
}

[Serializable]
public class SavedBuilding
{
    public string type;
    public int x, y;            // левая нижняя клетка
    public int level = 1;
    public bool fortified;
    public bool starter;        // стартовая постройка (нельзя снести)
    public int gold, wood, stone, food, iron, oats, berries, herbs, resin, leaves;   // склад здания
    public int w, h;            // размер в клетках (для карты клановой войны; 0 - старое сохранение)
    public bool manualWorkers;  // рабочих выбрал игрок (false - «Авто»)
    public int wantWorkers;     // сколько рабочих хочет игрок (в ручном режиме)
    // Что внутри здания: закреплённые курьеры, склад, лошади, модуль... Записи «ключ=значение» (см. CitySave).
    // Ключи с приставкой «set.» - настройки здания (их военный город берёт из домашнего), остальное - запасы.
    public List<string> state = new List<string>();
}

[Serializable]
public class SavedRefined
{
    public int kind;   // RefinedKind
    public int tier;   // QualityTier
    public int amount;
}

/// <summary>Боец армии: вид, казарма (по клетке), похлёбка, оружие и щит.</summary>
[Serializable]
public class SavedSoldier
{
    public string unit;
    public int bx, by;          // левая нижняя клетка его казармы
    public bool hearty;
    public bool merc;           // наёмник (живёт в Гостевом доме)
    public bool hasWeapon;
    public int wKind, wQuality, wUpgrade, wPoison, wSlow, wStars;
    public float wDur, wMaxDur, wForgeDamage, wForgeDurability, wForgeSpeed;
    public bool wForged;
    public int wSalute, wSaluteMk;   // эффект салюта на луке: заряды и марка
    public bool hasShield;
    public int sQuality, sUpgrade;
    public float sDur, sMaxDur;
    public int sChaosMk, sChaosCharges;   // ядро хаоса на щите (0 - нет)
    public bool hasBody;        // броня или плащ
    public int bKind, bQuality;
    public float bDur, bMaxDur;
    public int bUpgrade;        // усиление брони (0..3)
    public int cMk, cCharges;   // ядро хаоса у Мага стихий (cCharges 0 - нет)
    public bool ramBroken;      // носильщик тарана: таран сломан (нет поля - цел)
    public float ramWear;       // ... или побит: доля выбитой прочности
}

[Serializable]
public class CityData
{
    public int version = 1;
    public string ownerId;      // ставит сервер
    public string ownerName;    // ставит сервер
    public string savedUtc;     // ставит сервер
    public int seed;            // карта города

    // Казна
    public int gold, wood, stone, food, iron, oats, berries, herbs, potions, parts, leaves, resin, tar, rubber, slowPotions, marks;
    public List<SavedRefined> refined = new List<SavedRefined>();

    // Путь развития
    public int path, school, eng, war;

    public int population;
    public int day;
    public float timeInDay;

    public List<SavedBuilding> buildings = new List<SavedBuilding>();
    public List<SavedSoldier> army = new List<SavedSoldier>();

    // Местность (для набега на этот город): размер карты и клетки воды и леса, сжатые TerrainCode
    public int mapSize;
    public string terrain;

    // Доход города в час (CityIncome): по нему город «живёт», пока игрока нет
    public List<float> income = new List<float>();
    public float incomeSeconds;   // по скольким секундам игры посчитан

    // Запасы города вне зданий (ловушки, модули, полевое снаряжение, Дальний склад) и настройки (рационы,
    // режим накопления). Записи «ключ=значение» (см. CitySave).
    public List<string> extra = new List<string>();

    public bool npc;   // NPC-деревня сервера (обновление 14): ставит сервер
}

/// <summary>
/// Местность города одной строкой: клетки от (-size/2, -size/2) построчно, '.' - трава, 'W' - вода, 'F' - лес.
/// Подряд идущие одинаковые клетки сжаты: буква и сколько раз (".120F3W4").
/// </summary>
public static class TerrainCode
{
    public const char Grass = '.', Water = 'W', Forest = 'F';

    public static string Encode(int size, System.Func<int, int, char> at)
    {
        var sb = new System.Text.StringBuilder();
        int half = size / 2;
        char cur = '\0';
        int run = 0;
        for (int y = -half; y < size - half; y++)
            for (int x = -half; x < size - half; x++)
            {
                char c = at(x, y);
                if (c == cur) { run++; continue; }
                if (run > 0) sb.Append(cur).Append(run);
                cur = c;
                run = 1;
            }
        if (run > 0) sb.Append(cur).Append(run);
        return sb.ToString();
    }

    /// <summary>Каждая клетка, кроме травы: (x, y, буква).</summary>
    public static void Decode(int size, string code, System.Action<int, int, char> cell)
    {
        if (size <= 0 || string.IsNullOrEmpty(code)) return;
        int half = size / 2, i = 0, n = 0, total = size * size;
        while (i < code.Length && n < total)
        {
            char c = code[i++];
            int run = 0;
            while (i < code.Length && char.IsDigit(code[i])) run = run * 10 + (code[i++] - '0');
            for (int k = 0; k < run && n < total; k++, n++)
                if (c != Grass) cell(n % size - half, n / size - half, c);
        }
    }
}

/// <summary>Потери города после набега (на данных, без сцены): сервер вычитает их из сохранённого города.</summary>
public static class CityLoss
{
    static int[] Refined(CityData c, RefinedKind k)
    {
        var r = new int[3];
        if (c.refined != null)
            foreach (var x in c.refined)
                if (x != null && x.kind == (int)k && x.tier >= 0 && x.tier < 3) r[x.tier] += Mathf.Max(0, x.amount);
        return r;
    }

    /// <summary>Сколько всего лежит в городе (казна + склады зданий) - больше набег унести не может.</summary>
    public static RaidLoot Available(CityData c)
    {
        var a = new RaidLoot
        {
            gold = c.gold, wood = c.wood, stone = c.stone, food = c.food, iron = c.iron, oats = c.oats,
            berries = c.berries, herbs = c.herbs, potions = c.potions, resin = c.resin,
            planks = Refined(c, RefinedKind.Planks), plates = Refined(c, RefinedKind.StonePlates),
            steel = Refined(c, RefinedKind.Steel), reinforced = Refined(c, RefinedKind.ReinforcedPlates),
        };
        if (c.buildings != null)
            foreach (var b in c.buildings)
            {
                if (b == null) continue;
                a.gold += b.gold; a.wood += b.wood; a.stone += b.stone; a.food += b.food; a.iron += b.iron;
                a.oats += b.oats; a.berries += b.berries; a.herbs += b.herbs; a.resin += b.resin;
            }
        return a;
    }

    // Снять n сначала из казны, потом со складов зданий
    static void Take(ref int treasury, int n, CityData c, System.Func<SavedBuilding, int> get, System.Action<SavedBuilding, int> set)
    {
        int t = Mathf.Min(Mathf.Max(0, treasury), n);
        treasury -= t;
        n -= t;
        if (n <= 0 || c.buildings == null) return;
        foreach (var b in c.buildings)
        {
            if (b == null || n <= 0) continue;
            int k = Mathf.Min(get(b), n);
            if (k <= 0) continue;
            set(b, get(b) - k);
            n -= k;
        }
    }

    static void TakeRefined(CityData c, RefinedKind k, int[] amounts)
    {
        if (c.refined == null || amounts == null) return;
        for (int tier = 0; tier < 3; tier++)
        {
            int n = RaidLoot.At(amounts, tier);
            foreach (var x in c.refined)
            {
                if (n <= 0) break;
                if (x == null || x.kind != (int)k || x.tier != tier) continue;
                int m = Mathf.Min(x.amount, n);
                x.amount -= m;
                n -= m;
            }
        }
        c.refined.RemoveAll(x => x == null || x.amount <= 0);
    }

    public static void Apply(CityData c, RaidReport r)
    {
        if (c == null || r == null) return;
        RaidLoot l = r.loot ?? new RaidLoot();
        Take(ref c.gold, l.gold, c, b => b.gold, (b, v) => b.gold = v);
        Take(ref c.wood, l.wood, c, b => b.wood, (b, v) => b.wood = v);
        Take(ref c.stone, l.stone, c, b => b.stone, (b, v) => b.stone = v);
        Take(ref c.food, l.food, c, b => b.food, (b, v) => b.food = v);
        Take(ref c.iron, l.iron, c, b => b.iron, (b, v) => b.iron = v);
        Take(ref c.oats, l.oats, c, b => b.oats, (b, v) => b.oats = v);
        Take(ref c.berries, l.berries, c, b => b.berries, (b, v) => b.berries = v);
        Take(ref c.herbs, l.herbs, c, b => b.herbs, (b, v) => b.herbs = v);
        Take(ref c.resin, l.resin, c, b => b.resin, (b, v) => b.resin = v);
        c.potions = Mathf.Max(0, c.potions - l.potions);
        TakeRefined(c, RefinedKind.Planks, l.planks);
        TakeRefined(c, RefinedKind.StonePlates, l.plates);
        TakeRefined(c, RefinedKind.Steel, l.steel);
        TakeRefined(c, RefinedKind.ReinforcedPlates, l.reinforced);
        MarkRuins(c, r.ruinedAt, r.timeUtc);

        // Погибшие защитники: по одному бойцу этого вида
        if (r.killed != null && c.army != null)
            foreach (var name in r.killed)
            {
                int i = c.army.FindIndex(s => s != null && s.unit == name);
                if (i < 0) continue;
                c.army.RemoveAt(i);
                c.population = Mathf.Max(1, c.population - 1);
            }
    }

    /// <summary>Запись о руинах в здании (SavedBuilding.state): «ruin=оплачено,ход восстановления,когда (UTC)».</summary>
    public const string RuinKey = "ruin";

    /// <summary>Разрушенные в набеге здания - руины с момента набега (игра поднимет их, сколько успела бы за это время).</summary>
    public static void MarkRuins(CityData c, List<string> cells, string timeUtc)
    {
        if (c == null || c.buildings == null || cells == null) return;
        foreach (var cell in cells)
        {
            if (string.IsNullOrEmpty(cell)) continue;
            string[] a = cell.Split(',');
            if (a.Length < 2 || !int.TryParse(a[0], out int x) || !int.TryParse(a[1], out int y)) continue;
            foreach (var b in c.buildings)
            {
                if (b == null || b.x != x || b.y != y) continue;
                if (b.state == null) b.state = new List<string>();
                string pre = RuinKey + "=";
                b.state.RemoveAll(s => s != null && s.StartsWith(pre, StringComparison.Ordinal));
                b.state.Add(pre + "0,0," + (timeUtc ?? ""));
                break;
            }
        }
    }

    /// <summary>Защита новичка (final-8): Цитадель Ур.1 и ни одной казармы - другие игроки такой город не находят
    /// в поиске, а соседи-NPC на него не нападают. Построил казарму или поднял Цитадель - защита снята.</summary>
    public static bool IsNewbie(CityData c)
    {
        if (c == null || c.buildings == null) return true;
        if (HallLevel(c) >= 2) return false;
        foreach (var b in c.buildings)
            if (b != null && (b.type == "Казарма" || b.type == "Инженерная казарма")) return false;
        return true;
    }

    /// <summary>Уровень Цитадели города (для подбора противника по силе).</summary>
    public static int HallLevel(CityData c)
    {
        if (c != null && c.buildings != null)
            foreach (var b in c.buildings)
                if (b != null && b.type == "Ратуша") return Mathf.Max(1, b.level);
        return 1;
    }
}
