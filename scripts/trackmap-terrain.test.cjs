const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../Assets/TrackMap3D/trackmap.js'), 'utf8');
function extract(name, async = false) {
  const start = source.indexOf(`  ${async ? 'async ' : ''}function ${name}(`);
  assert.ok(start >= 0);
  return source.slice(start, source.indexOf('\n  }', start) + 4);
}
function harness(fromUrl) {
  const timers = new Map();
  const status = {}, label = {}, button = {};
  let renders = 0, requests = 0, nextId = 0;
  const viewer = { terrainProvider: { flat: true }, scene: { requestRender() { renders++; } } };
  const context = vm.createContext({
    viewer, terrainStatus: status, terrainStatusText: label, terrainRetry: button,
    console: { warn() {} }, text: { terrainLoading: 'loading', terrainError: 'failed', terrainRetry: 'retry' },
    Cesium: { ArcGISTiledElevationTerrainProvider: { fromUrl() { requests++; return fromUrl(); } } },
    setTimeout(fn, ms) { const id = ++nextId; timers.set(id, {fn, ms}); return id; },
    clearTimeout(id) { timers.delete(id); }
  });
  vm.runInContext(`let terrainLoading = false, terrainRetryTimer; ${extract('setTerrainStatus')} ${extract('loadTerrain', true)}`, context);
  return { load: allow => vm.runInContext(`loadTerrain(${allow !== false})`, context),
    status, label, button, viewer, timers, requests: () => requests, renders: () => renders,
    async fire(ms) { const entry = [...timers].find(([, v]) => v.ms === ms); assert.ok(entry); timers.delete(entry[0]); await entry[1].fn(); }
  };
}
test('transient DEM failure retries once and installs recovered terrain', async () => {
  let fail = true; const provider = { terrain: true };
  const h = harness(() => fail ? Promise.reject(new Error('network down')) : Promise.resolve(provider));
  await h.load();
  assert.equal(h.viewer.terrainProvider.flat, true);
  assert.equal(h.status.hidden, false); assert.equal(h.status.title, 'network down');
  assert.equal(h.button.disabled, false);
  fail = false; await h.fire(3000);
  assert.equal(h.requests(), 2); assert.equal(h.viewer.terrainProvider, provider);
  assert.equal(h.status.hidden, true); assert.equal(h.renders(), 1); assert.equal(h.timers.size, 0);
});
test('persistent failure has bounded retries and allows later manual recovery', async () => {
  let fail = true;
  const h = harness(() => fail ? Promise.reject(new Error('offline')) : Promise.resolve({ recovered: true }));
  await h.load(); await h.fire(3000);
  assert.equal(h.requests(), 2); assert.equal(h.timers.size, 0);
  fail = false; await h.load();
  assert.equal(h.viewer.terrainProvider.recovered, true); assert.equal(h.status.hidden, true);
});
test('timeout releases loading lock; a late old result cannot overwrite successful retry', async () => {
  let resolveOld, count = 0;
  const fresh = { fresh: true };
  const h = harness(() => ++count === 1 ? new Promise(resolve => {resolveOld=resolve}) : Promise.resolve(fresh));
  const pending = h.load(); await h.load(); assert.equal(h.requests(), 1);
  await h.fire(15000); await pending;
  assert.equal(h.button.disabled, false);
  await h.load(); assert.equal(h.viewer.terrainProvider, fresh);
  resolveOld({ stale: true }); await Promise.resolve(); await Promise.resolve();
  assert.equal(h.viewer.terrainProvider, fresh); assert.equal(h.timers.size, 0);
});
