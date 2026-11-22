namespace Koch.Kernel;

/// <summary>A string store such as the browser's localStorage. Each domain keeps its book under its own key.</summary>
public interface IKeyValueStore
{
    string? Read(string key);
    void Write(string key, string value);
}
