using System;
using UnityEngine;

/// <summary>
/// Щит города: после набега другого игрока (не соседей) город на время пропадает у других игроков во вкладке «Бой».
/// Сколько он стоит, решает сервер по тяжести набега (ShieldRules); время приходит в каждом ответе сервера (shieldUtc).
/// Где виден: плашка «Щит города» под кнопкой «Магазин марок» (рисует BuildingPlacer через DrawChip) - открывает окно,
/// в нём объяснение и кнопка «Снять щит» (если хочется обороняться).
/// </summary>
public class CityShield : MonoBehaviour
{
    public static CityShield Instance { get; private set; }

    static bool known;         // сервер прислал щит (старый сервер его не присылает - тогда плашки нет)
    static DateTime until;     // до этого времени (часы сервера) город под щитом
    static Rect chipRect, winRect;
    static int chipFrame = -10;

    bool open;
    bool dropping;
    string msg = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; known = false; until = DateTime.MinValue; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("CityShield");
        DontDestroyOnLoad(go);
        go.AddComponent<CityShield>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Ответ сервера: запомнить щит (зовёт AccountBackend на каждый ответ).</summary>
    public static void Sync(ApiResponse r)
    {
        if (r == null || !r.ok || !r.shieldKnown) return;
        known = true;
        until = TryUtc(r.shieldUtc, out DateTime t) ? t : DateTime.MinValue;
    }

    /// <summary>Сколько секунд ещё стоит щит (0 - щита нет).</summary>
    public static double SecondsLeft
    {
        get
        {
            if (!known || !AccountManager.LoggedIn || AccountManager.IsOffline) return 0.0;
            return Math.Max(0.0, (until - WorldClock.ServerUtc).TotalSeconds);
        }
    }

    public static bool Active => SecondsLeft > 0.0;

    /// <summary>Сколько осталось словами: «3 ч 20 мин».</summary>
    public static string LeftText => ShieldRules.Duration(SecondsLeft);

    public static bool IsPointerOver(Vector2 gp)
    {
        if (Instance == null) return false;
        return (Time.frameCount - chipFrame <= 2 && chipRect.Contains(gp)) || (Instance.open && winRect.Contains(gp));
    }

    /// <summary>Плашка щита (рисует BuildingPlacer под «Магазином марок»). Щита нет - ничего не рисует. Возвращает нижний край.</summary>
    public static float DrawChip(Rect r)
    {
        var s = Instance;
        if (s == null || WarCityMode.Active || !Active) return r.y - 6f;
        chipRect = r;
        chipFrame = Time.frameCount;
        // final-13: значок щита и время, а не просто надпись «Щит города» с таймером
        string label = $"{Loc.T("Щит")}  {UiKit.Col("#BFE4FF", LeftText)}";
        if (GameUi.IconChip(r, "ui_cityshield", label, UiKit.Info,
            "Пока стоит щит, другие игроки не видят ваш город во вкладке «Бой». Нажмите, чтобы узнать больше или снять щит.", s.open))
        {
            s.open = !s.open;
            if (s.open) s.msg = "";
        }
        return r.yMax;
    }

    void Update()
    {
        if (!open) return;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) open = false;
    }

    bool wasOpen;

    void OnGUI()
    {
        open = UiKit.KeepFront("shield", open, ref wasOpen);   // одно большое окно за раз
        if (!open || WarView.IsOpen) return;
        if (WarCityMode.Active || !AccountManager.LoggedIn) { open = false; return; }
        UiKit.Apply();
        GUI.depth = -10;

        bool on = Active;
        float w = Mathf.Min(460f, UiKit.W - 40f);
        float rowsH = ShieldRules.Tiers.Length * 18f;
        string head = on ? $"Город под щитом ещё <b>{UiKit.Col(UiKit.InfoHex, LeftText)}</b>." : UiKit.Col(UiKit.MutedHex, "Щита сейчас нет.");
        string body = head + "\nПока стоит щит, другие игроки не видят ваш город во вкладке «Бой» и не могут на него напасть. " +
                      "Соседи-NPC под щитом ждут, пока город отстроится (все здания починены, дома хотя бы 3 бойца), и тогда нападают только вполсилы.";
        // Высота текста - по настоящему переносу строк (в переводе текст длиннее, и кнопка уезжала за низ окна)
        float bh = UiKit.TextH(body, w - UiKit.Pad * 2f, UiKit.Body);
        float h = Mathf.Min(UiKit.WindowH(bh + 8f + 22f + rowsH + 10f + 36f + 40f), UiKit.H - 80f);
        winRect = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f, w, h);
        if (UiKit.Phone) { winRect = UiKit.CenterWindow(w, UiKit.WindowH(bh + 8f + 22f + rowsH + 10f + 36f + 40f)); w = winRect.width; h = winRect.height; }
        CameraController.BlockZoom();
        Rect c = UiKit.Window(winRect, "Щит города", out bool close);
        if (close) { open = false; return; }
        // final-13: большой значок щита в шапке окна
        GameUi.Icon(new Rect(winRect.xMax - (UiKit.Phone ? 84f : 66f), winRect.y + 2f, 30f, 30f), "ui_cityshield", on ? 1f : 0.4f);

        float y = c.y;
        LocGUI.Label(new Rect(c.x, y, c.width, bh), body, UiKit.Body);
        y += bh + 8f;

        LocGUI.Label(new Rect(c.x, y, c.width, 20f), UiKit.Col(UiKit.MutedHex, "Щит после набега игрока — чем сильнее разорили, тем дольше:"), UiKit.Small);
        y += 22f;
        GUIStyle right = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleRight, wordWrap = false };
        GUIStyle left = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        foreach (var t in ShieldRules.Tiers)
        {
            LocGUI.Label(new Rect(c.x + 6f, y, c.width - 90f, 18f), t.name, left);
            LocGUI.Label(new Rect(c.xMax - 80f, y, 80f, 18f), UiKit.B(ShieldRules.Duration(t.minutes * 60.0)), right);
            y += 18f;
        }
        y += 10f;

        string tip = "Город снова появится у других игроков во вкладке «Бой». Пригодится, если вы готовы обороняться.";
        if (UiKit.Button(new Rect(c.x, y, c.width, 32f), dropping ? "Снимаем щит…" : "Снять щит", on && !dropping, UiKit.Kind.Danger, tip))
            ConfirmDialog.Ask("Снять щит?",
                "Ваш город снова смогут найти и атаковать другие игроки. Вернуть щит нельзя: новый появится только после следующего набега на вас.",
                "Снять щит", Drop);
        y += 36f;
        if (msg != "") LocGUI.Label(new Rect(c.x, y, c.width, 36f), msg, UiKit.Small);
    }

    void Drop()
    {
        if (dropping || !AccountManager.LoggedIn) return;
        dropping = true;
        msg = "";
        AccountManager.Backend.Call(Api.ShieldDrop, AccountManager.Token, new ApiRequest(), res =>
        {
            dropping = false;
            if (res.ok)
            {
                until = DateTime.MinValue;
                open = false;
                WarManager.Notify("Щит снят: ваш город снова виден другим игрокам", true);
            }
            else msg = UiKit.Col(UiKit.BadHex, res.error != null && res.error.StartsWith("Неизвестный запрос")
                ? "Сервер старой версии: обновите StrategycaServer, чтобы снимать щит"
                : "Щит не снят: " + res.error);
        });
    }

    static bool TryUtc(string s, out DateTime d)
    {
        d = DateTime.MinValue;
        if (string.IsNullOrEmpty(s)) return false;
        if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out d)) { d = d.ToUniversalTime(); return true; }
        return false;
    }
}
