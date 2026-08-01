using NAudio.Wave;

namespace SMAP_WPF;

/// <summary>Freeverb(Schroeder-Moorer)混响, 调成洞穴参数(长衰减+明亮反射), 对应 SkyStudio 的 Unity Cave 预设。
/// 挂在混音器输出后, Enabled 开关旁路。</summary>
public class Reverb : ISampleProvider
{
    readonly ISampleProvider _src;
    public WaveFormat WaveFormat => _src.WaveFormat;
    public volatile bool Enabled;

    // Freeverb 经典调音(@44100), 立体声右声道加 spread 错开
    static readonly int[] CombTune = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    static readonly int[] AllPassTune = { 556, 441, 341, 225 };
    const int Spread = 23;
    const float Gain = 0.015f;

    readonly Comb[] _combL = new Comb[8], _combR = new Comb[8];
    readonly AllPass[] _apL = new AllPass[4], _apR = new AllPass[4];
    readonly float _wet, _dry;

    public Reverb(ISampleProvider src, float room = 0.92f, float damp = 0.05f, float wet = 0.32f)
    {
        _src = src;
        float feedback = room * 0.28f + 0.7f;   // 洞穴: 长衰减
        float d = damp * 0.4f;
        for (int i = 0; i < 8; i++)
        {
            _combL[i] = new Comb(CombTune[i]) { Feedback = feedback, Damp = d };
            _combR[i] = new Comb(CombTune[i] + Spread) { Feedback = feedback, Damp = d };
        }
        for (int i = 0; i < 4; i++)
        {
            _apL[i] = new AllPass(AllPassTune[i]) { Feedback = 0.5f };
            _apR[i] = new AllPass(AllPassTune[i] + Spread) { Feedback = 0.5f };
        }
        _wet = wet;
        _dry = 1f;   // 干声保留, 湿声叠加
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int n = _src.Read(buffer, offset, count);
        if (!Enabled || WaveFormat.Channels != 2) return n;
        for (int i = 0; i + 1 < n; i += 2)
        {
            float inL = buffer[offset + i], inR = buffer[offset + i + 1];
            float input = (inL + inR) * Gain;
            float outL = 0, outR = 0;
            for (int k = 0; k < 8; k++) { outL += _combL[k].Process(input); outR += _combR[k].Process(input); }
            for (int k = 0; k < 4; k++) { outL = _apL[k].Process(outL); outR = _apR[k].Process(outR); }
            buffer[offset + i] = inL * _dry + outL * _wet;
            buffer[offset + i + 1] = inR * _dry + outR * _wet;
        }
        return n;
    }

    // 阻尼反馈梳状滤波器
    sealed class Comb
    {
        readonly float[] _buf;
        int _idx;
        float _store;
        public float Feedback, Damp;
        public Comb(int size) => _buf = new float[size];
        public float Process(float inp)
        {
            float o = _buf[_idx];
            _store = o * (1 - Damp) + _store * Damp;
            _buf[_idx] = inp + _store * Feedback;
            if (++_idx >= _buf.Length) _idx = 0;
            return o;
        }
    }

    // 全通滤波器(扩散)
    sealed class AllPass
    {
        readonly float[] _buf;
        int _idx;
        public float Feedback;
        public AllPass(int size) => _buf = new float[size];
        public float Process(float inp)
        {
            float o = _buf[_idx];
            float outp = -inp + o;
            _buf[_idx] = inp + o * Feedback;
            if (++_idx >= _buf.Length) _idx = 0;
            return outp;
        }
    }
}
