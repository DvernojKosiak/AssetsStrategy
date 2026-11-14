using UnityEngine;

/// <summary>
/// Поле ввода, удобное на телефоне (обновление final-1).
/// Обычное поле IMGUI на телефоне плохо: клавиатура закрывает саму строку, а поставить курсор в середину слова почти
/// невозможно. Здесь на телефоне поле - кнопка: нажали - открылась системная клавиатура, а сверху экрана, над всем,
/// крупная строка с тем, что набрано. В строке курсор ставится нажатием в нужное место, рядом кнопки ◀ ▶ (на одну букву),
/// «Готово» закрывает клавиатуру. «Готово» на самой клавиатуре - то же самое (чат при этом сразу отправляет сообщение).
/// На компьютере - обычное поле GUI.TextField, как раньше.
/// </summary>
public static class MobileInput
{
    /// <summary>Своё поле нужно только на настоящем телефоне: в редакторе системной клавиатуры нет.</summary>
    public static bool On => Application.isMobilePlatform && TouchScreenKeyboard.isSupported;

    static TouchScreenKeyboard kb;
    static string activeId;
    static string text = "";
    static int caret;
    static int maxLen;
    static bool secure;
    static string hint;
    static string submittedId;
    static int openedFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { kb = null; activeId = null; text = ""; caret = 0; submittedId = null; }

    /// <summary>Открыта клавиатура: нажатия по карте не считаются (BuildingPlacer).</summary>
    public static bool Busy => kb != null && activeId != null;

    /// <summary>Сейчас набирают текст в этом поле.</summary>
    public static bool Editing(string id) { return activeId == id && kb != null; }

    /// <summary>В этом поле нажали «Готово» (один раз: потом false).</summary>
    public static bool Submitted(string id)
    {
        if (submittedId != id) return false;
        submittedId = null;
        return true;
    }

    /// <summary>Поле ввода. id - имя поля (оно же имя контрола IMGUI на компьютере).</summary>
    public static string Field(Rect r, string id, string value, int max, string placeholder = null,
                               bool password = false, TouchScreenKeyboardType type = TouchScreenKeyboardType.Default)
    {
        value = value ?? "";
        if (!On)
        {
            GUI.SetNextControlName(id);
            return password ? GUI.PasswordField(r, value, '•', max) : GUI.TextField(r, value, max);
        }

        if (activeId == id)
        {
            Poll();
            value = text;
            secure = password;   // «глазик» пароля переключили, пока клавиатура открыта
        }

        // Поле - кнопка в виде строки ввода
        GUIStyle fs = GUI.skin.textField;
        if (Event.current.type == EventType.Repaint) fs.Draw(r, GUIContent.none, false, false, false, activeId == id);
        string shown = password ? new string('•', value.Length) : value;
        var ls = new GUIStyle(fs) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
        if (shown == "" && !string.IsNullOrEmpty(placeholder))
            GUI.Label(r, UiKit.Col(UiKit.MutedHex, Loc.T(placeholder)), new GUIStyle(ls) { richText = true });
        else
        {
            // Длинный текст: видно конец (там, где пишут)
            Vector2 size = ls.CalcSize(new GUIContent(shown));
            GUI.BeginGroup(r);
            float inner = r.width - ls.padding.horizontal;
            float shift = size.x > inner ? size.x - inner : 0f;
            GUI.Label(new Rect(-shift, 0f, Mathf.Max(r.width, size.x + ls.padding.horizontal), r.height), shown, ls);
            GUI.EndGroup();
        }
        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Open(id, value, max, password, placeholder, type);
        return value;
    }

    static void Open(string id, string value, int max, bool password, string placeholder, TouchScreenKeyboardType type)
    {
        Close(false);
        activeId = id;
        text = value ?? "";
        caret = text.Length;
        maxLen = max;
        secure = password;
        hint = placeholder;
        openedFrame = Time.frameCount;
        TouchScreenKeyboard.hideInput = false;   // системная строка над клавиатурой тоже видна (где её показывает телефон)
        kb = TouchScreenKeyboard.Open(text, type, !password && type == TouchScreenKeyboardType.Default, false, password, false,
                                      placeholder != null ? Loc.T(placeholder) : "", max);
        try { if (kb != null) kb.selection = new RangeInt(caret, 0); } catch (System.Exception) { }
        MobileInputOverlay.Ensure();
    }

    static void Close(bool submit)
    {
        if (kb != null)
        {
            try { kb.active = false; } catch (System.Exception) { }
        }
        if (submit && activeId != null) submittedId = activeId;
        kb = null;
        activeId = null;
    }

    /// <summary>Свежий текст и курсор с клавиатуры; клавиатуру закрыли - поле больше не редактируется.</summary>
    internal static void Poll()
    {
        if (kb == null || activeId == null) return;
        string t = kb.text ?? "";
        if (maxLen > 0 && t.Length > maxLen) t = t.Substring(0, maxLen);
        text = t;
        try { caret = Mathf.Clamp(kb.selection.start, 0, text.Length); } catch (System.Exception) { caret = text.Length; }
        if (Time.frameCount - openedFrame < 2) return;   // клавиатура ещё открывается
        switch (kb.status)
        {
            case TouchScreenKeyboard.Status.Done: Close(true); break;
            case TouchScreenKeyboard.Status.Canceled:
            case TouchScreenKeyboard.Status.LostFocus: Close(false); break;
        }
    }

    static void SetCaret(int i)
    {
        caret = Mathf.Clamp(i, 0, text.Length);
        try { if (kb != null) kb.selection = new RangeInt(caret, 0); } catch (System.Exception) { }
    }

    static GUIStyle bigStyle;

    /// <summary>Крупная строка сверху экрана, пока открыта клавиатура (рисует MobileInputOverlay поверх всего).</summary>
    internal static void DrawOverlay()
    {
        if (kb == null || activeId == null) return;
        Poll();
        if (kb == null) return;

        if (bigStyle == null)
            bigStyle = new GUIStyle(UiKit.Body) { fontSize = Mathf.Max(18, UiKit.Body.fontSize + 4), alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow, richText = false };

        float w = UiKit.W - 16f;
        Rect panel = new Rect(8f, 8f, w, 92f);
        Color old = GUI.color;
        if (Event.current.type == EventType.Repaint)
        {
            GUI.color = new Color(0.07f, 0.06f, 0.05f, 0.96f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.82f, 0.35f, 0.9f);
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - 2f, panel.width, 2f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Строка текста: нажатие ставит курсор
        float btnW = 52f;
        Rect line = new Rect(panel.x + 10f, panel.y + 8f, w - 20f, 42f);
        string shown = secure ? new string('•', text.Length) : text;
        var content = new GUIContent(shown);
        float textW = bigStyle.CalcSize(content).x;
        Rect local = new Rect(0f, 0f, Mathf.Max(line.width, textW + 20f), line.height);
        float caretX = bigStyle.GetCursorPixelPosition(local, content, caret).x;
        float shift = Mathf.Clamp(caretX - line.width + 24f, 0f, Mathf.Max(0f, textW + 20f - line.width));
        local.x = -shift;

        if (Event.current.type == EventType.Repaint)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.07f);
            GUI.DrawTexture(line, Texture2D.whiteTexture);
            GUI.color = old;
        }
        GUI.BeginGroup(line);
        if (shown == "" && !string.IsNullOrEmpty(hint))
            GUI.Label(local, UiKit.Col(UiKit.MutedHex, Loc.T(hint)), new GUIStyle(bigStyle) { richText = true });
        else GUI.Label(local, shown, bigStyle);
        // Мигающий курсор
        if (Event.current.type == EventType.Repaint && ((int)(Time.unscaledTime * 2f) % 2 == 0))
        {
            GUI.color = new Color(1f, 0.85f, 0.4f);
            GUI.DrawTexture(new Rect(caretX - shift, 6f, 2f, line.height - 12f), Texture2D.whiteTexture);
            GUI.color = old;
        }
        Event e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp) && new Rect(0f, 0f, line.width, line.height).Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseUp)
                SetCaret(bigStyle.GetCursorStringIndex(local, content, e.mousePosition));
            e.Use();
        }
        GUI.EndGroup();

        // Кнопки: курсор на букву влево / вправо, в начало / в конец, «Готово»
        float by = panel.y + 54f, bh = 32f;
        float bx = panel.x + 10f;
        if (UiKit.Button(new Rect(bx, by, btnW, bh), "|◀", UiKit.Kind.Ghost, null)) SetCaret(0);
        bx += btnW + 6f;
        if (UiKit.Button(new Rect(bx, by, btnW, bh), "◀", UiKit.Kind.Normal, null)) SetCaret(caret - 1);
        bx += btnW + 6f;
        if (UiKit.Button(new Rect(bx, by, btnW, bh), "▶", UiKit.Kind.Normal, null)) SetCaret(caret + 1);
        bx += btnW + 6f;
        if (UiKit.Button(new Rect(bx, by, btnW, bh), "▶|", UiKit.Kind.Ghost, null)) SetCaret(text.Length);
        float doneW = 120f;
        if (UiKit.Button(new Rect(panel.xMax - doneW - 10f, by, doneW, bh), "Готово", UiKit.Kind.Primary, null)) Close(true);
        // Стереть букву перед курсором: удерживать - стирает дальше, всё быстрее (как на клавиатуре)
        Rect del = new Rect(panel.xMax - doneW - 10f - 6f - 64f, by, 64f, bh);
        DeleteButton(del);
        string count = maxLen > 0 ? $"{text.Length}/{maxLen}" : "";
        if (count != "") LocGUI.Label(new Rect(bx + btnW + 8f, by, Mathf.Max(0f, del.x - 8f - (bx + btnW + 8f)), bh),
                                      UiKit.Col(UiKit.MutedHex, count), new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleLeft });

        // Нажатия по строке не должны уходить в игру под ней
        if (panel.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) e.Use();
    }

    // ---------- «⌫» с повтором при удержании ----------

    static bool delHeld;
    static float delNext, delStart;

    static void DeleteButton(Rect r)
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
        {
            delHeld = true;
            delStart = Time.unscaledTime;
            delNext = delStart + 0.45f;   // первая буква - сразу, повтор - после паузы
            DeleteBack();
            e.Use();
        }
        else if (e.type == EventType.MouseUp && delHeld) { delHeld = false; e.Use(); }
        else if (e.type == EventType.MouseDrag && delHeld && !r.Contains(e.mousePosition)) delHeld = false;   // палец ушёл с кнопки
        if (delHeld && !PointerDown()) delHeld = false;   // отпустили вне окна - не стирать дальше
        if (delHeld && e.type == EventType.Repaint && Time.unscaledTime >= delNext)
        {
            DeleteBack();
            float held = Time.unscaledTime - delStart;
            delNext = Time.unscaledTime + (held > 2f ? 0.03f : held > 1f ? 0.06f : 0.1f);
        }
        UiKit.Button(r, "⌫", delHeld ? UiKit.Kind.Primary : UiKit.Kind.Normal, null);
    }

    static bool PointerDown()
    {
        try
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m != null && m.leftButton.isPressed) return true;
            return TouchInput.FingerDown;
        }
        catch (System.Exception) { return false; }
    }

    static void DeleteBack()
    {
        if (kb == null || caret <= 0 || text.Length == 0) return;
        int at = Mathf.Clamp(caret, 1, text.Length);
        // Суррогатная пара (смайлик) - стираем целиком
        int n = at >= 2 && char.IsLowSurrogate(text[at - 1]) && char.IsHighSurrogate(text[at - 2]) ? 2 : 1;
        text = text.Remove(at - n, n);
        caret = at - n;
        try { kb.text = text; kb.selection = new RangeInt(caret, 0); } catch (System.Exception) { }
    }

    /// <summary>Крупная строка сейчас закрывает этот кусок экрана (в координатах UiKit).</summary>
    public static bool IsPointerOver(Vector2 gp) { return kb != null && activeId != null && new Rect(8f, 8f, UiKit.W - 16f, 92f).Contains(gp); }
}

/// <summary>Рисует крупную строку ввода MobileInput поверх всех окон.</summary>
public class MobileInputOverlay : MonoBehaviour
{
    static MobileInputOverlay inst;

    public static void Ensure()
    {
        if (inst != null) return;
        var go = new GameObject("MobileInputOverlay");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<MobileInputOverlay>();
    }

    void Update() { MobileInput.Poll(); }

    void OnGUI()
    {
        GUI.depth = -3000;   // поверх всех окон игры
        UiKit.Apply();
        MobileInput.DrawOverlay();
    }
}
