using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Режим войны и тестовые набеги.
///  - Набег вызывается горячей клавишей (в интерфейсе нигде не подписана).
///  - Пока на карте есть враги (и ещё warCooldown секунд после последнего) - режим войны:
///    новые жители не прибывают, мирные прячутся от врагов в здания.
///  - Рисует полоски здоровья над людьми, врагами и повреждёнными зданиями.
/// Создаётся в BuildingPlacer.Start.
/// </summary>
public partial class WarManager : MonoBehaviour
{
    public static WarManager Instance { get; private set; }

    // Идёт война: на карте враги (или были совсем недавно)
    public static bool IsWar => Instance != null && Instance.war;

    [Header("Режим войны")]
    public float warCooldown = 30f;   // сколько секунд после последнего врага ещё длится война

    [Header("Тестовый набег")]
    public int smallWave = 5;
    public int bigWave = 12;

    [Header("Периодические набеги (сами, без клавиши)")]
    public bool autoRaids = true;
    public float firstRaidDelay = 600f;    // секунд игрового времени до первого набега
    public float raidIntervalMin = 720f;   // дальше - раз в 12-18 минут
    public float raidIntervalMax = 1080f;
    public int autoRaidBase = 3;           // размер первого набега
    public int autoRaidGrowth = 1;         // на сколько врагов больше каждый следующий
    public int autoRaidMax = 14;
    public float raidWarnSeconds = 20f;    // за сколько секунд предупредить, с какой стороны идут

    /// <summary>Во сколько раз реже набеги NPC (боты и соседи-соперники): все промежутки между ними × RaidRarity.
    /// 1.25 - набегов в 0,8 раза меньше. 1 - как было раньше.</summary>
    public const float RaidRarity = 1.25f;

    /// <summary>Сколько ждать до следующего периодического набега.</summary>
    float NextRaidInterval() => Random.Range(raidIntervalMin, raidIntervalMax) * RaidRarity;

    [Header("Вражеские юниты (те же классы, что у нас, другой цвет)")]
    public List<UnitType> enemyUnits = DefaultEnemyUnits();

    static List<UnitType> DefaultEnemyUnits()
    {
        return new List<UnitType>
        {
            new UnitType { unitName = "Мечник", maxHp = 100f, damage = 12f, attackRange = 1.1f, attackCooldown = 1f,   moveSpeed = 1.6f, color = new Color(0.6f, 0.2f, 0.7f),   bodyScale = 0.55f,
                           usesWeapon = true, weaponKind = WeaponKind.Sword, usesShield = true },
            new UnitType { unitName = "Лучник", maxHp = 70f,  damage = 9f,  attackRange = 6f,   attackCooldown = 1.4f, moveSpeed = 1.5f, color = new Color(0.45f, 0.3f, 0.6f),  bodyScale = 0.5f,
                           usesWeapon = true, weaponKind = WeaponKind.Bow },
            new UnitType { unitName = "Рыцарь", maxHp = 240f, damage = 24f, attackRange = 1.2f, attackCooldown = 1.3f, moveSpeed = 1.3f, color = new Color(0.32f, 0.32f, 0.42f), bodyScale = 0.75f,
                           usesWeapon = true, weaponKind = WeaponKind.Sword, usesShield = true },
            // Вражеский маг-целитель (с набега №3): лечит раненых своих
            new UnitType { unitName = "Полевой маг", maxHp = 70f, damage = 4f, attackRange = 4f, attackCooldown = 1.5f, moveSpeed = 1.6f, color = new Color(0.7f, 0.35f, 0.85f), bodyScale = 0.5f,
                           healPerSecond = 4f, healRange = 4f },
            // Конница (с набега №4)
            new UnitType { unitName = "Всадник", maxHp = 200f, damage = 20f, attackRange = 1.3f, attackCooldown = 1f, moveSpeed = 2.8f, color = new Color(0.8f, 0.25f, 0.45f), bodyScale = 0.8f,
                           requiresHorse = true, usesWeapon = true, weaponKind = WeaponKind.Sword, usesShield = true },
            new UnitType { unitName = "Конный лучник", maxHp = 140f, damage = 11f, attackRange = 6.5f, attackCooldown = 1.2f, moveSpeed = 2.6f, color = new Color(0.6f, 0.35f, 0.7f), bodyScale = 0.75f,
                           requiresHorse = true, usesWeapon = true, weaponKind = WeaponKind.Bow },
        };
    }

    // Если список сохранился в сцене старым - достраиваем недостающие классы
    void EnsureUnits()
    {
        if (enemyUnits == null) enemyUnits = new List<UnitType>();
        foreach (var d in DefaultEnemyUnits())
            if (!enemyUnits.Exists(u => u != null && u.unitName == d.unitName)) enemyUnits.Add(d);
    }

    bool war;
    float calm;

    // ---------- Сообщения ----------
    // На экране - только самое свежее сообщение (под баннером войны), все остальные копятся в журнале:
    // вкладка «Журнал» в окне настроек (⚙), см. MessageLog.

    struct Note { public string text; public bool good, minor, ultra; public float until, born; }
    static readonly List<Note> notes = new List<Note>();
    const int MaxNotes = 5;
    const float NoteSeconds = 6f;   // final-13: было 8 с - сообщения дольше висели поверх карты
    const float MinorSeconds = 3.5f;   // final-13-fix-2: мелкие вести - короче

    // final-13: будничные сообщения (кто что куда несёт) не всплывают поверх карты - только в журнал.
    // Раньше их было так много, что всплывающие надписи постоянно закрывали экран
    static readonly string[] quietKeys = { " несут ", ": несут", "Несут ", "несут в ", "принесли", "отнесут", " везут", "Везут", "едет за товаром", "Товар несут" };
    static bool Quiet(string text, bool good)
    {
        if (!good || string.IsNullOrEmpty(text)) return false;   // плохие новости всегда видно
        foreach (var k in quietKeys) if (text.Contains(k)) return true;
        return false;
    }

    // final-13-fix-2: «мелкие» вести (склад полон, кухня сварила, конюшня вырастила, курьеры, очередь...) -
    // маленькая плашка без звука, а в настройках их можно выключить совсем (тогда - только журнал).
    // Важные (враги, набег, война, армия, лазутчики) - как раньше: большая карточка и звук.
    static readonly string[] importantKeys = { "враг", "Враг", "набег", "Набег", "напал", "грабит", "Тревог", "тревог", "войн", "Войн",
        "Штурм", "штурм", "лазутчик", "Лазутчик", "разбит", "разруш", "подожг", "заметили", "Дозорные", "отступ", "Армия", "армия",
        "игрок", "Игрок", "Погиб", "погиб", "ранен", "Убит", "убит", "Щит снят", "осад", "Осад", "Аванпост" };
    static readonly string[] minorKeys = { "Склад полон", "пропало", "Похлёбк", "Кухня", "Конюшня", "нет места", "некуда", "Нет свободных",
        "Собирать пока нечего", "очередь", "Инженерный двор", "Двор военного строителя", "Режим накопления", "Утро:", "Курьеры", "кончились",
        "Оружейная:", "Казарма:", "Израсходовано", "заряжено" };

    // final-13-fix-3: крупная карточка - только у самых важных вестей (нападение, набег, враги у деревни, лазутчик).
    // Остальные важные - карточка поменьше
    static readonly string[] ultraKeys = { "напал", "грабит", "Набег", "набег №", "Дозорные", "Тревога", "врагов идут", "Враги у", "Вас заметили",
        "лазутчик", "Лазутчик", "Штурм", "осад", "Осад", "зашёл в игру", "Армия в деревне игрока", "прорвался" };

    public static bool IsUltra(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var k in ultraKeys) if (text.Contains(k)) return true;
        return false;
    }

    public static bool IsMinor(string text, bool good)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var k in importantKeys) if (text.Contains(k)) return false;
        foreach (var k in minorKeys) if (text.Contains(k)) return true;
        return good && !RaidManager.ViewingBattle;   // будничные хорошие вести; в набеге - приказы и ход боя видно крупно
    }

    public static void Notify(string text, bool good)
    {
        bool minor = IsMinor(text, good);
        if (Quiet(text, good) || (minor && !GameSettings.MinorNotes))
        {
            MessageLog.Add(text, good);
            AwayReport.Note(text, good);
            return;
        }
        // final-13: то же сообщение второй раз подряд - не новое всплывающее, а продление старого
        if (notes.Count > 0 && notes[notes.Count - 1].text == text)
        {
            Note last = notes[notes.Count - 1];
            last.until = Time.time + (minor ? MinorSeconds : NoteSeconds);
            notes[notes.Count - 1] = last;
        }
        else notes.Add(new Note { text = text, good = good, minor = minor, ultra = !minor && IsUltra(text), until = Time.time + (minor ? MinorSeconds : NoteSeconds), born = Time.time });
        if (notes.Count > MaxNotes)
        {
            int drop = notes.FindIndex(x => x.minor);   // сначала вытесняем мелкие - важное не теряется
            notes.RemoveAt(drop >= 0 && drop < notes.Count - 1 ? drop : 0);
        }
        MessageLog.Add(text, good);
        AwayReport.Note(text, good);   // «Пока вас не было…» - ещё и в окно-сводку при входе
        if (!minor) Sfx.Notify(good);   // final-13-fix-2: мелкие - без звука
        Debug.Log("[Война] " + text);
    }

    // ---------- Тревога: свернуть окна ----------
    // Окна подписываются (OnEnable/OnDisable); обработчик закрывает своё окно и возвращает true, если что-то закрыл.
    // Срабатывает на начало войны и на предупреждение дозорных. Можно вызвать и извне: WarManager.RaiseAlarm().

    public static event System.Func<bool> OnAlarm;
    public static int CollapseToken { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAlarmStatics() { OnAlarm = null; CollapseToken = 0; notes.Clear(); }

    public static void RaiseAlarm()
    {
        Sfx.Sting("horn_alarm");
        if (CollapseWindows() > 0) Notify("Тревога! Окна свёрнуты", false);
    }

    /// <summary>Свернуть все окна без сообщения о тревоге (например, после экрана входа). Возвращает, сколько закрыто.</summary>
    public static int CollapseWindows()
    {
        CollapseToken++;
        var h = OnAlarm;
        if (h == null) return 0;
        int closed = 0;
        foreach (System.Delegate d in h.GetInvocationList())
        {
            try { if (((System.Func<bool>)d)()) closed++; }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        return closed;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        EnsureUnits();
        raidTimer = firstRaidDelay * RaidRarity;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (GameAdmin.On && kb != null && kb.f8Key.wasPressedThisFrame)
        {
            if (kb.ctrlKey.isPressed) ClearEnemies();
            else SpawnWave(kb.shiftKey.isPressed ? bigWave : smallWave);
        }

        TickAutoRaids();

        bool wasWar = war;
        if (EnemyAgent.HostileCount > 0) { war = true; calm = warCooldown; }   // лазутчик войну не объявляет
        else if (war)
        {
            calm -= Time.deltaTime;
            if (calm <= 0f) war = false;
        }
        if (war && !wasWar) { RaiseAlarm(); WarHint(); }
        if (!war && wasWar) Sfx.Sting("victory", 0.7f);   // враги ушли или перебиты - отбились
    }

    // Короткая подсказка в первые войны (final-8: тестеры не понимали, как управлять защитниками)
    static void WarHint()
    {
        int shown = PlayerPrefs.GetInt("hint_war_v1", 0);
        if (shown >= 3) return;
        PlayerPrefs.SetInt("hint_war_v1", shown + 1);
        PlayerPrefs.Save();
        Notify(UiKit.Phone
            ? "Нападение! Нажмите на бойца, потом на карту — его отряд пойдёт туда (двойное нажатие — марш). Башни и стены бьют сами, раненые лечатся в казарме."
            : "Нападение! Щёлкните по бойцу, потом по карте — его отряд пойдёт туда (двойной щелчок — марш). Башни и стены бьют сами, раненые лечатся в казарме.", false);
    }

    // Периодические набеги: таймер идёт, только когда в городе есть Цитадель. Враги ещё на карте - набег откладывается.
    float raidTimer;
    int pendingSide = -1;
    bool raidWarned;
    int autoRaidsDone;
    int wavesSpawned;     // сколько волн уже было (любых): от этого растёт сила врагов

    void TickAutoRaids()
    {
        if (!autoRaids) return;
        if (WarCityMode.Active) return;    // на военный город нападают только с карты войны
        if (RivalManager.Active) return;   // нападают соседи-соперники сами, по своему расчёту
        var pm = PopulationManager.Instance;
        if (GridNavigation.Instance == null || pm == null || pm.Hall == null) return;
        if (NewbieShield.BlocksNpc) return;   // защита новичка (final-8): набеги ждут

        raidTimer -= Time.deltaTime;
        if (raidTimer > 0f)
        {
            if (raidTimer <= raidWarnSeconds && !raidWarned)
            {
                raidWarned = true;
                pendingSide = Random.Range(0, 4);
                Notify($"Дозорные видят врагов с {SideName(pendingSide)}! Набег через ~{raidWarnSeconds:F0} с", false);
                RaiseAlarm();
            }
            return;
        }

        // Прошлые враги ещё бродят по карте - не наваливаем новых
        if (EnemyAgent.All.Count > 0)
        {
            raidTimer = 30f;
            raidWarned = false;
            pendingSide = -1;
            return;
        }

        int size = AutoRaidSize();
        SpawnWave(size, pendingSide);
        autoRaidsDone++;
        pendingSide = -1;
        raidWarned = false;
        raidTimer = NextRaidInterval();
    }

    static string SideName(int side)
    {
        switch (side)
        {
            case 0: return "запада";
            case 1: return "востока";
            case 2: return "юга";
            default: return "севера";
        }
    }

    // ---------- Сила ботонабегов от уровня Цитадели ----------
    // Чем выше Цитадель, тем набег больше, состав и снаряжение - как у более поздних набегов,
    // а сами враги чуть крепче и бьют чуть сильнее. Немного, чтобы от набега можно было отбиться.

    public static int HallLevel
    {
        get
        {
            var pm = PopulationManager.Instance;
            return pm != null && pm.Hall != null ? Mathf.Clamp(pm.Hall.level, 1, 5) : 1;
        }
    }
    public static float HallHpMul => 1f + 0.15f * (HallLevel - 1);    // Ур.2: +15%, Ур.3: +30%
    public static float HallDmgMul => 1f + 0.12f * (HallLevel - 1);   // Ур.2: +12%, Ур.3: +24%
    public static int HallExtraEnemies => 2 * (HallLevel - 1);        // Ур.3: +4 врага
    public static int HallWaveBonus => 2 * (HallLevel - 1);           // состав и снаряжение - как у набега на 2 номера позже

    int AutoRaidSize()
    {
        return Mathf.Min(autoRaidMax + 2 * HallExtraEnemies, autoRaidBase + autoRaidGrowth * autoRaidsDone + HallExtraEnemies);
    }

    // ---------- Набег ----------

    public void SpawnWave(int count, int side = -1)
    {
        var nav = GridNavigation.Instance;
        if (nav == null || count <= 0) return;

        int number = wavesSpawned++;
        int level = number + HallWaveBonus;
        float hpMul = HallHpMul, dmgMul = HallDmgMul;
        float half = nav.mapSize / 2f;
        if (side < 0) side = Random.Range(0, 4);
        float along = Random.Range(-half * 0.6f, half * 0.6f);
        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            UnitType unit = PickUnit(level);
            if (unit == null) break;

            float off = Mathf.Clamp(along + Random.Range(-3f, 3f), -half + 1f, half - 1f);
            float edge = half - 0.5f - Random.Range(0f, 1.5f);
            Vector2 p;
            switch (side)
            {
                case 0: p = new Vector2(-edge, off); break;
                case 1: p = new Vector2(edge, off); break;
                case 2: p = new Vector2(off, -edge); break;
                default: p = new Vector2(off, edge); break;
            }

            Vector2Int c = nav.WorldToCell(p);
            if (nav.IsBlocked(c)) c = nav.NearestFree(c);
            EnemyAgent.Spawn(unit, nav.CellCenter(c) + Random.insideUnitCircle * 0.2f, level, hpMul, dmgMul);
            spawned++;
        }

        if (spawned > 0) Notify($"Набег №{number + 1}: {spawned} врагов идут с {SideName(side)}!" + (HallLevel > 1 ? $" Цитадель Ур.{HallLevel} — враги крепче" : ""), false);
    }

    // Состав растёт с номером набега: сначала мечники и лучники, потом маги-целители и конница
    UnitType PickUnit(int level)
    {
        if (enemyUnits == null || enemyUnits.Count == 0) return null;

        float total = 0f;
        var w = new float[enemyUnits.Count];
        for (int i = 0; i < w.Length; i++)
        {
            w[i] = enemyUnits[i] != null ? WeightFor(enemyUnits[i].unitName, level) : 0f;
            total += w[i];
        }
        if (total <= 0f) return enemyUnits[0];

        float r = Random.value * total;
        for (int i = 0; i < w.Length; i++)
        {
            if (r < w[i]) return enemyUnits[i];
            r -= w[i];
        }
        return enemyUnits[enemyUnits.Count - 1];
    }

    static float WeightFor(string unitName, int level)
    {
        switch (unitName)
        {
            case "Мечник": return 60f;
            case "Лучник": return 30f;
            case "Рыцарь": return 10f + level * 1.5f;
            case "Полевой маг": return level >= 2 ? 6f + level : 0f;
            case "Всадник": return level >= 3 ? 5f + level : 0f;
            case "Конный лучник": return level >= 4 ? 4f + level : 0f;
            default: return 10f;
        }
    }

    public void ClearEnemies()
    {
        foreach (var e in new List<EnemyAgent>(EnemyAgent.All))
            if (e != null) e.Despawn();
    }

    // ---------- UI ----------

    void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        UiKit.Apply();
        // final-13-fix-3: полоски здоровья - сначала, плашки и сообщения - поверх них (раньше полоски жителей залезали на баннер)
        if (Event.current.type == EventType.Repaint) DrawWorldBars();
        float notesY = TopY(240f);   // не хватает места между казной и кнопками справа - под верхней полосой
        BannerBottom = 0f;
        // Обновление 14: на экране боя (набег на чужую деревню) плашки войны и сообщения не показываем - там и так
        // мало места. Сообщения не теряются: они в журнале (⚙ → Журнал)
        bool battleView = RaidManager.ViewingBattle;
        if (!battleView)
        {
            if (war) notesY = BannerBottom = DrawBanner() + 6f;
            notesY = AccumulationMode.DrawOffer(notesY);   // кончился ресурс - кнопка «Включить режим накопления» (обновление 22)
            DrawNotes(notesY);
        }
        else notes.RemoveAll(n => Time.time > n.until);

    }

    void DrawWorldBars()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float ppu = UiKit.PointsPerUnit(cam); // пикселей на клетку

        // Здания: повреждённые и руины
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null || !b.IsDamageable) continue;
            bool ruined = b.IsRuined;
            if (!ruined && b.Hp >= b.MaxHp - 0.5f) continue;

            Vector2 c = b.Center;
            Vector2 at = new Vector2(c.x, c.y - b.type.size.y * 0.5f - 0.25f);
            float w = b.type.size.x * ppu * 0.9f;

            if (ruined)
            {
                Color col = b.RebuildPaid ? new Color(1f, 0.6f, 0.2f) : new Color(0.55f, 0.55f, 0.55f);
                DrawBar(cam, at, w, b.RebuildPaid ? b.RebuildProgress : 0f, col);
                DrawSmallLabel(cam, at + Vector2.up * 0.35f, "руины");
            }
            else
            {
                float frac = b.Hp / b.MaxHp;
                DrawBar(cam, at, w, frac, Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 0.9f, 0.35f), frac));
            }
        }

        // Полевые лагеря: повреждённые и руины
        foreach (var camp in CampMarker.All)
        {
            if (camp == null) continue;
            Vector2 at = camp.Position + Vector2.up * 1.1f;
            float w = Mathf.Clamp(ppu * 1.6f, 20f, 90f);
            if (camp.IsRuined)
            {
                DrawBar(cam, at, w, camp.Rebuilding ? camp.RebuildProgress : 0f, camp.Rebuilding ? new Color(1f, 0.6f, 0.2f) : new Color(0.55f, 0.55f, 0.55f));
                DrawSmallLabel(cam, at + Vector2.up * 0.35f, "руины лагеря");
            }
            else if (camp.Hp < camp.MaxHp - 0.5f)
            {
                float frac = camp.Hp / camp.MaxHp;
                DrawBar(cam, at, w, frac, Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 0.9f, 0.35f), frac));
            }
        }

        // Наши люди: в бою, а также раненые во время войны. В мирное время - только совсем ослабшие
        // (голод теперь есть у всех - иначе полоски висели бы над каждым).
        foreach (var a in CitizenAgent.All)
        {
            if (a == null || a.IsDead || a.IsInsideBuilding) continue;
            if (a.Hp >= a.MaxHp - 0.01f && !a.InCombat) continue;
            if (!war && !a.InCombat && a.HpFraction >= 0.5f) continue;
            Color col = a.IsSoldier ? new Color(0.3f, 0.9f, 0.3f) : new Color(1f, 0.85f, 0.3f);
            DrawBar(cam, (Vector2)a.transform.position + Vector2.up * 0.5f, Mathf.Clamp(ppu * 0.7f, 14f, 60f), a.Hp / a.MaxHp, col);
        }

        // Враги - всегда
        foreach (var e in EnemyAgent.All)
        {
            if (!EnemyAgent.Alive(e)) continue;
            if (!FogOfWar.IsVisible(e.Position)) continue;   // в тумане не видно
            DrawBar(cam, e.Position + Vector2.up * 0.55f, Mathf.Clamp(ppu * 0.7f, 14f, 60f), e.Hp / e.MaxHp, new Color(1f, 0.2f, 0.2f));
        }
    }

    /// <summary>Нижний край баннера войны с зазором (0 - баннера нет). По нему встаёт полоса «на вас напали».</summary>
    public static float BannerBottom { get; private set; }

    // Плашка шириной до 2×halfW по центру сверху: на одной строке с казной (y = 8), если места хватает,
    // иначе под верхней полосой - раньше на ноутбуке баннер войны ложился на строку жителей и на кнопку «Клан»
    static float TopY(float halfW)
    {
        float left = UiKit.W / 2f - halfW, right = UiKit.W / 2f + halfW;
        float rightEdge = Mathf.Min(ResourceManager.TopButtonsLeft, GameClock.RowLeft);   // «Клан», «Путь», «Город», ⚙ и плашка часов
        bool fits = left > ResourceManager.HudRight + 8f && right < rightEdge - 8f;
        return fits ? 8f : ResourceManager.HudH + 6f;
    }

    // Баннер войны сверху по центру. Возвращает нижний край.
    float DrawBanner()
    {
        int n = EnemyAgent.HostileCount;
        string line2 = n > 0 ? $"Врагов на карте: {n}" : $"Мир через {Mathf.CeilToInt(Mathf.Max(0f, calm))} с";
        string threat = RivalManager.ThreatLine();
        if (threat == "") threat = HomeSiege.ThreatLine();   // набег другого игрока вживую
        float siegeLeft = HomeSiege.LiveSecondsLeft;         // final-13: общий таймер живого набега
        // final-13: карточка в стиле игры - мечи, красная пульсирующая рамка, без «системной» надписи капсом
        float w = 380f, h = threat != "" ? 64f : 46f;
        Rect r = new Rect(UiKit.W / 2f - w / 2f, TopY(w / 2f), w, h);
        float pulse = n > 0 ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f) : 0f;
        GameUi.Card(r, new Color(1f, 0.32f, 0.25f), 1f, n > 0 ? 0.25f + 0.35f * pulse : 0f);
        GameUi.Icon(new Rect(r.x + 12f, r.y + 7f, 32f, 32f), "ui_swords");

        GUIStyle head = new GUIStyle(UiKit.Body) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft };
        head.normal.textColor = new Color(1f, 0.62f, 0.52f);
        float tx = r.x + 52f, tw = r.width - 60f;
        if (siegeLeft > 0f) tw -= 70f;
        LocGUI.Label(new Rect(tx, r.y + 4f, tw, 22f), n > 0 ? "Враги у деревни!" : "Враги отступили", head);

        GUIStyle st = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = false, fontSize = 12, clipping = TextClipping.Clip };
        st.normal.textColor = new Color(0.88f, 0.84f, 0.78f);
        LocGUI.Label(new Rect(tx, r.y + 24f, tw, 18f), line2 + UiKit.Col(UiKit.MutedHex, "  ·  новые жители не прибывают"), st);
        if (threat != "") LocGUI.Label(new Rect(tx, r.y + 42f, tw, 18f), threat, st);
        if (siegeLeft > 0f)
        {
            Rect pr = new Rect(r.xMax - 78f, r.y + 8f, 68f, 28f);
            GameUi.Pill(pr, siegeLeft < 120f ? UiKit.Bad : UiKit.Accent);
            var ts = new GUIStyle(UiKit.Body) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            ts.normal.textColor = siegeLeft < 120f ? new Color(1f, 0.6f, 0.5f) : new Color(1f, 0.88f, 0.55f);
            GUI.Label(pr, GameUi.Clock(siegeLeft), ts);
            UiKit.Tip(pr, Loc.T("Сколько ещё может идти набег на вашу деревню"));
        }
        UiKit.Tip(r, "Пока на карте враги (и ещё немного после) — война: новые жители не прибывают, " +
            "мирные прячутся в здания, строить нельзя, купцы не заходят.");
        return r.yMax;
    }

    // Короткое сообщение («тост») по центру сверху: тёмная плашка с цветной полоской слева.
    // Показываем только самое свежее; если за последние секунды пришли ещё - приписка «ещё N в журнале».
    void DrawNotes(float y)
    {
        notes.RemoveAll(n => Time.time > n.until);
        if (notes.Count == 0) return;
        // klan-voina-14: в военной деревне справа сверху плашка войны - сообщение по центру места левее неё
        float areaW = Mathf.Min(UiKit.W, WarView.ChipLeft - 6f);
        float maxW = Mathf.Min(500f, areaW - 12f);
        // final-13-fix-2: пока на экране важное - мелкое его не перекрывает (ждёт за ним маленькой плашкой)
        int imp = notes.FindLastIndex(x => !x.minor);
        int min = notes.FindLastIndex(x => x.minor);
        if (imp >= 0)
        {
            Note n = notes[imp];
            float b = GameUi.Toast(areaW / 2f, y + 2f, n.ultra ? maxW : Mathf.Min(420f, maxW), n.text, n.good, n.born, n.until, notes.Count - 1, !n.ultra);
            if (min > imp) { Note m = notes[min]; GameUi.MiniToast(areaW / 2f, b + 4f, Mathf.Min(380f, maxW), m.text, m.good, m.born, m.until); }
        }
        else if (min >= 0)
        {
            Note m = notes[min];
            GameUi.MiniToast(areaW / 2f, y + 2f, Mathf.Min(380f, maxW), m.text, m.good, m.born, m.until);
        }
    }

    static void DrawBar(Camera cam, Vector2 world, float w, float frac, Color fill)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z < 0f) return;

        w = Mathf.Max(w, 14f);
        float h = Mathf.Clamp(w * 0.12f, 3f, 7f);
        Rect r = new Rect(UiKit.ToGui(sp).x - w / 2f, UiKit.ToGui(sp).y - h / 2f, w, h);
        if (r.xMax < 0f || r.x > UiKit.W || r.yMax < 0f || r.y > UiKit.H) return;
        if (TouchControls.UiCovers(r)) return;   // под окном или кнопкой полоску не рисуем

        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void DrawSmallLabel(Camera cam, Vector2 world, string text)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z < 0f) return;
        GUIStyle st = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        st.normal.textColor = UiKit.Warn;
        Rect r = new Rect(UiKit.ToGui(sp).x - 40f, UiKit.ToGui(sp).y - 10f, 80f, 20f);
        if (TouchControls.UiCovers(r)) return;   // надпись над картой не лезет на окна и кнопки
        LocGUI.Label(r, text, st);
    }
}