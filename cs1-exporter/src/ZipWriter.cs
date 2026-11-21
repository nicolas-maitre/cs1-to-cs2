using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Cs1Exporter
{
    /// <summary>
    /// Minimal zip writer for .NET 3.5 / Mono 2, which has no ZipArchive.
    /// Writes local file headers, the (deflated or stored) data, then the central directory and the
    /// end-of-central-directory record (PKWARE APPNOTE 4.3). No zip64, so each entry and the whole file must
    /// stay under 4 GiB, which is far beyond a city export. Entry names are UTF-8 (general purpose bit 11).
    ///
    /// Each entry is given as a complete byte array: the CRC32 and both sizes are known before the local header
    /// is written, so no data descriptors are needed and every reader accepts the result.
    /// Deflate uses System.IO.Compression.DeflateStream (raw deflate, which is what zip method 8 expects). If it
    /// throws (Mono's DeflateStream needs the native MonoPosixHelper), the entry is stored instead.
    /// </summary>
    public sealed class ZipWriter : IDisposable
    {
        private readonly Stream _out;
        private readonly List<Entry> _entries = new List<Entry>();
        private bool _finished;

        /// <summary>Set when DeflateStream failed once; later entries are stored right away.</summary>
        public bool DeflateUnavailable { get; private set; }
        public string DeflateError { get; private set; }

        private sealed class Entry
        {
            public byte[] Name;
            public ushort Method;
            public uint Crc;
            public uint CompressedSize;
            public uint Size;
            public uint Offset;
            public ushort DosTime;
            public ushort DosDate;
        }

        public ZipWriter(Stream output) { _out = output; }

        public void Add(string name, byte[] data)
        {
            if (_finished) throw new InvalidOperationException("zip already finished");
            var e = new Entry
            {
                Name = Encoding.UTF8.GetBytes(name),
                Crc = Crc32.Compute(data, 0, data.Length),
                Size = (uint)data.Length,
                Offset = (uint)_out.Position,
            };
            DosDateTime(DateTime.Now, out e.DosTime, out e.DosDate);

            byte[] payload = data;
            e.Method = 0;
            if (!DeflateUnavailable && data.Length > 64)
            {
                try
                {
                    var compressed = Deflate(data);
                    if (compressed.Length < data.Length) { payload = compressed; e.Method = 8; }
                }
                catch (Exception ex)
                {
                    DeflateUnavailable = true;
                    DeflateError = ex.GetType().Name + ": " + ex.Message;
                }
            }
            e.CompressedSize = (uint)payload.Length;

            var w = new BinaryWriter(_out);
            w.Write(0x04034b50u);          // local file header signature
            w.Write((ushort)20);           // version needed to extract (2.0: deflate)
            w.Write((ushort)0x0800);       // flags: bit 11 = UTF-8 names
            w.Write(e.Method);
            w.Write(e.DosTime);
            w.Write(e.DosDate);
            w.Write(e.Crc);
            w.Write(e.CompressedSize);
            w.Write(e.Size);
            w.Write((ushort)e.Name.Length);
            w.Write((ushort)0);            // extra field length
            w.Write(e.Name);
            w.Write(payload);
            w.Flush();
            _entries.Add(e);
        }

        public void Add(string name, string utf8Text) { Add(name, new UTF8Encoding(false).GetBytes(utf8Text)); }

        public void Finish()
        {
            if (_finished) return;
            _finished = true;
            var w = new BinaryWriter(_out);
            long cdStart = _out.Position;
            foreach (var e in _entries)
            {
                w.Write(0x02014b50u);      // central directory header signature
                w.Write((ushort)20);       // version made by (2.0, MS-DOS attribute compatibility)
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
            w.Write((ushort)0);            // this disk
            w.Write((ushort)0);            // disk with the central directory
            w.Write((ushort)_entries.Count);
            w.Write((ushort)_entries.Count);
            w.Write((uint)(cdEnd - cdStart));
            w.Write((uint)cdStart);
            w.Write((ushort)0);            // comment length
            w.Flush();
        }

        public void Dispose() { Finish(); }

        private static byte[] Deflate(byte[] data)
        {
            using (var ms = new MemoryStream(data.Length / 3 + 64))
            {
                // Write in moderate chunks: Mono 2's DeflateStream wraps native zlib and is happier with
                // bounded buffers than with one huge write.
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
                {
                    const int chunk = 64 * 1024;
                    for (int i = 0; i < data.Length; i += chunk)
                        ds.Write(data, i, Math.Min(chunk, data.Length - i));
                }
                return ms.ToArray();
            }
        }

        private static void DosDateTime(DateTime t, out ushort time, out ushort date)
        {
            if (t.Year < 1980) t = new DateTime(1980, 1, 1);
            time = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
            date = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
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

        public static uint Compute(byte[] data, int offset, int count)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = offset, end = offset + count; i < end; i++) c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
