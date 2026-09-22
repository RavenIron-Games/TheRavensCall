// Cross-platform copy of the release theravenscall.html into public/, so the
// receiver always ships whatever the page track last wrote. Run via
// `npm run copy-page`. Safe to re-run any time (overwrites public/theravenscall.html).
import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const src = join(here, "..", "..", "..", "theravenscall.html");
const destDir = join(here, "..", "public");
const dest = join(destDir, "theravenscall.html");

mkdirSync(destDir, { recursive: true });
copyFileSync(src, dest);
console.log(`copied ${src} -> ${dest}`);
