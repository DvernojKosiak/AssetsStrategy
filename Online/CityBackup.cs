using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Запасная копия города на телефоне (обновление sohranenie-1).
/// Беда была такая: игрок что-то сделал (поставил пачку снарядов, отправил курьера) и сразу закрыл игру.
/// Сохранение уходило на сервер, но телефон (или выход из Play в Unity) обрывал запрос, и сервер так и не узнавал
/// о последних минутах игры. После входа город был таким, каким его сохранили в прошлый раз: снаряда в пушке нет.
///
/// Теперь каждое сохранение сначала пишется в файл на устройстве (быстро, без сети), а когда сервер подтвердил его,
/// копия помечается как доставленная. При входе: если копия новее города на сервере (сервер её не получил),
/// игра берёт копию, вычитает из неё набеги и добавляет продажи и награды, которые пришли с сервера,
/// и сразу отправляет город на сервер. Если на сервере город новее (играли с другого телефона), копия не нужна.
/// Время отсутствия считается от момента, когда была сделана копия, - всё досчитывается, как обычно.
/// </summary>
public static class CityBackup
{
    const string FileName = "city_pending.txt";
    static int seq;

    static string FilePath
    {
        get
        {
            try { return Path.Combine(Application.persistentDataPath, FileName); }
            catch (Exception) { return null; }
        }
    }

    static bool Usable => !Application.isEditor || IsFirstCopy();

    // В редакторе вторые окна (Multiplayer Play Mode) - другие игроки: у них копии нет, чтобы не путать города
    static bool IsFirstCopy()
    {
        string d = Application.dataPath.Replace('\\', '/');
        return !d.Contains("/Library/VP/");
    }

    /// <summary>Записать город перед отправкой на сервер. Возвращает номер копии (0 - не записали).</summary>
    public static int Write(string ownerId, string cityJson, DateTime capturedUtc)
    {
        if (string.IsNullOrEmpty(ownerId) || string.IsNullOrEmpty(cityJson) || !Usable || !WorldClock.Active) return 0;
        string p = FilePath;
        if (p == null) return 0;
        try
        {
            int n = ++seq;
            string head = ownerId + "\t" + capturedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + "\t" + n.ToString(CultureInfo.InvariantCulture);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, head + "\n" + cityJson);
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
            return n;
        }
        catch (Exception e) { Debug.LogWarning("[Копия города] не записана: " + e.Message); return 0; }
    }

    static void Rewrite(string ownerId, DateTime capturedUtc, string cityJson)
    {
        string p = FilePath;
        if (p == null || string.IsNullOrEmpty(cityJson)) return;
        try
        {
            string head = ownerId + "\t" + capturedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + "\t0";
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, head + "\n" + cityJson);
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
        }
        catch (Exception e) { Debug.LogWarning("[Копия города] не переписана: " + e.Message); }
    }

    /// <summary>Сервер принял сохранение с этим номером: копия больше не нужна (если после неё не было новой).</summary>
    public static void Confirm(int n)
    {
        if (n <= 0 || n != seq) return;
        Delete();
    }

    public static void Delete()
    {
        string p = FilePath;
        try { if (p != null && File.Exists(p)) File.Delete(p); }
        catch (Exception) { }
    }

    static bool Read(out string ownerId, out DateTime captured, out string json)
    {
        ownerId = json = null;
        captured = DateTime.MinValue;
        string p = FilePath;
        if (p == null || !Usable || !File.Exists(p)) return false;
        try
        {
            string all = File.ReadAllText(p);
            int nl = all.IndexOf('\n');
            if (nl <= 0) return false;
            string[] head = all.Substring(0, nl).Split('\t');
            if (head.Length < 2) return false;
            ownerId = head[0];
            if (!DateTime.TryParse(head[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured)) return false;
            captured = captured.ToUniversalTime();
            json = all.Substring(nl + 1);
            return !string.IsNullOrEmpty(json);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Город с сервера пришёл (вход в игру). Если на устройстве есть копия новее - вернуть её
    /// (с потерями от набегов, продажами и наградами, которые сервер уже учёл в своём городе). Иначе - city.
    /// </summary>
    public static CityData Prefer(string ownerId, CityData city, ApiResponse res)
    {
        if (city == null || string.IsNullOrEmpty(ownerId)) return city;   // нового города копия не касается
        if (!Read(out string owner, out DateTime captured, out string json)) return city;
        if (owner != ownerId) return city;   // копия другого аккаунта - не трогаем (вдруг он ещё войдёт)

        DateTime serverSaved = DateTime.MinValue;
        if (!string.IsNullOrEmpty(city.savedUtc))
            DateTime.TryParse(city.savedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out serverSaved);
        serverSaved = serverSaved.ToUniversalTime();
        if (captured <= serverSaved.AddSeconds(1.0)) { Delete(); return city; }   // сервер получил это сохранение (или новее)

        CityData mine = null;
        try { mine = JsonUtility.FromJson<CityData>(json); }
        catch (Exception) { mine = null; }
        if (mine == null || mine.buildings == null || mine.buildings.Count == 0) { Delete(); return city; }

        // То же, что сервер сделал со своим городом при загрузке (ServerCore.LoadCity)
        try
        {
            if (res != null && res.reports != null)
                foreach (var rep in res.reports) if (rep != null && !rep.live) CityLoss.Apply(mine, rep);
            if (res != null && res.sales != null)
                foreach (var s in res.sales) if (s != null) mine.marks += Mathf.Max(0, s.price);
            if (res != null && res.grants != null)
                foreach (var g in res.grants)
                {
                    if (g == null) continue;
                    mine.gold += g.gold; mine.wood += g.wood; mine.stone += g.stone; mine.food += g.food; mine.iron += g.iron;
                    mine.marks += Mathf.Max(0, g.marks);
                }
        }
        catch (Exception e) { Debug.LogWarning("[Копия города] " + e.Message); }

        mine.ownerId = city.ownerId;
        mine.ownerName = city.ownerName;
        mine.savedUtc = captured.ToString("o", CultureInfo.InvariantCulture);
        Debug.Log("[Копия города] сервер не получил последнее сохранение - берём копию с устройства от " + mine.savedUtc);
        // Обновление 12: копия на устройстве - уже с потерями от набегов. Раньше в файле оставался город ДО набега:
        // сервер отчёты уже отдал, и если игра не успела сохраниться (шёл бой), при следующем входе
        // город «откатывался» - руины пропадали, набега будто не было
        Rewrite(ownerId, captured, JsonUtility.ToJson(mine));
        return mine;
    }
}
