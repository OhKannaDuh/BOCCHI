using System.Globalization;
using System.Text.RegularExpressions;
using BOCCHI.Common.Services.Logging;
using Dalamud.Plugin.Services;
using Serilog;
using Serilog.Events;

namespace BOCCHI.Services.Logging;

public sealed class CapturingPluginLog : IPluginLog
{
    private static readonly Regex NamedHole = new(@"\{[^{}]+\}", RegexOptions.Compiled);

    private readonly IPluginLog inner;

    private readonly IBocchiLogBuffer buffer;

    public CapturingPluginLog(IPluginLog inner, IBocchiLogBuffer buffer)
    {
        this.inner = inner;
        this.buffer = buffer;

        if (inner.MinimumLogLevel > LogEventLevel.Debug)
        {
            inner.MinimumLogLevel = LogEventLevel.Debug;
        }
    }

    public ILogger Logger => inner.Logger;

    public LogEventLevel MinimumLogLevel
    {
        get => inner.MinimumLogLevel;
        set => inner.MinimumLogLevel = value;
    }

    public void Fatal(string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Error, null, messageTemplate, values);
        inner.Fatal(messageTemplate, values);
    }

    public void Fatal(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Error, exception, messageTemplate, values);
        inner.Fatal(exception, messageTemplate, values);
    }

    public void Error(string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Error, null, messageTemplate, values);
        inner.Error(messageTemplate, values);
    }

    public void Error(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Error, exception, messageTemplate, values);
        inner.Error(exception, messageTemplate, values);
    }

    public void Warning(string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Warning, null, messageTemplate, values);
        inner.Warning(messageTemplate, values);
    }

    public void Warning(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Warning, exception, messageTemplate, values);
        inner.Warning(exception, messageTemplate, values);
    }

    public void Information(string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Info, null, messageTemplate, values);
        inner.Information(messageTemplate, values);
    }

    public void Information(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Info, exception, messageTemplate, values);
        inner.Information(exception, messageTemplate, values);
    }

    public void Info(string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Info, null, messageTemplate, values);
        inner.Info(messageTemplate, values);
    }

    public void Info(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Info, exception, messageTemplate, values);
        inner.Info(exception, messageTemplate, values);
    }

    public void Debug(string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Debug, null, messageTemplate, values);
        inner.Debug(messageTemplate, values);
    }

    public void Debug(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Debug, exception, messageTemplate, values);
        inner.Debug(exception, messageTemplate, values);
    }

    public void Verbose(string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Verbose, null, messageTemplate, values);
        inner.Verbose(messageTemplate, values);
    }

    public void Verbose(Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(BocchiLogLevel.Verbose, exception, messageTemplate, values);
        inner.Verbose(exception, messageTemplate, values);
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values)
    {
        Capture(MapLevel(level), exception, messageTemplate, values);
        inner.Write(level, exception, messageTemplate, values);
    }

    private void Capture(BocchiLogLevel level, Exception? exception, string messageTemplate, object[] values)
    {
        string message = Render(messageTemplate, values);
        if (exception != null)
        {
            message = $"{message} :: {exception.GetType().Name}: {exception.Message}";
        }

        buffer.Append(level, message);
    }

    private static BocchiLogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => BocchiLogLevel.Verbose,
        LogEventLevel.Debug => BocchiLogLevel.Debug,
        LogEventLevel.Information => BocchiLogLevel.Info,
        LogEventLevel.Warning => BocchiLogLevel.Warning,
        LogEventLevel.Error => BocchiLogLevel.Error,
        LogEventLevel.Fatal => BocchiLogLevel.Error,
        _ => BocchiLogLevel.Info,
    };

    private static string Render(string template, object[] values)
    {
        if (values.Length == 0)
        {
            return template;
        }

        try
        {
            int index = 0;
            string converted = NamedHole.Replace(template, _ => "{" + index++ + "}");
            return string.Format(CultureInfo.InvariantCulture, converted, values);
        }
        catch
        {
            return template + " | " + string.Join(", ", values.Select(v => v?.ToString() ?? "null"));
        }
    }
}
