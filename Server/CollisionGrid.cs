using System.Text.Json;

namespace GuerraDasNacoes.Server;

public sealed class CollisionGrid
{
    public float originX, originZ, cellSize;
    public int width, height;
    public int[] blocked;

    public static CollisionGrid Load(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException("Grade de colisão ausente: " + path);
        var grid = JsonSerializer.Deserialize<CollisionGrid>(File.ReadAllText(path), WireJson.Options);
        grid.Validate();
        return grid;
    }

    public void Validate()
    {
        if (!float.IsFinite(originX) || !float.IsFinite(originZ) || !float.IsFinite(cellSize) ||
            cellSize < .02f || cellSize > 5 || width < 1 || height < 1 ||
            (long)width * height > 4_000_000 || blocked == null || blocked.Length != width * height ||
            blocked.Any(value => value != 0 && value != 1))
            throw new InvalidDataException("Grade de colisão inválida; o servidor não abrirá um mapa sem validação.");
    }

    public bool Free(float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z)) return false;
        int column = (int)Math.Floor((x - originX) / cellSize);
        int row = (int)Math.Floor((z - originZ) / cellSize);
        return column >= 0 && row >= 0 && column < width && row < height && blocked[row * width + column] == 0;
    }

    // Sample at less than half a grid cell, so a short move cannot skip a blocked cell.
    public bool ClearLine(float fromX, float fromZ, float toX, float toZ)
    {
        if (!Free(fromX, fromZ) || !Free(toX, toZ)) return false;
        double distance = Math.Sqrt((toX - fromX) * (toX - fromX) + (toZ - fromZ) * (toZ - fromZ));
        int samples = Math.Max(1, (int)Math.Ceiling(distance / Math.Min(.1, cellSize * .4)));
        for (int step = 1; step < samples; step++)
        {
            float fraction = (float)step / samples;
            if (!Free(fromX + (toX - fromX) * fraction, fromZ + (toZ - fromZ) * fraction)) return false;
        }
        return true;
    }
}

internal static class WireJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32
    };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
}
