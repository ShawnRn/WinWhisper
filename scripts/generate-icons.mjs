// Regenerate committed platform assets. Requires Node and sharp (0.34.x or later).
// WINWHISPER_SHARP_MODULE may point to an existing sharp package installation.
import { createRequire } from 'node:module';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const sharp = require(process.env.WINWHISPER_SHARP_MODULE || 'sharp');
const directory = new URL('../src/MemoAI.App/Assets/Brand/', import.meta.url);
const source = await readFile(new URL('WinWhisper.svg', directory));
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const images = await Promise.all(sizes.map(size => sharp(source, { density: 384 }).resize(size, size).png().toBuffer()));
const header = Buffer.alloc(6 + 16 * sizes.length);
header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
let offset = header.length;
images.forEach((image, i) => {
  const entry = 6 + i * 16, size = sizes[i];
  header[entry] = size === 256 ? 0 : size; header[entry + 1] = header[entry];
  header.writeUInt16LE(1, entry + 4); header.writeUInt16LE(32, entry + 6);
  header.writeUInt32LE(image.length, entry + 8); header.writeUInt32LE(offset, entry + 12);
  offset += image.length;
});
await writeFile(new URL('WinWhisper.ico', directory), Buffer.concat([header, ...images]));
await sharp(source, { density: 384 }).resize(256, 256).png().toFile(fileURLToPath(new URL('WinWhisper.png', directory)));
console.log('Generated WinWhisper PNG and multi-resolution ICO:', sizes.join(', '));
