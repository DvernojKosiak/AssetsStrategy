using UnityEngine;

/// <summary>
/// Защита новичка (final-8). Пока Цитадель Ур.1 и в городе нет ни одной казармы:
///  - другие игроки не находят город в поиске (это решает сервер: CityLoss.IsNewbie);
///  - соседи-NPC не нападают и не шлют лазутчиков (RivalManager, WarManager).
/// Защита снимается, как только появилась первая казарма (или Цитадель Ур.2). После этого у новичка
/// ещё GraceAfter секунд передышки от NPC, чтобы успеть обучить бойцов.
/// </summary>
public static class NewbieShield
{
    public const float GraceAfter = 600f;   // 10 минут после первой казармы
    static float checkAt;
    static bool cached = true;
    static float endedAt = -1f;             // когда защита снялась (Time.time); -1 - ещё стоит или снялась до запуска
    static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { checkAt = 0f; cached = true; endedAt = -1f; initialized = false; }

    /// <summary>Город ещё под защитой новичка.</summary>
    public static bool Active
    {
        get
        {
            if (Time.time < checkAt) return cached;
            checkAt = Time.time + 1f;
            bool now = Compute();
            if (!initialized) { initialized = true; cached = now; return cached; }   // при загрузке - без сообщений
            if (cached && !now)
            {
                endedAt = Time.time;
                WarManager.Notify($"Первая казарма готова — защита новичка снята. Другие игроки теперь видят ваш город, соседи-NPC могут напасть не раньше чем через {GraceAfter / 60f:F0} мин. Обучайте бойцов!", false);
            }
            cached = now;
            return cached;
        }
    }

    /// <summary>NPC пока не нападают: защита новичка или первые 10 минут после неё.</summary>
    public static bool BlocksNpc => Active || (endedAt >= 0f && Time.time - endedAt < GraceAfter);

    static bool Compute()
    {
        if (StorageLimits.HallLevel >= 2) return false;
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null || b.UnderConstruction) continue;
            if (b.GetComponent<BarracksController>() != null) return false;
        }
        return true;
    }
}
