using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Клетка моста поворачивается по своему пролёту (final-7). Картинка моста нарисована «с юга на север»:
/// доски поперёк, перила слева и справа. Через реку, что течёт с севера на юг, мост идёт с запада на восток -
/// тогда картинка поворачивается на 90°, и пролёт стыкуется с соседними клетками моста, берегами и дорожками.
/// Как идёт пролёт, клетка решает по соседям: продолжение (мост, дорожка, брусчатка, ворота) весит больше,
/// чем просто берег, вода - ничего. Перила всегда вдоль пролёта. Добавляется на «Мост» в BuildingPlacer.
/// </summary>
public class BridgeLook : MonoBehaviour
{
    BuildingInstance bld;
    SpriteRenderer look;
    float timer;
    int shown = -1;          // 0 - с юга на север (как нарисовано), 1 - с запада на восток
    Vector2Int shownCell;

    // Клетки дорог, мостов и ворот (общая на все мосты, не чаще раза в 0.5 с)
    static readonly HashSet<Vector2Int> links = new HashSet<Vector2Int>();
    static readonly HashSet<Vector2Int> bridges = new HashSet<Vector2Int>();
    static float linksTime = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { links.Clear(); bridges.Clear(); linksTime = -99f; }

    static void RebuildLinks()
    {
        if (Time.unscaledTime - linksTime < 0.5f) return;
        linksTime = Time.unscaledTime;
        links.Clear();
        bridges.Clear();
        foreach (var b in BuildingInstance.All)
        {
            if (b == null || b.type == null || !b.isActiveAndEnabled) continue;
            bool br = b.IsBridge;
            if (!br && !b.IsRoad && !b.IsGate) continue;
            Vector2Int o = b.OriginCell, sz = b.type.size;
            for (int x = 0; x < Mathf.Max(1, sz.x); x++)
                for (int y = 0; y < Mathf.Max(1, sz.y); y++)
                {
                    Vector2Int c = new Vector2Int(o.x + x, o.y + y);
                    links.Add(c);
                    if (br) bridges.Add(c);
                }
        }
    }

    // Сколько «весит» сосед как продолжение пролёта
    static int Weight(Vector2Int c)
    {
        if (bridges.Contains(c)) return 3;   // соседняя клетка моста - главное
        if (links.Contains(c)) return 2;     // дорожка или ворота на берегу
        return Rivers.IsWater(c) ? 0 : 1;    // берег
    }

    /// <summary>0 - пролёт с юга на север, 1 - с запада на восток.</summary>
    public static int SpanAt(Vector2Int c)
    {
        RebuildLinks();
        int ew = Weight(c + Vector2Int.left) + Weight(c + Vector2Int.right);
        int ns = Weight(c + Vector2Int.up) + Weight(c + Vector2Int.down);
        return ew > ns ? 1 : 0;
    }

    void Start()
    {
        bld = GetComponent<BuildingInstance>();
        timer = Random.Range(0f, 0.5f);
    }

    void LateUpdate()
    {
        if (bld == null || bld.type == null) return;
        if (look == null) look = GetComponentInChildren<SpriteRenderer>();
        if (look == null) return;
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f && shown >= 0 && shownCell == bld.OriginCell && Mathf.Abs(look.transform.eulerAngles.z - (shown == 1 ? 90f : 0f)) < 0.5f) return;
        timer = 0.5f;

        int span = SpanAt(bld.OriginCell);
        float want = span == 1 ? 90f : 0f;
        bool angleOk = Mathf.Abs(Mathf.DeltaAngle(look.transform.eulerAngles.z, want)) < 0.5f;
        if (span == shown && shownCell == bld.OriginCell && angleOk) return;
        shown = span;
        shownCell = bld.OriginCell;
        if (angleOk) return;

        // Поворачиваем картинку вокруг центра клетки (точка опоры у картинки может быть не в центре)
        Vector3 center = new Vector3(bld.OriginCell.x + 0.5f, bld.OriginCell.y + 0.5f, 0f);
        Transform t = look.transform;
        t.rotation = Quaternion.Euler(0f, 0f, want);
        Vector3 shift = center - look.bounds.center;
        shift.z = 0f;
        t.position += shift;
    }
}
