using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Logs conversion messages only once per key, so realtime mode doesn't spam the console on every update
/// </summary>
public class ConversionReporter
{
    readonly HashSet<string> _reported = new HashSet<string>();

    public void Warning(string key, string message, Object context) => Log(key, message, context, true);
    public void Info(string key, string message, Object context) => Log(key, message, context, false);

    void Log(string key, string message, Object context, bool warning)
    {
        if (!_reported.Add(key))
            return;

        if (warning)
            Debug.LogWarning(message, context);
        else
            Debug.Log(message, context);
    }
}
