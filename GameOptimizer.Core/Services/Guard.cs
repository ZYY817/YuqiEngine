namespace GameOptimizer.Core.Services;

public static class Guard
{
    public static void NotNull<T>(T? value, string paramName) where T : class
    {
        if (value is null)
            throw new ArgumentNullException(paramName);
    }

    public static void NotNullOrEmpty(string? value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty.", paramName);
    }

    public static void NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
    }

    public static void InRange(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be between {min} and {max}.");
    }

    public static void Positive(int value, string paramName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
    }

    public static void NotEmpty<T>(IEnumerable<T>? collection, string paramName)
    {
        if (collection is null || !collection.Any())
            throw new ArgumentException("Collection cannot be null or empty.", paramName);
    }

    public static void FileExists(string path, string paramName)
    {
        NotNullOrEmpty(path, paramName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}", path);
    }

    public static void DirectoryExists(string path, string paramName)
    {
        NotNullOrEmpty(path, paramName);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Directory not found: {path}");
    }
}
