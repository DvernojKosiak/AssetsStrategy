using UnityEngine;
using System.Collections.Generic;

public enum RaidBuildingKind
{
    Hall, House, Farm, Sawmill, Mine, Warehouse, Workshop, Barracks, Kitchen, Stable, Tower, Cannon, Wall, Gate,
    Barricade,  // заслон из брёвен: защитники ставят на подходах и прячутся за ним
    Gun         // пушка на земле: ядра прямой наводкой, осколки; через стены и дома не бьёт
}

/// <summary>
/// Здание вражеской деревни на карте набега.
///  - У здания есть прочность и склад (Loot). Разрушили - склад уносят наши бойцы, которые стояли рядом.
///  - Башня стреляет стрелами, магическая пушка - ядрами по площади (магию щит не держит). Стреляют только в бою.
///  - Стены и ворота загораживают проход. Разрушенные - проходимый завал. Поле фермы проходимо всегда.
///  - Заслон (Barricade) защитники ставят на подходах заранее или по тревоге: пока его строят (Rising), он
///    не загораживает и хрупкий. За заслоном (и за стеной) защитник получает меньше урона от тех, кто снаружи.
/// Создаёт BotVillage, удаляет RaidManager вместе с картой.
/// </summary>
public class RaidBuilding : MonoBehaviour
{
    public static readonly List<RaidBuilding> All = new List<RaidBuilding>();

    public RaidBuildingKind Kind { get; private set; }
    public string Title { get; private set; }
    public Vector2Int OriginCell { get; private set; }
    public Vector2Int SizeCells { get; private set; }
    public float Hp { get; private set; }
    public float MaxHp { get; private set; }
    public bool IsRuined { get; private set; }
    public readonly Loot Storage = new Loot();
    /// <summary>Деревня игрока: сложные вещи в здании (Spoils). Разрушили - по ним жребий на копию и кражу, потом список пуст.</summary>
    public List<SpoilStack> Stash { get; private set; }

    public void SetStash(List<SpoilStack> src)
    {
        Stash = null;
        if (src == null || src.Count == 0) return;
        Stash = new List<SpoilStack>();
        foreach (var x in src) if (x != null && x.count > 0) Stash.Add(new SpoilStack { key = x.key, count = x.count, group = x.group });
    }

    public void ClearStash() { Stash = null; }

    public bool IsBarrier => Kind == RaidBuildingKind.Wall || Kind == RaidBuildingKind.Gate || Kind == RaidBuildingKind.Barricade;
    public bool IsBarricade => Kind == RaidBuildingKind.Barricade;
    public bool Rising => riseLeft > 0f;   // заслон ещё строят
    public bool IsDefense => Kind == RaidBuildingKind.Tower || Kind == RaidBuildingKind.Cannon || Kind == RaidBuildingKind.Gun;
    public bool IsHall => Kind == RaidBuildingKind.Hall;
    /// <summary>Аванпост Ур.4 деревни игрока: стреляет, как башня, только намного слабее (OutpostController.Archer*).</summary>
    public bool OutpostArcher => TypeName == OutpostController.TypeName && Level >= OutpostController.ArcherLevel;
    public bool Blocks => !(Kind == RaidBuildingKind.Farm || IsRuined || Rising);   // руины (любого здания) проходимы
    public bool UnderAttack => Time.time - lastHit < 3f;

    // ---------- Оборона ----------
    // Урон чуть ослаблен (было 9 / 20 / 35, осколки 10). Тяжёлого Рыцаря башни бьют первым (RaidUnit.TowerTarget).
    const float TowerRange = 6.5f, TowerDamage = 7.7f, TowerCooldown = 1.3f;
    const float CannonRange = 8f, CannonDamage = 17f, CannonCooldown = 3.5f, CannonBlast = 1.4f;
    const float GunRange = 9f, GunDamage = 30f, GunCooldown = 3.5f, GunFragDamage = 8.5f;
    const float GunMinRange = 1.6f;            // вплотную ствол не опустить (как у нашей пушки, GroundCannonController.MinRange)
    public const float GunTailFree = 0.7f;     // боец у стены дома: угол этого дома прямую к нему не закрывает

    float powerMul = 1f;
    float riseLeft, riseTotal;
    const float CoverMul = 0.6f;     // столько урона проходит к защитнику, укрытому заслоном или стеной
    float shootTimer;
    float lastHit = -99f;
    SpriteRenderer look;
    Color baseColor;

    public Vector2 Center => RaidMap.Local(OriginCell.x + SizeCells.x / 2f, OriginCell.y + SizeCells.y / 2f);

    public string TypeName { get; private set; }   // деревня игрока: его вид здания (картинка - как у него)
    public int Level { get; private set; }

    public static RaidBuilding Create(RaidBuildingKind kind, string title, Vector2Int origin, Vector2Int size, float maxHp, Color color, float powerMul,
                                      string typeName = null, int level = 0)
    {
        GameObject go = new GameObject("Raid_" + title);
        RaidBuilding b = go.AddComponent<RaidBuilding>();
        b.Kind = kind;
        b.Title = title;
        b.OriginCell = origin;
        b.SizeCells = size;
        b.MaxHp = b.Hp = Mathf.Max(1f, maxHp);
        b.baseColor = color;
        b.powerMul = powerMul;
        b.TypeName = typeName;
        b.Level = level;
        b.shootTimer = Random.Range(0f, 1f);
        go.transform.position = new Vector3(b.Center.x, b.Center.y, 0f);

        // Сторона высадки снизу / сверху: в мире ширина и высота здания меняются местами (final-1)
        Vector2 wsz = RaidMap.WorldSize(new Vector2(size.x, size.y));

        // Квадрат здания (чуть меньше клеток, чтобы были видны швы)
        GameObject body = new GameObject("Body");
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(wsz.x - 0.08f, wsz.y - 0.08f, 1f);
        b.look = body.AddComponent<SpriteRenderer>();
        b.look.sprite = CombatFx.Square();
        b.look.color = color;
        b.look.sortingOrder = kind == RaidBuildingKind.Farm ? -50 : 0;

        // Нарисованная картинка здания (растягивается на его клетки)
        // У деревни игрока - картинка его здания его уровня (аванпост, кузня, рынок...), у соседей - по виду
        Sprite art = typeName != null ? GameArt.Building(typeName, level) : null;
        if (art == null) art = GameArt.Raid(kind);
        if (art != null)
        {
            b.look.sprite = art;
            b.baseColor = Color.white;
            b.look.color = Color.white;
            Vector2 a = art.bounds.size;
            body.transform.localScale = new Vector3(wsz.x / a.x, wsz.y / a.y, 1f);
            // Деревня соседа повёрнута стороной подхода (юг, север): клетки длинного здания легли поперёк,
            // а картинка нарисована стоя - не растягиваем её, а вписываем целиком (final-8).
            // У деревни игрока план повёрнут вместе с городом - там клетки и картинка совпадают.
            if (typeName == null && size.x != size.y && RaidMap.Side >= 2)
            {
                float k = Mathf.Min(wsz.x / a.x, wsz.y / a.y);
                body.transform.localScale = new Vector3(k, k, 1f);
            }
            ArtBuildingFx.Attach(b.look);
            if (kind == RaidBuildingKind.Gun && GameArt.FxBarrel() != null)
            {
                // на картинке пушки только поворотный круг - ствол отдельно, смотрит на подход
                GameObject br = new GameObject("Barrel");
                br.transform.SetParent(go.transform, false);
                br.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
                // дуло к нападающим: туда, откуда пришли (сторона высадки)
                Vector2 toward = RaidMap.WorldDir(Vector2.left);
                float aimDeg = Mathf.Atan2(toward.y, toward.x) * Mathf.Rad2Deg;
                br.transform.localPosition = new Vector3(toward.x * 0.5f, toward.y * 0.5f, 0f);
                br.transform.localRotation = Quaternion.Euler(0f, 0f, aimDeg);
                SpriteRenderer bsr = br.AddComponent<SpriteRenderer>();
                bsr.sprite = GameArt.FxBarrel();
                bsr.sortingOrder = 2;
                if (GameArt.FxMount() != null)
                {
                    // вертлюг с цапфами под стволом
                    GameObject mt = new GameObject("Mount");
                    mt.transform.SetParent(go.transform, false);
                    mt.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
                    mt.transform.localPosition = new Vector3(toward.x * 0.1f, toward.y * 0.1f, 0f);
                    mt.transform.localRotation = Quaternion.Euler(0f, 0f, aimDeg);
                    SpriteRenderer msr = mt.AddComponent<SpriteRenderer>();
                    msr.sprite = GameArt.FxMount();
                    msr.sortingOrder = 1;
                }
            }
            return b;
        }

        // Башне и пушке - тёмная "крыша", чтобы отличать от домов
        if (b.IsDefense)
        {
            GameObject top = new GameObject("Top");
            top.transform.SetParent(go.transform, false);
            top.transform.localScale = new Vector3(wsz.x * 0.45f, wsz.y * 0.45f, 1f);
            SpriteRenderer sr = top.AddComponent<SpriteRenderer>();
            sr.sprite = CombatFx.Square();
            sr.color = new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f);
            sr.sortingOrder = 1;
        }
        return b;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ---------- Геометрия ----------

    public Vector2 ClosestPoint(Vector2 p)
    {
        Rect r = RaidMap.WorldRect(OriginCell, SizeCells);
        return new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
    }

    public float DistanceTo(Vector2 p) { return Vector2.Distance(p, ClosestPoint(p)); }

    public bool Contains(Vector2 p) { return DistanceTo(p) <= 0.01f; }

    // ---------- Урон ----------

    public void TakeDamage(float dmg, RaidUnit by)
    {
        if (IsRuined || dmg <= 0f) return;
        Hp -= dmg;
        lastHit = Time.time;
        Sfx.At(dmg >= MaxHp * 0.08f ? "ram_hit" : "building_hit", Center);
        if (RaidManager.Instance != null) RaidManager.Instance.Alarm(Center, IsHall ? 22f : 14f);
        if (Hp <= 0f) Ruin(by);
    }

    /// <summary>Живой бой: прочность и руины - как сейчас у здания в деревне защитника (его игра считает бой).</summary>
    public void SetNet(float frac, bool ruined)
    {
        if (IsRuined) return;
        if (ruined) { Ruin(null); return; }
        float hp = Mathf.Clamp(frac, 0.01f, 1f) * MaxHp;
        if (hp < Hp - 0.5f) lastHit = Time.time;
        Hp = hp;
    }

    /// <summary>Заслон начинают строить: seconds - сколько строят. Пока строят, он проходим и хрупкий.</summary>
    public void StartRising(float seconds)
    {
        riseTotal = riseLeft = Mathf.Max(0.1f, seconds);
        Hp = MaxHp * 0.15f;
        ApplyRiseLook();
    }

    void ApplyRiseLook()
    {
        if (look == null) return;
        float k = riseTotal > 0f ? 1f - riseLeft / riseTotal : 1f;
        look.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(0.3f, 1f, k));
    }

    /// <summary>
    /// Сколько урона дойдёт до защитника в точке target от удара / выстрела из from:
    /// если рядом с ним (вплотную) целый заслон или стена, и она между ним и стрелком - урон меньше.
    /// </summary>
    public static float CoverFor(Vector2 target, Vector2 from)
    {
        Vector2 toShooter = from - target;
        if (toShooter.sqrMagnitude < 0.01f) return 1f;
        foreach (var b in All)
        {
            if (b == null || b.IsRuined || b.Rising || !b.IsBarrier) continue;
            float d = b.DistanceTo(target);
            if (d > 1.4f) continue;
            Vector2 toWall = b.ClosestPoint(target) - target;
            if (Vector2.Dot(toWall, toShooter) <= 0f) continue;          // стенка с другой стороны
            if (b.DistanceTo(from) <= d + 0.3f) continue;                // стрелок у самой стенки
            return CoverMul;
        }
        return 1f;
    }

    void Ruin(RaidUnit by)
    {
        Hp = 0f;
        IsRuined = true;
        riseLeft = 0f;
        Sfx.At("building_collapse", Center);
        look.color = new Color(baseColor.r * 0.3f, baseColor.g * 0.3f, baseColor.b * 0.3f, 0.85f);
        if (RaidMap.Instance != null) RaidMap.Instance.MarkDirty();   // руины - проходимы
        CombatFx.Death(Center);
        if (RaidManager.Instance != null) RaidManager.Instance.OnBuildingRuined(this, by);
    }

    // ---------- Стена соединяется с соседями (как дома, см. WallJoin) ----------

    bool joined;

    void JoinWall()
    {
        joined = true;
        if (look == null || (Kind != RaidBuildingKind.Wall && Kind != RaidBuildingKind.Gate)) return;
        int m = 0;
        foreach (var o in All)
        {
            if (o == null || o == this || (o.Kind != RaidBuildingKind.Wall && o.Kind != RaidBuildingKind.Gate)) continue;   // final-13-fix-3: только стены и ворота
            Vector2Int a = o.OriginCell, z = o.SizeCells;
            Vector2Int c = OriginCell;
            bool inX = c.x >= a.x && c.x < a.x + z.x, inY = c.y >= a.y && c.y < a.y + z.y;
            if (inX && c.y + 1 == a.y) m |= GameArt.JoinN;
            if (inX && c.y - 1 == a.y + z.y - 1) m |= GameArt.JoinS;
            if (inY && c.x + 1 == a.x) m |= GameArt.JoinE;
            if (inY && c.x - 1 == a.x + z.x - 1) m |= GameArt.JoinW;
        }
        int lv = Level > 0 ? Level : GameArt.RaidLevel;
        Sprite s = Kind == RaidBuildingKind.Gate ? GameArt.GateLook(lv, GameArt.GateVertical(m), false)
                                                 : GameArt.WallJoined(lv, m);
        if (s != null && look.sprite != CombatFx.Square()) look.sprite = s;   // у квадратика (картинок нет) ничего не меняем
    }

    // ---------- Стрельба ----------

    void Update()
    {
        if (!joined) JoinWall();   // в первом кадре все здания деревни уже стоят
        if (riseLeft > 0f && !IsRuined)
        {
            riseLeft -= Time.deltaTime;
            Hp = Mathf.Min(MaxHp, Hp + MaxHp * 0.85f * Time.deltaTime / riseTotal);
            ApplyRiseLook();
            if (riseLeft <= 0f)
            {
                riseLeft = 0f;
                if (RaidMap.Instance != null) RaidMap.Instance.MarkDirty();   // достроили - загораживает
            }
        }
        if ((!IsDefense && !OutpostArcher) || IsRuined) return;
        RaidManager rm = RaidManager.Instance;
        if (rm == null || !rm.BattleOn) return;

        shootTimer -= Time.deltaTime;
        if (shootTimer > 0f) return;

        if (OutpostArcher)
        {
            RaidUnit a = RaidUnit.TowerTarget(Center, OutpostController.ArcherRange + SizeCells.x * 0.5f);
            if (a == null) { shootTimer = 0.3f; return; }
            shootTimer = OutpostController.ArcherCooldown;
            CombatFx.Arrow(Center, a.Position);
            a.TakeDamage(OutpostController.ArcherDamage * TowerController.DefenseBalance * powerMul, null, false, Center);
            return;
        }

        if (Kind == RaidBuildingKind.Gun) { GunShoot(); return; }

        float range = Kind == RaidBuildingKind.Tower ? TowerRange : CannonRange;
        RaidUnit t = RaidUnit.TowerTarget(Center, range + SizeCells.x * 0.5f);
        if (t == null) { shootTimer = 0.3f; return; }

        if (Kind == RaidBuildingKind.Tower)
        {
            shootTimer = TowerCooldown;
            CombatFx.Arrow(Center, t.Position);
            t.TakeDamage(TowerDamage * powerMul, null, false, Center);
        }
        else
        {
            shootTimer = CannonCooldown;
            Vector2 at = t.Position;
            Vector2 from = Center;
            float dmg = CannonDamage * powerMul;
            CombatFx.Shell(Center, at, 0.9f, CannonBlast, () =>
            {
                // Пока ядро летело, поле боя могло исчезнуть. Откуда летело - для щита с ядром хаоса
                foreach (var u in new List<RaidUnit>(RaidUnit.All))
                    if (u != null && u.Attacker && u.Alive && Vector2.Distance(u.Position, at) <= CannonBlast)
                        u.TakeDamage(dmg, null, true, from);
            });
        }
    }

    /// <summary>Пушка соседей: бьёт только того, кого видит прямо (стены и дома заслоняют), ядро разлетается осколками.</summary>
    void GunShoot()
    {
        RaidBuilding self = this;
        RaidUnit t = RaidUnit.TowerTarget(Center, GunRange + SizeCells.x * 0.5f,
            u => Vector2.Distance(u.Position, Center) > GunMinRange && CannonBallistics.RaidFirstBlock(Center, u.Position, self, null, out _, GunTailFree) == null);
        if (t == null) { shootTimer = 0.4f; return; }
        shootTimer = GunCooldown;
        CannonShot shot = new CannonShot
        {
            damage = GunDamage * powerMul,
            buildingMul = 1f,
            barrierMul = 1f,
            fragments = 5,
            fragDamage = GunFragDamage * powerMul,
            fragRange = 2.2f,
            fragSpread = 32f,
            mana = 1f
        };
        CannonBallistics.FireRaid(Center, t.Position, t, null, shot, null, this, false);
    }

    // ---------- Подсказка ----------

    public string TooltipText
    {
        get
        {
            string hp = IsRuined ? "<color=#FF8060>руины</color>" : $"прочность {Mathf.CeilToInt(Hp)}/{Mathf.CeilToInt(MaxHp)}";
            string loot = Storage.IsEmpty ? "" : "\nсклад: <color=#FFD040>" + Storage.ShortText + "</color>";
            if (IsBarricade && !IsRuined)
                return $"<b>{Title}</b>  {hp}\n" + (Rising ? "<color=#FFD060>строят — пока не загораживает</color>" : "<color=#FF9060>защитники за ним получают меньше урона</color>");
            string def = OutpostArcher && !IsRuined ? "\n<color=#FF9060>сторожевой аванпост: отстреливается (слабее башни)</color>"
                : IsDefense && !IsRuined ? (Kind == RaidBuildingKind.Tower ? "\n<color=#FF9060>стреляет по нашим</color>"
                : Kind == RaidBuildingKind.Gun ? "\n<color=#FF9060>бьёт ядрами с осколками; из-за стен и домов не достанет</color>"
                : "\n<color=#FF9060>бьёт магией по площади</color>") : "";
            return $"<b>{Title}</b>  {hp}{def}{loot}";
        }
    }
}
