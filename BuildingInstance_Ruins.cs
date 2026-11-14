using UnityEngine;

/// <summary>
/// Руины переживают выход из игры:
///  - CitySave пишет в здание «ruin=оплачено,ход восстановления,когда (UTC по часам сервера)»;
///  - набег, пока игрока не было (сервер, CityLoss.MarkRuins), пишет туда же «ruin=0,0,время набега»;
///  - при входе здание снова в руинах и восстанавливается за время отсутствия, как если бы игра шла:
///    платит за восстановление (если есть чем, Цитадель и стартовые - даром) и поднимается за RebuildSeconds.
/// </summary>
public partial class BuildingInstance
{
    /// <summary>Сейчас по часам сервера (в офлайне - по часам компьютера).</summary>
    public static System.DateTime RuinClockUtc => ClockOverride ?? (WorldClock.Active ? WorldClock.ServerUtc : System.DateTime.UtcNow);

    /// <summary>
    /// На время одного сохранения: «сейчас» - это момент сворачивания игры (ResumeCatchUp). Пока игра была свёрнута,
    /// в ней ничего не менялось, поэтому её состояние - это состояние на момент сворачивания.
    /// </summary>
    public static System.DateTime? ClockOverride;

    /// <summary>Запись о руинах для сохранения (null - здание целое).</summary>
    public string RuinState()
    {
        if (!ruined) return null;
        return (RebuildPaid ? "1" : "0") + "," + RebuildProgress.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
             + "," + RuinClockUtc.ToString("o");
    }

    /// <summary>
    /// Вернуть руины из записи и прожить время с момента записи (восстановление шло без игрока).
    /// Склад здания не трогаем: что из него унесли, сервер уже вычел.
    /// </summary>
    public void RestoreRuin(string state)
    {
        if (string.IsNullOrEmpty(state) || WarCityMode.IsRuinedByWar(this)) return;
        string[] a = state.Split(',');
        bool paid = a.Length > 0 && a[0] == "1";
        float progress = a.Length > 1 && float.TryParse(a[1], System.Globalization.NumberStyles.Float,
                                                         System.Globalization.CultureInfo.InvariantCulture, out float p) ? Mathf.Clamp01(p) : 0f;
        double away = 0.0;
        if (a.Length > 2 && System.DateTime.TryParse(a[2], null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime at))
            away = System.Math.Max(0.0, (RuinClockUtc - at.ToUniversalTime()).TotalSeconds);
        SetRuin(paid, progress, away);
    }

    /// <summary>Набег был, пока игра открыта (отчёт сервера): здание в руинах с момента набега.</summary>
    public void RuinFromReport(string timeUtc)
    {
        if (ruined) return;
        double away = 0.0;
        if (!string.IsNullOrEmpty(timeUtc) && System.DateTime.TryParse(timeUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime at))
            away = System.Math.Max(0.0, (RuinClockUtc - at.ToUniversalTime()).TotalSeconds);
        SetRuin(false, 0f, away);
    }

    void SetRuin(bool paid, float progress, double away)
    {
        if (!IsDamageable) return;
        bool was = IsRuined;
        IsRuined = true;
        RebuildPaid = paid;
        RebuildProgress = progress;
        rebuildRetry = 0f;

        // Время без игрока: сначала оплата (как в TickHealth), потом стройка
        if (!RebuildPaid && away > 0.0)
        {
            if (FreeRebuild) RebuildPaid = true;
            else if (RebuildPlan(out int w, out int s))
            {
                var rm = ResourceManager.Instance;
                if (rm == null || rm.Spend(0, w, s, 0)) RebuildPaid = true;
            }
        }
        if (RebuildPaid && away > 0.0) RebuildProgress = Mathf.Min(1f, RebuildProgress + (float)(away / RebuildSeconds));

        if (RebuildPaid && RebuildProgress >= 1f)
        {
            IsRuined = false;
            RebuildPaid = false;
            RebuildProgress = 0f;
            hp = MaxHp;
        }
        else hp = Mathf.Max(0f, MaxHp * RebuildProgress);

        if (IsRuined != was || IsRuined)
        {
            if (IsRuined) SetWorking(true, 0);
            ApplyLook();
            if (IsBarrier || IsBridge) GridNavigation.MarkDirty();
            if (IsOutpost) Territory.MarkDirty();
        }
    }
}
