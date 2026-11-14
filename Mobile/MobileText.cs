using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Тексты игры писались для компьютера: «ЛКМ — поставить», «Shift — ещё», «(F)», «Esc — отмена».
/// На телефоне (Loc.T зовёт Fix для каждой строки интерфейса) такие подсказки убираются или
/// переписываются под палец: «ЛКМ по земле» → «Нажатие по земле», «двойной клик» → «двойное нажатие»,
/// а куски про клавиши, правую кнопку, колесо и Shift выпадают целиком (на телефоне для этого свои кнопки).
/// Работает и для русского, и для английского перевода.
/// </summary>
public static class MobileText
{
    /// <summary>Сейчас телефон (или включено сенсорное управление).</summary>
    public static bool On => Application.isMobilePlatform || TouchInput.Active;

    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

    // Признаки компьютерного управления: кусок текста с ними на телефоне не нужен
    static readonly Regex PcRx = new Regex(
        @"\bShift\b|\bCtrl\b|\bAlt\b|\bEsc\b|\bTab\b|^(?:Или )?Enter$|\bWASD\b|\bПКМ\b|\bRMB\b|\bMMB\b|ЛКМ можно зажать|\bF\d{1,2}\b|" +
        @"(?<![\p{L}\d.])[A-Z](?:\s*[/,]\s*[A-Z])*\s*[—–-]\s|(?<![\p{L}\d.])[1-9]\s*[–-]\s*9\s*[—–-]\s|" +
        @"[Кк]лавиш|[Пп]робел|\bSpace\b|[Кк]олес[оаи]?м?\s+мыш|[Кк]олёсик|\bмыш[ьиюкой]|\bmouse\b|mouse wheel|\bkeys\b|\bhotkey|" +
        @"правой кнопк|средней кнопк|right[- ]click|middle[- ]click|курсором|under the cursor",
        RegexOptions.Compiled);

    // Горячая клавиша в скобках: «(F)», «(M)», «(Esc)», «(клавиша B)», «[Пробел]», «(F10)»
    static readonly Regex KeyParenRx = new Regex(
        @"\s*[\(\[]\s*(?:клавиша\s+|key\s+)?(?:[A-Z]|F\d{1,2}|Esc|Tab|Enter|Пробел|Space|Shift|Ctrl|ПКМ|ЛКМ|LMB|RMB|[1-9]\s*[–-]\s*9)\s*[\)\]]",
        RegexOptions.Compiled);

    // Разделители перечислений в подсказках: кусок между ними - одна мысль
    static readonly Regex SplitRx = new Regex(@"(\n|,\s+|;\s+|\s+·\s+|\s{2,}|\.\s+(?=\p{Lu}))", RegexOptions.Compiled);

    static readonly Regex ParenRx = new Regex(@"\(([^()]*)\)", RegexOptions.Compiled);

    // Как сказать то же самое про палец (порядок важен: длинные сначала)
    static readonly string[,] Words =
    {
        // Целые фразы обучения и подсказок - сказать по-телефонному
        { "Двигайте карту: клавиши WASD или стрелки, либо зажмите правую кнопку мыши и тяните.", "Двигайте карту пальцем." },
        { "Колесо мыши приближает и отдаляет.", "Два пальца: развести — приблизить, свести — отдалить." },
        { "Тянуть ПКМ или колёсико — двигать карту, колесо — приблизить. S — стоп, Ctrl+A — все бойцы, Пробел — к своему городу.",
          "Пальцем — двигать карту, двумя пальцами — приблизить. Кнопки справа: «Стоп», «Все бойцы», «К городу»." },
        { "Отдалите камеру колесом", "Сведите два пальца" },
        { "? Управление мышью", "? Управление" },
        { "(ЛКМ, рамкой)", "(нажатием или «Рамкой»)" },
        { "Наведите на реку", "Ведите пальцем к реке" }, { "Навести на реку", "Вести пальцем к реке" },
        { "Наведите мышь на строку", "Подержите палец на строке" }, { "Наведите мышь на", "Подержите палец на" }, { "наведите мышь на", "подержите палец на" },
        { "Наведите курсор на", "Подержите палец на" }, { "наведите курсор на", "подержите палец на" },
        { " левой кнопкой мыши", "" }, { " левой кнопкой", "" },
        { ", ЛКМ можно зажать", "" }, { "ЛКМ можно зажать", "" },
        { "Один клик", "Одно нажатие" }, { "один клик", "одно нажатие" },
        { "ЛКМ или ПКМ по ", "Нажатие по " }, { "ЛКМ или ПКМ", "Нажатие" },
        { "ЛКМ по ", "Нажатие по " }, { "ЛКМ — ", "Нажатие — " }, { "ЛКМ", "Нажатие" },
        { "Двойной клик", "Двойное нажатие" }, { "двойной клик", "двойное нажатие" },
        { "Двойной щелчок", "Двойное нажатие" }, { "двойной щелчок", "двойное нажатие" },
        { "Кликните", "Нажмите" }, { "кликните", "нажмите" }, { "Кликнуть", "Нажать" }, { "кликнуть", "нажать" },
        { "Щёлкните", "Нажмите" }, { "щёлкните", "нажмите" }, { "Щелчок", "Нажатие" }, { "щелчок", "нажатие" },
        { "Клик", "Нажатие" }, { "клик", "нажатие" },
        { "(наведите)", "(подержите палец)" }, { "Наведите на", "Подержите палец на" }, { "наведите на", "подержите палец на" },
        { "Наведите", "Подержите палец" }, { "наведите", "подержите палец" },
        { "Наведи", "Подержи палец" }, { "наведи", "подержи палец" },
        { "LMB on ", "Tap on " }, { "LMB — ", "Tap — " }, { "LMB", "Tap" },
        { "Double-click", "Double tap" }, { "double-click", "double tap" }, { "Double click", "Double tap" }, { "double click", "double tap" },
        { "Click", "Tap" }, { "click", "tap" }, { "Hover", "Hold your finger" }, { "hover", "hold your finger" },
    };

    static Regex[] wordRx;

    // Замена только целого слова: «клик» не трогает «отклик»
    static Regex WordRx(int i)
    {
        if (wordRx == null) wordRx = new Regex[Words.GetLength(0)];
        if (wordRx[i] == null)
        {
            string w = Words[i, 0];
            string pre = char.IsLetter(w[0]) ? @"(?<!\p{L})" : "";
            string post = char.IsLetter(w[w.Length - 1]) ? @"(?=[^\p{L}]|$)" : "";
            wordRx[i] = new Regex(pre + Regex.Escape(w) + post, RegexOptions.Compiled);
        }
        return wordRx[i];
    }

    public static string Fix(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (cache.TryGetValue(s, out string done)) return done;

        string r = s;
        if (NeedsWork(r))
        {
            r = KeyParenRx.Replace(r, "");
            // Сначала - перечисления в скобках: «(ЛКМ — поставить, Shift — ещё, Esc — отмена)» → «(Нажатие — поставить)»
            r = ParenRx.Replace(r, m =>
            {
                string inner = Clean(m.Groups[1].Value);
                return inner.Trim().Length == 0 ? "" : "(" + inner + ")";
            });
            // «ЛКМ или ПКМ по земле» → «Нажатие по земле» - до чистки, иначе кусок выпал бы из-за ПКМ
            for (int i = 0; i < Words.GetLength(0); i++)
                if (r.IndexOf(Words[i, 0], System.StringComparison.Ordinal) >= 0) r = WordRx(i).Replace(r, Words[i, 1].Replace("$", "$$"));
            // По строкам: строка, которая начинается с клавиши («ПКМ — камера, …», «M — перенести …»), не нужна целиком
            string[] lines = r.Split('\n');
            var keptLines = new List<string>();
            string tagsLeft = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string head = StripTags(SplitRx.Split(Mask(line, out _))[0]);
                string kept = PcRx.IsMatch(head) ? "" : Clean(line);
                bool wasText = StripTags(line).Trim().Length > 0;
                if (wasText && StripTags(kept).Trim().Length == 0)
                {
                    // Строка выпала целиком - её теги (<color>, </b>) прицепляем к соседней, пустую строку не оставляем
                    var tags = new StringBuilder(); KeepTags(line, tags); tagsLeft += tags.ToString();
                    continue;
                }
                keptLines.Add(tagsLeft + kept);
                tagsLeft = "";
            }
            if (tagsLeft.Length > 0) { if (keptLines.Count == 0) keptLines.Add(tagsLeft); else keptLines[keptLines.Count - 1] += tagsLeft; }
            r = Tidy(string.Join("\n", keptLines));
            // Пробелы и переносы по краям - как были (строку могут склеивать с соседней)
            if (r.Length > 0) r = Regex.Match(s, @"^\s*").Value + r + Regex.Match(s, @"\s*$").Value;
            if (r == s) r = s;   // ничего не поменялось - та же строка (Loc.Tr не создаёт новый GUIContent)
        }

        if (cache.Count > 6000) cache.Clear();   // в строках бывают числа - кэш не должен расти без конца
        cache[s] = r;
        return r;
    }

    static bool NeedsWork(string s)
    {
        return PcRx.IsMatch(s) || KeyParenRx.IsMatch(s) || s.Contains("ЛКМ") || s.Contains("LMB")
            || s.Contains("клик") || s.Contains("Клик") || s.Contains("щелч") || s.Contains("Щелч") || s.Contains("щёлк") || s.Contains("Щёлк")
            || s.Contains("аведи") || s.Contains("lick") || s.Contains("over");
    }

    /// <summary>Убирает из перечисления куски про клавиши, мышь и колесо.</summary>
    // Скобки целиком прячем на время разбора: «(с вопросом; в войну нельзя)» не режется по «;»
    static string Mask(string s, out List<string> groups)
    {
        var g = new List<string>();
        string r = ParenRx.Replace(s, m => { g.Add(m.Value); return "\u0001" + (g.Count - 1) + "\u0002"; });
        groups = g;
        return r;
    }

    static readonly Regex UnmaskRx = new Regex("\u0001(\\d+)\u0002", RegexOptions.Compiled);

    static string Clean(string s)
    {
        if (!PcRx.IsMatch(s)) return s;
        string masked = Mask(s, out List<string> groups);
        if (!PcRx.IsMatch(masked)) return s;   // клавиши только в скобках - их уже почистили
        string[] parts = SplitRx.Split(masked);   // кусок, разделитель, кусок, ...
        var sb = new StringBuilder(s.Length);
        string pendingSep = null;
        bool any = false;
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            bool isSep = (i % 2) == 1;
            if (isSep) { pendingSep = p; continue; }
            if (PcRx.IsMatch(StripTags(p)))
            {
                // Кусок с клавишами выпадает. Перенос строки перед ним сохраняем, если он был (иначе склеятся строки)
                if (pendingSep == "\n" && any) { sb.Append('\n'); pendingSep = null; any = false; }
                KeepTags(p, sb);
                continue;
            }
            if (any && pendingSep != null) sb.Append(pendingSep);
            else if (pendingSep == "\n" && sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
            sb.Append(p);
            if (StripTags(p).Trim().Length > 0) any = true;
            pendingSep = null;
        }
        string res = sb.ToString();
        // Выпало последнее предложение - точка у предыдущего не должна пропасть
        if (masked.TrimEnd().EndsWith(".") && res.Trim().Length > 0 && !Regex.IsMatch(res.TrimEnd(), @"[.!?>…»)]$")) res = res.TrimEnd() + ".";
        return UnmaskRx.Replace(res, m => groups[int.Parse(m.Groups[1].Value)]);
    }

    static readonly Regex TagRx = new Regex(@"</?(?:b|i|color|size)(?:=[^>]*)?>", RegexOptions.Compiled);

    static string StripTags(string s) { return TagRx.Replace(s, ""); }

    // У выпавшего куска могли быть открывающие или закрывающие теги (<color>, <b>) - их оставляем, иначе разметка сломается
    static void KeepTags(string p, StringBuilder sb)
    {
        foreach (Match m in TagRx.Matches(p)) sb.Append(m.Value);
    }

    static readonly Regex EmptyTagRx = new Regex(@"<(b|i|color|size)(?:=[^>]*)?>\s*</\1>", RegexOptions.Compiled);

    static string Tidy(string s)
    {
        string r = s;
        for (int i = 0; i < 3; i++) r = EmptyTagRx.Replace(r, "");
        r = Regex.Replace(r, @"\(\s*\)", "");
        r = Regex.Replace(r, @"[ \t]+\n", "\n");
        r = Regex.Replace(r, @"\n{3,}", "\n\n");
        r = Regex.Replace(r, @"[ \t]{2,}", " ");
        r = Regex.Replace(r, @"\s+([,.;:!?)])", "$1");
        r = Regex.Replace(r, @"([,;:·—])\s*$", "");
        r = Regex.Replace(r, @"^\s*[,;·]\s*", "");
        return r.Trim();
    }
}
