namespace Pardeh.Skinning.Runtime.Scripting;

public enum MemberKind
{
    None = 0,
    Property = 1,
    Method = 2,
}

/// <summary>
/// An object exposed to skin scripts. Member names are case-insensitive, like the COM objects classic skins
/// were written against. Values are null, bool, double, string, object?[] or another <see cref="IScriptable"/>.
/// </summary>
public interface IScriptable
{
    MemberKind GetMemberKind(string name);

    object? GetMember(string name);

    void SetMember(string name, object? value);

    object? InvokeMember(string name, object?[] args);
}

/// <summary>Convenience base: register properties and methods by name; unknown members become expandos.</summary>
public abstract class ScriptableObject : IScriptable
{
    private readonly Dictionary<string, Func<object?>> _getters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action<object?>> _setters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<object?[], object?>> _methods = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object?> _expandos = new(StringComparer.OrdinalIgnoreCase);

    protected void Property(string name, Func<object?> get, Action<object?>? set = null)
    {
        _getters[name] = get;
        if (set is not null)
            _setters[name] = set;
    }

    protected void Method(string name, Func<object?[], object?> body) => _methods[name] = body;

    protected void Method(string name, Action body) => _methods[name] = _ =>
    {
        body();
        return null;
    };

    protected static object? Arg(object?[] args, int index) => index < args.Length ? args[index] : null;

    public virtual MemberKind GetMemberKind(string name)
    {
        if (_methods.ContainsKey(name))
            return MemberKind.Method;
        return _getters.ContainsKey(name) || _expandos.ContainsKey(name) ? MemberKind.Property : MemberKind.None;
    }

    public virtual object? GetMember(string name)
    {
        if (_getters.TryGetValue(name, out var getter))
            return getter();
        return _expandos.GetValueOrDefault(name);
    }

    /// <summary>Raised after a member was set (skins hook these with <c>name_onchange</c> handlers).</summary>
    public event Action<string>? MemberChanged;

    protected void NotifyChanged(string name) => MemberChanged?.Invoke(name);

    public virtual void SetMember(string name, object? value)
    {
        if (_setters.TryGetValue(name, out var setter))
        {
            var before = _getters.TryGetValue(name, out var getter) ? getter() : null;
            setter(value);
            var after = getter?.Invoke();
            if (!SkinValue.AreEqual(before, after))
                NotifyChanged(name);
            return;
        }

        if (_getters.ContainsKey(name))
            return; // read-only

        var changed = !_expandos.TryGetValue(name, out var old) || !SkinValue.AreEqual(old, value);
        _expandos[name] = value;
        if (changed)
            NotifyChanged(name);
    }

    public virtual object? InvokeMember(string name, object?[] args) =>
        _methods.TryGetValue(name, out var method) ? method(args) : null;
}
