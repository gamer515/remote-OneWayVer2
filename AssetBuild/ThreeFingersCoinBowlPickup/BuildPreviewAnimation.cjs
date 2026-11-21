const fs = require('node:fs');
const path = require('node:path');

const output = path.resolve(__dirname, '../../output/ThreeFingersCoinBowlPickup');
const crcTable = Array.from({ length: 256 }, (_, value) => {
  let checksum = value;
  for (let bit = 0; bit < 8; bit++) {
    checksum = checksum & 1 ? 0xedb88320 ^ (checksum >>> 1) : checksum >>> 1;
  }
  return checksum >>> 0;
});

function chunk(type, payload) {
  const content = Buffer.concat([Buffer.from(type), payload]);
  let checksum = 0xffffffff;
  for (const value of content) checksum = crcTable[(checksum ^ value) & 255] ^ (checksum >>> 8);
  const length = Buffer.alloc(4);
  length.writeUInt32BE(payload.length);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE((checksum ^ 0xffffffff) >>> 0);
  return Buffer.concat([length, content, crc]);
}

function readPng(file) {
  const bytes = fs.readFileSync(file);
  const chunks = [];
  for (let offset = 8; offset < bytes.length;) {
    const length = bytes.readUInt32BE(offset);
    chunks.push({
      type: bytes.toString('ascii', offset + 4, offset + 8),
      payload: bytes.subarray(offset + 8, offset + 8 + length),
    });
    offset += length + 12;
  }
  return chunks;
}

function extractComparisonFrames(view) {
  const folder = path.join(output, 'approach-before');
  const source = path.join(folder, `pickup-${view}.png`);
  if (!fs.existsSync(source)) return;
  const parts = readPng(source);
  const header = parts.find(part => part.type === 'IHDR').payload;
  const destination = path.join(folder, 'frames');
  fs.mkdirSync(destination, { recursive: true });
  let frame = -1;
  let data = [];
  const save = () => {
    if (frame < 0 || data.length === 0) return;
    const name = `${view}_${String(frame * 2).padStart(2, '0')}.png`;
    fs.writeFileSync(path.join(destination, name), Buffer.concat([
      Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
      chunk('IHDR', header), ...data, chunk('IEND', Buffer.alloc(0)),
    ]));
  };
  for (const part of parts) {
    if (part.type === 'fcTL') {
      save();
      frame++;
      data = [];
    } else if (part.type === 'IDAT') {
      data.push(chunk('IDAT', part.payload));
    } else if (part.type === 'fdAT') {
      data.push(chunk('IDAT', part.payload.subarray(4)));
    }
  }
  save();
}

for (const view of ['front', 'side']) {
  extractComparisonFrames(view);
  const frames = Array.from({ length: 31 }, (_, index) =>
    readPng(path.join(output, 'runtime', `${view}_${String(index * 2).padStart(2, '0')}.png`)));
  const header = frames[0].find(part => part.type === 'IHDR').payload;
  const animation = Buffer.alloc(8);
  animation.writeUInt32BE(frames.length, 0);
  animation.writeUInt32BE(1, 4); // Play once, then hold the lifted pose.
  const result = [Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('acTL', animation)];
  let sequence = 0;
  for (let index = 0; index < frames.length; index++) {
    const control = Buffer.alloc(26);
    control.writeUInt32BE(sequence++, 0);
    control.writeUInt32BE(header.readUInt32BE(0), 4);
    control.writeUInt32BE(header.readUInt32BE(4), 8);
    control.writeUInt16BE(1, 20);
    control.writeUInt16BE(index === frames.length - 1 ? 2 : 30, 22);
    result.push(chunk('fcTL', control));
    for (const data of frames[index].filter(part => part.type === 'IDAT')) {
      if (index === 0) {
        result.push(chunk('IDAT', data.payload));
      } else {
        const frameSequence = Buffer.alloc(4);
        frameSequence.writeUInt32BE(sequence++);
        result.push(chunk('fdAT', Buffer.concat([frameSequence, data.payload])));
      }
    }
  }
  result.push(chunk('IEND', Buffer.alloc(0)));
  fs.writeFileSync(path.join(output, `pickup-${view}.png`), Buffer.concat(result));
}
process.stdout.write('Created front and side APNG motion previews.\n');
