using UnityEngine;

/// <summary>
/// Квадрат 1×1 (как GameObject.CreatePrimitive(Quad)), но без коллайдера.
/// CreatePrimitive на телефоне (IL2CPP, «Strip Engine Code») ломается: MeshCollider нигде не упомянут,
/// Unity выбрасывает его из сборки - и земля, реки, зоны не создаются, город не строится.
/// </summary>
public static class PlainQuad
{
    static Mesh mesh;

    public static GameObject Create(string name = "Quad")
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "PlainQuad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        return go;
    }
}
