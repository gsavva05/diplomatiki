using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class UdpBreathReceiver : MonoBehaviour
{
    [Header("Network")]
    public int port = 5005;

    [Header("Signal")]
    public bool invertSignal = false;   // αν θέλεις αντιστροφή
    public float gain = 1f;             // προσαρμογή κλίμακας

    [Header("Filtering / Baseline")]
    [Range(0.01f, 0.5f)] public float emaAlpha = 0.15f;
    public float baselineSeconds = 2f;
    [Range(0.01f, 0.5f)] public float envAlpha = 0.05f;

    [Header("Thresholds")]
    public float slopeQuiet = 0.05f;
    public float slopeMove  = 0.20f;
    [Range(0.2f, 0.9f)] public float envHighFrac = 0.6f;
    public float minHighAmp = 0.10f;

    [Header("Box timing (optional)")]
    public float targetPhaseSeconds = 4f;

    private UdpClient client;
    private IPEndPoint ep;
    private readonly object lockObj = new object();
    private string lastRawStr = "0";
    private float lastRawValue = 0f;

    private float baseline = 0f;
    private float ema = 0f, prevEma = 0f;
    private float startTime;
    private float env = 0f;
    private float phaseStartTime;
    private float minPhaseDuration = 0.25f; // αποφυγή "τρεμόπαιγμα"

    public enum BoxPhase { Calibrating, Inhale, HoldFull, Exhale, HoldEmpty }
    [SerializeField] private BoxPhase phase = BoxPhase.Calibrating;

    public float CurrentValue => ema;
    public float PhaseSeconds => Time.time - phaseStartTime;
    public float PhaseProgress => Mathf.Clamp01((Time.time - phaseStartTime) / Mathf.Max(0.01f, targetPhaseSeconds));
    public BoxPhase CurrentPhase => phase;

    void Start()
    {
        Application.runInBackground = true;
        ep = new IPEndPoint(IPAddress.Any, port);
        client = new UdpClient(port);
        client.BeginReceive(OnReceive, null);

        startTime = Time.time;
        phase = BoxPhase.Calibrating;
        phaseStartTime = Time.time;

        Debug.Log($"[Vernier] Listening UDP on {port} …");
    }

    private void OnReceive(IAsyncResult ar)
    {
        try
        {
            byte[] data = client.EndReceive(ar, ref ep);
            string s = Encoding.UTF8.GetString(data).Trim();

            // JSON payload {"metrics":{"value":...}} ή σκέτο float
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                lock (lockObj) { lastRawValue = v; lastRawStr = s; }
            }
            else
            {
                int idx = s.IndexOf("\"value\"");
                if (idx >= 0)
                {
                    int colon = s.IndexOf(':', idx);
                    int comma = s.IndexOfAny(new[] { ',', '}', ']' }, colon + 1);
                    string sub = s.Substring(colon + 1, (comma > 0 ? comma : s.Length) - (colon + 1));
                    if (float.TryParse(sub, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        lock (lockObj) { lastRawValue = v; lastRawStr = v.ToString(CultureInfo.InvariantCulture); }
                }
            }
        }
        catch (ObjectDisposedException) { /* ignore */ }
        catch (Exception e) { Debug.LogWarning("[Vernier] UDP error: " + e.Message); }
        finally
        {
            if (client != null) client.BeginReceive(OnReceive, null);
        }
    }

    void Update()
    {
        float raw; string rawS;
        lock (lockObj) { raw = lastRawValue; rawS = lastRawStr; }
        raw = (invertSignal ? -raw : raw) * gain;

        if (Time.frameCount < 3) ema = raw;
        prevEma = ema;
        ema = Mathf.Lerp(ema, raw, emaAlpha);

        if (Time.time - startTime < baselineSeconds)
        {
            baseline = Mathf.Lerp(baseline, ema, 0.1f);
            if (Time.frameCount % 30 == 0)
                Debug.Log($"[Breath] Calibrating… baseline={baseline:0.000}");
            phase = BoxPhase.Calibrating;
            return;
        }

        bool resetPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.zKey.wasPressedThisFrame) resetPressed = true;
#else
        if (Input.GetKeyDown(KeyCode.Z)) resetPressed = true;
#endif
        if (resetPressed)
        {
            baseline = ema;
            Debug.Log($"[Breath] Baseline reset (Z). New baseline={baseline:0.000}");
        }

        float dev   = ema - baseline;
        float slope = (ema - prevEma) / Mathf.Max(Time.deltaTime, 1e-5f);
        env = Mathf.Lerp(env, Mathf.Abs(dev), envAlpha);
        float ampHigh = Mathf.Max(minHighAmp, env * envHighFrac);

        var next = phase;
        if (Mathf.Abs(slope) >= slopeMove)
        {
            next = (slope > 0f) ? BoxPhase.Inhale : BoxPhase.Exhale;
        }
        else if (Mathf.Abs(slope) < slopeQuiet)
        {
            if (dev > +ampHigh)      next = BoxPhase.HoldFull;
            else if (dev < -ampHigh) next = BoxPhase.HoldEmpty;
        }

        if (next != phase && (Time.time - phaseStartTime) >= minPhaseDuration)
        {
            phase = next;
            phaseStartTime = Time.time;
            Debug.Log($"[Box] Phase={phase} | val={ema:0.000} dev={dev:+0.000;-0.000} slope={slope:+0.000;-0.000} ampHigh={ampHigh:0.000} (raw:{rawS})");
        }
    }

    void OnDestroy()
    {
        try { client?.Close(); } catch { }
        client = null;
    }
}
