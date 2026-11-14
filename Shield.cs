using UnityEngine;

/// <summary>
/// Щит: вид качества, улучшение золотом и прочность. Делается в Оружейной, выдаётся бойцам из казармы.
/// При каждом физическом ударе врага:
///  1. шанс ПОЛНОСТЬЮ отразить удар (мал, зависит от качества и улучшений, по тяжёлым ударам ещё ниже);
///  2. иначе шанс заблокировать - тогда входящий урон режется на ReduceFraction;
///  3. иначе урон проходит полностью.
/// Каждый поглощённый удар изнашивает щит на 1. Магический урон щит не блокирует.
/// Боец погиб - щит погиб. Уволили - щит возвращается в Оружейную.
/// Ядро хаоса (школа Хаоса, кнопка в Магической оружейной): с небольшим шансом щит ОТРАЖАЕТ снаряд, прилетевший
/// издалека (стрелу, заряд, ядро), обратно в того, кто стрелял: в бойца или в постройку (башню, пушку).
/// Ядро держит несколько отражений (по марке), потом иссякает. Ядро остаётся на щите и при продаже на рынке.
/// Все числа - в таблицах, их можно менять.
/// </summary>
public class Shield
{
    public readonly QualityTier quality;
    public int upgrade;            // 0 - обычный, 1 - усиленный, 2 - позолоченный, 3 - золотой
    public float durability;
    public float maxDurability;

    // Ядро хаоса на щите (сохраняются и продаются вместе со щитом)
    public int chaosMk;        // 0 - ядра нет, 1..3 - марка ядра
    public int chaosCharges;   // сколько ещё отражений осталось

    public const int MaxUpgrade = 3;

    // Шанс блока
    static readonly float[] BlockByQuality = { 0.55f, 0.65f, 0.75f };
    static readonly float[] BlockUpgrade = { 0f, 0.05f, 0.08f, 0.12f };
    // Какую долю урона срезает блок
    static readonly float[] ReduceByQuality = { 0.40f, 0.50f, 0.60f };
    static readonly float[] ReduceUpgrade = { 0f, 0.05f, 0.10f, 0.15f };
    // Шанс полного отражения
    static readonly float[] ReflectByQuality = { 0.02f, 0.035f, 0.05f };
    static readonly float[] ReflectUpgrade = { 0f, 0.01f, 0.02f, 0.04f };
    // Прочность (в поглощённых ударах)
    static readonly float[] DurabilityByQuality = { 250f, 380f, 550f };
    static readonly float[] UpgradeDurability = { 1f, 1.2f, 1.4f, 1.7f };
    // Цена улучшения до уровня 1 / 2 / 3, золото
    static readonly int[] UpgradeGold = { 800, 2000, 4000 };

    // Ядро хаоса:                              Мк.I   Мк.II  Мк.III
    static readonly float[] ChaosChanceByMk =   { 0.06f, 0.08f, 0.11f };   // шанс отразить снаряд
    static readonly int[] ChaosReflectsByMk =   { 12,    16,    20 };      // сколько отражений держит ядро
    /// <summary>Ближе этого удар считается ударом вплотную (меч), а не снарядом - его ядро не отражает.</summary>
    public const float ChaosMinDistance = 1.8f;

    static readonly string[] TierNames = { "Обычный", "Хороший", "Превосходный" };
    static readonly string[] UpgradeNames = { "", "усиленный", "позолоченный", "золотой" };

    public Shield(QualityTier quality)
    {
        this.quality = quality;
        maxDurability = CalcMaxDurability();
        durability = maxDurability;
    }

    float CalcMaxDurability()
    {
        return DurabilityByQuality[(int)quality] * UpgradeDurability[Mathf.Clamp(upgrade, 0, MaxUpgrade)];
    }

    public static string UpgradeName(int tier) { return UpgradeNames[Mathf.Clamp(tier, 0, MaxUpgrade)]; }
    public static int UpgradeCost(int tier) { return UpgradeGold[Mathf.Clamp(tier, 1, MaxUpgrade) - 1]; }

    public string Name
    {
        get
        {
            string up = upgrade > 0 ? UpgradeNames[upgrade] + " " : "";
            string chaos = HasChaos ? " с ядром хаоса" : "";
            return TierNames[(int)quality] + " " + up + "щит" + chaos;
        }
    }

    int U { get { return Mathf.Clamp(upgrade, 0, MaxUpgrade); } }

    public float BlockChance { get { return Mathf.Clamp01(BlockByQuality[(int)quality] + BlockUpgrade[U]); } }
    public float ReduceFraction { get { return Mathf.Clamp01(ReduceByQuality[(int)quality] + ReduceUpgrade[U]); } }
    public float ReflectChance { get { return Mathf.Clamp01(ReflectByQuality[(int)quality] + ReflectUpgrade[U]); } }

    // Тяжёлые удары (рыцарь) отразить труднее, лёгкие (стрела) - легче
    public float ReflectChanceVs(float dmg)
    {
        return Mathf.Clamp01(ReflectChance * Mathf.Clamp(12f / Mathf.Max(1f, dmg), 0.5f, 1.25f));
    }

    public float Frac { get { return maxDurability > 0f ? Mathf.Clamp01(durability / maxDurability) : 0f; } }
    public int Percent { get { return Mathf.RoundToInt(Frac * 100f); } }

    // Чем выше - тем лучше щит (для автовыдачи)
    public float Score { get { return (BlockChance * ReduceFraction + ReflectChance + ChaosChance) * (0.6f + 0.4f * Frac); } }

    // ---------- Ядро хаоса ----------

    static int MkIndex(int mk) { return Mathf.Clamp(mk, 1, 3) - 1; }
    public static float ChaosChanceOf(int mk) { return ChaosChanceByMk[MkIndex(mk)]; }
    public static int ChaosReflectsOf(int mk) { return ChaosReflectsByMk[MkIndex(mk)]; }

    public bool HasChaos { get { return chaosMk > 0 && chaosCharges > 0; } }
    public float ChaosChance { get { return HasChaos ? ChaosChanceOf(chaosMk) : 0f; } }

    /// <summary>Наложить ядро хаоса (ModuleKind.ChaosCore). Начатое ядро мага (с остатком бросков) даёт меньше отражений.</summary>
    public bool ApplyChaosCore(ModuleKit kit)
    {
        if (kit.kind != ModuleKind.ChaosCore || HasChaos) return false;
        int mk = Mathf.Clamp(kit.mk, 1, 3);
        int full = ChaosReflectsOf(mk);
        if (kit.charges > 0)
        {
            int throws = ChaosMagic.Charges(mk);
            full = Mathf.Max(1, Mathf.RoundToInt(full * Mathf.Min(kit.charges, throws) / (float)throws));
        }
        chaosMk = mk;
        chaosCharges = full;
        return true;
    }

    /// <summary>Снаряд прилетел в щит: с шансом ядра отражаем его (тратится одно отражение). true - отражён.</summary>
    public bool TryChaosReflect()
    {
        if (!HasChaos || Random.value >= ChaosChance) return false;
        chaosCharges--;
        if (chaosCharges <= 0)
        {
            chaosCharges = 0;
            chaosMk = 0;
            WarManager.Notify($"Ядро хаоса на щите иссякло ({TierNames[(int)quality].ToLower()} щит)", false);
        }
        return true;
    }

    public string ChaosText { get { return HasChaos ? $"ядро хаоса Мк.{TowerModules.Roman(chaosMk)}: отражает {ChaosChance * 100f:F0}% снарядов, ещё {chaosCharges}" : ""; } }

    public bool CanUpgrade { get { return upgrade < MaxUpgrade; } }

    public void ApplyUpgrade()
    {
        if (!CanUpgrade) return;
        upgrade++;
        maxDurability = CalcMaxDurability();
        durability = maxDurability;
    }

    // Цена полной починки, золото
    public int RepairCost
    {
        get
        {
            float missing = 1f - Frac;
            if (missing <= 0.001f) return 0;
            return Mathf.Max(1, Mathf.CeilToInt(missing * 100f * ((int)quality + 1) * (1 + upgrade)));
        }
    }

    public void Repair() { durability = maxDurability; }

    // Один поглощённый удар. true - щит сломался.
    public bool Wear()
    {
        durability -= 1f;
        return durability <= 0f;
    }

    public string StatsText
    {
        get
        {
            string chaos = HasChaos ? $", <color=#C9A6FF>хаос {ChaosChance * 100f:F0}% ×{chaosCharges}</color>" : "";
            return $"блок {BlockChance * 100f:F0}% (−{ReduceFraction * 100f:F0}%), отраж. {ReflectChance * 100f:F1}%, проч. {Percent}%{chaos}";
        }
    }
}

/// <summary>
/// Щит бойца. Вешается на жителя-солдата, когда ему выдают щит (CitizenAgent менять не нужно).
/// Враги перед ударом вызывают ShieldBearer.Mitigate (см. EnemyAgent).
/// Боец погиб - щит потерян. Бойца уволили или убрали - щит возвращается в Оружейную.
/// </summary>
public class ShieldBearer : MonoBehaviour
{
    CitizenAgent agent;
    float checkTimer;

    public Shield Current { get; private set; }

    public static Shield ShieldOf(CitizenAgent a)
    {
        ShieldBearer b = a != null ? a.GetComponent<ShieldBearer>() : null;
        return b != null ? b.Current : null;
    }

    public static bool Equip(CitizenAgent a, Shield s)
    {
        if (a == null || s == null || !a.IsSoldier || a.IsDead) return false;
        ShieldBearer b = a.GetComponent<ShieldBearer>();
        if (b == null) b = a.gameObject.AddComponent<ShieldBearer>();
        if (b.Current != null) return false;
        b.Current = s;
        return true;
    }

    public static Shield Unequip(CitizenAgent a)
    {
        ShieldBearer b = a != null ? a.GetComponent<ShieldBearer>() : null;
        if (b == null) return null;
        Shield s = b.Current;
        b.Current = null;
        return s;
    }

    // Уволили: щит на склад Оружейной
    public static void ReturnToArmory(CitizenAgent a)
    {
        Shield s = Unequip(a);
        if (s != null && !ArmoryController.StashShield(s))
            WarManager.Notify("В Оружейных нет места: щит уволенного бойца потерян", false);
    }

    public static string TextOf(CitizenAgent a)
    {
        Shield s = ShieldOf(a);
        return s != null ? $"{s.Name} ({s.Percent}%)" + (s.HasChaos ? $" · хаос ×{s.chaosCharges}" : "") : "";
    }

    /// <summary>
    /// Что останется от урона после щита. magical - магический урон щит не берёт.
    /// by - нападающий (при отражении удар улетает обратно стрелой). Потом удар держит броня бойца (MitigateBody).
    /// </summary>
    public static float Mitigate(CitizenAgent target, float dmg, EnemyAgent by, bool magical = false)
    {
        if (dmg <= 0f || target == null) return dmg;
        if (ChaosReflect(target, dmg, by, magical)) return 0f;   // ядро хаоса: снаряд улетел обратно
        if (magical) return dmg;
        ShieldBearer b = target.GetComponent<ShieldBearer>();
        if (b == null || b.Current == null) return target.MitigateBody(dmg, false);   // без щита - только броня / плащ

        Shield s = b.Current;

        // 1. Полное отражение
        if (Random.value < s.ReflectChanceVs(dmg))
        {
            if (by != null) CombatFx.Arrow(target.transform.position, by.Position);
            b.Absorb(s, target);
            return 0f;
        }

        // 2. Обычный блок
        if (Random.value < s.BlockChance)
        {
            dmg *= 1f - s.ReduceFraction;
            b.Absorb(s, target);
        }
        // 3. Что прошло мимо щита - держит броня (CitizenAgent_Gear)
        return target.MitigateBody(dmg, false);
    }

    /// <summary>
    /// Ядро хаоса на щите бойца: снаряд, прилетевший издалека (стрела, заряд мага), с шансом улетает обратно в стрелка.
    /// Удары вплотную не отражаются. true - отражён (урона бойцу нет).
    /// </summary>
    public static bool ChaosReflect(CitizenAgent target, float dmg, EnemyAgent by, bool magical)
    {
        if (dmg <= 0f || target == null || !EnemyAgent.Alive(by)) return false;
        Shield s = ShieldOf(target);
        if (s == null || !s.HasChaos) return false;
        Vector2 me = target.transform.position;
        if (Vector2.Distance(me, by.Position) < Shield.ChaosMinDistance || !s.TryChaosReflect()) return false;
        CombatFx.ChaosReflect(me, by.Position);
        by.TakeDamage(dmg, target, null, magical);
        return true;
    }

    void Absorb(Shield s, CitizenAgent owner)
    {
        if (!s.Wear()) return;
        Current = null;
        WarManager.Notify($"Сломался щит: {s.Name} ({(owner != null ? owner.UnitName : "")})", false);
    }

    void Awake() { agent = GetComponent<CitizenAgent>(); }

    void Update()
    {
        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = 0.5f;

        // Бойца уволили (или казарму снесли) - щит возвращается на склад
        if (Current != null && agent != null && !agent.IsDead && !agent.IsSoldier) ReturnToArmory(agent);
    }

    void OnDestroy()
    {
        if (Current == null) return;
        Shield s = Current;
        Current = null;

        if (agent != null && agent.IsDead)
            WarManager.Notify($"Погиб {agent.UnitName}: вместе с ним потерян щит — {s.Name}", false);
        else if (!ArmoryController.StashShield(s))
            WarManager.Notify("В Оружейных нет места: щит потерян", false);
    }
}