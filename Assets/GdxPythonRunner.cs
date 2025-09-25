using System;
using System.IO;
using UnityEngine;
using UDebug = UnityEngine.Debug;

public class GdxPythonRunner : MonoBehaviour
{
    [Header("Paths")]
    [Tooltip("Πλήρης διαδρομή σε python.exe. Άφησέ το κενό για PATH.")]
    public string pythonExePath = "";  // π.χ. C:\\Users\\GEORGE\\AppData\\Local\\Programs\\Python\\Python313\\python.exe

    [Tooltip("Πλήρης διαδρομή στο stream_to_udp_gdx.py")]
    public string scriptPath = @"C:\Users\GEORGE\Desktop\ptixiaki\stream_to_udp_gdx.py";

    [Header("Args")]
    public string transport = "auto";   // auto | usb | ble
    public string udpHost   = "127.0.0.1";
    public int    udpPort   = 5005;
    public int    periodMs  = 50;

    [Header("Behavior")]
    public bool startOnPlay = true;
    public bool killOnStop  = true;
    public bool showConsoleWindow = false;   // βάλε true για ορατό παράθυρο Python
    public bool launchViaCmdWrapper = true;  // ΝΕΟ: εκκίνηση μέσω cmd.exe για 1:1 συμπεριφορά με CMD test

    private System.Diagnostics.Process _proc;

    void Start()
    {
        if (!startOnPlay) return;
        try
        {
            if (!File.Exists(scriptPath))
            {
                UDebug.LogError("[GDX Runner] Δεν βρέθηκε το script: " + scriptPath);
                return;
            }

            string py   = string.IsNullOrWhiteSpace(pythonExePath) ? "python" : pythonExePath;
            string args = $"\"{scriptPath}\" --transport {transport} --udp-host {udpHost} --udp-port {udpPort} --period-ms {periodMs}";

            System.Diagnostics.ProcessStartInfo psi;
            if (launchViaCmdWrapper)
            {
                string cmdLine = $"\"{py}\" {args}";
                psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/C " + cmdLine,
                    UseShellExecute = false,
                    CreateNoWindow  = !showConsoleWindow,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };
            }
            else
            {
                psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = py,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow  = !showConsoleWindow,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };
            }

            _proc = new System.Diagnostics.Process();
            _proc.StartInfo = psi;
            _proc.EnableRaisingEvents = true;
            _proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) UDebug.Log($"[GDXpy] {e.Data}"); };
            _proc.ErrorDataReceived  += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) UDebug.LogWarning($"[GDXpy] {e.Data}"); };

            if (_proc.Start())
            {
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
                UDebug.Log("[GDX Runner] Ξεκίνησε το Python streamer.");
            }
            else
            {
                UDebug.LogError("[GDX Runner] Αποτυχία εκκίνησης Python.");
            }
        }
        catch (Exception ex)
        {
            UDebug.LogError("[GDX Runner] " + ex.Message);
        }
    }

    void OnApplicationQuit() { StopProc(); }
    void OnDestroy()         { StopProc(); }

    private void StopProc()
    {
        if (!killOnStop) return;
        try
        {
            if (_proc != null && !_proc.HasExited)
            {
                try { _proc.CloseMainWindow(); } catch { }
                if (!_proc.WaitForExit(200)) { _proc.Kill(); }
                _proc.WaitForExit(200);
                _proc.Dispose();
                _proc = null;
                UDebug.Log("[GDX Runner] Τερμάτισε το Python streamer.");
            }
        }
        catch (Exception ex)
        {
            UDebug.LogWarning("[GDX Runner] StopProc: " + ex.Message);
        }
    }
}
