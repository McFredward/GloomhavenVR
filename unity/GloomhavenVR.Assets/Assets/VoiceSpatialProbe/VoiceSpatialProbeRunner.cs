// GloomhavenVR — VoiceSpatialProbe: a headless measurement of Unity's ACTUAL spatialiser output.
//
// WHAT THIS MEASURES, AND WHAT IT REFUSES TO MEASURE
// -------------------------------------------------
// It reads back the FINAL MIXED SAMPLES via UnityEngine.AudioRenderer and computes a per-channel
// RMS. It never reports the value it wrote to an AudioSource. If the mix is silent, the numbers
// are zero and the CSV says zero; there is no path in this file that can produce a curve without
// having captured samples that contain it.
//
// AudioRenderer is the offline capture path: Start() switches the engine out of device output and
// into user-driven recording, so this works on a box with no usable sound card, and — with
// Time.captureFramerate set — the DSP advances one captured frame per rendered frame, which makes
// the sweep both deterministic and faster than real time.
//
// The alternative, OnAudioFilterRead on the AudioListener's GameObject, is the fallback. Note that
// the SAME script on the SOURCE's GameObject would read the PRE-spatialisation buffer and would
// happily produce a flat, symmetric, entirely fictitious "measurement".
//
// THE FRAME
// ---------
// Listener at world origin, rotation identity, on its own GameObject with no AudioSource on it.
// Unity is LEFT-handed: +X right, +Y up, +Z forward. Bearing theta is clockwise from straight
// ahead in the horizontal plane, so the source sits at d * (sin theta, 0, cos theta) and theta=90
// is the listener's RIGHT — which is the positive-balance side, and the hard-pan control is what
// proves the capture agrees.
//
// THE SCALE (mirrors src/GloomhavenVR/Core/EnvSound.cs)
// ----------------------------------------------------
// EnvSound.ApplyScale is the whole convention, and it is two lines:
//     v.Source.minDistance = v.MinMeters * s;
//     v.Source.maxDistance = v.MaxMeters * s;
// where s = rigScale = WORLD UNITS PER PERCEIVED METRE. Every authored distance is in PERCEIVED
// metres and is multiplied by rigScale on its way to the AudioSource. This probe does the same for
// minDistance, maxDistance, the source's position, and (see CUSTOM ROLLOFF below) the curve's X
// axis. One deviation to declare: EnvSound only ever uses AudioRolloffMode.Logarithmic
// (EnvSound.cs:2038) and never calls SetCustomCurve, so Custom-mode support here is an EXTENSION
// past the shipped convention, not a replica of it.
//
// CUSTOM ROLLOFF, AND WHY IT HAS A UNITS SWITCH
// ---------------------------------------------
// Unity evaluates an AudioSourceCurveType.CustomRolloff curve over distance NORMALISED to
// maxDistance: key x=0 is the listener, key x=1 is maxDistance. A curve authored in perceived
// metres therefore has to be divided by the PERCEIVED maxDistance (equivalently: scaled to world
// units then divided by the WORLD maxDistance — the rigScale cancels). Because the caller's intent
// is not recoverable from the numbers, the config carries "customRolloffXUnits", one of
// "normalized" (default) or "perceivedMeters", and this file does exactly what it says.
//
// COMMAND LINE
//     <player> -batchmode -nographics -logFile <log> --probe-config <cfg.json> --probe-out <out.csv>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace GloomhavenVR.VoiceProbe
{
    /// <summary>One measurement point: a case's settings evaluated at one source position.</summary>
    internal sealed class Point
    {
        internal CaseSpec Case;
        internal string Mode;              // "pan" | "rolloff"
        internal float BearingDeg;
        internal float DistancePerceived;
    }

    /// <summary>One row of the config's "cases" array.</summary>
    internal sealed class CaseSpec
    {
        internal string Name = "case";
        internal bool Play = true;                  // false => the silence control
        internal string Sweep = "both";             // "both" | "pan" | "rolloff"
        internal float SpatialBlend = 1f;
        internal string RolloffMode = "Logarithmic";
        internal float MinDistancePerceived = 1f;
        internal float MaxDistancePerceived = 20f;
        internal float Spread;
        internal float DopplerLevel;
        internal float PanStereo;
        internal float Volume = 1f;
        internal float[][] CustomRolloff;
    }

    public sealed class VoiceSpatialProbeRunner : MonoBehaviour
    {
        // ---- config ------------------------------------------------------------------------
        private int _sampleRate = 48000;
        private int _dspBufferSize = 1024;
        private int _captureFramerate = 60;
        private float _rigScale = 1f;
        private float _toneHz = 440f;
        private float _toneAmplitude = 0.5f;
        private float _settleSeconds = 0.35f;
        private float _measureSeconds = 0.25f;
        private float _bearingDistance = 1.5f;
        private float[] _bearings = { 0f, 90f, 180f, 270f };
        private float[] _distances = { 1f, 2f, 4f, 8f };
        private string _customRolloffXUnits = "normalized";
        private readonly List<CaseSpec> _cases = new List<CaseSpec>();

        // ---- scene -------------------------------------------------------------------------
        private AudioSource _src;
        private AudioClip _clip;
        private GameObject _listenerGo;

        // ---- capture state -----------------------------------------------------------------
        private readonly List<Point> _points = new List<Point>();
        private int _pointIndex = -1;
        private int _channels = 2;
        private long _settleTarget, _measureTarget;   // in sample FRAMES
        private long _settleLeft, _measureLeft;
        private double _sumL2, _sumR2;
        private long _measured;
        private bool _finished;
        private StreamWriter _csv;
        private string _outPath;

        // ---- capture backend ----------------------------------------------------------------
        // Two of them, chosen with --capture-mode:
        //   audiorenderer  UnityEngine.AudioRenderer, the offline path. Preferred.
        //   filter         OnAudioFilterRead on the AudioListener's GameObject — the post-mix,
        //                  post-pan buffer. The same script on the SOURCE would be
        //                  PRE-spatialisation and would produce a confident, symmetric fiction.
        private bool _useFilter;
        private ListenerTap _tap;
        private bool _resetAudio = true;

        // ---- health -------------------------------------------------------------------------
        private long _framesRendered, _totalSampleFrames, _emptyFrames;
        private double _peakAbs;
        private int _framesSinceProgress;
        private double _dspAtStart;
        private float _captureStartRealtime;
        private const int StallFrameLimit = 200000;   // ~55 min of captured audio at 60 fps; a hang, not a sweep
        private const float AudioRendererGraceSeconds = 3f;

        // =====================================================================================

        private void Awake()
        {
            Application.runInBackground = true;
            try { UnityEngine.Rendering.SplashScreen.Stop(UnityEngine.Rendering.SplashScreen.StopBehavior.StopImmediate); }
            catch (Exception) { /* Personal licence may refuse; harmless under -nographics */ }

            string cfgPath = Arg("--probe-config");
            _outPath = Arg("--probe-out");
            if (string.IsNullOrEmpty(cfgPath) || string.IsNullOrEmpty(_outPath))
            {
                Fail("PROBE FAIL: need --probe-config <file.json> and --probe-out <file.csv>.");
                return;
            }

            try { LoadConfig(cfgPath); }
            catch (Exception e) { Fail("PROBE FAIL: config unreadable — " + e); return; }

            // ---- overrides, so a failing environment can be BISECTED without a rebuild --------
            string mode = Arg("--capture-mode") ?? "audiorenderer";
            _useFilter = string.Equals(mode, "filter", StringComparison.OrdinalIgnoreCase);
            _resetAudio = Flag("--no-audio-reset") ? false : true;
            string cfr = Arg("--capture-framerate");
            if (cfr != null && int.TryParse(cfr, out int cfrv)) _captureFramerate = cfrv;
            Log($"PROBE mode: capture '{(_useFilter ? "filter (OnAudioFilterRead on the AudioListener)" : "audiorenderer")}', " +
                $"AudioSettings.Reset {_resetAudio}, captureFramerate {_captureFramerate}");

            // ---- audio device: ask for the rate we want and then REPORT WHAT WE GOT ----------
            bool reset = false;
            if (_resetAudio)
            {
                var want = AudioSettings.GetConfiguration();
                want.sampleRate = _sampleRate;
                want.speakerMode = AudioSpeakerMode.Stereo;
                want.dspBufferSize = _dspBufferSize;
                reset = AudioSettings.Reset(want);
            }
            var got = AudioSettings.GetConfiguration();
            Log($"PROBE audio: Reset({_sampleRate} Hz, Stereo, dsp {_dspBufferSize}) -> {reset}; " +
                $"actual sampleRate {got.sampleRate}, speakerMode {got.speakerMode}, " +
                $"dspBufferSize {got.dspBufferSize}, numRealVoices {got.numRealVoices}, " +
                $"AudioSettings.outputSampleRate {AudioSettings.outputSampleRate}, " +
                $"driverCapabilities {AudioSettings.driverCapabilities}");

            if (got.sampleRate <= 0)
            {
                Fail($"PROBE FAIL: the engine reports sampleRate {got.sampleRate}. There is no audio " +
                     "clock here and nothing this rig prints afterwards would mean anything.");
                return;
            }
            _sampleRate = got.sampleRate;
            _channels = ChannelsFor(got.speakerMode);
            if (_channels < 2)
            {
                Fail($"PROBE FAIL: speakerMode {got.speakerMode} gives {_channels} channel(s). " +
                     "A stereo balance cannot be measured through a mono bus.");
                return;
            }

            BuildScene();
            BuildPointList();

            // ---- offline clock: one captured frame per rendered frame -----------------------
            // captureFramerate DOES NOT MAKE THE AUDIO OFFLINE — measured, not assumed. With it set
            // to 60 the game clock ran ~40x faster than wall time (Time.time 50 s at
            // realtimeSinceStartup 0.21 s) while AudioSettings.dspTime advanced at exactly wall
            // rate. The mixer here is real-time whatever the game clock does, so a sweep costs
            // (settle + measure) x points SECONDS, and spinning the game loop faster only burns a
            // core. Left configurable because it is the right knob on a platform where AudioRenderer
            // works, and defaulted to 0 in the shipped config for the reason above.
            if (_captureFramerate > 0) Time.captureFramerate = _captureFramerate;
            else Application.targetFrameRate = 250;

            if (_useFilter)
            {
                _tap = _listenerGo.AddComponent<ListenerTap>();
                Log($"PROBE capture: ListenerTap (OnAudioFilterRead) on '{_listenerGo.name}'. " +
                    $"channels {_channels}, sampleRate {_sampleRate}, " +
                    $"Time.captureFramerate {Time.captureFramerate}, points {_points.Count}");
            }
            else
            {
                if (!AudioRenderer.Start())
                {
                    Fail("PROBE FAIL: AudioRenderer.Start() returned false — the engine would not enter " +
                         "recording mode, so nothing downstream is a measurement.");
                    return;
                }
                Log($"PROBE AudioRenderer.Start() ok. channels {_channels}, sampleRate {_sampleRate}, " +
                    $"Time.captureFramerate {Time.captureFramerate}, points {_points.Count}");
            }
            _dspAtStart = AudioSettings.dspTime;
            _captureStartRealtime = Time.realtimeSinceStartup;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_outPath)));
                _csv = new StreamWriter(_outPath, false, new UTF8Encoding(false));
                _csv.WriteLine("case,mode,bearing_deg,distance_m_perceived,distance_world,rms_l,rms_r,rms_total,level_db,balance");
                _csv.Flush();
            }
            catch (Exception e) { Fail("PROBE FAIL: cannot write CSV — " + e); return; }

            _settleTarget = Math.Max(1, (long)Math.Round(_settleSeconds * _sampleRate));
            _measureTarget = Math.Max(1, (long)Math.Round(_measureSeconds * _sampleRate));
            Advance();
        }

        // =====================================================================================
        // The capture loop. AudioRenderer.Render MUST be called every frame; the state machine
        // below consumes the frames it produced. Boundaries are counted in SAMPLE FRAMES, never
        // in wall-clock seconds, so the run is correct whether the DSP is offline or real time.
        // =====================================================================================
        private void Update()
        {
            if (_finished || _csv == null) return;
            _framesRendered++;
            Heartbeat();

            if (_useFilter)
            {
                float[] chunk;
                bool any = false;
                while ((chunk = _tap.Take()) != null)
                {
                    int n = chunk.Length / _channels;
                    if (n <= 0) continue;
                    any = true;
                    _totalSampleFrames += n;
                    Consume(chunk, n);
                    if (_finished) return;
                }
                if (!any) _emptyFrames++;
            }
            else
            {
                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                if (n <= 0)
                {
                    _emptyFrames++;
                    if (FallBackToFilterIfDead()) return;
                    StallCheck();
                    return;
                }

                var buf = new NativeArray<float>(n * _channels, Allocator.Temp, NativeArrayOptions.ClearMemory);
                try
                {
                    if (!AudioRenderer.Render(buf))
                    {
                        Fail($"PROBE FAIL: AudioRenderer.Render returned false at frame {_framesRendered}.");
                        return;
                    }
                    _totalSampleFrames += n;
                    var managed = new float[n * _channels];
                    buf.CopyTo(managed);
                    Consume(managed, n);
                }
                finally { buf.Dispose(); }
            }
            StallCheck();
        }

        /// <summary>
        /// AudioRenderer is the PREFERRED capture and it is dead on this box. This is the switch
        /// to the fallback, and it is deliberately loud.
        ///
        /// MEASURED, Unity 2021.3.5f1, Linux standalone player, -batchmode -nographics under
        /// xvfb-run: AudioRenderer.Start() returns TRUE, GetSampleCountForCaptureFrame() then
        /// returns 0 for every frame forever, AND AudioSettings.dspTime stops advancing at the
        /// value it had when Start() was called (measured: frozen at 0.3627 across 200,001 frames,
        /// with AudioSource.timeSamples stuck at 0). Starting the recorder does not merely fail to
        /// capture — it stops the mixer. Without AudioRenderer the same build's dspTime advances
        /// normally and the listener tap returns real audio.
        ///
        /// The switch happens BEFORE any measurement completes, so no row is ever half one backend
        /// and half the other, and the settle window for the current point is restarted.
        /// </summary>
        private bool FallBackToFilterIfDead()
        {
            if (_totalSampleFrames > 0) return false;
            if (Time.realtimeSinceStartup - _captureStartRealtime < AudioRendererGraceSeconds) return false;

            double dsp = AudioSettings.dspTime;
            Debug.LogWarning(
                $"PROBE FALLBACK: AudioRenderer produced 0 sample frames in {AudioRendererGraceSeconds:0.#} s " +
                $"of wall clock ({_framesRendered} rendered frames) and dspTime moved " +
                $"{dsp - _dspAtStart:F4} s in that time. On this platform AudioRenderer.Start() " +
                "reports success and then stops the mixer. Switching to the OnAudioFilterRead tap on " +
                "the AudioListener — the post-mix, post-pan buffer — and RESTARTING the current " +
                "point. Every row in the CSV after this line comes from the tap, not from AudioRenderer.");

            try { AudioRenderer.Stop(); } catch (Exception) { }
            _useFilter = true;
            _tap = _listenerGo.AddComponent<ListenerTap>();
            _emptyFrames = 0;
            _captureStartRealtime = Time.realtimeSinceStartup;
            _dspAtStart = AudioSettings.dspTime;
            _framesSinceProgress = 0;
            // Re-arm the current point from scratch: its settle window was counted against a
            // backend that delivered nothing.
            _pointIndex--;
            Advance();
            return true;
        }

        /// <summary>
        /// The DSP clock, printed on a slow cadence.
        ///
        /// This is the field that separates "AudioRenderer is broken" from "there is no audio
        /// engine running at all". AudioSettings.dspTime advances only when the mixer actually
        /// processes buffers. If it is FROZEN, no capture path can produce a sample and neither
        /// backend is at fault; if it ADVANCES while GetSampleCountForCaptureFrame stays at 0,
        /// the fault is AudioRenderer's specifically. Without this line the two are
        /// indistinguishable and the temptation is to blame whichever one was being tried.
        /// </summary>
        private void Heartbeat()
        {
            if (_framesRendered % 300 != 0 || _framesRendered > 6000) return;
            Log($"PROBE tick frame {_framesRendered}: dspTime {AudioSettings.dspTime:F4} " +
                $"(+{AudioSettings.dspTime - _dspAtStart:F4} since capture start), " +
                $"Time.time {Time.time:F3}, Time.realtimeSinceStartup {Time.realtimeSinceStartup:F2}, " +
                $"captured sampleFrames {_totalSampleFrames}, empty frames {_emptyFrames}, " +
                $"source isPlaying {(_src != null && _src.isPlaying)} timeSamples {(_src != null ? _src.timeSamples : -1)} " +
                $"isVirtual {(_src != null && _src.isVirtual)}, listener volume {AudioListener.volume} " +
                $"pause {AudioListener.pause}, peak |sample| so far {_peakAbs:0.######}" +
                (_tap != null ? $", tap buffers delivered {_tap.Delivered} dropped {_tap.Dropped}" : ""));
        }

        private void Consume(float[] buf, int frames)
        {
            int i = 0;
            while (i < frames && !_finished)
            {
                if (_settleLeft > 0)
                {
                    int take = (int)Math.Min(_settleLeft, frames - i);
                    // The settle window is DISCARDED, but it still feeds the peak meter — a run whose
                    // only non-zero samples land in settle windows is a run worth being told about.
                    for (int k = 0; k < take; k++)
                    {
                        int b = (i + k) * _channels;
                        double a = Math.Abs(buf[b]); if (a > _peakAbs) _peakAbs = a;
                        double c = Math.Abs(buf[b + 1]); if (c > _peakAbs) _peakAbs = c;
                    }
                    _settleLeft -= take;
                    i += take;
                    continue;
                }

                int m = (int)Math.Min(_measureLeft, frames - i);
                for (int k = 0; k < m; k++)
                {
                    int b = (i + k) * _channels;
                    double l = buf[b], r = buf[b + 1];
                    _sumL2 += l * l;
                    _sumR2 += r * r;
                    double al = Math.Abs(l); if (al > _peakAbs) _peakAbs = al;
                    double ar = Math.Abs(r); if (ar > _peakAbs) _peakAbs = ar;
                }
                _measured += m;
                _measureLeft -= m;
                i += m;

                if (_measureLeft <= 0)
                {
                    WriteRow();
                    _framesSinceProgress = 0;
                    Advance();
                }
            }
        }

        private void WriteRow()
        {
            var p = _points[_pointIndex];
            double n = Math.Max(1, _measured);
            double rmsL = Math.Sqrt(_sumL2 / n);
            double rmsR = Math.Sqrt(_sumR2 / n);
            // Total is the ENERGY sum, sqrt(L^2 + R^2): a centred 2D source that lands at unity gain
            // in both channels then reads back the source amplitude itself, so a level of 0 dB means
            // "as loud as authored" and not "as loud as authored, minus a panning-law constant".
            double rmsT = Math.Sqrt(rmsL * rmsL + rmsR * rmsR);
            double den = rmsR + rmsL;
            double bal = den > 1e-12 ? (rmsR - rmsL) / den : 0.0;
            double db = rmsT > 1e-12 ? 20.0 * Math.Log10(rmsT) : -120.0;
            if (db < -120.0) db = -120.0;
            float world = p.DistancePerceived * _rigScale;

            _csv.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2:0.###},{3:0.####},{4:0.####},{5:0.########},{6:0.########},{7:0.########},{8:0.###},{9:0.######}",
                Csv(p.Case.Name), p.Mode, p.BearingDeg, p.DistancePerceived, world, rmsL, rmsR, rmsT, db, bal));
            _csv.Flush();
        }

        private void Advance()
        {
            _pointIndex++;
            _sumL2 = _sumR2 = 0.0;
            _measured = 0;
            if (_pointIndex >= _points.Count) { Finish(0); return; }

            var p = _points[_pointIndex];
            Apply(p);
            _settleLeft = _settleTarget;
            _measureLeft = _measureTarget;
        }

        // =====================================================================================

        private void Apply(Point p)
        {
            var c = p.Case;
            float rad = p.BearingDeg * Mathf.Deg2Rad;
            float world = p.DistancePerceived * _rigScale;
            // LEFT-HANDED: bearing clockwise from +Z, so +90 deg is +X, the listener's right.
            _src.transform.position = new Vector3(Mathf.Sin(rad) * world, 0f, Mathf.Cos(rad) * world);

            _src.spatialBlend = c.SpatialBlend;
            _src.panStereo = c.PanStereo;
            _src.spread = c.Spread;
            _src.dopplerLevel = c.DopplerLevel;
            _src.volume = c.Volume;
            _src.minDistance = c.MinDistancePerceived * _rigScale;   // EnvSound.ApplyScale convention
            _src.maxDistance = c.MaxDistancePerceived * _rigScale;   // EnvSound.ApplyScale convention

            switch (c.RolloffMode)
            {
                case "Linear": _src.rolloffMode = AudioRolloffMode.Linear; break;
                case "Custom":
                    _src.rolloffMode = AudioRolloffMode.Custom;
                    _src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, BuildRolloffCurve(c));
                    break;
                default: _src.rolloffMode = AudioRolloffMode.Logarithmic; break;
            }

            if (c.Play) { if (!_src.isPlaying) _src.Play(); }
            else if (_src.isPlaying) _src.Stop();
        }

        private AnimationCurve BuildRolloffCurve(CaseSpec c)
        {
            if (c.CustomRolloff == null || c.CustomRolloff.Length == 0)
                return AnimationCurve.Linear(0f, 1f, 1f, 0f);
            bool meters = string.Equals(_customRolloffXUnits, "perceivedMeters", StringComparison.OrdinalIgnoreCase);
            // Unity normalises the custom curve's X to maxDistance. Converting a perceived-metre X
            // to that normalised axis is x_perceived / maxDistance_perceived — the rigScale cancels,
            // because both the numerator and maxDistance are scaled by it.
            float denom = Mathf.Max(1e-6f, c.MaxDistancePerceived);
            var keys = new Keyframe[c.CustomRolloff.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                float x = c.CustomRolloff[i][0];
                keys[i] = new Keyframe(meters ? x / denom : x, c.CustomRolloff[i][1]);
            }
            // Straight-line segments between keys: a rolloff table is a table, and Unity's default
            // smoothed tangents would overshoot between two authored points and quietly invent
            // attenuation the caller never wrote. UnityEditor.AnimationUtility does not exist in a
            // player, so the tangents are set by hand.
            Array.Sort(keys, (a, b) => a.time.CompareTo(b.time));
            for (int i = 0; i < keys.Length; i++)
            {
                float sIn = i > 0 ? Slope(keys[i - 1], keys[i]) : 0f;
                float sOut = i < keys.Length - 1 ? Slope(keys[i], keys[i + 1]) : 0f;
                keys[i].inTangent = i > 0 ? sIn : sOut;
                keys[i].outTangent = i < keys.Length - 1 ? sOut : sIn;
            }
            return new AnimationCurve(keys);
        }

        private static float Slope(Keyframe a, Keyframe b)
        {
            float dt = b.time - a.time;
            return Mathf.Abs(dt) < 1e-9f ? 0f : (b.value - a.value) / dt;
        }

        // =====================================================================================

        private void BuildScene()
        {
            var listenerGo = _listenerGo = new GameObject("Listener");
            listenerGo.transform.position = Vector3.zero;
            listenerGo.transform.rotation = Quaternion.identity;
            listenerGo.AddComponent<AudioListener>();   // and NOTHING else — no AudioSource here

            var srcGo = new GameObject("Source");
            _src = srcGo.AddComponent<AudioSource>();
            _src.clip = _clip = MakeSine(_toneHz, _toneAmplitude, _sampleRate);
            _src.loop = true;
            _src.playOnAwake = false;
            // bypassListenerEffects MUST STAY FALSE. It was true here for one build and it cost a
            // round: it routes the source AROUND the AudioListener's filter chain, which is exactly
            // where the OnAudioFilterRead tap lives. The result was a perfectly healthy-looking run
            // — clip peak 0.5, listener volume 1, source isPlaying, timeSamples advancing, buffers
            // arriving — in which every captured sample was 0.0 and every row read -120 dB. A
            // measurement rig can disconnect its own probe and still pass every other check.
            _src.bypassEffects = false;
            _src.bypassListenerEffects = false;
            _src.bypassReverbZones = true;   // no reverb zone exists in this scene; belt and braces
            _src.dopplerLevel = 0f;

            // A muted listener would make every row in the CSV read -120 dB and look exactly like a
            // spatialiser that attenuates everything. Set it, then READ IT BACK, and say both.
            AudioListener.pause = false;
            AudioListener.volume = 1f;

            // Read the clip's samples back out of the engine. AudioClip.Create + SetData can succeed
            // and still leave an empty clip; if that ever happened, the whole run would measure
            // silence and the obvious (wrong) conclusion would be "the spatialiser outputs nothing".
            var back = new float[Math.Min(_clip.samples, 4096)];
            bool got = _clip.GetData(back, 0);
            double clipPeak = 0.0;
            for (int i = 0; i < back.Length; i++) clipPeak = Math.Max(clipPeak, Math.Abs(back[i]));
            Log($"PROBE clip: {_clip.name} {_clip.samples} samples @ {_clip.frequency} Hz, " +
                $"{_clip.channels} ch, {_clip.length * 1000f:0.##} ms, loadState {_clip.loadState}, " +
                $"GetData {got} peak {clipPeak:0.######} (this is the SOURCE material — if it is 0 the " +
                $"fault is here and not in the spatialiser), listener volume {AudioListener.volume} " +
                $"pause {AudioListener.pause}");
        }

        /// <summary>
        /// A steady sine whose length is a WHOLE number of periods, so the loop point has no
        /// discontinuity and the click cannot be mistaken for a spatialisation artefact.
        /// </summary>
        private static AudioClip MakeSine(float hz, float amp, int rate)
        {
            int samples = rate;                       // fallback: one second
            for (int k = 1; k <= 2000; k++)
            {
                double exact = rate * k / (double)hz;
                if (Math.Abs(exact - Math.Round(exact)) < 1e-9) { samples = (int)Math.Round(exact); break; }
            }
            if (samples < 256) samples *= (256 / samples) + 1;
            var data = new float[samples];
            double w = 2.0 * Math.PI * hz / rate;
            for (int i = 0; i < samples; i++) data[i] = (float)(amp * Math.Sin(w * i));
            var clip = AudioClip.Create($"sine_{hz:0}Hz", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void BuildPointList()
        {
            foreach (var c in _cases)
            {
                bool pan = c.Sweep == "both" || c.Sweep == "pan";
                bool roll = c.Sweep == "both" || c.Sweep == "rolloff";
                if (pan)
                    foreach (var b in _bearings)
                        _points.Add(new Point { Case = c, Mode = "pan", BearingDeg = b, DistancePerceived = _bearingDistance });
                if (roll)
                    foreach (var d in _distances)
                        _points.Add(new Point { Case = c, Mode = "rolloff", BearingDeg = 0f, DistancePerceived = d });
            }
        }

        // =====================================================================================

        private void LoadConfig(string path)
        {
            var root = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(path)))
                       ?? throw new FormatException("config root is not an object");

            _sampleRate = MiniJson.Int(root, "sampleRate", 48000);
            _dspBufferSize = MiniJson.Int(root, "dspBufferSize", 1024);
            _captureFramerate = MiniJson.Int(root, "captureFramerate", 60);
            _rigScale = MiniJson.Flt(root, "rigScale", 1f);
            _toneHz = MiniJson.Flt(root, "toneHz", 440f);
            _toneAmplitude = MiniJson.Flt(root, "toneAmplitude", 0.5f);
            _settleSeconds = MiniJson.Flt(root, "settleSeconds", 0.35f);
            _measureSeconds = MiniJson.Flt(root, "measureSeconds", 0.25f);
            _bearingDistance = MiniJson.Flt(root, "bearingDistanceMeters", 1.5f);
            _bearings = MiniJson.Floats(root, "bearings", _bearings);
            _distances = MiniJson.Floats(root, "distancesMeters", _distances);
            _customRolloffXUnits = MiniJson.Str(root, "customRolloffXUnits", "normalized");

            var list = MiniJson.AsList(root.ContainsKey("cases") ? root["cases"] : null)
                       ?? throw new FormatException("config has no 'cases' array");
            foreach (var o in list)
            {
                var c = MiniJson.AsObject(o) ?? throw new FormatException("a 'cases' entry is not an object");
                _cases.Add(new CaseSpec
                {
                    Name = MiniJson.Str(c, "name", "case"),
                    Play = MiniJson.Bool(c, "play", true),
                    Sweep = MiniJson.Str(c, "sweep", "both"),
                    SpatialBlend = MiniJson.Flt(c, "spatialBlend", 1f),
                    RolloffMode = MiniJson.Str(c, "rolloffMode", "Logarithmic"),
                    MinDistancePerceived = MiniJson.Flt(c, "minDistance", 1f),
                    MaxDistancePerceived = MiniJson.Flt(c, "maxDistance", 20f),
                    Spread = MiniJson.Flt(c, "spread", 0f),
                    DopplerLevel = MiniJson.Flt(c, "dopplerLevel", 0f),
                    PanStereo = MiniJson.Flt(c, "panStereo", 0f),
                    Volume = MiniJson.Flt(c, "volume", 1f),
                    CustomRolloff = MiniJson.Pairs(c, "customRolloff"),
                });
            }
            if (_cases.Count == 0) throw new FormatException("config's 'cases' array is empty");
            Log($"PROBE config: {path} — rigScale {_rigScale}, tone {_toneHz} Hz amp {_toneAmplitude}, " +
                $"settle {_settleSeconds}s measure {_measureSeconds}s, {_cases.Count} cases, " +
                $"{_bearings.Length} bearings @ {_bearingDistance} m, {_distances.Length} distances, " +
                $"customRolloffXUnits '{_customRolloffXUnits}'");
        }

        private static int ChannelsFor(AudioSpeakerMode m)
        {
            switch (m)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Stereo: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                case AudioSpeakerMode.Prologic: return 2;
                default: return 2;
            }
        }

        private void StallCheck()
        {
            if (++_framesSinceProgress <= StallFrameLimit) return;
            Fail($"PROBE FAIL: {StallFrameLimit} rendered frames with no completed measurement " +
                 $"(point {_pointIndex + 1}/{_points.Count}, sampleFrames captured {_totalSampleFrames}). " +
                 "Treating this as a hang rather than waiting for a number that is not coming.");
        }

        private void Finish(int code)
        {
            if (_finished) return;
            _finished = true;
            if (!_useFilter) { try { AudioRenderer.Stop(); } catch (Exception) { } }
            try { _csv?.Flush(); _csv?.Close(); } catch (Exception) { }
            _csv = null;
            Log($"PROBE dsp clock: dspTime advanced {AudioSettings.dspTime - _dspAtStart:F4} s " +
                $"over {_framesRendered} rendered frames. A frozen clock here means the engine " +
                "never mixed anything and NO capture path could have measured anything.");
            double secs = _totalSampleFrames / (double)Math.Max(1, _sampleRate);
            Log($"PROBE DONE code {code}. rows {Math.Max(0, Math.Min(_pointIndex, _points.Count))}/{_points.Count}, " +
                $"rendered frames {_framesRendered} ({_emptyFrames} empty), captured sample frames " +
                $"{_totalSampleFrames} = {secs:0.###} s of audio, peak |sample| over the whole run {_peakAbs:0.######}");
            if (_peakAbs <= 0.0)
                Log("PROBE WARNING: the peak absolute sample over the ENTIRE run is 0. Every number in " +
                    "the CSV is a measurement of silence. Do not read curves into it.");
            Log($"PROBE CSV {_outPath}");
            Application.Quit(code);
        }

        private void Fail(string msg)
        {
            Debug.LogError(msg);
            Debug.LogError($"PROBE FAIL context: dspTime advanced " +
                           $"{AudioSettings.dspTime - _dspAtStart:F4} s, captured sampleFrames " +
                           $"{_totalSampleFrames}, rendered frames {_framesRendered}.");
            _finished = true;
            if (!_useFilter) { try { AudioRenderer.Stop(); } catch (Exception) { } }
            try { _csv?.Flush(); _csv?.Close(); } catch (Exception) { }
            _csv = null;
            Application.Quit(3);
        }

        private void OnDestroy()
        {
            try { _csv?.Flush(); _csv?.Close(); } catch (Exception) { }
            _csv = null;
        }

        private static void Log(string s) => Debug.Log(s);

        private static string Csv(string s) =>
            s != null && (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0)
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;

        private static bool Flag(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], name, StringComparison.Ordinal)) return a[i + 1];
            return null;
        }
    }
}
