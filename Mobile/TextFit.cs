using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// На телефоне надпись, которая не влезает в своё место (цена из пяти ресурсов, длинное название на кнопке),
/// рисуется чуть мельче - ровно настолько, чтобы влезла (не мельче 65% обычного). Раньше хвост просто обрезался
/// или уезжал за край окна. LocGUI.Label / Button зовут это сами; на компьютере ничего не меняется.
/// </summary>
public static class TextFit
{
    static readonly GUIContent temp = new GUIContent();

    /// <summary>Общий GUIContent для одной надписи (как GUIContent.Temp у Unity) - без мусора в памяти.</summary>
    public static GUIContent Temp(string text)
    {
        temp.text = text;
        temp.image = null;
        temp.tooltip = null;
        return temp;
    }

    struct Key
    {
        public string text; public GUIStyle style; public int w, h;
        public override int GetHashCode() { return (text.GetHashCode() * 31 + style.GetHashCode()) * 31 + w * 7 + h; }
        public override bool Equals(object o) { var k = (Key)o; return k.w == w && k.h == h && ReferenceEquals(k.style, style) && k.text == text; }
    }

    // Размер шрифта (0 - влезает как есть); отрицательный - то же, но со словами «зол», «дер», «кам» вместо полных
    static readonly Dictionary<Key, int> sizeFor = new Dictionary<Key, int>();
    static readonly Dictionary<GUIStyle, Dictionary<int, GUIStyle>> smaller = new Dictionary<GUIStyle, Dictionary<int, GUIStyle>>();

    static readonly GUIContent shortC = new GUIContent();

    /// <summary>Стиль, в котором надпись влезет в r. Если для этого пришлось сократить «золота» до «зол», c подменяется.</summary>
    public static GUIStyle Style(Rect r, ref GUIContent c, GUIStyle st)
    {
        if (st == null || c == null || string.IsNullOrEmpty(c.text) || c.image != null) return st;
        if (!MobileText.On || r.width < 12f || r.height < 8f) return st;

        var key = new Key { text = c.text, style = st, w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height) };
        if (!sizeFor.TryGetValue(key, out int size))
        {
            size = Pick(r, c, st);
            if (sizeFor.Count > 5000) sizeFor.Clear();   // в надписях бывают числа - кэш не должен расти без конца
            sizeFor[key] = size;
        }
        if (size == 0) return st;
        if (size < 0)
        {
            shortC.text = Short(c.text); shortC.tooltip = c.tooltip; shortC.image = null;
            c = shortC;
            size = -size;
        }
        return size == BaseSize(st) ? st : Sized(st, size);
    }

    // Цены: «120 золота · 80 дерева» → «120 зол · 80 дер»
    static readonly System.Text.RegularExpressions.Regex ShortRx = new System.Text.RegularExpressions.Regex(
        @"(\d[\d\s]*)\s*(золота|дерева|камня|железа|досок|доски|овса|ягод|трав|смолы|стали)(?![\p{L}])",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly Dictionary<string, string> ShortWord = new Dictionary<string, string>
    {
        { "золота", "зол" }, { "дерева", "дер" }, { "камня", "кам" }, { "железа", "жел" }, { "досок", "дос" }, { "доски", "дос" },
        { "овса", "овс" }, { "ягод", "яг" }, { "трав", "трав" }, { "смолы", "смол" }, { "стали", "ст" },
    };

    static readonly Dictionary<string, string> shortCache = new Dictionary<string, string>();

    static string Short(string s)
    {
        if (shortCache.TryGetValue(s, out string r)) return r;
        r = ShortRx.Replace(s, m => m.Groups[1].Value.TrimEnd() + " " + ShortWord[m.Groups[2].Value]);
        if (shortCache.Count > 3000) shortCache.Clear();
        shortCache[s] = r;
        return r;
    }

    static int BaseSize(GUIStyle st) { return st.fontSize > 0 ? st.fontSize : 13; }

    static bool Fits(Rect r, GUIContent c, GUIStyle s)
    {
        if (s.wordWrap) return s.CalcHeight(c, r.width) <= r.height + 2f;
        return s.CalcSize(c).x <= r.width + 1f;
    }

    static int Pick(Rect r, GUIContent c, GUIStyle st)
    {
        if (Fits(r, c, st)) return 0;
        int b = BaseSize(st);
        int min = Mathf.Max(8, Mathf.RoundToInt(b * 0.65f));
        string sh = Short(c.text);
        GUIContent sc = sh != c.text ? new GUIContent(sh) : null;
        // Сначала - сокращённые слова в том же размере, потом мельче (сокращённые - раньше полных)
        for (int size = b; size >= min; size--)
        {
            GUIStyle s = size == b ? st : Sized(st, size);
            if (sc != null && Fits(r, sc, s)) return -size;
            if (size < b && Fits(r, c, s)) return size;
        }
        return sc != null ? -min : min;
    }

    static GUIStyle Sized(GUIStyle st, int size)
    {
        if (!smaller.TryGetValue(st, out var bySize)) { bySize = new Dictionary<int, GUIStyle>(); smaller[st] = bySize; }
        if (!bySize.TryGetValue(size, out GUIStyle s)) { s = new GUIStyle(st) { fontSize = size }; bySize[size] = s; }
        // Стиль могли поменять после того, как мы его скопировали (цвет текста меняют на ходу)
        s.normal.textColor = st.normal.textColor; s.hover.textColor = st.hover.textColor; s.active.textColor = st.active.textColor;
        s.alignment = st.alignment; s.wordWrap = st.wordWrap; s.richText = st.richText; s.fontStyle = st.fontStyle; s.clipping = st.clipping;
        s.font = st.font;
        return s;
    }
}
