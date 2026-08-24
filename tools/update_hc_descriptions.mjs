import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createHcCombatDescription } from "./hc_description_formatter.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const catalogPath = path.resolve(scriptDirectory, "..", "Data", "RePoeDebuffs.json");
const records = JSON.parse(await fs.readFile(catalogPath, "utf8"));

for (const record of records)
    record.combatDescription = createHcCombatDescription(record.internalId, record.description);

await fs.writeFile(catalogPath, JSON.stringify(records, null, 2) + "\n");
process.stdout.write(`updated ${records.length} RePoE records with HC combat descriptions\n`);

