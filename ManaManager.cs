using UnityEngine;

/// <summary>
/// Мана в воздухе. С началом каждого нового дня уровень выпадает случайно: 0-100%.
/// От него зависит сила всех целителей: гражданских, военных и лазарета.
/// Эффективность = minEfficiency + мана * (maxEfficiency - minEfficiency).
/// Создаётся в BuildingPlacer.Start.
/// </summary>
public class ManaManager : MonoBehaviour
{
    public static ManaManager Instance { get; private set; }

    [Header("Эффективность магов")]
    public float minEfficiency = 0.25f;   // при 0% маны
    public float maxEfficiency = 1.25f;   // при 100% маны (при 75% ровно x1)

    public int Percent { get; private set; } = 100;
    int lastDay = -1;

    public static float Factor
    {
        get
        {
            if (Instance == null) return 1f;
            return Mathf.Lerp(Instance.minEfficiency, Instance.maxEfficiency, Instance.Percent / 100f);
        }
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        var clock = GameClock.Instance;
        if (clock == null || clock.DayIndex == lastDay) return;
        bool firstLook = lastDay < 0;
        lastDay = clock.DayIndex;
        Percent = DayMana(clock.DayIndex);
        // Финал: мана дня зависит только от города и номера дня. Раньше она выпадала заново при каждом входе в игру
        // (и после сворачивания), так что её можно было «перекрутить», а сообщение о новом дне приходило при каждом входе.
        if (!firstLook) WarManager.Notify($"Новый день: мана {Percent}% — маги лечат на {Mathf.RoundToInt(Factor * 100f)}%", Percent >= 50);
    }

    static int DayMana(int day)
    {
        int seed = BuildingPlacer.Instance != null ? BuildingPlacer.Instance.WorldSeed : 0;
        unchecked
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(day * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (int)(h % 101u);
        }
    }

    // Мана видна в плашке часов наверху (GameClock): «✦ 64%» и тонкая полоска.
}
