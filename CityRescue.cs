using UnityEngine;

/// <summary>
/// Выход из тупика (final-8). Тестеры попадали в ловушку: жителей нет (или все заняты), а на новый Жилой дом
/// не хватает ресурсов - и заработать их некому. Если так длится RescueAfter секунд (и нет войны), Цитадель
/// выдаёт недостающее ровно на один Жилой дом и подсказывает, что делать. Не чаще раза в RescueAfter.
/// </summary>
public class CityRescue : MonoBehaviour
{
    const float RescueAfter = 600f;   // 10 минут тупика
    const float HintAfter = 45f;      // столько тупика - и подсказка, что Цитадель поможет
    float stuck, checkTimer;
    bool hinted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<CityRescue>() != null) return;
        var go = new GameObject("CityRescue");
        DontDestroyOnLoad(go);
        go.AddComponent<CityRescue>();
    }

    void Update()
    {
        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        float dt = 2f - checkTimer;
        checkTimer = 2f;

        if (!Stuck(out BuildingType house, out int g, out int w, out int s, out int f))
        {
            stuck = 0f;
            hinted = false;
            return;
        }
        stuck += dt;
        if (!hinted && stuck >= HintAfter)
        {
            hinted = true;
            WarManager.Notify($"Город в тупике: нет свободных рабочих и не на что построить Жилой дом. Если ничего не изменится, через {RescueAfter / 60f:F0} мин Цитадель выдаст ресурсы на один дом.", false);
        }
        if (stuck < RescueAfter) return;

        var rm = ResourceManager.Instance;
        int ag = Mathf.Max(0, g - rm.gold), aw = Mathf.Max(0, w - rm.wood), ast = Mathf.Max(0, s - rm.stone), af = Mathf.Max(0, f - rm.food);
        rm.Add(ag, aw, ast, af);
        stuck = 0f;
        hinted = false;
        WarManager.Notify("Цитадель выручила город: ресурсов хватает на Жилой дом. Постройте его — придут новые жители, и работа пойдёт снова.", true);
    }

    static bool Stuck(out BuildingType house, out int g, out int w, out int s, out int f)
    {
        house = null; g = w = s = f = 0;
        var bp = BuildingPlacer.Instance;
        var pm = PopulationManager.Instance;
        var rm = ResourceManager.Instance;
        if (bp == null || !bp.WorldReady || pm == null || pm.Hall == null || rm == null) return false;
        if (WarCityMode.Active || WarManager.IsWar || RaidManager.ViewingBattle || AccumulationMode.On) return false;
        if (bp.buildingTypes == null) return false;
        house = System.Array.Find(bp.buildingTypes, t => t != null && t.name == PopulationManager.HouseName);
        if (house == null) return false;
        Balance.NextBuildCost(house, out g, out w, out s, out f);
        if (rm.HasEnough(g, w, s, f)) return false;              // на дом хватает - не тупик
        if (pm.FreePeople > 0) return false;                     // есть свободные руки
        // Кто-то добывает дерево или золото - ресурсы скоро будут
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null || b.UnderConstruction || b.IsRealRuin) continue;
            if ((b.type.prodWood > 0 || b.type.prodGold > 0 || b.type.prodStone > 0) && b.workers > 0) return false;
        }
        return true;
    }
}
