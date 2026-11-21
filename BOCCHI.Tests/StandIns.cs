using System.Reflection;
using Ocelot.Services.Logger;

namespace BOCCHI.Tests;

// Stands in for any game or plugin interface: every member answers with a default unless a test says otherwise.
public class StandIn : DispatchProxy
{
    private readonly Dictionary<string, Func<object?[], object?>> answers = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public static T For<T>(Action<StandIn>? setup = null) where T : class
    {
        T proxy = Create<T, StandIn>();
        setup?.Invoke((StandIn)(object)proxy);
        return proxy;
    }

    public StandIn Answers(string member, object? value) => Answers(member, _ => value);

    public StandIn Answers(string member, Func<object?[], object?> answer)
    {
        answers[member] = answer;
        return this;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
        {
            return null;
        }

        string name = targetMethod.Name;
        Calls.Add(name);
        string property = name.StartsWith("get_", StringComparison.Ordinal) ? name[4..] : name;
        if (answers.TryGetValue(name, out Func<object?[], object?>? answer)
            || answers.TryGetValue(property, out answer))
        {
            return answer(args ?? []);
        }

        Type result = targetMethod.ReturnType;
        return result != typeof(void) && result.IsValueType ? Activator.CreateInstance(result) : null;
    }
}

public sealed class QuietLog<T> : ILogger<T>
{
    public void Log(string format, params object[] args) { }

    public void Info(string format, params object[] args) { }

    public void Warn(string format, params object[] args) { }

    public void Warning(string format, params object[] args) { }

    public void Error(string format, params object[] args) { }

    public void Debug(string format, params object[] args) { }

    public void Fatal(string format, params object[] args) { }

    public void Verbose(string format, params object[] args) { }

    public void Fatal(Exception? exception, string messageTemplate, params object[] values) { }

    public void Error(Exception? exception, string messageTemplate, params object[] values) { }

    public void Warn(Exception? exception, string messageTemplate, params object[] values) { }

    public void Warning(Exception? exception, string messageTemplate, params object[] values) { }

    public void Info(Exception? exception, string messageTemplate, params object[] values) { }

    public void Debug(Exception? exception, string messageTemplate, params object[] values) { }

    public void Verbose(Exception? exception, string messageTemplate, params object[] values) { }
}
