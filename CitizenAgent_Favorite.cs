/// <summary>Обновление 12: имя жителя из сохранения (бойцы и избранные не меняют имя после входа).</summary>
public partial class CitizenAgent
{
    public void RestoreName(string n)
    {
        if (string.IsNullOrEmpty(n)) return;
        citizenName = n;
        CitizenNames.Claim(n);
    }
}
