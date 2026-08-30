using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

/// <summary>
/// 出力バックエンドの種類。SettingsPanel から選択される。
/// </summary>
public enum AudioBackend { Wasapi, Asio, DirectSound }

/// <summary>
/// NAudioベースの低遅延オーディオエンジン。WASAPI/ASIO/DirectSoundを選択可能。
/// 出力は常時走らせた1本のミキサーに集約し、再生 = ミキサーへの入力追加のみで完結させる。
/// バッファサイズや出力方式の変更は Reinit() で即座に反映され、再生中のBGM/効果音は
/// 新しいミキサーへそのまま引き継がれるため音切れしない。
/// 既定の再生デバイスが変更/切断された場合も自動的に再初期化して復帰する。
/// </summary>
public static class AudioEngine
{
    private static IWavePlayer? _output;
    private static MixingSampleProvider? _mixer;
    private static readonly object _reinitLock = new();
    private static readonly HashSet<ISampleProvider> _activeInputs = new();

    private static MMDeviceEnumerator? _enumerator;
    private static DeviceNotificationClient? _notifyClient;
    private static volatile bool _reinitPending;

    public static bool IsReady { get; private set; }
    public static int SampleRate { get; private set; } = 48000;
    public static string OutputMode { get; private set; } = "(none)";
    public static string LastError { get; private set; } = "";

    private static long _reinitDueTicks;
    private const long ReinitDelayTicks = 300 * TimeSpan.TicksPerMillisecond;

    internal static WaveFormat MixFormat => _mixer!.WaveFormat;

    /// <summary>
    /// 利用可能なASIOドライバ名一覧。ドライバが1つも無ければ空配列。
    /// </summary>
    public static string[] GetAsioDriverNames()
    {
        try { return AsioOut.GetDriverNames(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// メインループから毎フレーム呼ぶこと。デバイス通知等で予約された再初期化を、
    /// メインスレッド上で実際に実行する。ASIOドライバはCOM/STAのスレッド制約に敏感なため、
    /// バックグラウンド(通知コールバック)スレッドから直接Init/Disposeさせないようにするための仕組み。
    /// </summary>
    public static void Update()
    {
        if (!_reinitPending) return;
        if (Environment.TickCount64 < _reinitDueTicks) return;

        _reinitPending = false;
        try { Reinit(); } catch (Exception ex) { LastError = ex.Message; }
    }

    public static void Init()
    {
        if (IsReady) return;

        try { MediaFoundationApi.Startup(); } catch { }

        try
        {
            _enumerator = new MMDeviceEnumerator();
            _notifyClient = new DeviceNotificationClient(RequestReinit);
            _enumerator.RegisterEndpointNotificationCallback(_notifyClient);
        }
        catch { }

        StartOutput();
    }

    /// <summary>
    /// 出力方式/バッファサイズ/既定デバイスの変更を即座に反映する。
    /// 再生中のミキサー入力(BGM/効果音)は新しい出力に再登録される。
    /// このメソッドは必ずメインスレッド(AudioEngine.Update()を呼んでいるスレッド、
    /// または設定画面操作と同じスレッド)から呼ぶこと。ASIOはスレッドアフィニティに敏感なため。
    /// </summary>
    public static void Reinit()
    {
        lock (_reinitLock)
        {
            StopOutputOnly();
            StartOutput();
        }
    }

    // デバイス通知はCOMコールバックスレッドから飛んでくるため、実処理は行わずフラグを立てるだけにする。
    // 実際のReinit()はAudioEngine.Update()経由でメインスレッドから呼ばれる。
    private static void RequestReinit()
    {
        _reinitPending = true;
        // デバイス切替直後は旧デバイスの解放が完了していないことがあるため少し待ってから実行する
        _reinitDueTicks = Environment.TickCount64 + ReinitDelayTicks / TimeSpan.TicksPerMillisecond;
    }

    private static void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // e.Exception != null ならデバイス切断など異常停止 → 自動復帰を試みる
        if (e.Exception != null) RequestReinit();
    }

    private static void StartOutput()
    {
        MMDevice? device = null;
        try { device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { }

        int bufferMs = Math.Clamp(SettingsPanel.BufferSizeMs, 5, 100);
        IsReady = false;

        bool started = SettingsPanel.OutputBackend switch
        {
            AudioBackend.Asio => TryStartAsio(),
            AudioBackend.DirectSound => TryStartDirectSound(bufferMs),
            _ => TryStartWasapi(device, bufferMs)
        };

        // 選択した方式で開けなかった場合、共有WASAPIで最低限鳴らす
        if (!started && device != null)
            started = TryStart(device, AudioClientShareMode.Shared, device.AudioClient.MixFormat.SampleRate, Math.Max(bufferMs, 15));

        if (started)
        {
            // 再生中だった入力(BGM/効果音)を新しいミキサーへ引き継ぐ
            foreach (var input in _activeInputs)
                _mixer!.AddMixerInput(input);
        }

        Console.WriteLine($"[Audio] {OutputMode}");
    }

    private static void StopOutputOnly()
    {
        try
        {
            if (_output != null)
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _output.Stop();
                _output.Dispose();
            }
        }
        catch { }
        _output = null;
        _mixer = null;
        IsReady = false;
    }

    private static bool TryStartWasapi(MMDevice? device, int bufferMs)
    {
        if (device == null) return false;

        if (SettingsPanel.UseWasapiExclusive)
        {
            // 設定画面で選択されたサンプルレートを最優先で試し、開けなければ既定候補にフォールバックする
            int preferred = SettingsPanel.PreferredSampleRate;
            foreach (int rate in new[] { preferred, 48000, 44100 }.Distinct())
            {
                if (TryStart(device, AudioClientShareMode.Exclusive, rate, bufferMs)) return true;
            }
        }

        int mixRate = device.AudioClient.MixFormat.SampleRate;
        return TryStart(device, AudioClientShareMode.Shared, mixRate, Math.Max(bufferMs, 10));
    }

    private static bool TryStart(MMDevice device, AudioClientShareMode mode, int rate, int latencyMs)
    {
        WasapiOut? output = null;
        try
        {
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { ReadFully = true };
            output = new WasapiOut(device, mode, true, latencyMs);
            IWaveProvider provider = mode == AudioClientShareMode.Exclusive
                ? new SampleToWaveProvider16(mixer)
                : new SampleToWaveProvider(mixer);
            output.Init(provider);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();

            _mixer = mixer;
            _output = output;
            SampleRate = rate;
            OutputMode = $"WASAPI {(mode == AudioClientShareMode.Exclusive ? "排他" : "共有")} {rate}Hz {latencyMs}ms";
            LastError = "";
            IsReady = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            output?.Dispose();
            return false;
        }
    }

    private static bool TryStartDirectSound(int bufferMs)
    {
        DirectSoundOut? output = null;
        try
        {
            // ミキサーのサンプルレートは設定画面で選択された値を使い、DirectSoundOutにはms単位のバッファのみ渡す
            int rate = SettingsPanel.PreferredSampleRate;
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { ReadFully = true };
            output = new DirectSoundOut(bufferMs);
            IWaveProvider provider = new SampleToWaveProvider16(mixer);
            output.Init(provider);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();

            _mixer = mixer;
            _output = output;
            SampleRate = rate;
            OutputMode = $"DirectSound {rate}Hz {bufferMs}ms";
            LastError = "";
            IsReady = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            output?.Dispose();
            return false;
        }
    }

    private static bool TryStartAsio()
    {
        AsioOut? output = null;
        try
        {
            var driverNames = AsioOut.GetDriverNames();
            if (driverNames.Length == 0)
            {
                LastError = "ASIOドライバが見つかりません";
                return false;
            }

            int idx = Math.Clamp(SettingsPanel.AsioDriverIndex, 0, driverNames.Length - 1);
            string driverName = driverNames[idx];

            int rate = SettingsPanel.PreferredSampleRate;
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { ReadFully = true };
            output = new AsioOut(driverName);
            IWaveProvider provider = new SampleToWaveProvider(mixer);
            output.Init(provider);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Play();

            _mixer = mixer;
            _output = output;
            SampleRate = rate;
            // 💡 ASIOのバッファサイズはドライバ側のコントロールパネルに依存し、NAudio側からは変更できない
            OutputMode = $"ASIO {driverName} {rate}Hz (バッファはドライバ設定に依存)";
            LastError = "";
            IsReady = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            output?.Dispose();
            return false;
        }
    }

    public static void Shutdown()
    {
        try
        {
            if (_enumerator != null && _notifyClient != null)
                _enumerator.UnregisterEndpointNotificationCallback(_notifyClient);
        }
        catch { }
        StopOutputOnly();
        _activeInputs.Clear();
    }

    public static void StopAll()
    {
        lock (_reinitLock)
        {
            _mixer?.RemoveAllMixerInputs();
            _activeInputs.Clear();
        }
    }

    // 💡 keepOnReinit 引数を追加し、BGMなどの永続再生と効果音などの使い捨て再生を区別します
    internal static void AddInput(ISampleProvider input, bool keepOnReinit = false)
    {
        lock (_reinitLock)
        {
            if (!IsReady) return;
            if (keepOnReinit)
            {
                _activeInputs.Add(input);
            }
            _mixer!.AddMixerInput(input);
        }
    }

    internal static void RemoveInput(ISampleProvider input)
    {
        lock (_reinitLock)
        {
            _activeInputs.Remove(input);
            _mixer?.RemoveMixerInput(input);
        }
    }

    /// <summary>
    /// 既定再生デバイスの追加/削除/切り替えを検知し、自動的にオーディオエンジンを再初期化する。
    /// </summary>
    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly Action _onChange;
        public DeviceNotificationClient(Action onChange) => _onChange = onChange;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChange();
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) => _onChange();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render) _onChange();
        }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    internal static WaveStream OpenReader(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".ogg") return new VorbisWaveReader(path);
        if (ext == ".wav")
        {
            try { return new WaveFileReader(path); }
            catch { return new MediaFoundationReader(path); }
        }
        return new MediaFoundationReader(path);
    }

    internal static ISampleProvider ToMixerFormat(IWaveProvider reader, float speedRatio = 1.0f)
    {
        ISampleProvider sp = reader.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);

        int targetRate = (int)Math.Round(SampleRate / Math.Max(0.1f, speedRatio));
        if (sp.WaveFormat.SampleRate != targetRate)
        {
            sp = new WdlResamplingSampleProvider(sp, targetRate);
        }
        return sp;
    }
}

/// <summary>
/// 全サンプルをメモリ常駐させた効果音。
/// </summary>
public sealed class SoundEffect
{
    private readonly float[] _data;
    public float Volume = 1f;
    public bool Loaded => _data.Length > 0;

    private SoundEffect(float[] data) => _data = data;

    public static SoundEffect Load(string path)
    {
        if (!AudioEngine.IsReady || !File.Exists(path)) return new SoundEffect(Array.Empty<float>());
        try
        {
            using var reader = AudioEngine.OpenReader(path);
            var sp = AudioEngine.ToMixerFormat(reader);
            var chunks = new List<float[]>();
            int total = 0;
            var buf = new float[16384];
            int n;
            while ((n = sp.Read(buf, 0, buf.Length)) > 0)
            {
                var chunk = new float[n];
                Array.Copy(buf, chunk, n);
                chunks.Add(chunk);
                total += n;
            }
            var data = new float[total];
            int pos = 0;
            foreach (var chunk in chunks)
            {
                Array.Copy(chunk, 0, data, pos, chunk.Length);
                pos += chunk.Length;
            }
            return new SoundEffect(data);
        }
        catch
        {
            return new SoundEffect(Array.Empty<float>());
        }
    }

    // 💡 音飛びやマルチスレッド競合を防ぐため、プールを廃止して再生の都度インスタンスを生成します。
    //    C#のGCはこうした使い捨ての小さなオブジェクトの高速回収が得意なため、パフォーマンス上の懸念はありません。
    public void Play()
    {
        if (!Loaded) return;
        var inst = new Instance(this);
        // 💡 再生終了時にミキサーから自動的に解放され、GCされるようにkeepOnReinitをfalseにして追加します。
        AudioEngine.AddInput(inst, keepOnReinit: false);
    }

    private sealed class Instance : ISampleProvider
    {
        private readonly SoundEffect _owner;
        private int _pos;
        public WaveFormat WaveFormat => AudioEngine.MixFormat;

        public Instance(SoundEffect owner)
        {
            _owner = owner;
            _pos = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var data = _owner._data;
            int n = Math.Min(count, data.Length - _pos);
            float v = _owner.Volume;
            if (v == 1f)
            {
                Array.Copy(data, _pos, buffer, offset, n);
            }
            else
            {
                for (int i = 0; i < n; i++) buffer[offset + i] = data[_pos + i] * v;
            }
            _pos += n;

            return n;
        }
    }
}

/// <summary>
/// ストリーミング再生の楽曲。
/// PlaybackSpeed プロパティにより、速度・ピッチ変更（0.5倍〜5.0倍等）にリアルタイム対応します。
/// </summary>
public sealed class MusicTrack : ISampleProvider, IDisposable
{
    private const int DECODE_CHUNK = 8192;

    private readonly string _filePath;
    private readonly object _ringLock = new();
    private readonly float[] _ring;
    private int _ringRead, _ringWrite, _ringCount;

    private WaveStream? _reader;
    private ISampleProvider? _source;
    private Thread? _decodeThread;
    private volatile bool _decodeRun;
    private volatile bool _decodeEnded;

    private volatile bool _playing;
    private volatile bool _ended;
    private long _samplesPlayed;
    private long _snapshotTicks;

    private float _speed = 1.0f;

    public float Volume = 1f;
    public bool Looping;
    public bool Loaded { get; private set; }
    public bool Ended => _ended;
    public bool IsPlaying => _playing && !_ended;

    /// <summary>
    /// 演奏速度/ピッチ (0.5〜5.0等)。設定時にリサンプラーを再構築して即座に適用します。
    /// </summary>
    public float PlaybackSpeed
    {
        get => _speed;
        set => SetSpeed(value);
    }

    public WaveFormat WaveFormat => AudioEngine.MixFormat;

    private MusicTrack(string path)
    {
        _filePath = path;
        _ring = new float[AudioEngine.SampleRate * 2]; // 1秒分バッファ
        ReopenSource(0, 1.0f);
    }

    public static MusicTrack Load(string path) => new(path);

    public void SetSpeed(float speed)
    {
        if (Math.Abs(_speed - speed) < 0.001f || speed <= 0f) return;
        _speed = speed;

        if (!Loaded) return;

        double currentSec = PositionSeconds;
        bool wasPlaying = _playing;

        Pause();
        StopDecodeThread();

        ReopenSource(currentSec, _speed);

        if (wasPlaying)
        {
            Play(currentSec);
        }
    }

    private void ReopenSource(double startSec, float speed)
    {
        if (!AudioEngine.IsReady || !File.Exists(_filePath)) return;

        try
        {
            _reader?.Dispose();
            _reader = AudioEngine.OpenReader(_filePath);
            _source = AudioEngine.ToMixerFormat(_reader, speed);
            Loaded = true;

            if (startSec > 0 && _reader != null && _reader.CanSeek)
            {
                try
                {
                    _reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                }
                catch { }
            }

            lock (_ringLock)
            {
                _ringRead = _ringWrite = _ringCount = 0;
            }
            _decodeEnded = false;
        }
        catch
        {
            _reader?.Dispose();
            _reader = null;
            _source = null;
            Loaded = false;
        }
    }

    public double PositionSeconds
    {
        get
        {
            double pos = Interlocked.Read(ref _samplesPlayed) / (double)(AudioEngine.SampleRate * 2);
            if (IsPlaying)
            {
                double elapsed = (Stopwatch.GetTimestamp() - Volatile.Read(ref _snapshotTicks))
                                 / (double)Stopwatch.Frequency;
                pos += Math.Clamp(elapsed, 0.0, 0.05);
            }
            return pos;
        }
    }

    public void Play(double startSeconds = 0)
    {
        if (!Loaded) return;
        if (_ended) Stop();
        if (_playing) return;

        if (startSeconds > 0 && _reader != null && _reader.CanSeek)
        {
            StopDecodeThread();
            lock (_ringLock) { _ringRead = _ringWrite = _ringCount = 0; }
            _decodeEnded = false;

            try
            {
                _reader.CurrentTime = TimeSpan.FromSeconds(startSeconds);
                Interlocked.Exchange(ref _samplesPlayed, (long)(startSeconds * AudioEngine.SampleRate * 2));
            }
            catch { }
        }

        if (!_decodeEnded && (_decodeThread == null || !_decodeThread.IsAlive)) StartDecodeThread();

        // 💡 デコードスレッドが1回も書き込まないうちにミキサーへ登録すると、
        //    特にASIOの極小バッファ(数ms)では最初のコールバックが無音を読んでプチノイズになる。
        //    リングバッファに最低限のデータが溜まる(またはタイムアウトする)まで待ってから登録する。
        int prefillSamples = System.Math.Min(_ring.Length / 2, AudioEngine.SampleRate / 10 * 2); // 最大100ms分
        var prefillTimeout = Stopwatch.StartNew();
        while (!_decodeEnded && prefillTimeout.ElapsedMilliseconds < 200)
        {
            int count;
            lock (_ringLock) count = _ringCount;
            if (count >= prefillSamples) break;
            Thread.Sleep(1);
        }

        Volatile.Write(ref _snapshotTicks, Stopwatch.GetTimestamp());
        _playing = true;
        // 💡 楽曲（BGM）はReinit時にも引き継ぐ必要があるため、keepOnReinitをtrueにして追加します。
        AudioEngine.AddInput(this, keepOnReinit: true);
    }

    public void Pause()
    {
        _playing = false;
        AudioEngine.RemoveInput(this);
    }

    public void Stop()
    {
        Pause();
        StopDecodeThread();
        lock (_ringLock) { _ringRead = _ringWrite = _ringCount = 0; }
        Interlocked.Exchange(ref _samplesPlayed, 0);
        _decodeEnded = false;
        _ended = false;
        if (_reader != null)
        {
            try { _reader.Position = 0; } catch { }
        }
    }

    public void Dispose()
    {
        Pause();
        StopDecodeThread();
        _reader?.Dispose();
        _reader = null;
        _source = null;
    }

    int ISampleProvider.Read(float[] buffer, int offset, int count)
    {
        int n;
        lock (_ringLock)
        {
            n = Math.Min(count, _ringCount);
            int first = Math.Min(n, _ring.Length - _ringRead);
            Array.Copy(_ring, _ringRead, buffer, offset, first);
            if (n > first) Array.Copy(_ring, 0, buffer, offset + first, n - first);
            _ringRead = (_ringRead + n) % _ring.Length;
            _ringCount -= n;
        }

        float v = Volume;
        if (v != 1f)
        {
            for (int i = 0; i < n; i++) buffer[offset + i] *= v;
        }

        if (n > 0)
        {
            Interlocked.Add(ref _samplesPlayed, n);
            Volatile.Write(ref _snapshotTicks, Stopwatch.GetTimestamp());
        }

        if (n < count)
        {
            if (_decodeEnded)
            {
                _ended = true;
                _playing = false;
                return n;
            }
            Array.Clear(buffer, offset + n, count - n);
            n = count;
        }
        return n;
    }

    private void StartDecodeThread()
    {
        _decodeRun = true;
        _decodeThread = new Thread(DecodeLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _decodeThread.Start();
    }

    private void StopDecodeThread()
    {
        _decodeRun = false;
        _decodeThread?.Join(500);
        _decodeThread = null;
    }

    private void DecodeLoop()
    {
        var buf = new float[DECODE_CHUNK];
        while (_decodeRun)
        {
            bool full;
            lock (_ringLock) full = _ring.Length - _ringCount < DECODE_CHUNK;
            if (full)
            {
                Thread.Sleep(1);
                continue;
            }

            int n;
            try { n = _source!.Read(buf, 0, DECODE_CHUNK); }
            catch { n = 0; }

            if (n <= 0)
            {
                if (Looping && _reader != null && _reader.CanSeek)
                {
                    try { _reader.Position = 0; continue; } catch { }
                }
                _decodeEnded = true;
                return;
            }

            lock (_ringLock)
            {
                int first = Math.Min(n, _ring.Length - _ringWrite);
                Array.Copy(buf, 0, _ring, _ringWrite, first);
                if (n > first) Array.Copy(buf, first, _ring, 0, n - first);
                _ringWrite = (_ringWrite + n) % _ring.Length;
                _ringCount += n;
            }
        }
    }
}