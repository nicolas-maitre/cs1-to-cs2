// 16-bit greyscale PNG writer and reader (node:zlib only). Enough for heightmaps; the reader only accepts what the
// writer produces (colour type 0, bit depth 16, no interlace) plus all five filter types.
import { deflateSync, inflateSync } from 'node:zlib';
import { crc32 } from './zip.mjs';

const SIG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}

/**
 * values: Uint16Array of width*height, rows top to bottom as the image is viewed (PNG row 0 = top).
 * Returns the PNG file as a Buffer.
 */
export function encodeGray16(width, height, values) {
  if (values.length !== width * height) throw new Error('values length mismatch');
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 16; ihdr[9] = 0; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const stride = width * 2 + 1;
  const raw = Buffer.alloc(stride * height);
  for (let y = 0; y < height; y++) {
    const o = y * stride;
    raw[o] = 0; // filter: none
    for (let x = 0; x < width; x++) raw.writeUInt16BE(values[y * width + x], o + 1 + x * 2);
  }
  return Buffer.concat([SIG, chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw, { level: 6 })), chunk('IEND', Buffer.alloc(0))]);
}

/** Returns {width, height, values: Uint16Array (PNG row 0 first)}. */
export function decodeGray16(buf) {
  if (!buf.subarray(0, 8).equals(SIG)) throw new Error('not a PNG');
  let p = 8, width = 0, height = 0;
  const idat = [];
  while (p < buf.length) {
    const len = buf.readUInt32BE(p);
    const type = buf.toString('ascii', p + 4, p + 8);
    const data = buf.subarray(p + 8, p + 8 + len);
    if (type === 'IHDR') {
      width = data.readUInt32BE(0); height = data.readUInt32BE(4);
      if (data[8] !== 16 || data[9] !== 0 || data[12] !== 0) throw new Error('only 16-bit greyscale, non-interlaced PNGs are supported');
    } else if (type === 'IDAT') idat.push(data);
    else if (type === 'IEND') break;
    p += 12 + len;
  }
  const raw = inflateSync(Buffer.concat(idat));
  const bpp = 2, stride = width * 2;
  const out = new Uint16Array(width * height);
  let prev = Buffer.alloc(stride);
  for (let y = 0; y < height; y++) {
    const f = raw[y * (stride + 1)];
    const line = Buffer.from(raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)));
    for (let i = 0; i < stride; i++) {
      const a = i >= bpp ? line[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
      let v;
      switch (f) {
        case 0: v = 0; break;
        case 1: v = a; break;
        case 2: v = b; break;
        case 3: v = (a + b) >> 1; break;
        case 4: { const pp = a + b - c, pa = Math.abs(pp - a), pb = Math.abs(pp - b), pc = Math.abs(pp - c); v = pa <= pb && pa <= pc ? a : pb <= pc ? b : c; break; }
        default: throw new Error('bad PNG filter ' + f);
      }
      line[i] = (line[i] + v) & 0xff;
    }
    for (let x = 0; x < width; x++) out[y * width + x] = line.readUInt16BE(x * 2);
    prev = line;
  }
  return { width, height, values: out };
}
