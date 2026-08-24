import crypto from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createHcCombatDescription } from "./hc_description_formatter.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const pluginDirectory = path.resolve(scriptDirectory, "..");
const dataDirectory = path.join(pluginDirectory, "Data");
const iconDirectory = path.join(pluginDirectory, "assets", "repoe");
const baseUrl = "https://repoe-fork.github.io/poe2/";
const harmfulCategories = new Set(["Debuff", "Hex", "Mark"]);

async function fetchText(relativeUrl) {
    const response = await fetch(new URL(relativeUrl, baseUrl));
    if (!response.ok)
        throw new Error(`${relativeUrl}: HTTP ${response.status}`);
    return await response.text();
}

function decodeHtml(value) {
    return (value ?? "")
        .replaceAll("&amp;", "&")
        .replaceAll("&quot;", '"')
        .replaceAll("&#x27;", "'")
        .replaceAll("&lt;", "<")
        .replaceAll("&gt;", ">");
}

function cleanGameMarkup(value) {
    return decodeHtml(value)
        .replace(/\[([^\]|]+)\|([^\]]+)\]/g, "$2")
        .replace(/\[([^\]]+)\]/g, "$1")
        .replace(/\s+/g, " ")
        .trim();
}

function buildVisualMaps(html) {
    const iconByDefinition = new Map();
    const iconByVisual = new Map();
    const blockPattern = /<div id="([^"]+)">([\s\S]*?)<\/div>/g;

    for (const blockMatch of html.matchAll(blockPattern)) {
        const body = blockMatch[2];
        const iconPath = decodeHtml(body.match(/<img src="([^"]+)"/)?.[1] ?? "");
        if (!iconPath)
            continue;

        for (const definitionMatch of body.matchAll(/BuffDefinitions id:\s*([^\s<]+)/gi))
            iconByDefinition.set(definitionMatch[1].trim(), iconPath);

        const visualsText = body.match(/BuffVisuals id\(s\):\s*([^<]*)/i)?.[1] ?? "";
        for (const visualId of visualsText.split(",").map(value => value.trim()).filter(Boolean))
            iconByVisual.set(visualId, iconPath);
    }

    return { iconByDefinition, iconByVisual };
}

function makeLocalIconKeys(sourcePaths) {
    const pathByBaseName = new Map();
    const keyBySourcePath = new Map();

    for (const sourcePath of sourcePaths) {
        const baseName = path.basename(sourcePath, path.extname(sourcePath));
        const collision = pathByBaseName.get(baseName);
        const suffix = collision && collision !== sourcePath
            ? "_" + crypto.createHash("sha1").update(sourcePath).digest("hex").slice(0, 8)
            : "";
        pathByBaseName.set(baseName, sourcePath);
        keyBySourcePath.set(sourcePath, `repoe/${baseName}${suffix}`);
    }

    return keyBySourcePath;
}

async function downloadIcon(sourcePath, localKey) {
    const destination = path.join(pluginDirectory, "assets", `${localKey}.png`);
    try {
        await fs.access(destination);
        return;
    } catch {
        // Download missing assets only. Existing files are stable, exact source snapshots.
    }

    const response = await fetch(new URL(sourcePath, baseUrl));
    if (!response.ok)
        throw new Error(`${sourcePath}: HTTP ${response.status}`);
    await fs.writeFile(destination, Buffer.from(await response.arrayBuffer()));
}

async function runWithConcurrency(items, worker, concurrency) {
    let nextIndex = 0;
    const workers = Array.from({ length: Math.min(concurrency, items.length) }, async () => {
        while (true) {
            const index = nextIndex++;
            if (index >= items.length)
                return;
            await worker(items[index], index);
        }
    });
    await Promise.all(workers);
}

await fs.mkdir(dataDirectory, { recursive: true });
await fs.mkdir(iconDirectory, { recursive: true });

const [buffText, visualHtml, sourceVersion] = await Promise.all([
    fetchText("buffs.json"),
    fetchText("buff_visuals.html"),
    fetchText("version.txt").catch(() => "unknown"),
]);
const buffs = JSON.parse(buffText);
const { iconByDefinition, iconByVisual } = buildVisualMaps(visualHtml);

const sourceIconById = new Map();
for (const [internalId, source] of Object.entries(buffs)) {
    if (!harmfulCategories.has(source.category))
        continue;
    const iconPath = iconByDefinition.get(internalId) || iconByVisual.get(source.visuals) || "";
    sourceIconById.set(internalId, iconPath);
}

const uniqueIconPaths = [...new Set([...sourceIconById.values()].filter(Boolean))].sort();
const localKeyBySourcePath = makeLocalIconKeys(uniqueIconPaths);
const records = Object.entries(buffs)
    .filter(([, source]) => harmfulCategories.has(source.category))
    .map(([internalId, source]) => {
        const sourceIconPath = sourceIconById.get(internalId) || "";
        return {
            internalId,
            name: cleanGameMarkup(source.name),
            description: cleanGameMarkup(source.description),
            combatDescription: createHcCombatDescription(internalId, cleanGameMarkup(source.description)),
            category: source.category ?? "",
            invisible: Boolean(source.invisible),
            stats: Array.isArray(source.stats) ? source.stats : [],
            visualId: source.visuals ?? "",
            icon: sourceIconPath ? localKeyBySourcePath.get(sourceIconPath) : "",
            stackLimit: Number.isInteger(source.stack_limit) ? source.stack_limit : null,
        };
    })
    .sort((left, right) => left.internalId.localeCompare(right.internalId));

let downloaded = 0;
await runWithConcurrency(uniqueIconPaths, async sourcePath => {
    await downloadIcon(sourcePath, localKeyBySourcePath.get(sourcePath));
    downloaded++;
    if (downloaded % 50 === 0 || downloaded === uniqueIconPaths.length)
        process.stdout.write(`icons ${downloaded}/${uniqueIconPaths.length}\n`);
}, 12);

const info = {
    source: baseUrl,
    sourceVersion: sourceVersion.trim(),
    categories: [...harmfulCategories],
    definitionCount: records.length,
    namedDefinitionCount: records.filter(record => record.name).length,
    iconCount: uniqueIconPaths.length,
};

await fs.writeFile(path.join(dataDirectory, "RePoeDebuffs.json"), JSON.stringify(records, null, 2) + "\n");
await fs.writeFile(path.join(dataDirectory, "RePoeCatalogInfo.json"), JSON.stringify(info, null, 2) + "\n");
process.stdout.write(`generated ${records.length} harmful definitions (${records.filter(record => record.name).length} named)\n`);
