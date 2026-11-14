using UnityEngine;

/// <summary>
/// Нарисованная трава под городом вместо пиксельного шума.
///  - Плитка травы (Resources/Art/n/grass_tile.png, 8×8 клеток) повторяется по всей карте: травинки, клевер, камушки, цветы.
///  - Поверх - крупные пятна: где трава гуще и темнее, где выгорела на солнце. Плитка перестаёт быть заметной.
///  - Сетка клеток - отдельным тонким слоем (как и раньше, её силу задаёт BuildingPlacer.gridLineStrength).
/// Сам находит землю города (GameMap_Background из BuildingPlacer) и переодевает её; BuildingPlacer не трогает.
/// Если картинки травы нет, остаётся старая земля.
/// </summary>
public class GroundArt : MonoBehaviour
{
    const string BackgroundName = "GameMap_Background";
    const float TileCells = 8f;   // сколько клеток в одной плитке травы

    GameObject dressed;
    Renderer bg;
    Renderer patches, grid;
    float nextLook;

    static GroundArt instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("GroundArt");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GroundArt>();
    }

    void Update()
    {
        if (dressed == null && Time.unscaledTime >= nextLook)
        {
            nextLook = Time.unscaledTime + 0.5f;
            GameObject go = GameObject.Find(BackgroundName);
            if (go != null) Dress(go);
        }
        // Земля под городом выключена (карта войны рисует свою) - наши слои тоже прячем
        if (bg != null)
        {
            bool on = bg.enabled && bg.gameObject.activeInHierarchy;
            if (patches != null && patches.enabled != on) patches.enabled = on;
            if (grid != null && grid.enabled != on) grid.enabled = on;
        }
    }

    void Dress(GameObject go)
    {
        dressed = go;
        Texture2D grass = Resources.Load<Texture2D>("Art/n/grass_tile");
        if (grass == null) return;

        bg = go.GetComponent<Renderer>();
        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (bg == null || mf == null) return;

        float size = Mathf.Max(1f, go.transform.localScale.x);   // квадрат mapSize × mapSize клеток
        grass.wrapMode = TextureWrapMode.Repeat;
        grass.filterMode = FilterMode.Trilinear;
        grass.anisoLevel = 4;

        // Плитка повторяется: растягиваем координаты текстуры на квадрате (Sprites/Default сам повторять не умеет)
        mf.mesh = TiledQuad(size / TileCells);
        bg.material.mainTexture = grass;
        bg.material.color = Color.white;

        // Крупные пятна поверх плитки
        patches = Layer(go, "GroundPatches", PatchTexture(Mathf.RoundToInt(size)), 1f, -99);

        // Сетка клеток
        BuildingPlacer bp = BuildingPlacer.Instance;
        bool lines = bp == null || bp.showGridLines;
        float k = bp != null ? bp.gridLineStrength : 0.10f;
        if (lines && k > 0.001f) grid = Layer(go, "GroundGrid", CellTexture(k), size, -98);
    }

    static Renderer Layer(GameObject parent, string name, Texture2D tex, float tiles, int order)
    {
        GameObject q = new GameObject(name);
        q.transform.SetParent(parent.transform, false);
        q.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        q.AddComponent<MeshFilter>().mesh = TiledQuad(tiles);
        MeshRenderer r = q.AddComponent<MeshRenderer>();
        r.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        r.sortingOrder = order;
        return r;
    }

    /// <summary>Квадрат 1×1 (как у Quad), координаты текстуры от 0 до tiles.</summary>
    static Mesh TiledQuad(float tiles)
    {
        var m = new Mesh { name = "TiledQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(tiles, 0f), new Vector2(0f, tiles), new Vector2(tiles, tiles) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Пятна на всю карту: 4 точки на клетку, мягкие (билинейно растягиваются).</summary>
    static Texture2D PatchTexture(int cells)
    {
        int res = Mathf.Clamp(cells * 4, 16, 1024);
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GroundPatches" };
        var px = new Color32[res * res];
        float k = cells / (float)res;   // клеток на точку
        Color dark = new Color(0.10f, 0.26f, 0.08f), sun = new Color(0.86f, 0.86f, 0.46f), dry = new Color(0.62f, 0.55f, 0.30f);
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float cx = x * k, cy = y * k;
                float big = Mathf.PerlinNoise(311f + cx * 0.07f, 177f + cy * 0.07f);
                float mid = Mathf.PerlinNoise(91f + cx * 0.21f, 403f + cy * 0.21f);
                float spot = Mathf.PerlinNoise(733f + cx * 0.45f, 59f + cy * 0.45f);
                float v = big * 0.7f + mid * 0.3f;
                Color c; float a;
                if (v < 0.5f) { c = dark; a = Mathf.Clamp01((0.5f - v) * 1.5f) * 0.42f; }
                else { c = sun; a = Mathf.Clamp01((v - 0.5f) * 1.5f) * 0.22f; }
                // Кое-где выгоревшие проплешины
                if (spot > 0.70f) { float d = Mathf.Clamp01((spot - 0.70f) * 5f) * 0.20f; c = Color.Lerp(c, dry, d / Mathf.Max(0.01f, a + d)); a = Mathf.Max(a, d); }
                c.a = a;
                px[y * res + x] = c;
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Одна клетка сетки: прозрачная, тонкая тёмная линия по краю.</summary>
    static Texture2D CellTexture(float strength)
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "GroundGrid" };
        var px = new Color32[S * S];
        byte a = (byte)Mathf.Clamp(strength * 255f * 0.6f, 0f, 255f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                bool line = x == 0 || y == 0;
                px[y * S + x] = new Color32(0, 0, 0, line ? a : (byte)0);
            }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}
