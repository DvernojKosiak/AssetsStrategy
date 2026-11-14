using System;
using System.IO;
using System.Text;
using System.Threading;

/// <summary>
/// Сервер Strategyca отдельной программой: запускается двойным щелчком, Unity не нужен.
/// Необязательные параметры: --port 7777, --data "папка с данными", --local (только с этого компьютера),
/// --fast-war (клановые войны идут быстро: сутки войны = 10 минут, для проверки).
/// </summary>
public static class ServerApp
{
    /// <summary>Та же папка, что у сервера в Unity (Application.persistentDataPath/Server).</summary>
    public static string DefaultDataDir
    {
        get
        {
            const string company = "DefaultCompany", product = "Strategy";   // как в ProjectSettings
            if (OperatingSystem.IsWindows())
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);   // ...\AppData\Local
                return Path.Combine(Path.GetDirectoryName(local) ?? local, "LocalLow", company, product, "Server");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", company, product, "Server");
            return Path.Combine(home, ".config", "unity3d", company, product, "Server");
        }
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.Title = "Сервер Strategyca"; } catch (Exception) { }

        int port = Api.DefaultPort;
        string dataDir = DefaultDataDir;
        bool lan = true, fastWar = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if (a == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) { port = p; i++; }
            else if (a == "--data" && i + 1 < args.Length) { dataDir = args[i + 1]; i++; }
            else if (a == "--local") lan = false;
            else if (a == "--fast-war") fastWar = true;
        }

        Console.WriteLine("=== Сервер Strategyca ===");
        StrategycaServer s = StrategycaServer.Start(port, dataDir, out string error, lan);
        if (s == null)
        {
            Console.WriteLine();
            Console.WriteLine("Сервер не запустился: " + error);
            Console.WriteLine("Нажмите Enter, чтобы закрыть окно.");
            Console.ReadLine();
            return 1;
        }

        if (fastWar) Environment.SetEnvironmentVariable("STRATEGYCA_WAR_DAY", "600");
        GoogleServices.Install(s.Core);   // вход через Google Play Games и push-уведомления (если настроены)
        Console.WriteLine();
        Console.WriteLine($"Адрес для игры на этом компьютере: http://localhost:{port}");
        if (fastWar) Console.WriteLine("Быстрые клановые войны: сутки войны = 10 минут (для проверки).");
        Console.WriteLine("Для друзей: запустите туннель (см. инструкцию) и дайте им его адрес.");
        Console.WriteLine("Проверка: откройте адрес в браузере, там будет «Сервер Strategyca работает».");
        Console.WriteLine($"Для Google Play Console: политика конфиденциальности - <адрес сервера>{Api.WebPrivacy}, удаление аккаунта - <адрес сервера>{Api.WebDelete}");
        Console.WriteLine($"Жалобы игроков (никому не давайте): http://localhost:{port}{Api.WebAdmin}?key={s.Core.Config.adminKey}");
        Console.WriteLine();
        Console.WriteLine("Чтобы выключить сервер, закройте это окно или нажмите Ctrl+C.");
        Console.WriteLine();

        var quit = new ManualResetEvent(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
        AppDomain.CurrentDomain.ProcessExit += (_, __) => StrategycaServer.Stop();
        quit.WaitOne();
        StrategycaServer.Stop();
        return 0;
    }
}
