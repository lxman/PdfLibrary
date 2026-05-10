using System.IO;

namespace Jbig2Decoder.Mq
{
    /// <summary>
    /// Side-by-side debug trace for the MQ decoder. When opened, every
    /// MqDecoder.Decode call appends a fixed-format line capturing the
    /// pre-decode context byte, the decoded bit, and the post-decode A/C/CT
    /// state. The format mirrors a matching trace patched into jbig2dec so
    /// the two streams diff line-for-line; the first divergence localises any
    /// MQ-state desync to a specific decode index.
    ///
    /// Disabled (no overhead) unless explicitly opened via <see cref="Open"/>.
    /// </summary>
    internal static class MqTrace
    {
        private static StreamWriter? _writer;
        private static long _counter;
        private static readonly object Sync = new object();

        public static bool Enabled => _writer != null;

        public static void Open(string path)
        {
            lock (Sync)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = new StreamWriter(path) { AutoFlush = false };
                _counter = 0;
            }
        }

        public static void Close()
        {
            lock (Sync)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }

        // C is intentionally omitted — our impl uses the JPEG2000-style "LPS-low"
        // C convention while jbig2dec uses Figure F.1's complement-init form, so
        // the raw C registers diverge by construction even when both decoders are
        // correct. The decoded bit, A, and CT are spec-identical though, which is
        // what we diff against.
        public static void Log(byte preCx, int bit, uint a, int ct)
        {
            if (_writer == null) return;
            _writer.WriteLine($"{_counter:D6} pre={preCx:X2} bit={bit} A={a:X4} CT={ct}");
            _counter++;
        }

        /// <summary>
        /// Records a decoder-entry marker so the bit stream can be split into
        /// per-decoder runs. <paramref name="tag"/> is a short stable label
        /// (e.g. "IADH", "IAFS", "IAID") emitted by both decoders so the diff
        /// pinpoints which decoder is out of step.
        /// </summary>
        public static void LogEnter(string tag)
        {
            if (_writer == null) return;
            _writer.WriteLine($"{_counter:D6} ENTER {tag}");
        }
    }
}
