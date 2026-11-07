// Bundles the browser prototype into a single self-contained page (data + three.js + game code inline).
import { build } from 'esbuild';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { gameData } from './build-data.mjs';

const out = await build({ entryPoints: ['src/game/main.js'], bundle: true, format: 'iife', minify: true, write: false, target: ['es2020', 'safari15'], legalComments: 'none' });
const js = out.outputFiles[0].text.replace(/<\/script/gi, '<\\/script');
const data = JSON.stringify(gameData()).replace(/<\//g, '<\\/');
const page = readFileSync('page.html', 'utf8') + `\n<script>window.FFTS_DATA=${data};</script>\n<script>${js}</script>\n`;
mkdirSync('dist', { recursive: true });
writeFileSync('dist/fight-for-the-south.html', page);
// Local test copy with the same skeleton the artifact host adds.
writeFileSync('dist/local.html', `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover"></head><body>${page}</body></html>`);
console.log('built', (page.length / 1024).toFixed(0) + ' KB');
