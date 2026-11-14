using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Журнал сообщений игры: всё, что приходит в WarManager.Notify (набеги, рынок, дозорные, завод…).
/// На экране сверху висит только самое свежее сообщение, а весь список - во вкладке «Журнал» окна настроек (⚙).
/// Хранится только в памяти, до перезапуска игры.
/// </summary>
public static class MessageLog
{
    public struct Entry { public string text; public bool good; public string stamp; public int count; }

    const int MaxEntries = 300;
    static readonly List<Entry> entries = new List<Entry>();
    static Vector2 scroll;

    /// <summary>Сколько сообщений пришло, пока вкладка «Журнал» не открыта (для бейджа на шестерёнке и вкладке).</summary>
    public static int Unread { get; private set; }
    public static IReadOnlyList<Entry> Entries => entries;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { entries.Clear(); Unread = 0; scroll = Vector2.zero; }

    public static void Add(string text, bool good)
    {
        if (string.IsNullOrEmpty(text)) return;
        string stamp = Stamp();
        // Одно и то же сообщение подряд - не плодим строки, а пишем «×2», «×3»
        int last = entries.Count - 1;
        if (last >= 0 && entries[last].text == text && entries[last].good == good)
        {
            Entry e = entries[last];
            e.count++;
            e.stamp = stamp;
            entries[last] = e;
        }
        else
        {
            entries.Add(new Entry { text = text, good = good, stamp = stamp, count = 1 });
            if (entries.Count > MaxEntries) entries.RemoveAt(0);
        }
        Unread++;
    }

    public static void Clear() { entries.Clear(); Unread = 0; }

    static string Stamp()
    {
        GameClock c = GameClock.Instance;
        if (c == null) return "";
        float h = c.Hours;
        int hh = Mathf.FloorToInt(h), mm = Mathf.FloorToInt((h - hh) * 60f);
        return $"{hh:00}:{mm:00}";
    }

    // ---------- Вкладка «Журнал» в настройках ----------

    const float ListH = 380f;

    public static float TabHeight(float w) { return ListH + 8f + UiKit.BtnH; }

    public static void Draw(Rect r)
    {
        Unread = 0;   // вкладку открыли - всё прочитано
        float x = r.x, y = r.y, w = r.width;

        if (entries.Count == 0)
        {
            LocGUI.Label(new Rect(x, y, w, 40f), UiKit.Col(UiKit.MutedHex, "Сообщений пока нет. Здесь копятся все оповещения: набеги, рынок, дозорные, производство."), UiKit.Small);
            return;
        }

        GUIStyle st = new GUIStyle(UiKit.Body) { fontSize = 12, wordWrap = true, richText = true };
        GUIStyle stamp = new GUIStyle(UiKit.Small) { wordWrap = false };
        const float stampW = 44f, gap = 4f;
        float listH = Mathf.Min(ListH, r.height - UiKit.BtnH - 8f);
        float textW = w - 16f - stampW - 8f;   // 16 - полоса прокрутки

        // Высоты строк считаем заранее: свежие сверху
        float total = 0f;
        var heights = new float[entries.Count];
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            heights[i] = Mathf.Max(18f, st.CalcHeight(Loc.Content(RowText(entries[i])), textW)) + gap;
            total += heights[i];
        }

        Rect view = new Rect(x, y, w, listH);
        scroll = UiKit.BeginScrollView(view, scroll, new Rect(0f, 0f, w - 16f, total));
        float ry = 0f;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry e = entries[i];
            float h = heights[i];
            // Рисуем только видимые строки
            if (ry + h >= scroll.y && ry <= scroll.y + listH)
            {
                Color old = GUI.color;
                Color stripe = e.good ? UiKit.Good : UiKit.Warn;
                GUI.color = new Color(stripe.r, stripe.g, stripe.b, 0.9f);
                GUI.DrawTexture(new Rect(0f, ry + 2f, 3f, h - gap - 2f), Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(new Rect(8f, ry, stampW, 18f), UiKit.Col(UiKit.MutedHex, e.stamp), stamp);
                st.normal.textColor = e.good ? UiKit.Text : new Color(1f, 0.88f, 0.75f);
                LocGUI.Label(new Rect(8f + stampW, ry, textW, h - gap), RowText(e), st);
            }
            ry += h;
        }
        GUI.EndScrollView();

        y += listH + 8f;
        LocGUI.Label(new Rect(x, y, w - 110f, UiKit.BtnH), UiKit.Col(UiKit.MutedHex, $"Сообщений: {entries.Count}"), new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft });
        if (UiKit.Button(new Rect(x + w - 100f, y, 100f, UiKit.BtnH), "Очистить", UiKit.Kind.Ghost, "Стереть все сообщения из журнала")) Clear();
    }

    static string RowText(Entry e) { return e.count > 1 ? $"{e.text} ×{e.count}" : e.text; }
}
