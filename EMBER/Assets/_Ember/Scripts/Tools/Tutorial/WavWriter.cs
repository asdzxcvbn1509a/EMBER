using System;
using System.IO;
using System.Text;
using Unity.Collections;

namespace Ember
{
    /// <summary>Streams 32-bit float PCM (WAVE_FORMAT_IEEE_FLOAT) to disk; the chunk sizes are patched on Dispose.</summary>
    public sealed class WavWriter : IDisposable
    {
        readonly FileStream fs;
        readonly BinaryWriter w;
        float[] floats = new float[0];
        byte[] bytes = new byte[0];

        public int Rate { get; }
        public int Channels { get; }
        public long SampleFrames { get; private set; }

        public WavWriter(string path, int rate, int channels)
        {
            Rate = rate;
            Channels = channels;
            fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            w = new BinaryWriter(fs);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(0);                                  // patched
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)3);                           // IEEE float
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 4);
            w.Write((short)(channels * 4));
            w.Write((short)32);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(0);                                  // patched
        }

        /// <summary>Appends interleaved samples (count = sample frames x channels).</summary>
        public void Write(NativeArray<float> data, int count)
        {
            if (count <= 0) return;
            if (floats.Length < count) floats = new float[count];
            if (bytes.Length < count * 4) bytes = new byte[count * 4];
            NativeArray<float>.Copy(data, floats, count);
            Buffer.BlockCopy(floats, 0, bytes, 0, count * 4);
            w.Write(bytes, 0, count * 4);
            SampleFrames += count / Channels;
        }

        /// <summary>Appends silence (used when a frame produced no audio, to keep sound and picture aligned).</summary>
        public void WriteSilence(int sampleFrames)
        {
            int count = sampleFrames * Channels;
            if (count <= 0) return;
            if (bytes.Length < count * 4) bytes = new byte[count * 4];
            Array.Clear(bytes, 0, count * 4);
            w.Write(bytes, 0, count * 4);
            SampleFrames += sampleFrames;
        }

        public void Dispose()
        {
            long data = SampleFrames * Channels * 4;
            w.Flush();
            fs.Seek(4, SeekOrigin.Begin);
            w.Write((int)Math.Min(int.MaxValue, 36 + data));
            fs.Seek(40, SeekOrigin.Begin);
            w.Write((int)Math.Min(int.MaxValue, data));
            w.Flush();
            w.Dispose();
            fs.Dispose();
        }
    }
}
