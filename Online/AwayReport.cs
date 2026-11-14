using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Окно «Пока вас не было» при входе в игру: что город заработал и потратил, кто на вас нападал и что унёс,
/// что продано на рынке, чем кончились походы. Собирается из обычных сообщений (WarManager.Notify), в которых
/// есть «вас не было», - их легко пропустить внизу экрана, а набег на город пропускать нельзя.
/// Окно появляется, когда город уже открыт, и закрывается кнопкой «Понятно».
/// </summary>
public class AwayReport : MonoBehaviour
{
    const float CollectSeconds = 10f;   // столько после загрузки города собираем сообщения
    const int MaxLines = 12;

    struct Line { public string text; public bool good; }
    static readonly List<Line> lines = new List<Line>();
    static AwayReport inst;
    static float firstAt = -1f;
    static bool open;
    Vector2 scroll;
    float contentH = 100f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { lines.Clear(); inst = null; firstAt = -1f; open = false; }

    /// <summary>Сообщение игры: если оно про время без игрока - в сводку.</summary>
    public static void Note(string text, bool good)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("вас не было")) return;
        if (lines.Count >= MaxLines) return;
        lines.Add(new Line { text = text, good = good });
        if (firstAt < 0f) firstAt = Time.unscaledTime;
        if (inst == null)
        {
            var go = new GameObject("AwayReport");
            inst = go.AddComponent<AwayReport>();
        }
    }

    public static bool IsPointerOver(Vector2 gp) { return open && inst != null && inst.rect.Contains(gp); }

    Rect rect;

    void OnGUI()
    {
        if (lines.Count == 0) return;
        if (StartupScreen.Covering || !AccountManager.LoggedIn || BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) return;
        if (!open && Time.unscaledTime - firstAt < 1.5f) return;   // даём досчитаться остальному (набеги идут чуть позже)
        open = true;
        UiKit.Apply();
        GUI.depth = -20;

        float w = Mathf.Min(520f, UiKit.W - 24f);
        float h = Mathf.Min(contentH + 110f, UiKit.H - 40f);
        rect = new Rect((UiKit.W - w) * 0.5f, (UiKit.H - h) * 0.5f, w, h);
        Rect c = UiKit.Window(rect, "Пока вас не было", out bool close);

        Rect view = new Rect(c.x, c.y, c.width, c.height - 44f);
        float bar = contentH > view.height ? UiKit.ScrollBarW + 4f : 0f;
        float lw = view.width - bar;
        scroll = UiKit.BeginScrollView(view, scroll, new Rect(0f, 0f, lw, Mathf.Max(contentH, view.height)));
        float y = 0f;
        foreach (var l in lines)
        {
            string t = (l.good ? UiKit.Col(UiKit.GoodHex, "• ") : UiKit.Col(UiKit.BadHex, "• ")) + l.text;
            float th = UiKit.TextH(t, lw - 4f, UiKit.Body);
            LocGUI.Label(new Rect(0f, y, lw - 4f, th), t, UiKit.Body);
            y += th + 8f;
        }
        // Набеги с записью боя: посмотреть, как всё было (final-9)
        foreach (var o in ReplayViewer.Fresh())
        {
            if (UiKit.Button(new Rect(0f, y, lw - 4f, UiKit.BtnH), "▶ Повтор: набег игрока " + o.name, UiKit.Kind.Normal)) ReplayViewer.Play(o);
            y += UiKit.BtnH + 6f;
        }
        contentH = y;
        GUI.EndScrollView();

        if (UiKit.Button(new Rect(c.x + (c.width - 160f) * 0.5f, c.yMax - 38f, 160f, 34f), "Понятно", UiKit.Kind.Primary) || close)
        {
            lines.Clear();
            open = false;
            firstAt = -1f;
            Destroy(gameObject);
            inst = null;
        }
    }

    void Update()
    {
        // Прошло много времени, а город так и не открылся (офлайн и т.п.) - не копим старое
        if (!open && firstAt >= 0f && Time.unscaledTime - firstAt > CollectSeconds * 6f) { lines.Clear(); firstAt = -1f; }
    }
}
