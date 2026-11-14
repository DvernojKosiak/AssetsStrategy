using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Языки игры: русский (как написано в коде), английский и литовский.
///
/// Как устроено: весь текст в коде остаётся русским. Перевод делается в момент рисования:
/// LocGUI.Label / Button / Box / Toggle, Loc.Content и окна UiKit пропускают строку через Loc.T.
/// Поэтому названия зданий, ключи сохранений и сообщения сервера не меняются - меняется только то, что видно на экране.
///
/// Словари: Resources/Lang/en.txt и lt.txt, строка «русский текст TAB перевод».
///  - {0}, {1}... - вставки (числа, имена): «Нужна Цитадель Ур.{0}» → «Requires Town Hall Lv.{0}».
///  - Строка режется так же, как при сборке словаря: по тегам <b>/<color>, по переносам строк,
///    знаки препинания по краям не входят в ключ.
///  - Если целой строки в словаре нет, она переводится по кусочкам между цифрами и знаками.
///    Кусок без перевода остаётся как есть (так имена игроков и сообщения чата не портятся).
///
/// Новый текст в коде: пишем по-русски как обычно, потом добавляем строку в en.txt и lt.txt.
/// Чего нет в словаре, показывается по-русски.
/// </summary>
public static class Loc
{
    public enum Lang { RU = 0, EN = 1, LT = 2 }

    public static readonly string[] LangNames = { "Русский", "English", "Lietuvių" };
    public static readonly string[] LangCodes = { "ru", "en", "lt" };

    const string PrefKey = "lang";

    static bool inited;
    static Lang current = Lang.RU;

    public static Lang Current { get { Init(); return current; } }

    /// <summary>Язык сменился (окна, которые что-то кэшируют, могут пересчитаться).</summary>
    public static event Action Changed;

    static Dictionary<string, string> dict = new Dictionary<string, string>();
    static Dictionary<string, string> dictLower = new Dictionary<string, string>();
    static readonly Dictionary<string, List<Tmpl>> templates = new Dictionary<string, List<Tmpl>>();
    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
    static readonly HashSet<string> keep = new HashSet<string>();

    class Tmpl
    {
        public Regex rx;
        public string value;
        public int literal;   // сколько букв вне вставок: чем больше, тем точнее шаблон
        public List<int> holes;   // номера вставок в порядке появления в ключе
    }

    static readonly Regex TagRx = new Regex(@"</?(?:b|i|color|size|material|quad)(?:=[^>]*)?>", RegexOptions.Compiled);
    static readonly Regex HoleRx = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);
    const string StripChars = " \t\r.,:;!?…-–—•·*()[]«»\"'/|+=>#№%~^_";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inited = false; Changed = null; }

    static void Init()
    {
        if (inited) return;
        inited = true;
        // Первый запуск - язык телефона (русский, литовский, остальным английский); дальше - выбранный в настройках
        int saved = PlayerPrefs.HasKey(PrefKey) ? PlayerPrefs.GetInt(PrefKey, 0) : (int)SystemLang();
        current = (Lang)Mathf.Clamp(saved, 0, LangNames.Length - 1);
        Load();
    }

    static Lang SystemLang()
    {
        switch (Application.systemLanguage)
        {
            case SystemLanguage.Russian:
            case SystemLanguage.Belarusian:
            case SystemLanguage.Ukrainian: return Lang.RU;
            case SystemLanguage.Lithuanian: return Lang.LT;
            default: return Lang.EN;
        }
    }

    /// <summary>Выбрать язык (запоминается).</summary>
    public static void Set(Lang lang)
    {
        Init();
        if (lang == current) return;
        current = lang;
        PlayerPrefs.SetInt(PrefKey, (int)lang);
        PlayerPrefs.Save();
        Load();
        if (Changed != null) Changed();
    }

    static void Load()
    {
        dict = new Dictionary<string, string>();
        dictLower = new Dictionary<string, string>();
        templates.Clear();
        cache.Clear();
        if (current == Lang.RU) return;

        TextAsset ta = Resources.Load<TextAsset>("Lang/" + LangCodes[(int)current]);
        if (ta == null) { Debug.LogWarning("Нет словаря Resources/Lang/" + LangCodes[(int)current]); return; }
        Parse(ta.text);
        // Дополнения к словарю: Resources/Lang/en_*.txt (их кладут обновления, чтобы не трогать общий файл)
        string prefix = LangCodes[(int)current] + "_";
        foreach (TextAsset extra in Resources.LoadAll<TextAsset>("Lang"))
            if (extra != null && extra.name.StartsWith(prefix)) Parse(extra.text);
    }

    static void Parse(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            string key = Unescape(line.Substring(0, tab));
            string val = Unescape(line.Substring(tab + 1));
            if (val.Length == 0) continue;
            Add(key, val);
            // Ключ с точкой, «?» или скобкой по краям: на экране эти знаки отрезаются до поиска (Split),
            // поэтому такой ключ раньше не находился никогда. Кладём ещё и ключ без них (обновление 22)
            Split(key, out string kp, out string kc, out string ks);
            if (kc.Length > 0 && kc.Length < key.Length && !dict.ContainsKey(kc))
                Add(kc, TrimEdges(val, kp.Length, ks.Length));
        }
    }

    static void Add(string key, string val)
    {
        dict[key] = val;
        string low = key.ToLowerInvariant();
        if (!dictLower.ContainsKey(low)) dictLower[low] = val;
        if (HoleRx.IsMatch(key)) AddTemplate(key, val);
    }

    // Убрать у перевода столько же знаков по краям, сколько отрезано у ключа (только знаки из StripChars)
    static string TrimEdges(string v, int head, int tail)
    {
        int a = 0, b = v.Length;
        while (head-- > 0 && a < b && StripChars.IndexOf(v[a]) >= 0) a++;
        while (tail-- > 0 && b > a && StripChars.IndexOf(v[b - 1]) >= 0) b--;
        return v.Substring(a, b - a);
    }

    static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
    }

    static void AddTemplate(string key, string val)
    {
        var sb = new StringBuilder("^");
        int last = 0, literal = 0;
        var holes = new List<int>();
        foreach (Match m in HoleRx.Matches(key))
        {
            holes.Add(int.Parse(m.Groups[1].Value));
            string lit = key.Substring(last, m.Index - last);
            literal += lit.Length;
            sb.Append(Regex.Escape(lit));
            sb.Append("(.+?)");
            last = m.Index + m.Length;
        }
        string tail = key.Substring(last);
        literal += tail.Length;
        sb.Append(Regex.Escape(tail)).Append('$');

        // Корзина - самое длинное русское слово шаблона
        string anchor = null;
        foreach (string w in Words(HoleRx.Replace(key, " ")))
            if (anchor == null || w.Length > anchor.Length) anchor = w;
        if (anchor == null) return;

        // В переводе вставки могут стоять в другом порядке - сопоставляем по номеру
        var t = new Tmpl { rx = new Regex(sb.ToString(), RegexOptions.Singleline), value = val, literal = literal, holes = holes };
        if (!templates.TryGetValue(anchor, out List<Tmpl> list)) templates[anchor] = list = new List<Tmpl>();
        list.Add(t);
    }

    static IEnumerable<string> Words(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && !IsCyr(s[i])) i++;
            int a = i;
            while (i < s.Length && (IsCyr(s[i]) || s[i] == '-')) i++;
            if (i - a >= 2) yield return s.Substring(a, i - a).ToLowerInvariant();
        }
    }

    static bool IsCyr(char c) { return (c >= 'А' && c <= 'я') || c == 'Ё' || c == 'ё'; }

    static bool HasCyr(string s)
    {
        for (int i = 0; i < s.Length; i++) if (IsCyr(s[i])) return true;
        return false;
    }

    /// <summary>Не переводить эту строку (сообщения игроков в чате, имена).</summary>
    public static string Keep(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (keep.Count > 4000) keep.Clear();
        keep.Add(s);
        return s;
    }

    /// <summary>Перевод строки интерфейса на выбранный язык (на телефоне - ещё и без подсказок про мышь и клавиши).</summary>
    public static string T(string s)
    {
        string r = TRaw(s);
        if (string.IsNullOrEmpty(r) || !MobileText.On || keep.Contains(s)) return r;
        return MobileText.Fix(r);
    }

    static string TRaw(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        Init();
        if (s.IndexOf("Ратуш", StringComparison.Ordinal) >= 0 && !keep.Contains(s)) s = Hall(s);
        if (current == Lang.RU || !HasCyr(s)) return s;
        if (keep.Contains(s)) return s;
        if (cache.TryGetValue(s, out string r)) return r;

        var sb = new StringBuilder(s.Length + 16);
        int last = 0;
        foreach (Match m in TagRx.Matches(s))
        {
            sb.Append(Chunk(s.Substring(last, m.Index - last)));
            sb.Append(m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(Chunk(s.Substring(last)));
        r = sb.ToString();

        if (cache.Count > 20000) cache.Clear();
        cache[s] = r;
        return r;
    }

    // Главное здание называется Цитадель, но в сохранениях и на сервере его тип по-прежнему "Ратуша"
    // (так старые города и старый сервер работают как раньше). На экране старое имя заменяется здесь.
    static readonly Regex HallRx = new Regex(@"Ратуш(ей|а|и|е|у)(?![а-яё])", RegexOptions.Compiled);

    public static string Hall(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf("Ратуш", StringComparison.Ordinal) < 0) return s;
        return HallRx.Replace(s, m => m.Groups[1].Value == "ей" ? "Цитаделью" : m.Groups[1].Value == "а" || m.Groups[1].Value == "у" ? "Цитадель" : "Цитадели");
    }

    /// <summary>Перевод с подстановкой: Loc.F("Нужно {0} досок", n).</summary>
    public static string F(string format, params object[] args) { return string.Format(T(format), args); }

    static string Chunk(string s)
    {
        if (!HasCyr(s)) return s;
        if (s.IndexOf('\n') < 0) return Line(s, 0);
        string[] lines = s.Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = Line(lines[i], 0);
        return string.Join("\n", lines);
    }

    static void Split(string s, out string pre, out string core, out string suf)
    {
        int a = 0, b = s.Length;
        while (a < b && StripChars.IndexOf(s[a]) >= 0) a++;
        while (b > a && StripChars.IndexOf(s[b - 1]) >= 0) b--;
        pre = s.Substring(0, a);
        core = s.Substring(a, b - a);
        suf = s.Substring(b);
    }

    static bool Lookup(string core, out string v)
    {
        if (dict.TryGetValue(core, out v)) return true;
        // Короткие метки («К», «с») регистр не подгоняем: «с» ≠ «С» на карте
        if (core.Length > 2 && dictLower.TryGetValue(core.ToLowerInvariant(), out v))
        {
            // Тот же текст другими буквами: «СКЛАД», «склад» → подгоняем регистр перевода
            if (core.ToUpperInvariant() == core) v = v.ToUpperInvariant();
            else if (char.IsUpper(core[0]) && v.Length > 0 && char.IsLower(v[0])) v = char.ToUpperInvariant(v[0]) + v.Substring(1);
            else if (char.IsLower(core[0]) && v.Length > 1 && char.IsUpper(v[0]) && !char.IsUpper(v[1])) v = char.ToLowerInvariant(v[0]) + v.Substring(1);
            return true;
        }
        return false;
    }

    static string Line(string line, int depth)
    {
        if (!HasCyr(line)) return line;
        Split(line, out string pre, out string core, out string suf);
        if (core.Length == 0) return line;
        if (Lookup(core, out string v)) return pre + v + Quotes(v, suf);
        if (depth < 3 && Template(core, depth, out v)) return pre + v + Quotes(v, suf);
        if (depth < 3 && Segments(core, depth, out v)) return pre + v + suf;
        return pre + Pieces(core, depth) + suf;
    }

    // Обновление 22: строка, склеенная из текста и чисел («Город не сохранён: 5», «Переплавить, ... немного: 1 сталь»)
    // или из нескольких предложений. Раньше такая строка сразу рвалась на кусочки по каждой запятой, и фразы
    // с запятыми оставались по-русски. Теперь сначала режем по числам и по концам предложений и переводим
    // каждый кусок целиком (сначала предложения, внутри предложения - куски между числами);
    // по кусочкам (Pieces) - только то, что и так не нашлось.
    static readonly Regex NumRx = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);
    static readonly Regex SentRx = new Regex(@"(?<=[.!?…])\s+(?=\S)", RegexOptions.Compiled);

    static bool Segments(string core, int depth, out string result)
    {
        result = null;
        Regex rx = SentRx.IsMatch(core) ? SentRx : NumRx.IsMatch(core) ? NumRx : null;   // сначала предложения, внутри них - числа
        if (rx == null) return false;
        var sb = new StringBuilder(core.Length + 16);
        int last = 0;
        bool changed = false;
        foreach (Match m in rx.Matches(core))
        {
            changed |= Seg(sb, core.Substring(last, m.Index - last), depth);
            sb.Append(m.Value);
            last = m.Index + m.Length;
        }
        changed |= Seg(sb, core.Substring(last), depth);
        if (!changed) return false;
        result = sb.ToString();
        return true;
    }

    static bool Seg(StringBuilder sb, string part, int depth)
    {
        if (!HasCyr(part)) { sb.Append(part); return false; }
        string t = Line(part, depth + 1);
        sb.Append(t);
        return !ReferenceEquals(t, part) && t != part;
    }

    // «Выйти из клана «{0}»?» → перевод открыл кавычку по-своему (" или „) - закрываем так же
    static string Quotes(string v, string suf)
    {
        if (suf.IndexOf('»') < 0) return suf;
        int open = 0;
        foreach (char c in v) { if (c == '"') open ^= 1; else if (c == '„') open = 2; else if (c == '“' && open == 2) open = 0; }
        if (open == 1) return suf.Replace('»', '"');
        if (open == 2) return suf.Replace('»', '“');
        return suf;
    }

    static bool Template(string core, int depth, out string result)
    {
        result = null;
        if (templates.Count == 0) return false;
        Tmpl best = null;
        Match bestM = null;
        foreach (string w in Words(core))
        {
            if (!templates.TryGetValue(w, out List<Tmpl> list)) continue;
            foreach (Tmpl t in list)
            {
                if (best != null && t.literal <= best.literal) continue;
                Match m = t.rx.Match(core);
                if (m.Success) { best = t; bestM = m; }
            }
        }
        if (best == null) return false;
        Match mm = bestM;
        List<int> order = best.holes;
        result = HoleRx.Replace(best.value, h =>
        {
            int gi = order.IndexOf(int.Parse(h.Groups[1].Value)) + 1;
            return gi > 0 && gi < mm.Groups.Count ? Line(mm.Groups[gi].Value, depth + 1) : h.Value;
        });
        return true;
    }

    // Кусочки между цифрами и знаками: «Ур.3 Цитадель» → «Ур» «3» «Цитадель»
    static string Pieces(string s, int depth)
    {
        var sb = new StringBuilder(s.Length + 8);
        int i = 0;
        bool changed = false;
        while (i < s.Length)
        {
            int a = i;
            bool word = IsWordChar(s, i);
            while (i < s.Length && IsWordChar(s, i) == word) i++;
            string part = s.Substring(a, i - a);
            if (word && HasCyr(part))
            {
                Split(part, out string pre, out string core, out string suf);
                if (core.Length > 0 && core != s && Lookup(core, out string v)) { part = pre + v + suf; changed = true; }
                else if (core.Length > 0 && core != s && depth < 3 && Template(core, depth, out v)) { part = pre + v + suf; changed = true; }
                else if (core.Length > 0 && Phrases(core, out v)) { part = pre + v + suf; changed = true; }
            }
            sb.Append(part);
        }
        return changed ? sb.ToString() : s;
    }

    static bool IsWordChar(string s, int i)
    {
        char c = s[i];
        if (char.IsLetter(c) || c == ' ') return true;
        // дефис и апостроф внутри слова
        return (c == '-' || c == '\'') && i > 0 && i + 1 < s.Length && char.IsLetter(s[i - 1]) && char.IsLetter(s[i + 1]);
    }

    // Кусок из нескольких известных слов/фраз: переводим, только если известно всё
    static bool Phrases(string s, out string result)
    {
        result = null;
        string[] w = s.Split(' ');
        if (w.Length < 2 || w.Length > 12) return false;
        var outp = new List<string>();
        int i = 0;
        while (i < w.Length)
        {
            if (w[i].Length == 0) { outp.Add(""); i++; continue; }
            bool found = false;
            for (int j = w.Length; j > i; j--)
            {
                string phrase = string.Join(" ", w, i, j - i);
                if (!HasCyr(phrase)) { if (j == i + 1) { outp.Add(phrase); found = true; i = j; break; } continue; }
                if (Lookup(phrase, out string v)) { outp.Add(v); i = j; found = true; break; }
            }
            if (!found) return false;
        }
        result = string.Join(" ", outp);
        return true;
    }

    // ---------- GUIContent ----------

    public static GUIContent Content(string text) { return new GUIContent(T(text)); }
    public static GUIContent Content(string text, string tooltip) { return new GUIContent(T(text), T(tooltip)); }
    public static GUIContent Content(string text, Texture image) { return new GUIContent(T(text), image); }
    public static GUIContent Content(string text, Texture image, string tooltip) { return new GUIContent(T(text), image, T(tooltip)); }
    public static GUIContent Content(Texture image) { return new GUIContent(image); }
    public static GUIContent Content(Texture image, string tooltip) { return new GUIContent(image, T(tooltip)); }
    public static GUIContent Content(GUIContent src) { return Tr(src); }

    /// <summary>Копия GUIContent с переведённым текстом (none и пустые - как есть).</summary>
    public static GUIContent Tr(GUIContent c)
    {
        if (c == null || c == GUIContent.none) return c;
        if (current == Lang.RU)
        {
            if (!MobileText.On) return c;
            string mt = MobileText.Fix(c.text), mtip = MobileText.Fix(c.tooltip);
            return ReferenceEquals(mt, c.text) && ReferenceEquals(mtip, c.tooltip) ? c : new GUIContent(mt, c.image, mtip);
        }
        bool t1 = !string.IsNullOrEmpty(c.text) && HasCyr(c.text);
        bool t2 = !string.IsNullOrEmpty(c.tooltip) && HasCyr(c.tooltip);
        if (!t1 && !t2) return c;
        return new GUIContent(t1 ? T(c.text) : c.text, c.image, t2 ? T(c.tooltip) : c.tooltip);
    }
}

/// <summary>
/// Те же GUI.Label / Button / Box / Toggle, только текст переводится на выбранный язык (см. Loc).
/// В коде игры вместо GUI.Label пишем LocGUI.Label - остальное без изменений.
/// </summary>
public static class LocGUI
{
    public static void Label(Rect r, string text) { var c = TextFit.Temp(Loc.T(text)); var fs = TextFit.Style(r, ref c, GUI.skin.label); GUI.Label(r, c, fs); }
    public static void Label(Rect r, string text, GUIStyle st) { var c = TextFit.Temp(Loc.T(text)); var fs = TextFit.Style(r, ref c, st); GUI.Label(r, c, fs); }
    public static void Label(Rect r, GUIContent c) { c = Loc.Tr(c); var fs = TextFit.Style(r, ref c, GUI.skin.label); GUI.Label(r, c, fs); }
    public static void Label(Rect r, GUIContent c, GUIStyle st) { c = Loc.Tr(c); var fs = TextFit.Style(r, ref c, st); GUI.Label(r, c, fs); }
    public static void Label(Rect r, Texture t) { GUI.Label(r, t); }
    public static void Label(Rect r, Texture t, GUIStyle st) { GUI.Label(r, t, st); }

    public static bool Button(Rect r, string text) { var c = TextFit.Temp(Loc.T(text)); var fs = TextFit.Style(r, ref c, GUI.skin.button); return GUI.Button(r, c, fs); }
    public static bool Button(Rect r, string text, GUIStyle st) { var c = TextFit.Temp(Loc.T(text)); var fs = TextFit.Style(r, ref c, st); return GUI.Button(r, c, fs); }
    public static bool Button(Rect r, GUIContent c) { c = Loc.Tr(c); var fs = TextFit.Style(r, ref c, GUI.skin.button); return GUI.Button(r, c, fs); }
    public static bool Button(Rect r, GUIContent c, GUIStyle st) { c = Loc.Tr(c); var fs = TextFit.Style(r, ref c, st); return GUI.Button(r, c, fs); }
    public static bool Button(Rect r, Texture t) { return GUI.Button(r, t); }
    public static bool Button(Rect r, Texture t, GUIStyle st) { return GUI.Button(r, t, st); }

    // TouchControls.NoteBox: на телефоне палец на окне не двигает карту (Mobile/TouchControls.cs)
    public static void Box(Rect r, string text) { TouchControls.NoteBox(r); GUI.Box(r, Loc.T(text)); }
    public static void Box(Rect r, string text, GUIStyle st) { TouchControls.NoteBox(r); GUI.Box(r, Loc.T(text), st); }
    public static void Box(Rect r, GUIContent c) { TouchControls.NoteBox(r); GUI.Box(r, Loc.Tr(c)); }
    public static void Box(Rect r, GUIContent c, GUIStyle st) { TouchControls.NoteBox(r); GUI.Box(r, Loc.Tr(c), st); }
    public static void Box(Rect r, Texture t) { TouchControls.NoteBox(r); GUI.Box(r, t); }
    public static void Box(Rect r, Texture t, GUIStyle st) { TouchControls.NoteBox(r); GUI.Box(r, t, st); }

    // final-13-fix-2: галочки-переключатели рисует GameUi.Check (свой квадратик с галочкой вместо стандартного кружка Unity)
    public static bool Toggle(Rect r, bool on, string text) { return GameUi.Check(r, on, Loc.T(text), GUI.skin.toggle); }
    public static bool Toggle(Rect r, bool on, string text, GUIStyle st)
    {
        if (st == null || st.name == "toggle") return GameUi.Check(r, on, Loc.T(text), st ?? GUI.skin.toggle);
        return GUI.Toggle(r, on, Loc.T(text), st);
    }
    public static bool Toggle(Rect r, bool on, GUIContent c) { return GUI.Toggle(r, on, Loc.Tr(c)); }
    public static bool Toggle(Rect r, bool on, GUIContent c, GUIStyle st) { return GUI.Toggle(r, on, Loc.Tr(c), st); }
    public static bool Toggle(Rect r, bool on, Texture t) { return GUI.Toggle(r, on, t); }
    public static bool Toggle(Rect r, bool on, Texture t, GUIStyle st) { return GUI.Toggle(r, on, t, st); }
}
