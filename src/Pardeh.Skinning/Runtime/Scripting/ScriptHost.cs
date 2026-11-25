using Jint;
using Jint.Native;

namespace Pardeh.Skinning.Runtime.Scripting;

/// <summary>
/// Sandboxed JavaScript engine for one skin view. Host objects are exposed through JS Proxies so member
/// lookup is case-insensitive (COM semantics) and works inside <c>with</c> scopes for event handlers.
/// Scripts get no CLR, file, network or process access: only the objects registered here.
/// </summary>
public sealed class ScriptHost
{
    private const string Bootstrap = """
        var __wrap = function (id) {
            return new Proxy({}, {
                get: function (t, k) {
                    if (typeof k !== 'string') return undefined;
                    var kind = __hk(id, k);
                    if (kind === 2) return function () { return __hc(id, k, Array.prototype.slice.call(arguments)); };
                    if (kind === 1) return __hg(id, k);
                    return undefined;
                },
                set: function (t, k, v) { if (typeof k === 'string') __hs(id, k, v); return true; },
                has: function (t, k) { return typeof k === 'string' && __hk(id, k) !== 0; }
            });
        };
        var __emptyScope = {};
        """;

    private readonly Engine _engine;
    private readonly Action<string> _log;
    private readonly List<IScriptable> _objects = [];
    private readonly Dictionary<IScriptable, JsValue> _proxies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<JsValue, IScriptable> _reverse = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, JsValue> _handlerCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Prepared<Acornima.Ast.Script>> _expressionCache = new(StringComparer.Ordinal);
    private readonly JsValue _wrap;
    private readonly JsValue _emptyScope;
    private readonly SuspendableDeadline _deadline = new(TimeSpan.FromSeconds(3));

    /// <summary>
    /// Pauses the script time limit while the host shows something modal (a file dialog, a message box)
    /// on behalf of a script, so a user taking their time doesn't abort the script.
    /// </summary>
    public IDisposable SuspendLimits() => _deadline.Suspend();

    /// <summary>Stops runaway scripts (infinite loops) while letting modal host calls take as long as they need.</summary>
    private sealed class SuspendableDeadline(TimeSpan limit) : Constraint
    {
        private long _start = System.Diagnostics.Stopwatch.GetTimestamp();
        private long _pausedTicks;
        private long _pauseStart;
        private int _depth;

        public override void Reset()
        {
            if (_depth > 0)
                return; // a nested call during a modal operation must not restart the outer clock
            _start = System.Diagnostics.Stopwatch.GetTimestamp();
            _pausedTicks = 0;
        }

        public override void Check()
        {
            if (_depth > 0)
                return;
            var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - _start - _pausedTicks;
            if (elapsed > limit.TotalSeconds * System.Diagnostics.Stopwatch.Frequency)
                throw new TimeoutException($"Script ran longer than {limit.TotalSeconds:0} seconds.");
        }

        public IDisposable Suspend()
        {
            if (_depth++ == 0)
                _pauseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            return new Resume(this);
        }

        private sealed class Resume(SuspendableDeadline owner) : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (_done)
                    return;
                _done = true;
                if (--owner._depth == 0)
                    owner._pausedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - owner._pauseStart;
            }
        }
    }

    /// <summary>
    /// Builds and runs a throw-away script engine on a worker thread so the (slow, JIT-bound) first engine start overlaps
    /// the rest of the application start-up instead of delaying the first skin.
    /// </summary>
    public static Task Prewarm() => Task.Run(() =>
    {
        try
        {
            // (the warm-up is left out of the trace)
            new ScriptHost(_ => { }).Execute("var warm = [1, 2, 3].map(function (x) { return x * 2; }).join(',') + 'a'.length;", "warm-up");
        }
        catch (Exception)
        {
            // Only a warm-up.
        }
    });

    public ScriptHost(Action<string> log)
    {
        // Bindings re-run every tick: report each distinct error once.
        var reported = new HashSet<string>(StringComparer.Ordinal);
        _log = message =>
        {
            if (reported.Count < 500 && reported.Add(message))
                log(message);
        };
        Pardeh.Skinning.Runtime.SkinView.Trace?.Invoke("engine: creating");
        _engine = new Engine(options =>
        {
            options.Strict = false;
            options.Constraints.Constraints.Add(_deadline);
            options.Constraints.MaxRecursionDepth = 256;
        });

        _engine.SetValue("__hk", new Func<int, string, int>((id, name) => (int)_objects[id].GetMemberKind(name)));
        _engine.SetValue("__hg", new Func<int, string, JsValue>((id, name) => ToJs(_objects[id].GetMember(name))));
        _engine.SetValue("__hs", new Action<int, string, JsValue>((id, name, value) => _objects[id].SetMember(name, FromJs(value))));
        _engine.SetValue("__hc", new Func<int, string, JsValue, JsValue>((id, name, args) =>
        {
            var list = FromJs(args) as object?[] ?? [];
            return ToJs(_objects[id].InvokeMember(name, list));
        }));

        Pardeh.Skinning.Runtime.SkinView.Trace?.Invoke("engine: created, host objects set");
        _engine.Execute(Bootstrap);
        Pardeh.Skinning.Runtime.SkinView.Trace?.Invoke("engine: bootstrap done");
        _wrap = _engine.GetValue("__wrap");
        _emptyScope = _engine.GetValue("__emptyScope");
    }

    /// <summary>Defines a global variable.</summary>
    public void SetGlobal(string name, object? value)
    {
        try
        {
            _engine.SetValue(name, ToJs(value));
        }
        catch (Exception ex)
        {
            _log($"Cannot define global '{name}': {ex.Message}");
        }
    }

    /// <summary>Runs a script file (function declarations become globals).</summary>
    public void Execute(string code, string sourceName)
    {
        try
        {
            _engine.Execute(code, sourceName);
        }
        catch (Exception ex)
        {
            _log($"Script error in {sourceName}: {ex.Message}");
        }
    }

    /// <summary>Evaluates an expression. Returns null (and logs) on error.</summary>
    public object? Evaluate(string expression)
    {
        try
        {
            if (!_expressionCache.TryGetValue(expression, out var prepared))
            {
                prepared = Engine.PrepareScript(expression);
                _expressionCache[expression] = prepared;
            }

            return FromJs(_engine.Evaluate(prepared));
        }
        catch (Exception ex)
        {
            _log($"Expression error in '{expression}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Evaluates an attribute expression with <paramref name="self"/>'s members in scope
    /// (so <c>width="JScript:parent.width-left"</c> sees the element's own <c>left</c>).
    /// </summary>
    public object? EvaluateIn(IScriptable self, string expression)
    {
        var key = "\u0002" + expression;
        try
        {
            if (!_handlerCache.TryGetValue(key, out var function))
            {
                try
                {
                    function = _engine.Evaluate($"(function (__self) {{ with (__self) {{ return (\n{expression}\n); }} }})");
                }
                catch (Exception ex) when (ex.GetType().Name.Contains("Parse", StringComparison.Ordinal) || ex.Message.Contains("Unexpected", StringComparison.Ordinal))
                {
                    // Old JScript skins use statements as values ("if(a){1}else{2}"): take the completion value, as eval does.
                    var literal = JsonEncode(expression);
                    function = _engine.Evaluate($"(function (__self) {{ with (__self) {{ return eval({literal}); }} }})");
                }

                _handlerCache[key] = function;
            }

            return FromJs(_engine.Invoke(function, JsValue.Undefined, [ToJs(self)]));
        }
        catch (Exception ex)
        {
            _log($"Expression error in '{Shorten(expression)}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Runs event handler code with <paramref name="self"/>'s members in scope (so <c>value</c>, <c>down</c> … work)
    /// and the named event arguments (e.g. <c>NewState</c>) defined.
    /// </summary>
    public void RunHandler(IScriptable? self, string code, IReadOnlyList<(string Name, object? Value)> args)
    {
        var key = args.Count == 0 ? code : code + "\u0001" + string.Join(",", args.Select(a => a.Name));
        try
        {
            if (!_handlerCache.TryGetValue(key, out var function))
            {
                var parameters = string.Concat(args.Select(a => ", " + a.Name));
                function = _engine.Evaluate($"(function (__self{parameters}) {{ with (__self) {{\n{code}\n}} }})");
                _handlerCache[key] = function;
            }

            var jsArgs = new JsValue[args.Count + 1];
            jsArgs[0] = self is null ? _emptyScope : ToJs(self);
            for (var i = 0; i < args.Count; i++)
                jsArgs[i + 1] = ToJs(args[i].Value);

            _engine.Invoke(function, JsValue.Undefined, jsArgs);
        }
        catch (Exception ex)
        {
            _log($"Handler error in '{Shorten(code)}': {ex.Message}");
        }
    }

    public JsValue ToJs(object? value)
    {
        switch (value)
        {
            case null:
                return JsValue.Null;
            case JsValue js:
                return js;
            case IScriptable scriptable:
                if (!_proxies.TryGetValue(scriptable, out var proxy))
                {
                    _objects.Add(scriptable);
                    proxy = _engine.Invoke(_wrap, _objects.Count - 1);
                    _proxies[scriptable] = proxy;
                    _reverse[proxy] = scriptable;
                }

                return proxy;
            case string s:
                return new JsString(s);
            case bool b:
                return b ? JsBoolean.True : JsBoolean.False;
            case double d:
                return new JsNumber(d);
            case int i:
                return new JsNumber(i);
            case long l:
                return new JsNumber(l);
            case float f:
                return new JsNumber(f);
            case object?[] array:
                {
                    var items = array.Select(ToJs).ToArray();
                    return new Jint.Native.JsArray(_engine, items);
                }

            default:
                return new JsString(value.ToString() ?? string.Empty);
        }
    }

    public object? FromJs(JsValue value)
    {
        if (value.IsUndefined() || value.IsNull())
            return null;
        if (value.IsBoolean())
            return value.AsBoolean();
        if (value.IsNumber())
            return value.AsNumber();
        if (value.IsString())
            return value.AsString();
        if (_reverse.TryGetValue(value, out var scriptable))
            return scriptable;
        if (value.IsArray())
        {
            var array = value.AsArray();
            var length = (int)array.Length;
            var result = new object?[length];
            for (var i = 0; i < length; i++)
                result[i] = FromJs(array[(uint)i]);
            return result;
        }

        return value.ToString();
    }

    private static string JsonEncode(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    private static string Shorten(string code)
    {
        code = code.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return code.Length > 60 ? code[..60] + "…" : code;
    }
}
