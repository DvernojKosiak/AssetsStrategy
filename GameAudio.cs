using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Звук всей игры: эффекты, фон природы и музыка. Создаётся сам при запуске, ставить на сцену ничего не нужно.
///
/// Как звучит:
///  - эффекты (Sfx.At / Sfx.Ui) - из Resources/Audio, у каждого своя громкость, разброс высоты и ограничение
///    «не чаще, чем…» (в большом бою сотни ударов в секунду, а слышно только несколько);
///  - звук на карте зависит от камеры: что за краем экрана - тише и стихает совсем, что левее - левее в наушниках;
///    чем дальше камера отъехала, тем тише мелочь;
///  - фон: днём ветер и птицы, ночью сверчки и сова, в бою - далёкий шум схватки;
///  - музыка: днём две мирные мелодии с паузами между ними, ночью тихая, в бою (набег на город, свой набег) - боевая,
///    на карте войны и пока армия в походе - походный марш. Смена - плавная.
///
/// Громкость - ползунки в Настройках (GameSettings): общая, музыка, звуки, окружение.
/// Щелчок по любой кнопке интерфейса слышен сам: ловим, когда IMGUI захватил мышь (GUIUtility.hotControl).
/// </summary>
public static class Sfx
{
    /// <summary>Звук в точке карты (бой, стройка, здания).</summary>
    public static void At(string name, Vector2 pos, float vol = 1f) { GameAudio.Play(name, vol, true, pos); }

    /// <summary>Звук интерфейса: не зависит от камеры.</summary>
    public static void Ui(string name, float vol = 1f) { GameAudio.Play(name, vol, false, Vector2.zero); }

    /// <summary>Важное событие (построено, победа...): на полсекунды глушит обычный звук сообщения, чтобы не звучали вместе.</summary>
    public static void Sting(string name, float vol = 1f) { GameAudio.MarkSting(); GameAudio.Play(name, vol, false, Vector2.zero); }

    /// <summary>Сообщение в ленте (WarManager.Notify): хорошее - колокольчик, плохое - низкий сигнал.</summary>
    public static void Notify(bool good)
    {
        if (GameAudio.StingRecently) return;
        Ui(good ? "notify_good" : "notify_bad", good ? 0.45f : 0.5f);
    }
}

public class GameAudio : MonoBehaviour
{
    // ---------- Настройки звуков: громкость, разброс высоты, не чаще чем (с), сколько сразу ----------

    struct Spec
    {
        public float vol, pitch, gap; public int max;
        public Spec(float v, float p, float g, int m) { vol = v; pitch = p; gap = g; max = m; }
    }

    static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
    {
        // интерфейс
        { "ui_click",        new Spec(0.45f, 0.04f, 0.04f, 2) },
        { "logo_cinetica",   new Spec(0.85f, 0.00f, 1.00f, 1) },   // заставка Cinetica Games
        { "ui_open",         new Spec(0.35f, 0.03f, 0.10f, 1) },
        { "ui_close",        new Spec(0.30f, 0.03f, 0.10f, 1) },
        { "ui_coins",        new Spec(0.50f, 0.05f, 0.15f, 2) },
        { "notify_good",     new Spec(1.00f, 0.00f, 0.80f, 1) },
        { "notify_bad",      new Spec(1.00f, 0.00f, 0.80f, 1) },
        // стройка и здания
        { "build_place",     new Spec(0.70f, 0.06f, 0.08f, 2) },
        { "build_hammer",    new Spec(0.30f, 0.10f, 0.12f, 3) },
        { "build_saw",       new Spec(0.22f, 0.06f, 0.50f, 1) },
        { "build_complete",  new Spec(0.60f, 0.00f, 0.40f, 1) },
        { "upgrade_complete",new Spec(0.65f, 0.00f, 0.40f, 1) },
        { "demolish",        new Spec(0.75f, 0.05f, 0.20f, 1) },
        { "building_hit",    new Spec(0.40f, 0.10f, 0.18f, 3) },
        { "building_collapse",new Spec(0.85f, 0.06f, 0.30f, 2) },
        { "ram_hit",         new Spec(0.80f, 0.06f, 0.25f, 2) },
        // выбор здания
        { "sel_barracks",    new Spec(0.40f, 0.04f, 0.15f, 1) },
        { "sel_forge",       new Spec(0.40f, 0.03f, 0.15f, 1) },
        { "sel_market",      new Spec(0.45f, 0.04f, 0.15f, 1) },
        { "sel_townhall",    new Spec(0.40f, 0.00f, 0.15f, 1) },
        { "sel_magic",       new Spec(0.35f, 0.03f, 0.15f, 1) },
        { "sel_tower",       new Spec(0.45f, 0.04f, 0.15f, 1) },
        { "sel_farm",        new Spec(0.45f, 0.04f, 0.15f, 1) },
        { "sel_sawmill",     new Spec(0.40f, 0.04f, 0.15f, 1) },
        { "sel_mine",        new Spec(0.45f, 0.04f, 0.15f, 1) },
        { "sel_house",       new Spec(0.50f, 0.05f, 0.15f, 1) },
        { "sel_kitchen",     new Spec(0.40f, 0.04f, 0.15f, 1) },
        { "sel_stable",      new Spec(0.55f, 0.04f, 0.15f, 1) },
        { "sel_storage",     new Spec(0.45f, 0.04f, 0.15f, 1) },
        { "sel_wall",        new Spec(0.40f, 0.04f, 0.15f, 1) },
        { "sel_bridge",      new Spec(0.40f, 0.04f, 0.15f, 1) },
        { "sel_default",     new Spec(0.45f, 0.06f, 0.10f, 1) },
        // бой
        { "bow",             new Spec(0.32f, 0.08f, 0.07f, 4) },
        { "sword",           new Spec(0.30f, 0.10f, 0.06f, 5) },
        { "death",           new Spec(0.45f, 0.08f, 0.10f, 3) },
        { "cannon",          new Spec(0.75f, 0.06f, 0.15f, 3) },
        { "cannon_impact",   new Spec(0.55f, 0.08f, 0.10f, 3) },
        { "catapult",        new Spec(0.60f, 0.06f, 0.15f, 2) },
        { "explosion",       new Spec(0.70f, 0.08f, 0.12f, 3) },
        { "shell_launch",    new Spec(0.35f, 0.06f, 0.15f, 2) },
        { "magic_zap",       new Spec(0.30f, 0.08f, 0.08f, 3) },
        { "fire_whoosh",     new Spec(0.40f, 0.06f, 0.12f, 2) },
        { "fire_burst",      new Spec(0.55f, 0.06f, 0.15f, 2) },
        { "water_wave",      new Spec(0.55f, 0.05f, 0.30f, 1) },
        { "splash",          new Spec(0.55f, 0.06f, 0.20f, 2) },
        { "steam_shot",      new Spec(0.60f, 0.06f, 0.15f, 2) },
        { "steam_puff",      new Spec(0.35f, 0.06f, 0.15f, 2) },
        { "heal",            new Spec(0.30f, 0.05f, 0.25f, 2) },
        { "chaos",           new Spec(0.45f, 0.05f, 0.20f, 2) },
        { "trap_snap",       new Spec(0.60f, 0.08f, 0.10f, 2) },
        { "coins_small",     new Spec(0.30f, 0.08f, 0.12f, 2) },
        // события
        { "horn_alarm",      new Spec(0.70f, 0.00f, 20f,   1) },
        { "raid_march",      new Spec(0.65f, 0.00f, 3f,    1) },
        { "victory",         new Spec(0.75f, 0.00f, 3f,    1) },
        { "defeat",          new Spec(0.75f, 0.00f, 3f,    1) },
    };
    static readonly Spec DefaultSpec = new Spec(0.5f, 0.04f, 0.08f, 2);

    // Музыка: громкость каждой мелодии (ночная и так плотная - тише)
    static readonly Dictionary<string, float> MusicGain = new Dictionary<string, float>
    {
        { "music_town_1", 0.42f }, { "music_town_2", 0.42f }, { "music_night", 0.30f },
        { "music_battle", 0.45f }, { "music_march", 0.45f },
    };

    const int Voices = 24;               // сколько звуков одновременно
    const float FadeSec = 2.5f;          // смена музыки
    const string Root = "Audio/";

    // ---------- Создание ----------

    static GameAudio inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; lastPlay.Clear(); variants.Clear(); clips.Clear(); stingUntil = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("GameAudio");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<GameAudio>();
    }

    AudioSource[] voices;
    readonly string[] voiceName = new string[Voices];
    readonly float[] voiceStart = new float[Voices];
    AudioSource musicA, musicB, ambDay, ambNight, ambBattle;
    bool musicOnA = true;

    void Awake()
    {
        voices = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++) voices[i] = NewSource(false);
        musicA = NewSource(true);
        musicB = NewSource(true);
        ambDay = NewSource(true);
        ambNight = NewSource(true);
        ambBattle = NewSource(true);
        StartLoop(ambDay, "amb_day");
        StartLoop(ambNight, "amb_night");
        StartLoop(ambBattle, "amb_battle");
        nextDayTrack = Random.Range(0, 2);
    }

    AudioSource NewSource(bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;   // своё «пространство»: громкость и панорама от камеры
        s.volume = 0f;
        return s;
    }

    void StartLoop(AudioSource s, string clip)
    {
        s.clip = Clip(clip);
        if (s.clip == null) return;
        s.volume = 0f;
        s.time = Random.Range(0f, s.clip.length * 0.9f);   // фон начинается с разных мест
        s.Play();
    }

    // ---------- Клипы ----------

    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, int> variants = new Dictionary<string, int>();

    static AudioClip Clip(string file)
    {
        if (clips.TryGetValue(file, out var c)) return c;
        c = Resources.Load<AudioClip>(Root + file);
        clips[file] = c;
        return c;
    }

    /// <summary>«bow» -> bow_1 / bow_2 / bow_3 случайно; «ui_click» -> сам ui_click.</summary>
    static AudioClip Pick(string name)
    {
        if (!variants.TryGetValue(name, out int n))
        {
            n = 0;
            while (Clip(name + "_" + (n + 1)) != null) n++;
            variants[name] = n;
        }
        return n == 0 ? Clip(name) : Clip(name + "_" + Random.Range(1, n + 1));
    }

    // ---------- Эффекты ----------

    static readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
    static float stingUntil;

    public static void MarkSting() { stingUntil = Time.unscaledTime + 0.6f; }
    public static bool StingRecently => Time.unscaledTime < stingUntil;

    public static void Play(string name, float vol, bool world, Vector2 pos)
    {
        if (inst == null || string.IsNullOrEmpty(name)) return;
        inst.PlayNow(name, vol, world, pos);
    }

    void PlayNow(string name, float vol, bool world, Vector2 pos)
    {
        float level = GameSettings.EffectsLevel;
        if (level <= 0.001f) return;
        if (!Specs.TryGetValue(name, out Spec sp)) sp = DefaultSpec;

        float now = Time.unscaledTime;
        if (lastPlay.TryGetValue(name, out float last) && now - last < sp.gap) return;

        float pan = 0f;
        if (world)
        {
            float k = WorldGain(pos, out pan);
            if (k <= 0.02f) return;   // далеко за краем экрана - не слышно
            vol *= k;
        }

        // Не больше sp.max одинаковых звуков сразу; нет свободного голоса - берём самый старый
        int same = 0, pick = -1, oldest = -1;
        for (int i = 0; i < voices.Length; i++)
        {
            if (!voices[i].isPlaying) { if (pick < 0) pick = i; continue; }
            if (voiceName[i] == name) same++;
            if (oldest < 0 || voiceStart[i] < voiceStart[oldest]) oldest = i;
        }
        if (same >= sp.max) return;
        AudioClip clip = Pick(name);
        if (clip == null) return;
        if (pick < 0) pick = oldest;
        if (pick < 0) return;
        AudioSource src = voices[pick];
        voiceName[pick] = name;
        voiceStart[pick] = now;

        lastPlay[name] = now;
        src.Stop();
        src.clip = clip;
        src.volume = Mathf.Clamp01(sp.vol * vol * level);
        src.pitch = 1f + (sp.pitch > 0f ? Random.Range(-sp.pitch, sp.pitch) : 0f);
        src.panStereo = pan;
        src.Play();
    }

    /// <summary>Громкость звука в точке карты: на экране - 1, за краем стихает; отъехали камерой - мелочь тише.</summary>
    static float WorldGain(Vector2 pos, out float pan)
    {
        pan = 0f;
        Camera cam = Camera.main;
        if (cam == null) return 1f;
        float h = cam.orthographic ? cam.orthographicSize : 10f;
        float w = h * cam.aspect;
        Vector2 d = pos - (Vector2)cam.transform.position;
        float nx = Mathf.Abs(d.x) / Mathf.Max(0.1f, w), ny = Mathf.Abs(d.y) / Mathf.Max(0.1f, h);
        float n = Mathf.Max(nx, ny);
        float edge = n <= 1f ? 1f : Mathf.Clamp01(1f - (n - 1f) / 0.6f);
        float zoom = Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(7f, 24f, h));
        pan = Mathf.Clamp(d.x / Mathf.Max(0.1f, w), -1f, 1f) * 0.55f;
        return edge * edge * zoom;
    }

    // ---------- Каждый кадр: щелчки, выбор зданий, окна, фон, музыка ----------

    int lastHot;
    BuildingInstance lastSelected;
    static int windowsSeen, windowsPrev;
    float windowSoundAt;

    /// <summary>UiKit.Window зовёт это, когда рисует окно: так мы слышим, что окно открылось или закрылось.</summary>
    public static void NoteWindow()
    {
        if (Event.current != null && Event.current.type == EventType.Repaint) windowsSeen++;
    }

    void Update()
    {
        // Щелчок по кнопке, вкладке, галочке: IMGUI захватил мышь
        int hot = GUIUtility.hotControl;
        if (hot != 0 && lastHot == 0) Sfx.Ui("ui_click");
        lastHot = hot;

        // Выбрали здание на карте - у каждого свой звук
        BuildingInstance sel = BuildingPlacer.Selected;
        if (sel != lastSelected)
        {
            if (sel != null && sel.type != null) Sfx.At(SelectSound(sel), sel.Center, 1f);
            lastSelected = sel;
        }

        TickAmbience();
        TickMusic();
        TickRaidPhase();
    }

    void LateUpdate()
    {
        // Число окон на экране выросло - «открылось», уменьшилось - «закрылось»
        if (Time.unscaledTime > windowSoundAt)
        {
            if (windowsSeen > windowsPrev) { Sfx.Ui("ui_open"); windowSoundAt = Time.unscaledTime + 0.15f; }
            else if (windowsSeen < windowsPrev) { Sfx.Ui("ui_close"); windowSoundAt = Time.unscaledTime + 0.15f; }
        }
        windowsPrev = windowsSeen;
        windowsSeen = 0;
    }

    static string SelectSound(BuildingInstance b)
    {
        string n = b.type.name;
        if (b.IsTownHall) return "sel_townhall";
        if (b.IsBarrier) return "sel_wall";
        if (b.IsBridge) return "sel_bridge";
        if (b.IsRoad) return "sel_default";
        if (Has(n, "Казарм") || Has(n, "Оружейн") || Has(n, "Двор военного")) return "sel_barracks";
        if (Has(n, "Кузн") || Has(n, "Литейн") || Has(n, "Завод") || Has(n, "Инженерн") || Has(n, "Мастерск")) return "sel_forge";
        if (Has(n, "Торгов") || Has(n, "Рын") || Has(n, "Обоз") || Has(n, "Курьер")) return "sel_market";
        if (Has(n, "Маг") || Has(n, "Алтар") || Has(n, "Зелье") || Has(n, "целител")) return "sel_magic";
        if (Has(n, "Башн") || Has(n, "Пушк") || Has(n, "Аванпост")) return "sel_tower";
        if (Has(n, "Ферм") || Has(n, "собирател")) return "sel_farm";
        if (Has(n, "Лесопил")) return "sel_sawmill";
        if (Has(n, "Шахт") || Has(n, "Камен")) return "sel_mine";
        if (Has(n, "Кухн")) return "sel_kitchen";
        if (Has(n, "Конюш")) return "sel_stable";
        if (Has(n, "Хранил") || Has(n, "Склад")) return "sel_storage";
        if (Has(n, "дом")) return "sel_house";
        return "sel_default";
    }

    static bool Has(string s, string part) { return s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0; }

    // ---------- Бой: где он идёт ----------

    static bool InBattle
    {
        get
        {
            if (RaidManager.ViewingBattle) return true;
            if (WarView.IsOpen) return false;
            return WarManager.IsWar;
        }
    }

    static bool Marching
    {
        get
        {
            if (WarView.IsOpen) return true;
            var rm = RaidManager.Instance;
            if (rm == null || RaidManager.ViewingBattle) return false;
            return rm.State == RaidManager.Phase.Marching || rm.State == RaidManager.Phase.TravelOut;
        }
    }

    // ---------- Фон ----------

    void TickAmbience()
    {
        float level = GameSettings.AmbienceLevel * StartupScreen.SoundGain;   // на заставке тишина
        bool night = GameClock.Instance != null && GameClock.Instance.IsNight;
        bool battle = InBattle;
        bool onMap = !WarView.IsOpen;
        float dt = Time.unscaledDeltaTime;
        Fade(ambDay, onMap && !night ? 0.55f * level : 0f, dt, 0.25f);
        Fade(ambNight, onMap && night ? 0.6f * level : 0f, dt, 0.25f);
        Fade(ambBattle, battle ? 0.4f * level : 0f, dt, 0.35f);
    }

    static void Fade(AudioSource s, float target, float dt, float speed)
    {
        if (s == null || s.clip == null) return;
        s.volume = Mathf.MoveTowards(s.volume, target, dt * speed);
    }

    // ---------- Музыка ----------

    string wantTrack, curTrack;
    int nextDayTrack;
    float silenceLeft = 3f;   // в начале игры - пара секунд тишины

    void TickMusic()
    {
        float level = GameSettings.MusicLevel * StartupScreen.SoundGain;   // на заставке тишина
        AudioSource cur = musicOnA ? musicA : musicB, other = musicOnA ? musicB : musicA;
        float dt = Time.unscaledDeltaTime;

        string want;
        bool loopIt = true;
        if (InBattle) want = "music_battle";
        else if (Marching) want = "music_march";
        else if (GameClock.Instance != null && GameClock.Instance.IsNight) want = "music_night";
        else { want = null; loopIt = false; }   // днём мелодии идут по очереди с паузами

        if (want == null)
        {
            // Мирный день: доиграла мелодия - пауза 20-50 с - следующая
            bool dayPlaying = curTrack != null && curTrack.StartsWith("music_town") && cur.isPlaying;
            if (!dayPlaying)
            {
                if (curTrack != null && !curTrack.StartsWith("music_town")) { silenceLeft = Mathf.Min(silenceLeft, 4f); curTrack = null; }
                if (curTrack != null && curTrack.StartsWith("music_town")) { curTrack = null; silenceLeft = Random.Range(20f, 50f); }
                silenceLeft -= dt;
                if (silenceLeft <= 0f)
                {
                    want = nextDayTrack == 0 ? "music_town_1" : "music_town_2";
                    nextDayTrack = 1 - nextDayTrack;
                }
            }
            else want = curTrack;
        }

        if (want != curTrack && (want != null || cur.isPlaying))
        {
            // Плавно гасим текущую, новую - на второй источник
            musicOnA = !musicOnA;
            var tmp = cur; cur = other; other = tmp;
            if (want != null)
            {
                cur.clip = Clip(want);
                cur.loop = loopIt;
                cur.volume = 0f;
                if (cur.clip != null) cur.Play();
            }
            curTrack = want;
        }

        float gain = curTrack != null && MusicGain.TryGetValue(curTrack, out float g) ? g : 0.4f;
        Fade(cur, curTrack != null ? gain * level : 0f, dt, 1f / FadeSec);
        Fade(other, 0f, dt, 1f / FadeSec);
        if (other.isPlaying && other.volume <= 0.001f) other.Stop();
        if (curTrack == null && cur.isPlaying && cur.volume <= 0.001f) cur.Stop();
    }

    // ---------- Набег: армия вышла ----------

    RaidManager.Phase lastPhase = RaidManager.Phase.Idle;

    void TickRaidPhase()
    {
        var rm = RaidManager.Instance;
        if (rm == null) return;
        var ph = rm.State;
        if (ph != lastPhase && ph == RaidManager.Phase.Marching) Sfx.Sting("raid_march");
        lastPhase = ph;
    }

    // ---------- Свернули игру ----------

    void OnApplicationFocus(bool focus)
    {
        if (!GameSettings.MuteInBackground) { AudioListener.pause = false; return; }
        AudioListener.pause = !focus;
    }

    void OnApplicationPause(bool paused)
    {
        if (GameSettings.MuteInBackground) AudioListener.pause = paused;
    }

    // ---------- Слушатель ----------

    void Start()
    {
        // Без AudioListener Unity молчит. Обычно он на камере; если нет - вешаем на себя.
        if (FindObjectOfType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
    }
}
