using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Очередь заказов мастерской (final-8): до Max заказов «на потом».
/// Ресурсы заранее не берутся - заказ начнётся, когда мастерская освободится и хватит сырья.
/// Не хватает - очередь ждёт и пишет, чего ждёт (по одному сообщению на новую причину).
/// Используют: Мастерская, Оружейная, Кузница (полевые укрепления - по номеру изделия).
/// </summary>
public class CraftQueue<T>
{
    public const int Max = 3;
    public readonly List<T> Items = new List<T>();
    public string Waiting = "";   // почему первый заказ не начинается ("" - начнётся, как только освободится)
    float timer;

    public int Count => Items.Count;
    public bool Full => Items.Count >= Max;

    public bool Add(T item, string title, string itemName)
    {
        if (item == null) return false;
        if (Full) { WarManager.Notify($"{title}: очередь полна ({Max})", false); return false; }
        Items.Add(item);
        timer = 0f;
        WarManager.Notify($"{title}: «{itemName}» в очереди ({Items.Count}/{Max}). Сырьё возьмём, когда начнётся работа", true);
        return true;
    }

    /// <summary>Раз в 2 с: свободна - пробуем начать первый заказ. problem: "" - можно; tryStart: true - начали.</summary>
    public void Tick(bool busy, System.Func<T, string> problem, System.Func<T, bool> tryStart, string title)
    {
        if (busy || Items.Count == 0) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = 2f;
        T next = Items[0];
        string p = problem != null ? problem(next) : "";
        if (p == "" && tryStart(next))
        {
            Items.RemoveAt(0);
            Waiting = "";
            return;
        }
        if (p == "") p = "не получилось начать";
        if (p != Waiting) WarManager.Notify($"{title}: очередь ждёт — {p}", false);
        Waiting = p;
    }

    public float Height => Items.Count > 0 ? 28f + (Waiting != "" ? 18f : 0f) : 0f;

    /// <summary>Строка «Очередь: ✕ доски ✕ плиты». Нажатие убирает заказ (сырьё ещё не взято).</summary>
    public float Draw(float x, float y, float w, System.Func<T, string> nameOf)
    {
        if (Items.Count == 0) return y;
        LocGUI.Label(new Rect(x, y + 2f, 74f, 22f), UiKit.Col(UiKit.MutedHex, $"Очередь {Items.Count}/{Max}:"), UiKit.Small);
        float bx = x + 76f, bw = (w - 76f) / Max - 4f;
        for (int i = 0; i < Items.Count; i++)
        {
            string n = nameOf(Items[i]);
            if (UiKit.Button(new Rect(bx, y + 1f, bw, 24f), "✕ " + n, UiKit.Kind.Ghost, $"Убрать «{n}» из очереди (сырьё ещё не взято)"))
            {
                Items.RemoveAt(i);
                if (i == 0) Waiting = "";
                break;
            }
            bx += bw + 4f;
        }
        y += 28f;
        if (Waiting != "")
        {
            LocGUI.Label(new Rect(x, y, w, 18f), UiKit.Col(UiKit.WarnHex, "Ждёт: " + Waiting), UiKit.Small);
            y += 18f;
        }
        return y;
    }

    /// <summary>Для сохранения: номера заказов в списке рецептов.</summary>
    public string Save(IList<T> all)
    {
        var parts = new List<string>();
        foreach (var it in Items) { int i = all.IndexOf(it); if (i >= 0) parts.Add(i.ToString()); }
        return string.Join(",", parts);
    }

    public void Load(string s, IList<T> all)
    {
        Items.Clear();
        Waiting = "";
        if (string.IsNullOrEmpty(s) || all == null) return;
        foreach (var p in s.Split(','))
            if (int.TryParse(p, out int i) && i >= 0 && i < all.Count && all[i] != null && Items.Count < Max) Items.Add(all[i]);
    }
}
