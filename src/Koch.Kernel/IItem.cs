namespace Koch.Kernel;

/// <summary>Something the sync adds, replaces or removes as a whole, found by its id.</summary>
public interface IItem
{
    Guid Id { get; }
}
