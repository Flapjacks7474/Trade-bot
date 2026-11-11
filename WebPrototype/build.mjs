// Bundles the browser version into a page plus its asset files (models, animations, lighting map).
import { build } from 'esbuild';
import { readFileSync, writeFileSync, mkdirSync, cpSync } from 'node:fs';
import { gameData } from './build-data.mjs';

const out = await build({ entryPoints: ['src/game/main.js'], bundle: true, format: 'iife', minify: true, write: false, target: ['es2020', 'safari15'], legalComments: 'none' });
const js = out.outputFiles[0].text.replace(/<\/script/gi, '<\\/script');
const data = JSON.stringify(gameData()).replace(/<\//g, '<\\/');
const page = readFileSync('page.html', 'utf8') + `\n<script>window.FFTS_DATA=${data};</script>\n<script>${js}</script>\n`;
mkdirSync('dist', { recursive: true });
writeFileSync('dist/fight-for-the-south.html', page);
cpSync('assets', 'dist/assets', { recursive: true });
// Local test copy with the same skeleton the artifact host adds.
writeFileSync('dist/local.html', `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover"></head><body>${page}</body></html>`);
console.log('built', (page.length / 1024).toFixed(0) + ' KB page + assets/');
