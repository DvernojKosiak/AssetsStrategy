using System;
using UnityEngine;

/// <summary>
/// Общее время игры: у всех игроков на одном сервере одинаковые сутки и часы.
/// Считается от начала мира (worldStartUtc) по часам сервера, которые приходят в каждом ответе.
/// В офлайне не действует - там часы идут как раньше, со скоростями.
/// </summary>
public static class WorldClock
{
    static bool synced;
    static DateTime worldStart;
    static double offsetSeconds;   // часы сервера минус часы этого компьютера

    /// <summary>Общее время включено: вошли в аккаунт и знаем часы сервера.</summary>
    public static bool Active => synced && AccountManager.LoggedIn && !AccountManager.IsOffline;

    /// <summary>Сколько реальных секунд прошло с начала мира.</summary>
    public static double Seconds => Math.Max(0, (DateTime.UtcNow.AddSeconds(offsetSeconds) - worldStart).TotalSeconds);

    /// <summary>Сейчас по часам сервера.</summary>
    public static DateTime ServerUtc => DateTime.UtcNow.AddSeconds(offsetSeconds);

    public static void Sync(ApiResponse r)
    {
        if (r == null || string.IsNullOrEmpty(r.serverUtc) || string.IsNullOrEmpty(r.worldStartUtc)) return;
        if (!TryUtc(r.serverUtc, out DateTime server) || !TryUtc(r.worldStartUtc, out DateTime start)) return;
        double off = (server - DateTime.UtcNow).TotalSeconds;
        // Первое значение берём сразу, дальше сглаживаем (ответы идут с задержкой сети)
        offsetSeconds = synced ? offsetSeconds * 0.8 + off * 0.2 : off;
        worldStart = start;
        synced = true;
    }

    static bool TryUtc(string s, out DateTime d)
    {
        if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out d)) { d = d.ToUniversalTime(); return true; }
        return false;
    }
}
