using UnityEngine;

/// <summary>
/// Разворачивает рисунок туда, куда объект идёт (рисунки смотрят вправо).
/// Если задан flipRoot - разворачивается он целиком (масштабом), иначе - все спрайты объекта (flipX).
/// Мелкие шаги копятся, чтобы при ходьбе почти вертикально человечек не дёргался.
/// </summary>
public class Facing : MonoBehaviour
{
    public Transform flipRoot;
    public bool Left { get; private set; }

    Vector3 last;
    float drift;
    bool started;
    SpriteRenderer[] parts;
    float refresh;
    bool appliedLeft;

    /// <summary>Повернуться к точке (например, к цели в бою).</summary>
    public void LookAt(Vector2 point)
    {
        float dx = point.x - transform.position.x;
        if (Mathf.Abs(dx) > 0.05f) { Left = dx < 0f; drift = 0f; }
    }

    void LateUpdate()
    {
        Vector3 p = transform.position;
        if (!started) { last = p; started = true; }
        float dx = p.x - last.x;
        last = p;

        // Скачок (зашёл в здание, телепорт) не считается шагом
        if (Mathf.Abs(dx) < 2f)
        {
            drift = Mathf.Clamp(drift + dx, -0.06f, 0.06f);
            if (drift <= -0.05f) Left = true;
            else if (drift >= 0.05f) Left = false;
        }
        Apply();
    }

    void Apply()
    {
        if (flipRoot != null)
        {
            Vector3 s = flipRoot.localScale;
            float want = Mathf.Abs(s.x) * (Left ? -1f : 1f);
            if (s.x != want) { s.x = want; flipRoot.localScale = s; }
            return;
        }

        refresh -= Time.deltaTime;
        if (parts == null || refresh <= 0f)
        {
            parts = GetComponentsInChildren<SpriteRenderer>(true);
            refresh = 0.5f;
            appliedLeft = !Left;   // новые части тоже развернуть
        }
        if (appliedLeft == Left) return;
        appliedLeft = Left;
        foreach (var r in parts)
            if (r != null && r.flipX != Left) r.flipX = Left;
    }
}
