using UnityEngine;

/// <summary>
/// Инструменты для проверки (кнопки «Админ» в окнах пути и школ, F7 - достроить всё, F8 - тестовый набег,
/// F9 - ресурсы, F12 - туман). Работают только в редакторе Unity: в собранной игре у игроков их нет.
/// </summary>
public static class GameAdmin
{
    public static bool On => Application.isEditor;
}
