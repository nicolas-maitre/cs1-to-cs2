using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Cs1Exporter
{
    /// <summary>
    /// Streaming zip writer for .NET 3.5 / Mono 2, which has no ZipArchive (PKWARE APPNOTE 4.3, no zip64, UTF-8 names).
    ///
    /// An entry is opened with BeginEntry, written through the returned stream (CRC-32 and size are counted on the
    /// way) and deflated straight into the output file, so a big city never needs the whole JSON in memory.
    /// EndEntry seeks back and patches CRC and sizes into the local header (the output must be seekable), so no data
    /// descriptors are needed and every reader accepts the result. EndEntry(keep: false) truncates the file back to
    /// where the entry began: a section that fails half-way leaves no trace in the archive.
    ///
    /// Deflate uses System.IO.Compression.DeflateStream (raw deflate = zip method 8). It needs Mono's native
    /// MonoPosixHelper; it is probed once, and if it doesn't work every entry is stored instead.
    /// </summary>
    public sealed class ZipWriter : IDisposable
    {
        private readonly Stream _out;
        private readonly List<Entry> _entries = new List<Entry>();
        private Entry _open;
        private EntryStream _openStream;
        private bool _finished;

        public bool UseDeflate { get; private set; }
        public string DeflateError { get; private set; }
        public int EntryCount { get { return _entries.Count; } }

        private sealed class Entry
        {
            public byte[] Name;
            public ushort Method;
            public uint Crc, CompressedSize, Size, Offset;
            public ushort DosTime, DosDate;
        }

        public ZipWriter(Stream output, bool deflate = true)
        {
            if (!output.CanSeek) throw new ArgumentException("the zip output must be seekable");
            _out = output;
            UseDeflate = deflate && ProbeDeflate();
        }

        private bool ProbeDeflate()
        {
            try
            {
                var probe = Encoding.ASCII.GetBytes("{\"probe\":\"cs1x deflate probe cs1x deflate probe cs1x\"}");
                using (var ms = new MemoryStream())
                {
                    using (var d = new DeflateStream(ms, CompressionMode.Compress, true)) d.Write(probe, 0, probe.Length);
                    if (ms.Length == 0) throw new InvalidOperationException("DeflateStream wrote nothing");
                }
                return true;
            }
            catch (Exception e)
            {
                DeflateError = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>Starts an entry; write its content to the returned stream, then call EndEntry.</summary>
        public Stream BeginEntry(string name)
        {
            if (_finished) throw new InvalidOperationException("zip already finished");
            if (_open != null) throw new InvalidOperationException("previous entry still open");
            var e = new Entry { Name = Encoding.UTF8.GetBytes(name), Method = (ushort)(UseDeflate ? 8 : 0), Offset = (uint)_out.Position };
            DosDateTime(DateTime.Now, out e.DosTime, out e.DosDate);
            WriteLocalHeader(e);
            _open = e;
            _openStream = new EntryStream(_out, UseDeflate);
            return _openStream;
        }

        /// <summary>Finishes the open entry. keep = false drops it from the file entirely.</summary>
        public void EndEntry(bool keep = true)
        {
            var e = _open;
            if (e == null) return;
            _open = null;
            var s = _openStream;
            _openStream = null;
            long dataStart = e.Offset + 30 + e.Name.Length;
            try { s.Finish(); }
            catch { if (keep) throw; }
            if (!keep)
            {
                _out.SetLength(e.Offset);
                _out.Position = e.Offset;
                return;
            }
            if (s.Count == 0 && e.Method != 0)
            {
                // Some DeflateStreams write no block at all for empty input, which strict inflaters reject: store it.
                _out.SetLength(dataStart);
                _out.Position = e.Offset + 8;
                e.Method = 0;
                new BinaryWriter(_out).Write(e.Method);
                _out.Position = dataStart;
            }
            long end = _out.Position;
            e.Crc = s.Crc;
            e.Size = (uint)s.Count;
            e.CompressedSize = (uint)(end - dataStart);
            _out.Position = e.Offset + 14;
            var w = new BinaryWriter(_out);
            w.Write(e.Crc);
            w.Write(e.CompressedSize);
            w.Write(e.Size);
            w.Flush();
            _out.Position = end;
            _entries.Add(e);
        }

        public void Add(string name, byte[] data)
        {
            var s = BeginEntry(name);
            s.Write(data, 0, data.Length);
            EndEntry(true);
        }

        public void Add(string name, string utf8Text) { Add(name, new UTF8Encoding(false).GetBytes(utf8Text)); }

        private void WriteLocalHeader(Entry e)
        {
            var w = new BinaryWriter(_out);
            w.Write(0x04034b50u);          // local file header signature
            w.Write((ushort)20);           // version needed to extract (2.0: deflate)
            w.Write((ushort)0x0800);       // flags: bit 11 = UTF-8 names
            w.Write(e.Method);
            w.Write(e.DosTime);
            w.Write(e.DosDate);
            w.Write(0u);                   // CRC-32, patched by EndEntry
            w.Write(0u);                   // compressed size, patched
            w.Write(0u);                   // uncompressed size, patched
            w.Write((ushort)e.Name.Length);
            w.Write((ushort)0);            // extra field length
            w.Write(e.Name);
            w.Flush();
        }

        public void Finish()
        {
            if (_finished) return;
            if (_open != null) EndEntry(false);
            _finished = true;
            var w = new BinaryWriter(_out);
            long cdStart = _out.Position;
            foreach (var e in _entries)
            {
                w.Write(0x02014b50u);      // central directory header signature
                w.Write((ushort)20);       // version made by
                w.Write((ushort)20);       // version needed to extract
                w.Write((ushort)0x0800);
                w.Write(e.Method);
                w.Write(e.DosTime);
                w.Write(e.DosDate);
                w.Write(e.Crc);
                w.Write(e.CompressedSize);
                w.Write(e.Size);
                w.Write((ushort)e.Name.Length);
                w.Write((ushort)0);        // extra field length
                w.Write((ushort)0);        // comment length
                w.Write((ushort)0);        // disk number start
                w.Write((ushort)0);        // internal attributes
                w.Write(0u);               // external attributes
                w.Write(e.Offset);
                w.Write(e.Name);
            }
            long cdEnd = _out.Position;
            w.Write(0x06054b50u);          // end of central directory signature
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)_entries.Count);
            w.Write((ushort)_entries.Count);
            w.Write((uint)(cdEnd - cdStart));
            w.Write((uint)cdStart);
            w.Write((ushort)0);            // comment length
            w.Flush();
        }

        public void Dispose() { Finish(); }

        private static void DosDateTime(DateTime t, out ushort time, out ushort date)
        {
            if (t.Year < 1980) t = new DateTime(1980, 1, 1);
            time = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
            date = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
        }

        /// <summary>Counts CRC-32 and size of what is written, and forwards it (deflated or as is) to the zip file.</summary>
        private sealed class EntryStream : Stream
        {
            private readonly Stream _file;
            private readonly DeflateStream _deflate;
            private uint _crc = 0xFFFFFFFFu;
            private bool _done;
            public long Count;
            public uint Crc { get { return _crc ^ 0xFFFFFFFFu; } }

            public EntryStream(Stream file, bool deflate)
            {
                _file = file;
                if (deflate) _deflate = new DeflateStream(file, CompressionMode.Compress, true);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_done) throw new ObjectDisposedException("zip entry");
                _crc = Crc32.Update(_crc, buffer, offset, count);
                Count += count;
                // Bounded chunks: Mono 2's DeflateStream wraps native zlib and prefers moderate writes.
                const int chunk = 64 * 1024;
                for (int i = 0; i < count; i += chunk)
                {
                    int n = Math.Min(chunk, count - i);
                    if (_deflate != null) _deflate.Write(buffer, offset + i, n); else _file.Write(buffer, offset + i, n);
                }
            }

            /// <summary>Flushes the deflater (end of stream) without closing the zip file.</summary>
            public void Finish()
            {
                if (_done) return;
                _done = true;
                if (_deflate != null) _deflate.Close();
            }

            public override void Flush() { }
            protected override void Dispose(bool disposing) { /* EndEntry finishes the entry */ }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return !_done; } }
            public override long Length { get { return Count; } }
            public override long Position { get { return Count; } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }
    }

    /// <summary>CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320), as zip uses.</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = MakeTable();

        private static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        /// <summary>Running update; start with 0xFFFFFFFF and xor the result with 0xFFFFFFFF at the end.</summary>
        public static uint Update(uint c, byte[] data, int offset, int count)
        {
            for (int i = offset, end = offset + count; i < end; i++) c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c;
        }

        public static uint Compute(byte[] data, int offset, int count)
        {
            return Update(0xFFFFFFFFu, data, offset, count) ^ 0xFFFFFFFFu;
        }
    }
}
