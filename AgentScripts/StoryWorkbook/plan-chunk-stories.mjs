import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';

export function planChunkStories(script, seed, npcSettings = null) {
    const beats = new Map(script.main_story.beats.map(beat => [beat.id, beat]));
    const sequence = script.main_story.sequence;
    if (beats.size !== script.main_story.beats.length ||
        new Set(sequence).size !== beats.size ||
        sequence.length !== beats.size ||
        sequence.some(id => !beats.has(id))) {
        throw new Error('The main sequence must contain every main event exactly once.');
    }

    const npcIds = new Map(npcSettings?.npcs.map(npc => [npc.name, npc.id]) ?? []);
    const deliveries = new Map(script.pet_network.map(pet => [pet.from_main, pet]));
    const entry = (kind, id, npc) => {
        if (npcSettings && !npcIds.has(npc)) {
            throw new Error(`Unknown NPC for ${id}: ${npc}`);
        }
        return { kind, id, npc, npcId: npcIds.get(npc) ?? null };
    };

    const choose = (key, size) => createHash('sha256')
        .update(JSON.stringify([seed, key])).digest().readUInt32LE(0) % size;
    const windows = new Map(script.side_windows.map(window => [
        window.before_main, window
    ]));
    const sourceChunks = new Map(script.chunks.map(chunk => [chunk.id, chunk]));
    const usedSideNpcs = new Set();
    const chunks = [];

    for (let offset = 0; offset < sequence.length;) {
        const index = chunks.length + 1;
        const source = beats.get(sequence[offset]).chunk_id;
        const main = [];
        const count = 1 + choose(`chunk:${index}:main-count`, 2);
        while (main.length < count && beats.get(sequence[offset])?.chunk_id === source) {
            main.push(beats.get(sequence[offset++]));
        }

        const slots = main.map(beat => ({
            ...entry('main', beat.id, beat.primary_npc),
            prerequisiteFlags: beat.prerequisite_flags,
            completionFlag: beat.id + '_DONE',
            petMessageId: deliveries.get(beat.id)?.id ?? null
        }));
        const chunkNpcs = new Set(main.map(beat => beat.primary_npc));
        for (let slot = main.length; slot < 4; slot++) {
            const positions = [];
            for (let position = 0; position <= slots.length; position++) {
                const nextMain = slots.slice(position).find(item => item.kind === 'main');
                const nextId = nextMain?.id ?? sequence[offset];
                const window = windows.get(nextId);
                // Before the opening main, use optional personal stories from its story region.
                const pool = nextId === sequence[0] ? sourceChunks.get(source).npc_pool :
                    window?.chunk_id === source ? window.npc_pool : [];
                const candidates = pool.filter(npc => !chunkNpcs.has(npc) && !usedSideNpcs.has(npc) &&
                    script.personal_dialogue_catalog[npc]?.chunk_id === source &&
                    script.personal_dialogue_catalog[npc].variants.length > 0);
                if (candidates.length) positions.push({ position, candidates, window });
            }
            if (!positions.length) break;
            const key = `chunk:${index}:side:${slot}`;
            const { position, candidates, window } = positions[choose(key, positions.length)];
            const npc = candidates[choose(key + ':npc', candidates.length)];
            const variants = script.personal_dialogue_catalog[npc].variants;
            const variant = variants[choose('variant:' + npc, variants.length)];
            slots.splice(position, 0, { ...entry('side', variant.id, npc), windowId: window?.id ?? null });
            usedSideNpcs.add(npc);
        }
        const lastMain = main.at(-1);
        const petFlags = [...new Set(deliveries.get(lastMain.id)?.variants.map(variant => variant.sets_flag) ?? [])];
        if (petFlags.length !== 1 || !petFlags[0]) {
            throw new Error(`Missing or inconsistent pet delivery flag after ${lastMain.id}.`);
        }
        const requiredMainIds = main.map(beat => beat.id);
        chunks.push({
            index, storyRegion: source, requiredMainIds, slots,
            exitCondition: { allFlags: requiredMainIds.map(id => id + '_DONE'), petMustArrive: petFlags[0] }
        });
    }

    return { seed, chunks, bossUnlock: script.boss_chunk.unlock };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    const [, , source, seed, settings] = process.argv;
    if (!source || seed === undefined) {
        throw new Error('Usage: node plan-chunk-stories.mjs <chapter narrative JSON> <run seed> [NPC settings JSON]');
    }
    const readJson = path => JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
    console.log(JSON.stringify(planChunkStories(readJson(source), seed, settings ? readJson(settings) : null), null, 2));
}
