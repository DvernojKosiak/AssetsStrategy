using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Обновление 12: карточки отрядов в набеге (внизу экрана) и кнопка «Все».
///  - У каждого отряда карточка: рисунок бойца (того, кого в отряде больше всех), название, сколько живых и здоровье.
///    Нажатие по карточке выбирает отряд (ещё нажатие - снимает; можно выбрать несколько).
///  - «Все»: приказы с карты получают все отряды.
///  - Ничего не выбрано и «Все» не нажата - касание карты ничего не приказывает, карту можно спокойно листать.
///    Раньше любой случайный тап (особенно на телефоне, пока двигаешь карту) отправлял туда всю армию.
/// </summary>
public partial class RaidManager
{
    bool allSquads;            // нажата «Все»
    float noSquadHintAt = -99f;

    /// <summary>Приказы с карты сейчас кому-то пойдут.</summary>
    bool OrdersArmed => allSquads || selected.Count > 0;

    /// <summary>Кому идут приказы с карты: выбранные отряды или все (при «Все»); никого - пустой список.</summary>
    List<RaidUnit> OrderTargets()
    {
        var list = Attackers();
        if (allSquads && selected.Count == 0) return list;
        list.RemoveAll(u => u.Squad == null || !selected.Contains(u.Squad));
        return list;
    }

    float flashUntil = -1f;

    // Обновление 13: подсказку над карточками можно закрыть (тестеры: «никак не закрыть эту подсказку»)
    static bool HintOff
    {
        get { try { return PlayerPrefs.GetInt("raid.squadhint.off", 0) == 1; } catch (System.Exception) { return false; } }
        set { try { PlayerPrefs.SetInt("raid.squadhint.off", value ? 1 : 0); PlayerPrefs.Save(); } catch (System.Exception) { } }
    }

    void NoSquadHint()
    {
        flashUntil = Time.unscaledTime + 1.4f;   // карточки внизу мигают: «выберите здесь»
        if (Time.unscaledTime - noSquadHintAt < 6f || !HintOff) return;   // подсказка над карточками видна - сообщение не нужно
        noSquadHintAt = Time.unscaledTime;
        WarManager.Notify("Чтобы отдать приказ, выберите отряд внизу или нажмите «Все»", false);
    }

    struct SquadCard { public Squad sq; public int alive; public int hp; public UnitType unit; }
    readonly List<SquadCard> cards = new List<SquadCard>();
    readonly Dictionary<UnitType, int> unitCount = new Dictionary<UnitType, int>();

    int cardsFrame = -1;

    void CollectCards()
    {
        if (cardsFrame == Time.frameCount) return;   // OnGUI - несколько раз за кадр, считаем один
        cardsFrame = Time.frameCount;
        cards.Clear();
        var att = AttackersNow();
        foreach (var sq in squads)
        {
            if (sq == null) continue;
            int alive = 0;
            float hpSum = 0f;
            unitCount.Clear();
            UnitType best = null;
            int bestN = 0;
            foreach (var u in att)
            {
                if (u.Squad != sq) continue;
                alive++;
                hpSum += u.HpFraction;
                UnitType t = u.ShownUnit;
                if (t == null) continue;
                unitCount.TryGetValue(t, out int n);
                unitCount[t] = ++n;
                if (n > bestN) { bestN = n; best = t; }
            }
            if (alive == 0) continue;
            cards.Add(new SquadCard { sq = sq, alive = alive, hp = Mathf.RoundToInt(hpSum / alive * 100f), unit = best });
        }
    }

    void DrawSquadBar()
    {
        if (State != Phase.Battle && State != Phase.Scouting) return;
        if (Event.current.type == EventType.Layout || cards.Count == 0) CollectCards();
        if (cards.Count == 0) return;
        // Ушедшие отряды (все погибли) из выбора убираем
        selected.RemoveWhere(sq => !cards.Exists(c => c.sq == sq));

        bool phone = UiKit.Phone;
        float cw = phone ? 92f : 88f, ch = phone ? 58f : 56f, gap = 4f;
        float x0 = SideW + 8f, x1 = UiKit.W - 200f;
        if (x1 - x0 < cw * 2f + gap) { x0 = 8f; x1 = UiKit.W - 8f; }
        int n = cards.Count + 1;
        int perRow = Mathf.Max(1, Mathf.FloorToInt((x1 - x0 + gap) / (cw + gap)));
        int maxRows = phone ? 2 : 3;   // на телефоне карточки не должны закрывать полэкрана - там двигают карту
        int rows = Mathf.Min(maxRows, Mathf.CeilToInt(n / (float)perRow));
        if (rows * perRow < n) perRow = Mathf.CeilToInt(n / (float)rows);   // не влезло - карточки уже
        cw = Mathf.Min(cw, (x1 - x0 + gap) / perRow - gap);
        float y0 = UiKit.H - 8f - rows * (ch + gap) + gap;
        float hintH = State == Phase.Battle && !OrdersArmed && !HintOff ? 22f : 0f;
        Rect all = new Rect(x0, y0 - hintH, Mathf.Min(perRow, n) * (cw + gap) - gap, rows * (ch + gap) - gap + hintH);
        if (hintH > 0f) all.width = Mathf.Max(all.width, 260f);
        Publish(all);

        if (hintH > 0f)
        {
            GUIStyle hs = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
            Rect hr = new Rect(x0, y0 - hintH, Mathf.Max(all.width, 260f), 20f);
            Color o = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(hr, Texture2D.whiteTexture);
            GUI.color = o;
            LocGUI.Label(new Rect(hr.x + 6f, hr.y + 1f, hr.width - 34f, 18f), UiKit.Col(UiKit.WarnHex, "Приказ: нажмите отряд внизу (или «Все»), потом место на карте"), hs);
            if (LocGUI.Button(new Rect(hr.xMax - 24f, hr.y, 24f, 20f), "✕", UiKit.ButtonStyle(UiKit.Kind.Ghost))) HintOff = true;
        }

        for (int i = 0; i < n; i++)
        {
            int row = i / perRow, col = i % perRow;
            Rect r = new Rect(x0 + col * (cw + gap), y0 + row * (ch + gap), cw, ch);
            if (i == 0) DrawAllCard(r);
            else DrawSquadCard(r, cards[i - 1]);
            // Нажали по карте, не выбрав отряд - карточки мигают
            if (Time.unscaledTime < flashUntil && Event.current.type == EventType.Repaint && Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.6f)
            {
                Color o = GUI.color;
                GUI.color = UiKit.Warn;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.y, 2f, r.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax - 2f, r.y, 2f, r.height), Texture2D.whiteTexture);
                GUI.color = o;
            }
        }
    }

    void DrawAllCard(Rect r)
    {
        int total = 0;
        foreach (var c in cards) total += c.alive;
        bool on = allSquads && selected.Count == 0;
        if (LocGUI.Button(r, GUIContent.none, UiKit.ButtonStyle(on ? UiKit.Kind.Primary : UiKit.Kind.Normal)))
        {
            selected.Clear();
            allSquads = !on;
        }
        GUIStyle big = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        GUIStyle sm = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        LocGUI.Label(new Rect(r.x, r.y + 6f, r.width, 22f), on ? "✓ Все" : "Все", big);
        LocGUI.Label(new Rect(r.x, r.y + r.height - 24f, r.width, 18f), UiKit.Col(UiKit.MutedHex, $"{total} бойц."), sm);
        UiKit.Tip(r, on ? "Приказы с карты получают все отряды. Нажмите ещё раз, чтобы просто листать карту"
                        : "Нажмите, чтобы приказы с карты получали все отряды сразу");
    }

    void DrawSquadCard(Rect r, SquadCard c)
    {
        bool on = selected.Contains(c.sq);
        if (LocGUI.Button(r, GUIContent.none, UiKit.ButtonStyle(on ? UiKit.Kind.Primary : UiKit.Kind.Normal)))
        {
            bool multi = UiKit.Phone || (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.shiftKey.isPressed);
            if (on) selected.Remove(c.sq);
            else
            {
                if (!multi) selected.Clear();
                selected.Add(c.sq);
            }
            allSquads = false;
        }
        float icon = r.height - 14f;
        Rect ir = new Rect(r.x + 4f, r.y + 4f, Mathf.Min(icon, r.width * 0.42f), icon);
        if (c.unit != null && UnitIcons.Has(c.unit)) UnitIcons.Draw(ir, c.unit);
        else if (c.unit != null && Event.current.type == EventType.Repaint)
        {
            Color o = GUI.color;
            GUI.color = c.unit.color;
            GUI.DrawTexture(new Rect(ir.center.x - ir.width * 0.25f, ir.center.y - ir.height * 0.25f, ir.width * 0.5f, ir.height * 0.5f), Texture2D.whiteTexture);
            GUI.color = o;
        }
        float tx = ir.xMax + 3f, tw = r.xMax - tx - 3f;
        GUIStyle name = new GUIStyle(UiKit.Small) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
        GUIStyle sm = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip };
        LocGUI.Label(new Rect(tx, r.y + 4f, tw, 18f), (on ? "✓ " : "") + c.sq.Name, name);
        LocGUI.Label(new Rect(tx, r.y + 21f, tw, 18f), $"{c.alive} · <color={HpHex(c.hp)}>{c.hp}%</color>", sm);
        if (c.unit != null) LocGUI.Label(new Rect(tx, r.y + 37f, tw, 16f), UiKit.Col(UiKit.MutedHex, c.unit.unitName), sm);
        // Полоска здоровья отряда
        if (Event.current.type == EventType.Repaint)
        {
            Rect bar = new Rect(r.x + 4f, r.yMax - 7f, r.width - 8f, 3f);
            Color o = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = c.hp >= 75 ? UiKit.Good : c.hp >= 50 ? UiKit.Warn : UiKit.Bad;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * c.hp / 100f, bar.height), Texture2D.whiteTexture);
            GUI.color = o;
        }
        UiKit.Tip(r, $"{c.sq.Name}: {c.alive} бойцов, здоровье {c.hp}%" + (c.unit != null ? $"\nБольше всего: {c.unit.unitName}" : "")
            + (on ? "\nВыбран — нажатие по карте отдаёт ему приказ. Нажмите карточку ещё раз, чтобы снять выбор" : "\nНажмите, чтобы выбрать отряд"));
    }
}
