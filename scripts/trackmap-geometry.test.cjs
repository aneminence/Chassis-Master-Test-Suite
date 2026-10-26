const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');
const source = fs.readFileSync(require('node:path').join(__dirname, '../Assets/TrackMap3D/trackmap.js'), 'utf8');
function extract(name) {
  const start = source.indexOf(`  function ${name}(`);
  assert.ok(start >= 0, name);
  const end = source.indexOf('\n  }', start) + 4;
  return source.slice(start, end);
}
function harness() {
  const context = vm.createContext({});
  vm.runInContext(`
    const groundTrackLiftMeters = 0, recordedTrackLiftMeters = 10;
    const maxGroundChunkPoints = 512, cursorBoxHalfHeightM = 0.75, cursorBoxLengthM = 5;
    const viewer = { scene: { globe: { getHeight: () => 100 } } };
    const sceneState = { altitudeMode: 'ground', tracks: [
      { id: 1, hasAltitude: true, baseline: 100, points: [[0, 0, 100], [0.002, 0, 140]] }
    ] };
    const Cesium = {
      Cartographic: { fromDegrees: (longitude, latitude) => ({ longitude, latitude }) },
      Cartesian3: { fromDegrees: (longitude, latitude, height) => ({ longitude, latitude, height }) },
      Math: { toRadians: x => x * Math.PI / 180 },
      HeadingPitchRoll: class { constructor(h) { this.heading = h; } },
      Transforms: { headingPitchRollQuaternion: (_p, hpr) => hpr.heading }
    };
    ${['isGroundMode', 'trackUsesGroundHeights', 'displayHeight', 'trackDisplayHeightNear', 'cursorOrientation', 'cursorPitch', 'cursorPosition', 'trackChunks'].map(extract).join('\n')}
  `, context);
  return code => vm.runInContext(code, context);
}
test('Ground has no artificial altitude offset', () => {
  const run = harness();
  assert.equal(run('trackDisplayHeightNear(0.001, 0)'), 0);
  assert.equal(run('trackDisplayHeightNear(0.001, 0.0001)'), 0);
});
test('Recorded/Ground switching and no-altitude fallback share displayHeight', () => {
  const run = harness();
  run("sceneState.altitudeMode = 'recorded'");
  assert.equal(run('trackDisplayHeightNear(0.001, 0)'), 130);
  run('sceneState.tracks[0].hasAltitude = false');
  assert.equal(run('trackDisplayHeightNear(0.001, 0)'), 0);
  assert.equal(run('trackDisplayHeightNear(0, 0, [])'), undefined);
});
test('GPS cardinal headings align box X with north/east/south/west in ENU', () => {
  const run = harness();
  for (const [heading, east, north] of [[0, 0, 1], [90, 1, 0], [180, 0, -1], [270, -1, 0]]) {
    const angle = run(`cursorOrientation({}, { heading: ${heading} })`);
    // Cesium HPR rotates around negative Z, starting at local X (east).
    assert.ok(Math.abs(Math.cos(angle) - east) < 1e-12);
    assert.ok(Math.abs(-Math.sin(angle) - north) < 1e-12);
  }
});

test('vehicle bottom, rather than top, coincides with track height in both modes', () => {
  const run = harness();
  const point = '{ trackId: 1, longitude: 0.001, latitude: 0, altitude: 999 }';
  let result = run(`cursorPosition(${point})`);
  assert.equal(result.useGround, true);
  // RELATIVE_TO_GROUND: Cesium adds half-height itself; the input is bottom clearance.
  assert.equal(result.height, 0);
  run("sceneState.altitudeMode = 'recorded'");
  result = run(`cursorPosition(${point})`);
  assert.equal(result.useGround, false);
  assert.equal(result.height - 0.75, 130);
});
test('bounded ground chunks cover all segments without gaps and reject duplicate positions', () => {
  const run = harness();
  run('const points = Array.from({length: 1200}, (_, i) => [i / 10000, 0, 100])');
  const chunks = run('trackChunks(points, true)');
  assert.equal(chunks.length, 3);
  assert.ok(chunks.every(c => c.length <= 512));
  assert.equal(chunks.reduce((n, c) => n + c.length - 1, 0), 1199);
  for (let i = 1; i < chunks.length; i++)
    assert.deepEqual(chunks[i - 1].at(-1), chunks[i][0]);
  assert.equal(run('trackChunks([[0,0,0], [0,0,1], [1,0,0]], true)[0].length'), 2);
  assert.equal(run('trackChunks(points, false).length'), 1);
  assert.equal(run('trackChunks([[0,0,0], [0,0,0]], true).length'), 0);
});
function sceneHarness() {
  const context = vm.createContext({});
  vm.runInContext(`
    let sceneState = { altitudeMode: 'ground', tracks: [] }, gateState = [];
    let lastAltitudeMode = null, lastDatasetKey = '', trackRenderRevision = 0, lastGateRenderKey = '';
    let trackEntityIds = new Set(), gateEntityIds = new Set();
    let cursorPoint, compareCursorPoints = [];
    const trackRenderKeys = new Map(), maxGroundChunkPoints = 512;
    const groundTrackLiftMeters = 0, recordedTrackLiftMeters = 10;
    const text = { waiting: '', missing: () => '' };
    const items = new Map();
    let adds = 0, removes = 0, renders = 0;
    const viewer = {
      entities: {
        get values() { return [...items.values()]; },
        getById: id => items.get(id),
        add: entity => {
          adds++;
          if (entity.polyline) {
            let value = entity.polyline.clampToGround;
            Object.defineProperty(entity.polyline, 'clampToGround', {
              get: () => ({getValue: () => value}), set: next => { value = next; }
            });
          }
          items.set(entity.id, entity); return entity;
        },
        remove: entity => { removes++; return items.delete(entity.id); },
        removeById: id => { removes++; return items.delete(id); }
      },
      scene: { requestRender: () => { renders++; } }
    };
    const Cesium = {
      Cartesian3: { fromDegreesArrayHeights: values => values },
      Color: { BLACK: 'black', fromCssColorString: s => s },
      PolylineOutlineMaterialProperty: class { constructor(o) { Object.assign(this, o); } }
    };
    function setNotice() {}
    function fit() {}
    function setCursor() {}
    function setCursors() {}
    function refreshGateLegend() {}
    function upsertGateMarker() {}
    ${['isGroundMode', 'trackUsesGroundHeights', 'displayHeight', 'trackChunks',
      'clearTrackEntities', 'updateTracks', 'trackDisplayHeightNear', 'upsertGatePolyline', 'updateGates'].map(extract).join('\n')}
  `, context);
  return code => vm.runInContext(code, context);
}
test('unchanged scene messages keep geometry identities and skip gate rebuilds', () => {
  const run = sceneHarness();
  run(`sceneState.tracks = [{id: 7, hasAltitude: true, color: '#ff0000',
    points: Array.from({length: 1200}, (_, i) => [i / 10000, 0, 100])}];
    gateState = [{id: 1, lon0: .05, lat0: -.001, lon1: .05, lat1: .001}];
    updateTracks();
    const originalPositions = items.get('track-7-0').polyline.positions;`);
  assert.equal(run('adds'), 4); // Three track chunks and one gate.
  assert.equal(run("items.get('track-7-0').polyline.clampToGround.getValue()"), true);
  assert.equal(run("items.get('gate-1').polyline.zIndex"), 10);
  assert.equal(run("items.get('track-7-0').polyline.zIndex"), 0);
  run('for (let i = 0; i < 100; i++) { updateTracks(); updateGates(); }');
  assert.equal(run('adds'), 4);
  assert.equal(run('removes'), 0);
  assert.equal(run("items.get('track-7-0').polyline.positions === originalPositions"), true);
});
test('mode switches replace ground chunks and clear obsolete entities', () => {
  const run = sceneHarness();
  run(`sceneState.tracks = [{id: 7, hasAltitude: true, points:
    Array.from({length: 1200}, (_, i) => [i / 10000, 0, 100])}];
    gateState = [{id: 1, lon0: .05, lat0: -.001, lon1: .05, lat1: .001}];
    updateTracks(); sceneState.altitudeMode = 'recorded'; updateTracks();`);
  assert.equal(run("items.has('track-7-1')"), false);
  assert.equal(run("items.get('track-7-0').polyline.clampToGround.getValue()"), false);
  assert.equal(run("items.get('gate-1').polyline.clampToGround.getValue()"), false);
  assert.equal(run("items.get('gate-1').polyline.positions[2]"), 110.05);
  run("sceneState.altitudeMode = 'ground'; updateTracks()");
  assert.equal(run("items.has('track-7-2')"), true);
  assert.equal(run("items.get('gate-1').polyline.clampToGround.getValue()"), true);
  run('sceneState.tracks = []; gateState = []; updateTracks()');
  assert.equal(run('items.size'), 0);
});

test('Ground slope follows rendered terrain and tilted bottom plane stays on slope', () => {
  const run = harness();
  run('viewer.scene.globe.getHeight = p => 100 + p.latitude * 111320 * 0.5');
  const up = run('cursorPosition({trackId: 1, longitude: 0.001, latitude: 0, heading: 0})');
  assert.ok(Math.abs(up.pitch - Math.atan(0.5)) < 1e-10);
  assert.ok(Math.abs((up.height + 0.75) * Math.cos(up.pitch) - 0.75) < 1e-10);
  const down = run('cursorPosition({trackId: 1, longitude: 0.001, latitude: 0, heading: 180})');
  assert.ok(Math.abs(down.pitch + up.pitch) < 1e-10);
  run('viewer.scene.globe.getHeight = () => undefined');
  assert.equal(run('cursorPosition({trackId: 1, longitude: 0.001, latitude: 0}).pitch'), 0);
});
test('Recorded slope uses track altitude instead of DEM', () => {
  const run = harness();
  run("sceneState.altitudeMode = 'recorded'");
  const result = run('cursorPosition({trackId: 1, longitude: 0.001, latitude: 0, heading: 90})');
  assert.ok(Math.abs(result.pitch - Math.atan(40 / (0.002 * 111320))) < 1e-10);
  assert.ok(Math.abs((result.height - 130) * Math.cos(result.pitch) - 0.75) < 1e-10);
});
