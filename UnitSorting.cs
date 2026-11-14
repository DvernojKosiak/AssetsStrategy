using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Порядок рисования бойцов (final-8: «текстуры рыцарей налезают друг на друга»).
/// Человечек собран из нескольких картинок (тело 10, броня 15, оружие 16-17...). Раньше эти слои у двух
/// бойцов, стоящих рядом, перемешивались: броня заднего рисовалась поверх тела переднего.
/// Теперь каждый боец/житель/враг - одна группа (SortingGroup): рисуется целиком, а между собой группы
/// идут по высоте на экране (кто ниже - тот ближе, рисуется позже). Картинки внутри группы - как были.
/// </summary>
public class UnitSorting : MonoBehaviour
{
    const int GroupOrder = 10;   // выше земли и зданий (0), ниже вспышек боя (20+) и ночного затемнения (50)
    float scan;
    Camera lastCam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<UnitSorting>() != null) return;
        var go = new GameObject("UnitSorting");
        DontDestroyOnLoad(go);
        go.AddComponent<UnitSorting>();
    }

    void Update()
    {
        scan -= Time.unscaledDeltaTime;
        if (scan > 0f) return;
        scan = 0.5f;

        Camera cam = Camera.main;
        if (cam != null && cam != lastCam)
        {
            lastCam = cam;
            cam.transparencySortMode = TransparencySortMode.CustomAxis;   // при равном порядке - выше по экрану рисуется раньше
            cam.transparencySortAxis = Vector3.up;
        }

        foreach (var a in CitizenAgent.All) if (a != null) Group(a.gameObject);
        foreach (var e in EnemyAgent.All) if (e != null) Group(e.gameObject);
        foreach (var u in RaidUnit.All) if (u != null) Group(u.gameObject);
    }

    static void Group(GameObject go)
    {
        if (go.GetComponent<SortingGroup>() != null) return;
        var g = go.AddComponent<SortingGroup>();
        g.sortingOrder = GroupOrder;
    }
}
