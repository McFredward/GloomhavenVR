using System;
using System.Collections.Generic;
using System.IO;
using Apparance.Net;
using UnityEngine;

/// <summary>Executes the selected game's native engine in the build-time Windows host.</summary>
public sealed class QuestProceduralHostProbe : MonoBehaviour, ILogging
{
    [Serializable] private sealed class Receipt
    {
        public int schema = 1;
        public string scope = "original-windows-procedural-build-host", unityVersion, error;
        public bool nativeEngineStarted, nativeEntityCreated, nativeUpdateExecuted, cleanupCompleted;
        public int nativeEntityHandle, procedureFiles, errors;
        public string[] messages;
    }
    readonly List<string> messages = new List<string>();
    int errors;

    void Start()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "--quest-host-receipt");
        if (index < 0 || index + 1 >= arguments.Length)
        {
            UnityEngine.Debug.LogError("Original procedural host receipt destination is missing.");
            Application.Quit(64);
            return;
        }
        var receipt = new Receipt { unityVersion = Application.unityVersion };
        bool started = false;
        try
        {
            string procedures = Path.Combine(Application.streamingAssetsPath, "Procedures");
            receipt.procedureFiles = Directory.GetFiles(procedures, "*.proc", SearchOption.AllDirectories).Length;
            if (receipt.procedureFiles == 0) throw new InvalidDataException("No selected original procedures are available.");
            Engine.Start(procedures, this, 1, 32, false);
            started = receipt.nativeEngineStarted = Engine.IsRunning;
            if (!started) throw new InvalidOperationException("Original native engine did not start.");
            Entity entity = Engine.CreateEntity();
            receipt.nativeEntityHandle = entity.Handle;
            receipt.nativeEntityCreated = entity.Handle != 0;
            Engine.Update(0.1f, new Apparance.Net.Vector3(0, 0, 0));
            receipt.nativeUpdateExecuted = true;
        }
        catch (Exception error)
        {
            receipt.error = error.ToString();
            UnityEngine.Debug.LogException(error);
        }
        finally
        {
            try { if (started) Engine.Stop(); receipt.cleanupCompleted = true; }
            catch (Exception error) { receipt.error = (receipt.error ?? "") + "\nCleanup: " + error; }
            receipt.errors = errors;
            receipt.messages = messages.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arguments[index + 1])));
            File.WriteAllText(arguments[index + 1], JsonUtility.ToJson(receipt, true));
            Application.Quit(receipt.error == null && receipt.nativeEngineStarted && receipt.nativeEntityCreated && receipt.nativeUpdateExecuted ? 0 : 1);
        }
    }

    public void LogMessage(string message) { Record("info", message); }
    public void LogWarning(string message) { Record("warning", message); }
    public void LogError(string message) { errors++; Record("error", message); }
    void Record(string level, string message)
    {
        if (messages.Count < 40) messages.Add(level + ": " + message);
    }
}
