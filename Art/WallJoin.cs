using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стена и ворота смотрят на соседей и берут подходящую картинку: прямой кусок поперёк или вдоль, угол, развилка, конец.
/// Соседи - стены и ворота. Ворота в стене, идущей вверх-вниз, рисуются вдоль неё; открытые - распахнуты.
/// Если картинок нет (старый набор), ничего не меняет. Добавляется на «Стену» и «Ворота» в BuildingPlacer.
/// </summary>
public class WallJoin : MonoBehaviour
{
    BuildingInstance bld;
    SpriteRenderer look;
    GateController gate;
    float timer;

    // Клетка → стена / ворота / башня (общая на всех, пересобирается не чаще раза в 0.3 с)
    static readonly Dictionary<Vector2Int, BuildingInstance> map = new Dictionary<Vector2Int, BuildingInstance>();
    static float mapTime = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { map.Clear(); mapTime = -99f; }

    static bool Joins(BuildingInstance b)
    {
        return b != null && b.type != null && b.isActiveAndEnabled && (b.IsWall || b.IsGate);   // final-13-fix-3: башни - нет (стена «прилипала» к башне)
    }

    static void RebuildMap()
    {
        if (Time.unscaledTime - mapTime < 0.3f) return;
        mapTime = Time.unscaledTime;
        map.Clear();
        foreach (var b in BuildingInstance.All)
        {
            if (!Joins(b)) continue;
            Vector2Int o = b.OriginCell, sz = b.type.size;
            for (int x = 0; x < Mathf.Max(1, sz.x); x++)
                for (int y = 0; y < Mathf.Max(1, sz.y); y++)
                    map[new Vector2Int(o.x + x, o.y + y)] = b;
        }
    }

    /// <summary>Соседи клетки: 1 север, 2 восток, 4 юг, 8 запад.</summary>
    public static int MaskAt(Vector2Int c, BuildingInstance self)
    {
        RebuildMap();
        int m = 0;
        if (Has(c + Vector2Int.up, self)) m |= GameArt.JoinN;
        if (Has(c + Vector2Int.right, self)) m |= GameArt.JoinE;
        if (Has(c + Vector2Int.down, self)) m |= GameArt.JoinS;
        if (Has(c + Vector2Int.left, self)) m |= GameArt.JoinW;
        return m;
    }

    static bool Has(Vector2Int c, BuildingInstance self)
    {
        return map.TryGetValue(c, out BuildingInstance b) && b != null && b != self;
    }

    /// <summary>Ворота в стене вдоль (вверх-вниз)? - для засова и панели.</summary>
    public bool Vertical { get; private set; }

    void Start()
    {
        bld = GetComponent<BuildingInstance>();
        gate = GetComponent<GateController>();
        look = GetComponentInChildren<SpriteRenderer>();
        timer = Random.Range(0f, 0.3f);
    }

    int mask = -1;

    void LateUpdate()
    {
        if (bld == null || bld.type == null || look == null) return;
        timer -= Time.unscaledDeltaTime;
        if (timer <= 0f || mask < 0)
        {
            timer = 0.4f;
            mask = MaskAt(bld.OriginCell, bld);
        }

        Sprite want;
        if (bld.IsGate)
        {
            Vertical = GameArt.GateVertical(mask);
            bool open = gate != null && !bld.IsRuined && gate.OpenAmount > 0.5f;
            want = GameArt.GateLook(bld.level, Vertical, open);
        }
        else want = GameArt.WallJoined(bld.level, mask);

        if (want != null && look.sprite != want) look.sprite = want;
    }
}
