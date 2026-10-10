using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Resonite SDK window section: one-click headless .resonitepackage export.</summary>
public static class HeadlessExportPanel
{
    const string HeadlessKey = "ResoniteSDK.Headless.Folder";
    const string ExportKey = "ResoniteSDK.Headless.ExportFolder";
    const string VariantsKey = "ResoniteSDK.Headless.IncludeVariants";

    static GameObject _target;
    static volatile string _status;
    static string _lastPackage;
    static bool _busy;

    public static void OnGUI(EditorWindow window)
    {
        GUILayout.Label("HEADLESS PACKAGE EXPORT:");
        GUI.enabled = !_busy;

        var headless = EditorPrefs.GetString(HeadlessKey, "");
        if (string.IsNullOrEmpty(headless))
            headless = HeadlessResoniteServer.FindHeadless() ?? "";
        headless = FolderField("Resonite Headless", headless, HeadlessKey);
        if (!HeadlessResoniteServer.IsHeadless(headless))
            EditorGUILayout.HelpBox("Resonite Headless not found. Install it (Steam beta \"headless\") and pick its Headless folder.",
                MessageType.Warning);

        FolderField("Export folder", EditorPrefs.GetString(ExportKey, ""), ExportKey);

        if (_target == null)
            _target = HeadlessPackageExport.DefaultTarget();
        _target = (GameObject)EditorGUILayout.ObjectField("Object to export", _target, typeof(GameObject), true);

        var variants = EditorGUILayout.Toggle("Include asset variants", EditorPrefs.GetBool(VariantsKey, false));
        EditorPrefs.SetBool(VariantsKey, variants);

        GUI.enabled = !_busy && !EditorApplication.isCompiling && _target != null
            && HeadlessResoniteServer.IsHeadless(headless) && !string.IsNullOrWhiteSpace(EditorPrefs.GetString(ExportKey, ""));
        if (GUILayout.Button(_busy ? "Exporting..." : "Export .resonitepackage (headless)"))
            Export(window, _target, headless, EditorPrefs.GetString(ExportKey), variants);
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.None);
        if (!_busy && _lastPackage != null && File.Exists(_lastPackage) && GUILayout.Button("Show exported package"))
            EditorUtility.RevealInFinder(_lastPackage);

        if (_busy)
            window.Repaint();
    }

    static string FolderField(string label, string value, string key)
    {
        GUILayout.BeginHorizontal();
        value = EditorGUILayout.TextField(label, value);
        if (GUILayout.Button("...", GUILayout.Width(28)))
        {
            var picked = EditorUtility.OpenFolderPanel(label, value, "");
            if (!string.IsNullOrEmpty(picked))
            {
                value = picked;
                GUI.FocusControl(null);
            }
        }
        GUILayout.EndHorizontal();
        EditorPrefs.SetString(key, value);
        return value;
    }

    static async void Export(EditorWindow window, GameObject target, string headless, string folder, bool variants)
    {
        _busy = true;
        _lastPackage = null;
        void Log(string message)
        {
            _status = message;
            Debug.Log("[Headless export] " + message);
        }
        try
        {
            _lastPackage = await HeadlessPackageExport.ExportAsync(target, headless, folder, variants, Log);
        }
        catch (Exception ex)
        {
            var error = ex is AggregateException aggregate ? aggregate.Flatten().InnerException : ex;
            _status = "Export failed: " + error.Message;
            Debug.LogError("[Headless export] " + error);
        }
        finally
        {
            _busy = false;
            if (window != null)
                window.Repaint();
        }
    }
}
