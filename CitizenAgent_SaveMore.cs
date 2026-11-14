using UnityEngine;

/// <summary>
/// Сохранение того, что у жителя «в руках» и на полевой пушке (продолжение класса CitizenAgent, обновление sohranenie-1).
///  - Курьер уже забрал груз со склада здания и несёт его в Цитадель: при сохранении груз записывается,
///    после входа он уже в казне (донесли, пока игрока не было). Раньше этот груз пропадал.
///  - Полевая пушка: подъёмник, пачка магических снарядов (сколько осталось), выбранный вид снарядов и «Авто».
///    Раньше после входа пушка стояла пустой, а пачка пропадала.
/// </summary>
public partial class CitizenAgent
{
    /// <summary>Курьер несёт груз в Цитадель: сколько дойдёт до казны (с учётом доли Курьерской). false - не несёт.</summary>
    public bool CourierCargoForSave(int[] into)
    {
        if (!courierActive || !courierLoaded || dead || into == null) return false;
        bool any = false;
        for (int i = 0; i < courierLoad.Length && i < into.Length; i++)
        {
            int n = courierShare >= 0.999f ? courierLoad[i] : Mathf.FloorToInt(courierLoad[i] * courierShare);
            if (n <= 0) continue;
            into[i] += n;
            any = true;
        }
        return any;
    }

    /// <summary>Полевая пушка: что на ней стоит (для сохранения). false - не пушка или на ней ничего нет и настройки обычные.</summary>
    public bool CannonGearForSave(out int liftMk, out ModuleKind shell, out int shellMk, out int charges, out ModuleKind pref, out bool auto)
    {
        liftMk = cannonLift.mk;
        shell = CannonHasShells ? cannonShell : ModuleKind.None;
        shellMk = CannonHasShells ? cannonShellMk : 0;
        charges = CannonHasShells ? cannonShellCharges : 0;
        pref = cannonShellPref;
        auto = cannonShellAuto;
        return IsFieldCannon && (liftMk > 0 || shell != ModuleKind.None || pref != ModuleKind.None || !auto);
    }

    /// <summary>После загрузки: вернуть на полевую пушку подъёмник, пачку снарядов и настройки.</summary>
    public void RestoreCannonGear(int liftMk, ModuleKind shell, int shellMk, int charges, ModuleKind pref, bool auto)
    {
        if (!IsFieldCannon) return;
        if (liftMk > 0 && !cannonLift.Has) cannonLift.Set(Mathf.Clamp(liftMk, 1, 3));
        if (MagicShells.IsShell(shell) && charges > 0)
        {
            if (CannonHasShells)   // пушка уже успела взять другую пачку - та уходит обратно на склад
            {
                int full = MagicShells.Charges(cannonShellMk);
                EngineerYard.AddToHall(new ModuleKit { kind = cannonShell, mk = cannonShellMk, charges = cannonShellCharges < full ? cannonShellCharges : 0 });
            }
            cannonShell = shell;
            cannonShellMk = Mathf.Clamp(shellMk, 1, 3);
            cannonShellCharges = Mathf.Min(charges, MagicShells.Charges(cannonShellMk));
        }
        cannonShellPref = MagicShells.IsShell(pref) ? pref : ModuleKind.None;
        cannonShellAuto = auto;
    }
}
