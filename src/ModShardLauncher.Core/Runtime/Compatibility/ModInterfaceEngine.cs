using System.Dynamic;
using Serilog;

namespace ModShardLauncher.Mods;

/// <summary>
/// Legacy compatibility shell for the removed script console.
/// <para>
/// The console (socket bridge, injected GML and CLI/MCP tooling) is gone. This type and its
/// members stay public only so that already compiled <c>.sml</c> packages referencing it continue
/// to load; <see cref="Execute"/> is a logging no-op.
/// </para>
/// </summary>
[Obsolete("The script console was removed; this type only exists so older mods keep loading.")]
public class ModInterfaceEngine : EngineObject
{
    public static dynamic Instance = new ModInterfaceEngine();

    public string ModPath = string.Empty;

    public int CurrentCallbackID = 0;

    public bool IsLoadHooks = false;

    public Dictionary<string, EngineObject> Variables;

    public Dictionary<string, ModDelegate> HookDelegates;

    public Dictionary<int, Action<string>> Callbacks;

    public ModInterfaceEngine()
    {
        Variables = new Dictionary<string, EngineObject>();
        HookDelegates = new Dictionary<string, ModDelegate>();
        Callbacks = new Dictionary<int, Action<string>>();
        HookDelegates.Add("OnGameStart", new ModDelegate());
    }

    /// <summary>Kept for binary compatibility; there is nothing left to initialise.</summary>
    public void Initalize()
    {
    }

    public void Execute(string script, params object[] objects) =>
        Log.Warning("The script console was removed; ModInterfaceEngine.Execute(\"{Script}\") was ignored.", script);

    public void DoHook(string name, object[] parameters)
    {
        if (HookDelegates.TryGetValue(name, out ModDelegate? hook))
        {
            hook.Invoke(parameters);
        }
    }

    public void AddHook(string hook, Action<object[]> action)
    {
        if (HookDelegates.TryGetValue(hook, out ModDelegate? hookDelegate))
        {
            hookDelegate += action;
        }
    }
}

/// <summary>A simple multicast delegate wrapper exposed to the dynamic engine object.</summary>
[Obsolete("The script console was removed; this type only exists so older mods keep loading.")]
public class ModDelegate : EngineObject
{
    private readonly List<Action<object[]>> _actions = new();

    public static ModDelegate operator +(ModDelegate @delegate, Action<object[]> action)
    {
        @delegate._actions.Add(action);
        return @delegate;
    }

    public void Invoke(object[] obj)
    {
        foreach (Action<object[]> action in _actions)
        {
            action.Invoke(obj);
        }
    }
}

/// <summary>Dynamic object used by the old script engine so GML-side calls looked like C# properties.</summary>
[Obsolete("The script console was removed; this type only exists so older mods keep loading.")]
public class EngineObject : DynamicObject
{
    private readonly Dictionary<string, object> _values;

    public EngineObject() => _values = new Dictionary<string, object>();

    public object? GetPropertyValue(string propertyName) =>
        _values.TryGetValue(propertyName, out object? value) ? value : null;

    public void SetPropertyValue(string propertyName, object value)
    {
        if (_values.ContainsKey(propertyName))
        {
            _values[propertyName] = value;
        }
        else
        {
            _values.Add(propertyName, value);
        }
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = GetPropertyValue(binder.Name);
        return result is not null;
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        if (value is not null)
        {
            SetPropertyValue(binder.Name, value);
        }

        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        result = null;
        object? member = GetPropertyValue(binder.Name);
        if (member is null)
        {
            return false;
        }

        if (member.GetType() == typeof(Action<object[]>))
        {
            object?[] actualArgs = args ?? Array.Empty<object?>();
            for (int i = 0; i < actualArgs.Length; i++)
            {
                if (actualArgs[i]?.GetType() == typeof(string))
                {
                    actualArgs[i] = "\"" + actualArgs[i]!.ToString()!.Replace(" ", "_") + "\"";
                }
            }

            ((Action<object[]>)member)(actualArgs!);
            return true;
        }

        return false;
    }
}
