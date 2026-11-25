namespace Koch.Domain.Kernel;

/// <summary>Something the sync adds, replaces or removes as a whole, found by its id.</summary>
public interface IItem
{
    Guid Id { get; }
}

/// <summary>A string store such as the browser's localStorage. Each domain keeps its book under its own key.</summary>
public interface IKeyValueStore
{
    string? Read(string key);
    void Write(string key, string value);
}
