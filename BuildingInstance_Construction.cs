using UnityEngine;

/// <summary>
/// Время стройки и улучшения (цены и секунды - в Balance):
///  - Новое здание сначала стройка: не работает (как руины), в нём не живут и не работают, враги его не трогают.
///    Снос стройки возвращает всю цену.
///  - Улучшение: когда житель донёс материалы (UpgradeRules), идёт время; уровень растёт в конце.
///    Пока идёт улучшение, здание работает на старом уровне.
///  - Время идёт и без игрока: в сохранении лежит, сколько осталось и когда записано (часы сервера).
///  - F7 (админ, для тестов) - закончить все стройки и улучшения сразу. Ускорить одно здание можно за марки.
/// </summary>
public partial class BuildingInstance : ISavedBuilding
{
    float buildLeft, buildTotal;          // стройка: сколько осталось и сколько всего, секунд
    int upgradeTarget;                    // до какого уровня идёт улучшение (0 - не идёт)
    float upgradeLeft, upgradeTotal;

    public bool UnderConstruction => buildLeft > 0f;
    public bool Upgrading => upgradeTarget > 0;
    public int UpgradeTarget => upgradeTarget;
    public float BuildLeft => buildLeft;
    public float UpgradeLeft => upgradeLeft;
    public float BuildProgress => buildTotal > 0f ? Mathf.Clamp01(1f - buildLeft / buildTotal) : 1f;
    public float UpgradeProgress => upgradeTotal > 0f ? Mathf.Clamp01(1f - upgradeLeft / upgradeTotal) : 1f;

    /// <summary>Сколько осталось ждать (стройка или улучшение).</summary>
    public float TimerLeft => UnderConstruction ? buildLeft : (Upgrading ? upgradeLeft : 0f);

    static bool TimersOff => WarCityMode.Active;   // в военном городе всё встаёт сразу

    /// <summary>Только что поставили: здание сначала строится.</summary>
    public void StartConstruction(float seconds)
    {
        if (seconds <= 0f || TimersOff) return;
        buildTotal = buildLeft = seconds;
        hp = MaxHp * 0.1f;
        ApplyLook();
        if (!IsRoad) Sfx.At("build_place", Center);
        if (IsBarrier || IsBridge) GridNavigation.MarkDirty();   // недостроенная стена - ещё не преграда
        if (IsOutpost) Territory.MarkDirty();
    }

    /// <summary>Материалы донесли: начинается время улучшения (или уровень растёт сразу, если времени нет).</summary>
    public void StartUpgrade(int target)
    {
        float sec = TimersOff ? 0f : Balance.UpgradeSeconds(type, target);
        upgradeInProgress = true;
        if (sec <= 0f) { upgradeTarget = target; FinishUpgrade(false); return; }
        upgradeTarget = target;
        upgradeTotal = upgradeLeft = sec;
    }

    float workSoundIn;   // стук молотка и пила, пока идёт стройка или улучшение

    void TickTimers(float dt)
    {
        if ((buildLeft > 0f || upgradeTarget > 0) && !IsRoad)
        {
            workSoundIn -= dt;
            if (workSoundIn <= 0f)
            {
                workSoundIn = Random.Range(1.2f, 3.2f);
                Sfx.At(Random.value < 0.8f ? "build_hammer" : "build_saw", Center);
            }
        }

        if (buildLeft > 0f)
        {
            buildLeft -= dt;
            if (buildLeft <= 0f) FinishConstruction(true);
            else
            {
                hp = MaxHp * Mathf.Lerp(0.1f, 1f, BuildProgress);
                ApplyLook();
            }
        }
        if (upgradeTarget > 0)
        {
            upgradeLeft -= dt;
            if (upgradeLeft <= 0f) FinishUpgrade(true);
        }
    }

    void FinishConstruction(bool notify)
    {
        buildLeft = buildTotal = 0f;
        hp = MaxHp;
        ApplyLook();
        if (IsBarrier || IsBridge) GridNavigation.MarkDirty();
        if (IsOutpost) Territory.MarkDirty();
        if (notify && type != null && !IsRoad) Sfx.Sting("build_complete", IsBarrier ? 0.5f : 1f);
        if (notify && type != null && !IsRoad && !IsBarrier && !IsBridge)
        {
            WarManager.Notify($"Построено: {type.name}", true);
            FreshMarkers.Mark(this);   // final-8: над новым зданием - заметная метка «Готово!»
        }
    }

    void FinishUpgrade(bool notify)
    {
        int target = upgradeTarget;
        upgradeTarget = 0;
        upgradeLeft = upgradeTotal = 0f;
        upgradeInProgress = false;
        if (target <= level) return;
        level = target;
        // новый уровень - здание как новое (но не посреди набега: раненое здание так не вылечить)
        if (!IsRuined && !UpgradeRules.RaidBlocksUpgrade) hp = MaxHp;
        Territory.MarkDirty();           // выросла Цитадель / Аванпост - зона застройки больше
        if (notify && type != null) Sfx.Sting("upgrade_complete", IsBarrier ? 0.5f : 1f);
        if (notify && type != null && !IsBarrier) WarManager.Notify($"{type.name}: улучшено до Ур. {level}", true);
    }

    /// <summary>Закончить стройку и улучшение этого здания сразу.</summary>
    public void FinishTimers()
    {
        if (UnderConstruction) FinishConstruction(false);
        if (Upgrading) FinishUpgrade(false);
    }

    // ---------- Ускорение ----------

    /// <summary>Цена ускорения: растёт с оставшимся временем и уровнем (стройка - как Ур.1), у Цитадели вдвое.</summary>
    public int SkipCost => Balance.SkipMarks(TimerLeft, UnderConstruction ? 1 : UpgradeTarget, type != null && type.name == "Ратуша");

    public bool CanSkipForMarks
    {
        get
        {
            var rm = ResourceManager.Instance;
            return TimerLeft > 0f && rm != null && rm.marks >= SkipCost;
        }
    }

    public bool SkipForMarks()
    {
        if (!UpgradeRules.SkipForMarksOn || !CanSkipForMarks) return false;
        int cost = SkipCost;
        if (!ResourceManager.Instance.SpendMarks(cost)) return false;
        CityIncome.Skip();
        FinishTimers();
        WarManager.Notify($"{(type != null ? type.name : "Здание")}: готово сразу за {cost} ◈", true);
        return true;
    }

    /// <summary>F7 (админ): все стройки и улучшения в городе - готовы.</summary>
    public static void AdminSkipAll()
    {
        int n = 0;
        foreach (var b in new System.Collections.Generic.List<BuildingInstance>(All))
        {
            if (b == null || b.TimerLeft <= 0f) continue;
            b.FinishTimers();
            n++;
        }
        WarManager.Notify(n > 0 ? $"[Админ F7] Готово сразу: {n} стро{(n == 1 ? "йка" : "ек")} и улучшени{(n == 1 ? "е" : "й")}"
                                : "[Админ F7] Сейчас ничего не строится", n > 0);
    }

    /// <summary>Строка для панели: «Стройка 40% · ещё 1 мин 20 с».</summary>
    public string TimerText
    {
        get
        {
            if (UnderConstruction) return $"Стройка {BuildProgress * 100f:F0}% · ещё {Balance.Dur(buildLeft)}";
            if (Upgrading) return $"Улучшение до Ур. {upgradeTarget}: {UpgradeProgress * 100f:F0}% · ещё {Balance.Dur(upgradeLeft)}";
            return "";
        }
    }

    // ---------- Сохранение (CitySave зовёт всех ISavedBuilding на здании) ----------

    public void SaveState(SaveBag bag)
    {
        string now = RuinClockUtc.ToString("o");
        bag.Set("bld.build", UnderConstruction ? SaveBag.F(buildLeft) + "," + SaveBag.F(buildTotal) + "," + now : null);
        bag.Set("bld.upg", Upgrading ? SaveBag.I(upgradeTarget) + "," + SaveBag.F(upgradeLeft) + "," + SaveBag.F(upgradeTotal) + "," + now : null);
        // Материалы для улучшения ещё несут (всё уже списано): после входа улучшение идёт, как будто их донесли (final-1)
        if (!Upgrading && upgradeInProgress && !TimersOff && type != null)
        {
            float sec = Mathf.Max(0.01f, Balance.UpgradeSeconds(type, level + 1));
            bag.Set("bld.upg", SaveBag.I(level + 1) + "," + SaveBag.F(sec) + "," + SaveBag.F(sec) + "," + now);
        }
        // ...и для укрепления: после входа здание уже укреплено
        bag.Set("bld.fortWait", fortifyInProgress && !fortified ? "1" : null);
        SaveBoost(bag);
    }

    public void LoadState(SaveBag bag)
    {
        LoadBoost(bag);
        if (bag.Get("bld.fortWait") == "1") fortified = true;
        if (TimersOff) return;
        string b = bag.Get("bld.build");
        if (!string.IsNullOrEmpty(b))
        {
            string[] a = b.Split(',');
            float left = SaveBag.AtF(a, 0) - (float)Away(a, 2);
            if (left > 0f)
            {
                buildTotal = Mathf.Max(left, SaveBag.AtF(a, 1));
                buildLeft = left;
                hp = MaxHp * Mathf.Lerp(0.1f, 1f, BuildProgress);
                ApplyLook();
                if (IsBarrier || IsBridge) GridNavigation.MarkDirty();
                if (IsOutpost) Territory.MarkDirty();
            }
        }
        string u = bag.Get("bld.upg");
        if (!string.IsNullOrEmpty(u))
        {
            string[] a = u.Split(',');
            int target = SaveBag.At(a, 0);
            if (target > level)
            {
                upgradeTarget = target;
                upgradeInProgress = true;
                upgradeTotal = Mathf.Max(0.01f, SaveBag.AtF(a, 2));
                upgradeLeft = SaveBag.AtF(a, 1) - (float)Away(a, 3);
                if (upgradeLeft <= 0f) FinishUpgrade(false);
            }
        }
    }

    // Сколько секунд прошло с записи (по часам сервера)
    static double Away(string[] a, int i)
    {
        if (a == null || i >= a.Length) return 0.0;
        if (!System.DateTime.TryParse(a[i], null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime at)) return 0.0;
        return System.Math.Max(0.0, (RuinClockUtc - at.ToUniversalTime()).TotalSeconds);
    }
}
