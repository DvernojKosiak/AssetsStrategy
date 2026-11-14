using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// final-13: «усталость от лечения» магов. Раньше отряд из 8-10 полевых магов лечил одного бойца в 8-10 раз быстрее,
/// и его было не убить. Теперь каждый следующий снаряд лечения в того же бойца за короткое время лечит слабее:
/// первый - 100%, второй - 60%, третий - 35%, дальше - 15%. Один-два мага на отряд лечат как раньше,
/// а толпа магов уже не делает бойца бессмертным: им выгоднее лечить разных бойцов (они так и выбирают цель).
/// Лимита на число магов в отряде нет.
/// </summary>
public static class MageHeal
{
    public const float Window = 1.8f;   // снаряды за это время считаются «подряд» (снаряд мага - раз в 0,8 с)
    static readonly float[] Share = { 1f, 0.6f, 0.35f, 0.15f };

    // Ключ - сам объект (без GetInstanceID: в новых версиях Unity он запрещён)
    static readonly Dictionary<Object, List<float>> recent = new Dictionary<Object, List<float>>();
    static float cleanAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { recent.Clear(); cleanAt = 0f; }

    static List<float> Of(Object target, bool create)
    {
        if (!recent.TryGetValue(target, out var l))
        {
            if (!create) return null;
            l = new List<float>(4);
            recent[target] = l;
        }
        float now = Time.time;
        l.RemoveAll(t => now - t > Window);
        return l;
    }

    /// <summary>Сколько снарядов лечения боец получил за последние Window секунд (для выбора цели: лечить тех, кого не лечат).</summary>
    public static int Recent(Object target)
    {
        if (target == null) return 0;
        var l = Of(target, false);
        return l != null ? l.Count : 0;
    }

    /// <summary>Снаряд мага долетел: сколько он на самом деле лечит (и запоминаем его).</summary>
    public static float Apply(Object target, float amount)
    {
        if (target == null || amount <= 0f) return 0f;
        float now = Time.time;
        if (now > cleanAt)
        {
            cleanAt = now + 10f;
            var dead = new List<Object>();
            foreach (var kv in recent) if (kv.Key == null || kv.Value.Count == 0 || now - kv.Value[kv.Value.Count - 1] > Window) dead.Add(kv.Key);
            foreach (var k in dead) recent.Remove(k);
        }
        var l = Of(target, true);
        float k2 = Share[Mathf.Min(l.Count, Share.Length - 1)];
        l.Add(now);
        return amount * k2;
    }
}
