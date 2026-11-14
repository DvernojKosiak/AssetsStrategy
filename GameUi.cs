using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// final-13: общий «игровой» вид для плашек и уведомлений - чтобы игра выглядела как игра, а не как таблица.
///  - Icon: значки интерфейса (Resources/Art/i/ui_*.png и значки вещей) - щит города, мечи, песочные часы, колокол…
///  - Card: карточка уведомления: тёмная плашка с цветной светящейся рамкой и полосой слева.
///  - Pill: скруглённая «таблетка» (таймер, счётчик).
///  - Toast: всплывающее сообщение со значком, плавным появлением и исчезновением (WarManager.DrawNotes).
///  - RaidTimer: общий таймер набега - одинаковый у нападающего и у защитника.
/// </summary>
public static class GameUi
{
    // ---------- Значки ----------

    static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    static readonly HashSet<string> noIcon = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { icons.Clear(); noIcon.Clear(); }

    public static Texture2D IconTex(string id)
    {
        if (string.IsNullOrEmpty(id) || noIcon.Contains(id)) return null;
        if (icons.TryGetValue(id, out var t) && t != null) return t;
        t = Resources.Load<Texture2D>("Art/i/" + id);
        if (t == null) { noIcon.Add(id); return null; }
        icons[id] = t;
        return t;
    }

    /// <summary>Нарисовать значок. Нет картинки - ничего (false).</summary>
    public static bool Icon(Rect r, string id, float alpha = 1f)
    {
        Texture2D t = IconTex(id);
        if (t == null) return false;
        if (Event.current.type != EventType.Repaint) return true;
        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        GUI.DrawTexture(r, t, ScaleMode.ScaleToFit);
        GUI.color = old;
        return true;
    }

    // ---------- Плашки ----------

    static GUIStyle card, pill, glow;

    static void Ensure()
    {
        if (card != null && card.normal.background != null) return;
        card = new GUIStyle
        {
            normal = { background = UiKit.Shaded(new Color(0.16f, 0.15f, 0.15f, 0.97f), new Color(0.07f, 0.07f, 0.08f, 0.97f), new Color(1f, 1f, 1f, 1f), 9, 40, 0.5f) },
            border = new RectOffset(12, 12, 12, 12),
        };
        pill = new GUIStyle
        {
            normal = { background = UiKit.Shaded(new Color(0.20f, 0.19f, 0.18f, 0.96f), new Color(0.09f, 0.09f, 0.10f, 0.96f), new Color(1f, 1f, 1f, 1f), 11, 32, 0.7f) },
            border = new RectOffset(13, 13, 13, 13),
        };
        // Мягкое свечение (рамка тает наружу)
        const int N = 48;
        var gt = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color[N * N];
        float half = N * 0.5f, inner = half - 16f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - inner, qy = Mathf.Abs(y + 0.5f - half) - inner;
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float a = Mathf.Clamp01(1f - d / 16f);
                px[y * N + x] = new Color(1f, 1f, 1f, a * a);
            }
        gt.SetPixels(px);
        gt.Apply();
        glow = new GUIStyle { normal = { background = gt }, border = new RectOffset(20, 20, 20, 20) };
    }

    /// <summary>Карточка: тёмная плашка, рамка цвета accent, полоса слева, при glow &gt; 0 - свечение вокруг.</summary>
    public static void Card(Rect r, Color accent, float alpha = 1f, float glowK = 0f, bool stripe = true)
    {
        if (Event.current.type != EventType.Repaint) return;
        Ensure();
        Color old = GUI.color;
        if (glowK > 0f)
        {
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.55f * glowK * alpha);
            glow.Draw(new Rect(r.x - 14f, r.y - 14f, r.width + 28f, r.height + 28f), GUIContent.none, false, false, false, false);
        }
        // Тень
        GUI.color = new Color(0f, 0f, 0f, 0.35f * alpha);
        card.Draw(new Rect(r.x + 1f, r.y + 3f, r.width, r.height), GUIContent.none, false, false, false, false);
        // Рамка тонируется: белая рамка текстуры × цвет
        GUI.color = new Color(Mathf.Lerp(1f, accent.r, 0.75f), Mathf.Lerp(1f, accent.g, 0.75f), Mathf.Lerp(1f, accent.b, 0.75f), alpha);
        card.Draw(r, GUIContent.none, false, false, false, false);
        if (stripe)
        {
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.95f * alpha);
            GUI.DrawTexture(new Rect(r.x + 3f, r.y + 6f, 3f, r.height - 12f), Texture2D.whiteTexture);
        }
        // Мягкий отсвет цвета сверху
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.10f * alpha);
        GUI.DrawTexture(new Rect(r.x + 4f, r.y + 3f, r.width - 8f, r.height * 0.45f), Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>Скруглённая «таблетка» с рамкой цвета accent.</summary>
    public static void Pill(Rect r, Color accent, float alpha = 1f)
    {
        if (Event.current.type != EventType.Repaint) return;
        Ensure();
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.35f * alpha);
        pill.Draw(new Rect(r.x + 1f, r.y + 2f, r.width, r.height), GUIContent.none, false, false, false, false);
        GUI.color = new Color(Mathf.Lerp(1f, accent.r, 0.8f), Mathf.Lerp(1f, accent.g, 0.8f), Mathf.Lerp(1f, accent.b, 0.8f), alpha);
        pill.Draw(r, GUIContent.none, false, false, false, false);
        GUI.color = old;
    }

    /// <summary>Таблетка-кнопка со значком: щит города, таймер и т.п. true - нажали.</summary>
    public static bool IconChip(Rect r, string icon, string text, Color accent, string tip = null, bool on = false)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Pill(r, on ? UiKit.Accent : accent, hover ? 1f : 0.92f);
        float s = Mathf.Min(r.height - 4f, 30f);
        bool has = Icon(new Rect(r.x + 5f, r.y + (r.height - s) * 0.5f, s, s), icon);
        float lx = has ? s + 9f : 10f;
        var st = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip, fontStyle = FontStyle.Bold };
        LocGUI.Label(new Rect(r.x + lx, r.y, r.width - lx - 8f, r.height), text, st);
        if (!string.IsNullOrEmpty(tip)) UiKit.Tip(r, tip);
        bool click = GUI.Button(r, GUIContent.none, GUIStyle.none);
        if (click) Sfx.Ui("ui_click", 0.6f);
        return click;
    }

    /// <summary>
    /// final-13-fix-2: плитка-переключатель со значком (вместо голой галочки с надписью): значок слева,
    /// надпись (можно с цветом), справа - кружок с галочкой. Включена - рамка светится. Возвращает новое состояние.
    /// </summary>
    public static bool OptionTile(Rect r, string icon, string label, bool on, string tip = null, bool enabled = true)
    {
        bool hover = enabled && r.Contains(Event.current.mousePosition);
        Color accent = !enabled ? UiKit.Muted : on ? UiKit.Good : UiKit.Muted;
        Card(r, accent, enabled ? (hover ? 1f : 0.93f) : 0.6f, on ? 0.35f : 0f, on);
        float s = Mathf.Min(r.height - 8f, 28f);
        Icon(new Rect(r.x + 9f, r.y + (r.height - s) * 0.5f, s, s), icon, enabled ? 1f : 0.45f);
        float bs = Mathf.Min(r.height - 12f, 20f);
        Rect box = new Rect(r.xMax - bs - 8f, r.y + (r.height - bs) * 0.5f, bs, bs);
        Pill(box, on ? UiKit.Good : UiKit.Muted, enabled ? 1f : 0.5f);
        if (on) Icon(new Rect(box.x + 1f, box.y + 1f, box.width - 2f, box.height - 2f), "ui_check");
        var st = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleLeft, wordWrap = true, clipping = TextClipping.Clip, richText = true, fontSize = 12 };
        if (!enabled) st.normal.textColor = UiKit.Muted;
        float lx = 9f + s + 7f;
        LocGUI.Label(new Rect(r.x + lx, r.y, box.x - r.x - lx - 4f, r.height), label, st);
        if (!string.IsNullOrEmpty(tip)) UiKit.Tip(r, tip);
        if (enabled && GUI.Button(r, GUIContent.none, GUIStyle.none)) { Sfx.Ui("ui_click", 0.6f); return !on; }
        return on;
    }

    static readonly Dictionary<GUIStyle, GUIStyle> checkLabels = new Dictionary<GUIStyle, GUIStyle>();

    /// <summary>
    /// final-13-fix-2: галочка-переключатель: скруглённый квадратик (вкл. - зелёный с галочкой), справа текст стиля st.
    /// Вместо стандартного кружка Unity - по ней проходят все LocGUI.Toggle со стилем toggle.
    /// </summary>
    public static bool Check(Rect r, bool on, string text, GUIStyle st)
    {
        bool en = GUI.enabled;
        bool hover = en && r.Contains(Event.current.mousePosition);
        float bs = Mathf.Clamp(r.height - 4f, 14f, 20f);
        Rect box = new Rect(r.x + 1f, r.y + (r.height - bs) * 0.5f, bs, bs);
        Pill(box, on ? UiKit.Good : (hover ? UiKit.Accent : UiKit.Muted), en ? 1f : 0.5f);
        if (on) Icon(new Rect(box.x + 1f, box.y + 1f, box.width - 2f, box.height - 2f), "ui_check", en ? 1f : 0.5f);
        if (!string.IsNullOrEmpty(text))
        {
            if (!checkLabels.TryGetValue(st, out var ls) || ls == null)
            {
                if (checkLabels.Count > 64) checkLabels.Clear();
                ls = new GUIStyle(st) { padding = new RectOffset(0, 0, st.padding.top, st.padding.bottom), overflow = new RectOffset(), richText = true, imagePosition = ImagePosition.TextOnly };
                ls.normal.background = ls.hover.background = ls.active.background = ls.onNormal.background = ls.onHover.background = ls.onActive.background = ls.focused.background = ls.onFocused.background = null;
                ls.onNormal.textColor = ls.normal.textColor;
                checkLabels[st] = ls;
            }
            Color old = GUI.color;
            if (!en) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.6f);
            GUI.Label(new Rect(box.xMax + 4f, r.y, r.width - bs - 5f, r.height), text, ls);
            GUI.color = old;
        }
        if (en && GUI.Button(r, GUIContent.none, GUIStyle.none)) { Sfx.Ui("ui_click", 0.5f); return !on; }
        return on;
    }

    // ---------- final-13-fix-3: карточки для окон (клан, лиги, война) ----------

    static GUIStyle capSt, bigSt, subSt, rowTitle, rowText;

    static void EnsureText()
    {
        if (capSt != null) return;
        capSt = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft, richText = true, fontSize = 11 };
        capSt.normal.textColor = new Color(0.80f, 0.78f, 0.72f);
        bigSt = new GUIStyle(UiKit.Body) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, richText = true };
        bigSt.normal.textColor = new Color(1f, 0.92f, 0.70f);
        subSt = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.LowerLeft, richText = true };
        rowTitle = new GUIStyle(UiKit.Body) { wordWrap = true, richText = true, fontStyle = FontStyle.Bold };
        rowText = new GUIStyle(UiKit.Small) { wordWrap = true, richText = true };
        rowText.normal.textColor = new Color(0.82f, 0.81f, 0.78f);
    }

    /// <summary>Карточка с числом: значок слева, сверху подпись, крупно значение, снизу (можно пусто) пояснение.</summary>
    public static void StatCard(Rect r, string icon, string caption, string value, string sub, Color accent, string tip = null)
    {
        EnsureText();
        Card(r, accent, 1f, 0f, true);
        float s = Mathf.Min(r.height - 14f, 34f);
        Icon(new Rect(r.x + 12f, r.y + (r.height - s) * 0.5f, s, s), icon);
        float lx = r.x + 12f + s + 10f, lw = r.xMax - lx - 8f;
        LocGUI.Label(new Rect(lx, r.y + 5f, lw, 16f), caption, capSt);
        bool hasSub = !string.IsNullOrEmpty(sub);
        LocGUI.Label(new Rect(lx, r.y + (hasSub ? 17f : 20f), lw, 26f), value, bigSt);
        if (hasSub) LocGUI.Label(new Rect(lx, r.yMax - 21f, lw, 18f), sub, subSt);
        if (!string.IsNullOrEmpty(tip)) UiKit.Tip(r, tip);
    }

    /// <summary>Высота строки-правила InfoRow при ширине w.</summary>
    public static float InfoRowH(string title, string text, float w)
    {
        EnsureText();
        float tw = w - 44f;
        float h = string.IsNullOrEmpty(title) ? 0f : rowTitle.CalcHeight(new GUIContent(Loc.T(title)), tw);
        if (!string.IsNullOrEmpty(text)) h += rowText.CalcHeight(new GUIContent(Loc.T(text)), tw);
        return Mathf.Max(34f, h + 10f);
    }

    /// <summary>Строка-правило: круглый значок слева, жирный заголовок и пояснение. Возвращает нижний край.</summary>
    public static float InfoRow(float x, float y, float w, string icon, string title, string text, Color accent, bool card = false)
    {
        EnsureText();
        float h = InfoRowH(title, text, w);
        Rect r = new Rect(x, y, w, h);
        if (card) Card(r, accent, 0.9f, 0f, false);
        Rect ic = new Rect(x + 6f, y + 6f, 26f, 26f);
        Pill(ic, accent, 0.9f);
        Icon(new Rect(ic.x + 3f, ic.y + 3f, 20f, 20f), icon);
        float tx = x + 40f, tw = w - 44f, ty = y + 5f;
        if (!string.IsNullOrEmpty(title))
        {
            float th = rowTitle.CalcHeight(new GUIContent(Loc.T(title)), tw);
            LocGUI.Label(new Rect(tx, ty, tw, th), title, rowTitle);
            ty += th;
        }
        if (!string.IsNullOrEmpty(text))
        {
            float th = rowText.CalcHeight(new GUIContent(Loc.T(text)), tw);
            LocGUI.Label(new Rect(tx, ty, tw, th), text, rowText);
        }
        return y + h;
    }

    /// <summary>Значок лиги: таблетка цвета лиги с короной и названием.</summary>
    public static void LeagueBadge(Rect r, string label, Color c)
    {
        Pill(r, c, 1f);
        float s = r.height - 6f;
        Icon(new Rect(r.x + 5f, r.y + 3f, s, s), "ui_crown");
        var st = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip, richText = true, fontStyle = FontStyle.Bold };
        LocGUI.Label(new Rect(r.x + s + 9f, r.y, r.width - s - 12f, r.height), label, st);
    }

    /// <summary>Строка «значок + текст» (запасы похода, цены и т.п.).</summary>
    public static void IconLine(Rect r, string icon, string text, GUIStyle st)
    {
        float s = Mathf.Min(r.height, 20f);
        bool has = Icon(new Rect(r.x, r.y + (r.height - s) * 0.5f, s, s), icon);
        float lx = has ? s + 5f : 0f;
        LocGUI.Label(new Rect(r.x + lx, r.y, r.width - lx, r.height), text, st);
    }

    public static string Clock(double seconds)
    {
        int t = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
        if (t >= 3600) return $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}";
        return $"{t / 60}:{t % 60:00}";
    }

    // ---------- Уведомления ----------

    public enum Tone { Good, Bad, Info }

    /// <summary>Какой значок поставить у сообщения: по смыслу текста.</summary>
    public static string IconFor(string text, bool good)
    {
        string s = (text ?? "").ToLowerInvariant();
        bool Has(string k) => s.Contains(k);
        if (Has("щит")) return "ui_cityshield";
        if (Has("погиб") || Has("пал ") || Has("никто не вернулся") || Has("разрушен")) return "ui_skull";
        if (Has("набег") || Has("напал") || Has("враг") || Has("грабит") || Has("атак") || Has("бой") || Has("тревог") || Has("штурм")) return "ui_swords";
        if (Has("лиг") || Has("рейтинг") || Has("побед") || Has("награда")) return "ui_crown";
        if (Has("построен") || Has("улучшен") || Has("стройк") || Has("отстро") || Has("починен")) return "ui_hammer";
        if (Has("марк")) return "marks";
        if (Has("армия") || Has("отряд") || Has("боец") || Has("бойц") || Has("поход") || Has("казарм")) return "ui_helmet";
        if (Has("золот") || Has("продано") || Has("выручк") || Has("куплен")) return "gold";
        if (Has("сервер") || Has("обновлен") || Has("сохранен") || Has("связ")) return "ui_info";
        if (Has("клан") || Has("участок") || Has("флаг")) return "ui_flag";
        return good ? "ui_check" : "ui_bell";
    }

    public static Color ToneColor(bool good, string icon)
    {
        if (icon == "ui_swords" || icon == "ui_skull") return new Color(1f, 0.38f, 0.30f);
        if (icon == "ui_cityshield" || icon == "ui_info") return UiKit.Info;
        if (icon == "ui_crown" || icon == "gold" || icon == "marks") return UiKit.Accent;
        return good ? UiKit.Good : UiKit.Warn;
    }

    /// <summary>
    /// Всплывающее сообщение по центру сверху: карточка со значком. born / until - когда появилось и когда исчезнет.
    /// more - сколько ещё сообщений пришло следом (кружок «+N»). Возвращает нижний край.
    /// </summary>
    public static float Toast(float centerX, float y, float maxW, string text, bool good, float born, float until, int more, bool compact = false)
    {
        float now = Time.time;
        float aIn = Mathf.Clamp01((now - born) / 0.22f);
        float aOut = Mathf.Clamp01((until - now) / 0.6f);
        float a = Mathf.Min(aIn, aOut);
        if (a <= 0f) return y;
        float ease = 1f - (1f - aIn) * (1f - aIn);
        float slide = (1f - ease) * -14f;

        string icon = IconFor(text, good);
        Color accent = ToneColor(good, icon);
        // final-13-fix-3: compact - для обычных вестей: мельче шрифт и значок, ниже карточка; крупно - только самое важное
        GUIStyle st = new GUIStyle(UiKit.Body) { fontSize = compact ? 12 : 14, wordWrap = true, alignment = TextAnchor.MiddleLeft, fontStyle = compact ? FontStyle.Normal : FontStyle.Bold };
        st.normal.textColor = new Color(0.96f, 0.94f, 0.90f);
        string t = Loc.T(text);
        GUIContent gc = new GUIContent(t);
        float iconS = compact ? 22f : 32f, padL = compact ? 9f : 12f, padR = compact ? 9f : 12f;
        float badgeW = more > 0 ? 30f : 0f;
        float tw = Mathf.Min(st.CalcSize(gc).x + 2f, maxW - iconS - padL - padR - 8f - badgeW);
        float th = Mathf.Max(18f, st.CalcHeight(gc, tw));
        float w = iconS + padL + 8f + tw + padR + badgeW, h = Mathf.Max(compact ? 32f : 48f, th + (compact ? 10f : 16f));
        Rect r = new Rect(centerX - w / 2f, y + slide, w, h);

        Color old = GUI.color;
        Card(r, accent, compact ? a * 0.95f : a, compact ? 0f : (good ? 0.15f : 0.45f));
        Icon(new Rect(r.x + padL, r.y + (h - iconS) * 0.5f, iconS, iconS), icon, a);
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.Label(new Rect(r.x + padL + iconS + 8f, r.y + (h - th) * 0.5f, tw, th), gc, st);
        if (more > 0)
        {
            Rect b = new Rect(r.xMax - padR - 24f, r.y + (h - 22f) * 0.5f, 24f, 22f);
            Pill(b, UiKit.Muted, a);
            var bs = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontStyle = FontStyle.Bold };
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(b, "+" + more, bs);
            UiKit.Tip(b, Loc.T("Ещё сообщения — в журнале (⚙ → Журнал)"));
        }
        GUI.color = old;
        return r.yMax;
    }

    /// <summary>
    /// final-13-fix-2: маленькая плашка для будничных вестей (склад, кухня, производство): одна-две строки мелким шрифтом,
    /// маленький значок, полупрозрачная. Возвращает нижний край.
    /// </summary>
    public static float MiniToast(float centerX, float y, float maxW, string text, bool good, float born, float until)
    {
        float now = Time.time;
        float a = Mathf.Min(Mathf.Clamp01((now - born) / 0.18f), Mathf.Clamp01((until - now) / 0.5f)) * 0.92f;
        if (a <= 0f) return y;
        string icon = IconFor(text, good);
        Color accent = ToneColor(good, icon);
        if (miniStyle == null)
        {
            miniStyle = new GUIStyle(UiKit.Small) { fontSize = 11, wordWrap = true, alignment = TextAnchor.MiddleLeft, richText = true };
            miniStyle.normal.textColor = new Color(0.92f, 0.90f, 0.85f);
        }
        GUIContent gc = new GUIContent(Loc.T(text));
        const float iconS = 16f, pad = 7f;
        float tw = Mathf.Min(miniStyle.CalcSize(gc).x + 2f, maxW - iconS - pad * 3f);
        float th = Mathf.Min(Mathf.Max(14f, miniStyle.CalcHeight(gc, tw)), 30f);   // не больше двух строк
        float w = iconS + pad * 3f + tw, h = Mathf.Max(24f, th + 8f);
        Rect r = new Rect(centerX - w / 2f, y, w, h);
        Color old = GUI.color;
        Card(r, accent, a * 0.85f, 0f, false);
        Icon(new Rect(r.x + pad, r.y + (h - iconS) * 0.5f, iconS, iconS), icon, a);
        GUI.color = new Color(1f, 1f, 1f, a);
        miniStyle.clipping = TextClipping.Clip;
        GUI.Label(new Rect(r.x + pad * 2f + iconS, r.y + (h - th) * 0.5f, tw, th), gc, miniStyle);
        GUI.color = old;
        return r.yMax;
    }

    static GUIStyle miniStyle;

    // ---------- Общий таймер набега ----------

    /// <summary>
    /// Таймер набега по центру сверху (у нападающего и у защитника одинаковый): песочные часы, «Набег · 8:42»,
    /// подпись снизу. Последние 2 минуты - красный и пульсирует. Возвращает прямоугольник.
    /// </summary>
    public static Rect RaidTimer(float centerX, float y, float secondsLeft, string caption, string sub)
    {
        bool hurry = secondsLeft < 120f;
        float pulse = hurry ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
        Color accent = hurry ? Color.Lerp(new Color(1f, 0.45f, 0.3f), new Color(1f, 0.25f, 0.2f), pulse) : UiKit.Accent;
        float w = 210f, h = string.IsNullOrEmpty(sub) ? 40f : 54f;
        Rect r = new Rect(centerX - w / 2f, y, w, h);
        Card(r, accent, 1f, hurry ? 0.4f + 0.4f * pulse : 0.15f, false);
        Icon(new Rect(r.x + 10f, r.y + 6f, 28f, 28f), "ui_hourglass");
        var cap = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
        cap.normal.textColor = new Color(0.85f, 0.82f, 0.74f);
        LocGUI.Label(new Rect(r.x + 44f, r.y + 4f, w - 110f, 32f), caption, cap);
        var big = new GUIStyle(UiKit.Body) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
        big.normal.textColor = hurry ? new Color(1f, 0.55f, 0.45f) : new Color(1f, 0.88f, 0.55f);
        GUI.Label(new Rect(r.x + 40f, r.y + 3f, w - 52f, 34f), Clock(secondsLeft), big);
        if (!string.IsNullOrEmpty(sub))
        {
            var ss = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, fontSize = 11 };
            ss.normal.textColor = UiKit.Muted;
            LocGUI.Label(new Rect(r.x + 8f, r.y + 34f, w - 16f, 18f), sub, ss);
        }
        return r;
    }
}
