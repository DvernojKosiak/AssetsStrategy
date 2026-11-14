using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Дорожка и брусчатка смотрят на соседей и рисуют себе клетку под них:
///  - колея идёт туда, где есть продолжение дороги (прямо, вверх-вниз, поворотом, развилкой);
///  - там, где дороги дальше нет, край мягко уходит в траву (у брусчатки камни торчат в траву, а щели между ними зарастают);
///  - на стыке дорожки и брусчатки камни понемногу тонут в земле, а не обрываются ровной линией.
/// Соседи-продолжения: дорожка, брусчатка, мост и ворота. Картинки клеток собираются в игре из двух основ
/// (Resources/Art/b/road_base и cobble_base) и запоминаются, одинаковых клеток не собирает дважды.
/// Если основ нет (старый набор картинок), ничего не меняет. Добавляется на «Дорожку» и «Брусчатку» в BuildingPlacer.
/// </summary>
public class RoadJoin : MonoBehaviour
{
    const int N = 128;            // клетка - 128 точек, как у остальных картинок зданий
    const int Dirt = 1, Cobble = 2, Link = 3;   // кто стоит в клетке: дорожка, брусчатка, мост / ворота

    BuildingInstance bld;
    SpriteRenderer look;
    float timer;
    int key = -1;
    Sprite want;

    // Клетка → кто в ней (общая на всех, пересобирается не чаще раза в 0.3 с)
    static readonly Dictionary<Vector2Int, int> map = new Dictionary<Vector2Int, int>();
    static float mapTime = -99f;
    static readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();
    static Color32[] dirtPx, cobPx;
    static bool baseTried;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { map.Clear(); mapTime = -99f; sprites.Clear(); dirtPx = cobPx = null; baseTried = false; }

    static int KindOf(BuildingInstance b)
    {
        if (b == null || b.type == null || !b.isActiveAndEnabled) return 0;
        if (b.IsCobble) return Cobble;
        if (b.IsRoad) return Dirt;
        if (b.IsBridge || b.IsGate) return Link;
        return 0;
    }

    static void RebuildMap()
    {
        if (Time.unscaledTime - mapTime < 0.3f) return;
        mapTime = Time.unscaledTime;
        map.Clear();
        foreach (var b in BuildingInstance.All)
        {
            int k = KindOf(b);
            if (k == 0) continue;
            Vector2Int o = b.OriginCell, sz = b.type.size;
            for (int x = 0; x < Mathf.Max(1, sz.x); x++)
                for (int y = 0; y < Mathf.Max(1, sz.y); y++)
                    map[new Vector2Int(o.x + x, o.y + y)] = k;
        }
    }

    static int At(Vector2Int c) { return map.TryGetValue(c, out int k) ? k : 0; }

    static readonly Vector2Int[] Side = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };       // С В Ю З
    static readonly Vector2Int[] Diag = { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1) };   // СВ ЮВ ЮЗ СЗ

    /// <summary>Что вокруг клетки: сама (2 бита), 4 стороны (по 2 бита), 4 угла (есть ли дорога по диагонали).</summary>
    static int KeyAt(Vector2Int c, int self)
    {
        RebuildMap();
        int key = self;
        int[] k = new int[4];
        for (int i = 0; i < 4; i++) { k[i] = At(c + Side[i]); key |= k[i] << (2 + 2 * i); }
        // Угол важен, только если дорога идёт в обе соседние стороны: тогда без дороги по диагонали угол скругляется
        for (int i = 0; i < 4; i++)
            if (k[i] != 0 && k[(i + 1) % 4] != 0 && At(c + Diag[i]) != 0) key |= 1 << (10 + i);
        return key;
    }

    void Start()
    {
        bld = GetComponent<BuildingInstance>();
        look = GetComponentInChildren<SpriteRenderer>();
        timer = Random.Range(0f, 0.3f);
    }

    void LateUpdate()
    {
        if (bld == null || bld.type == null || look == null) return;
        timer -= Time.unscaledDeltaTime;
        if (timer <= 0f || key < 0)
        {
            timer = 0.4f;
            int k = KeyAt(bld.OriginCell, bld.IsCobble ? Cobble : Dirt);
            if (k != key || want == null) { key = k; want = SpriteFor(k); }
        }
        if (want != null && look.sprite != want) look.sprite = want;
    }

    // ---------- Сборка картинки клетки ----------

    static Sprite SpriteFor(int key)
    {
        Sprite s;
        // Картинки, созданные в прошлом запуске Play, Unity уже удалил - собираем заново
        if (sprites.TryGetValue(key, out s) && s != null) return s;
        if (!LoadBases()) return null;
        s = Build(key);
        sprites[key] = s;
        return s;
    }

    static bool LoadBases()
    {
        if (!baseTried)
        {
            baseTried = true;
            dirtPx = Read(Resources.Load<Texture2D>("Art/b/road_base"));
            cobPx = Read(Resources.Load<Texture2D>("Art/b/cobble_base"));
        }
        return dirtPx != null && cobPx != null;
    }

    /// <summary>Точки картинки. Картинки из Resources обычно нельзя читать напрямую - тогда копируем через видеокарту.</summary>
    static Color32[] Read(Texture2D t)
    {
        if (t == null || t.width != N || t.height != N) return null;
        if (t.isReadable) return t.GetPixels32();
        RenderTexture rt = RenderTexture.GetTemporary(N, N, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        RenderTexture prev = RenderTexture.active;
        Graphics.Blit(t, rt);
        RenderTexture.active = rt;
        Texture2D tmp = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(0, 0, N, N), 0, 0);
        tmp.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Color32[] px = tmp.GetPixels32();
        Object.Destroy(tmp);
        return px;
    }

    static readonly Vector2[] Mid = { new Vector2(64, 128), new Vector2(128, 64), new Vector2(64, 0), new Vector2(0, 64) };
    static readonly Vector2[] Corner = { new Vector2(128, 128), new Vector2(128, 0), new Vector2(0, 0), new Vector2(0, 128) };

    static Sprite Build(int key)
    {
        int self = key & 3;
        int[] k = new int[4];
        for (int i = 0; i < 4; i++) k[i] = (key >> (2 + 2 * i)) & 3;
        bool[] round = new bool[4];   // скруглить угол (дорога в обе стороны, а по диагонали - трава)
        for (int i = 0; i < 4; i++) round[i] = k[i] != 0 && k[(i + 1) % 4] != 0 && ((key >> (10 + i)) & 1) == 0;

        List<int> conn = new List<int>();
        for (int i = 0; i < 4; i++) if (k[i] != 0) conn.Add(i);
        List<int> one = new List<int>(1) { 0 };

        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                int idx = y * N + x;
                Color dirt = dirtPx[idx], cob = cobPx[idx];
                float stoneHere = Stone(cob);
                Color col;
                float stone;   // насколько здесь камень (камни торчат в траву дальше, чем щели)

                if (self == Dirt)
                {
                    col = dirt * Ruts(fx, fy, conn);
                    stone = 0f;
                    // У брусчатки отдельные камни заходят на дорожку
                    for (int i = 0; i < 4; i++)
                    {
                        if (k[i] != Cobble) continue;
                        float t = 1f - EdgeDist(i, fx, fy) / 40f;
                        float b = SmoothStep(0.45f, 0.75f, t + (Noise(fx, fy, 9) - 0.5f) * 0.9f) * stoneHere;
                        if (b > 0f) col = Color.Lerp(col, cob, b);
                    }
                }
                else
                {
                    col = cob;
                    stone = stoneHere;
                    // К дорожке камни понемногу тонут в земле: сначала щели, потом сами камни
                    for (int i = 0; i < 4; i++)
                    {
                        if (k[i] != Dirt) continue;
                        float t = 1f - EdgeDist(i, fx, fy) / 56f;
                        float b = SmoothStep(0.25f, 0.6f, t * 1.4f + (Noise(fx, fy, 9) - 0.5f) * 1.2f - stoneHere * 0.3f);
                        if (b <= 0f) continue;
                        one[0] = i;
                        col = Color.Lerp(col, dirt * Ruts(fx, fy, one), b);
                        stone *= 1f - b;
                    }
                }

                // Край в траву: неровный, мягкий
                float n = Noise(fx, fy, 1);
                float a = 1f;
                for (int i = 0; i < 4; i++)
                    if (k[i] == 0) a *= Fade(EdgeDist(i, fx, fy), n, stone);
                for (int i = 0; i < 4; i++)
                    if (round[i]) a *= Fade(Vector2.Distance(new Vector2(fx, fy), Corner[i]), n, stone);

                if (self == Dirt) col *= 0.86f + 0.14f * a;   // земля к краю чуть темнее
                col.a = a;
                px[idx] = col;
            }

        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            name = "road_" + key
        };
        tex.SetPixels32(px);
        tex.Apply(true, true);
        Sprite s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 128f, 0, SpriteMeshType.FullRect);
        s.name = tex.name;
        return s;
    }

    /// <summary>Расстояние (в точках) от точки до стороны клетки: 0 С, 1 В, 2 Ю, 3 З.</summary>
    static float EdgeDist(int side, float x, float y)
    {
        switch (side)
        {
            case 0: return N - y;
            case 1: return N - x;
            case 2: return y;
            default: return x;
        }
    }

    static float Stone(Color c) { return SmoothStep(128f / 255f, 150f / 255f, (c.r + c.g + c.b) / 3f); }

    static float Fade(float d, float n, float stone)
    {
        float m = 3f + 10f * n;
        return SmoothStep(m - 6f, m + 6f, d + stone * 8f);
    }

    /// <summary>Колея: два следа колёс по 17 точек от середины дороги. Возвращает множитель яркости.</summary>
    static float Ruts(float x, float y, List<int> sides)
    {
        if (sides.Count == 0) return 1f;
        float dc;
        Vector2 p = new Vector2(x, y);
        if (sides.Count == 2 && (sides[0] + 2) % 4 != sides[1])
        {
            // Поворот: колея идёт дугой вокруг угла клетки
            bool n = sides.Contains(0), e = sides.Contains(1);
            Vector2 c = new Vector2(e ? N : 0, n ? N : 0);
            dc = Mathf.Abs(Vector2.Distance(p, c) - 64f);
        }
        else
        {
            Vector2 center = new Vector2(64f, 64f);
            dc = float.MaxValue;
            foreach (int s in sides) dc = Mathf.Min(dc, SegDist(p, center, Mid[s]));
        }
        float d = dc - 17f;
        float r = Mathf.Exp(-(d * d) / (2f * 2.4f * 2.4f));
        return 1f - 0.14f * r * (0.7f + 0.6f * Noise(x, y, 5));
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // Шум, повторяющийся каждые 128 точек: края соседних клеток совпадают
    static float Noise(float x, float y, int seed) { return 0.65f * ValueNoise(x, y, 8, seed) + 0.35f * ValueNoise(x, y, 16, seed + 7); }

    static float ValueNoise(float x, float y, int cells, int seed)
    {
        float c = (float)N / cells;
        float fx = x / c, fy = y / c;
        int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fy);
        float tx = fx - i, ty = fy - j;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);
        float a = Hash(Wrap(i, cells), Wrap(j, cells), seed), b = Hash(Wrap(i + 1, cells), Wrap(j, cells), seed);
        float d = Hash(Wrap(i, cells), Wrap(j + 1, cells), seed), e = Hash(Wrap(i + 1, cells), Wrap(j + 1, cells), seed);
        float lo = a + (b - a) * tx, hi = d + (e - d) * tx;
        return lo + (hi - lo) * ty;
    }

    static int Wrap(int i, int n) { return ((i % n) + n) % n; }

    static float Hash(int i, int j, int s)
    {
        unchecked
        {
            uint n = (uint)i * 374761393u + (uint)j * 668265263u + (uint)s * 1442695041u;
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0xffff) / 65535f;
        }
    }
}
