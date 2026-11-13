const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

// Exercise the actual message handler and camera callback without WebView2,
// Cesium downloads, a desktop window, or the user's map/session preferences.
const source = fs.readFileSync(path.resolve(__dirname, "../../Assets/TrackMap3D/trackmap.js"), "utf8");
const handler = source.match(/function handleMessage\(event\) \{[\s\S]*?\n  \}/)?.[0];
const moveEnd = source.match(/viewer\.camera\.moveEnd\.addEventListener\(\(\) => \{([\s\S]*?)\n    \}\);/)?.[1];
assert.ok(handler, "Track map message handler is present");
assert.ok(moveEnd, "Track map camera completion callback is present");

function createScene() {
  return vm.runInNewContext(`(() => {
    let cameraMoving = true;
    let pendingTrackPayload;
    let sceneState = { tracks: [{ id: 0 }], altitudeMode: "ground" };
    let gateState = [{ id: "gate" }];
    let lastDatasetKey, lastAltitudeMode, lastGateRenderKey, cursorEntity, cursorPoint;
    let compareCursorPoints = [];
    const trackRenderKeys = new Map(), trackEntityIds = new Set(), gateEntityIds = new Set();
    const compareCursorEntities = new Map();
    let rebuilds = 0, clears = 0, fits = [];
    const viewer = { entities: { removeAll() { clears++; } } };
    const text = { waiting: "waiting" };
    function refreshGateLegend() {}
    function setNotice() {}
    function updateTracks() { rebuilds++; }
    function fit() { fits.push(sceneState.selectedTrackId); }
    ${handler}
    return {
      receive: handleMessage,
      finishMove() { ${moveEnd} },
      snapshot() { return JSON.stringify({ sceneState, gateState, pendingTrackPayload, rebuilds, clears, fits }); }
    };
  })()`);
}

test("clear cancels a deferred scene before camera motion ends", () => {
  const scene = createScene();
  scene.receive({ data: { type: "tracks", payload: { tracks: [{ id: 7 }], altitudeMode: "ground" } } });
  assert.equal(JSON.parse(scene.snapshot()).pendingTrackPayload.tracks[0].id, 7);
  scene.receive({ data: { type: "clear" } });
  scene.finishMove();
  const state = JSON.parse(scene.snapshot());
  assert.deepEqual(state.sceneState.tracks, []);
  assert.deepEqual(state.gateState, []);
  assert.equal(state.pendingTrackPayload, undefined);
  assert.equal(state.rebuilds, 0);
  assert.equal(state.clears, 1);
});

test("ordinary camera motion still applies only the newest deferred scene", () => {
  const scene = createScene();
  for (const id of [7, 8])
    scene.receive({ data: { type: "tracks", payload: { tracks: [{ id }], altitudeMode: "ground" } } });
  scene.finishMove();
  const state = JSON.parse(scene.snapshot());
  assert.deepEqual(state.sceneState.tracks, [{ id: 8 }]);
  assert.equal(state.pendingTrackPayload, undefined);
  assert.equal(state.rebuilds, 1);
});

test("hide applies immediately during camera motion and cancels the stale deferred scene", () => {
  const scene = createScene();
  scene.receive({ data: { type: "tracks", payload: { tracks: [{ id: 0, isVisible: false }], altitudeMode: "ground" } } });
  const state = JSON.parse(scene.snapshot());
  assert.equal(state.sceneState.tracks[0].isVisible, false);
  assert.equal(state.pendingTrackPayload, undefined);
  assert.equal(state.rebuilds, 1);
});

test("Fit uses the selected file from the newest deferred scene", () => {
  const scene = createScene();
  scene.receive({ data: { type: "tracks", payload: { tracks: [{ id: 3 }, { id: 7 }], altitudeMode: "ground" } } });
  scene.receive({ data: { type: "selectedTrack", trackId: 7 } });
  scene.receive({ data: { type: "fit" } });
  const state = JSON.parse(scene.snapshot());
  assert.deepEqual(state.fits, [7]);
  assert.equal(state.sceneState.selectedTrackId, 7);
  assert.equal(state.pendingTrackPayload, undefined);
});

function extractFunction(name) {
  const result = source.match(new RegExp(`  function ${name}\\([^]*?\\n  \\}`))?.[0];
  assert.ok(result, `${name} is present`);
  return result;
}

function fitScene(selectedTrackId) {
  return vm.runInNewContext(`(() => {
    const sceneState = { selectedTrackId: ${selectedTrackId}, tracks: [
      { id: 3, points: [[1, 2, 0, 0], [2, 3, 0, 1]], isVisible: true },
      { id: 7, points: [[110, 30, 0, 11], [111, 31, 0, 22]], isVisible: false }
    ] };
    let flight;
    const viewer = { scene: { globe: { getHeight() { return 0; } } },
      camera: { flyToBoundingSphere(sphere) { flight = sphere; } } };
    const Cesium = {
      Cartographic: { fromDegrees(x, y) { return { x, y }; } },
      Cartesian3: { fromDegreesArrayHeights(points) { return points; } },
      BoundingSphere: { fromPoints(points) { return { points, radius: 1 }; } },
      HeadingPitchRange: class {}, Math: { toRadians(value) { return value; } }
    };
    function trackUsesGroundHeights() { return true; }
    ${extractFunction("fit")}
    fit();
    return JSON.stringify({ flight, sceneState });
  })()`);
}

test("Fit bounds include only the selected hidden source without revealing it", () => {
  const state = JSON.parse(fitScene(7));
  assert.deepEqual(state.flight.points, [110, 30, 0, 111, 31, 0]);
  assert.equal(state.sceneState.tracks[1].isVisible, false);
  assert.equal(state.flight.radius, 30);
});

test("an unavailable selected file never falls back to fitting another file", () => {
  assert.equal(JSON.parse(fitScene(-1)).flight, undefined);
});

function pickScene(entityId, visible = true) {
  return vm.runInNewContext(`(() => {
    let messages = [];
    const sceneState = { sceneRevision: 42, tracks: [
      { id: 7, isVisible: ${visible}, points: [[0, 0, 0, 100], [0.01, 0, 0, 900]] }
    ] };
    const viewer = { scene: {
      pick() { return { id: { id: ${JSON.stringify(entityId)} } }; },
      globe: { pick() { return {}; } }
    }, camera: { getPickRay() { return {}; } } };
    const Cesium = { Cartographic: { fromCartesian() { return { longitude: 0.009, latitude: 0 }; } },
      Math: { toDegrees(value) { return value; } } };
    const window = { chrome: { webview: { postMessage(message) { messages.push(message); } } } };
    ${extractFunction("handleSceneClick")}
    handleSceneClick({ position: {} });
    return JSON.stringify(messages);
  })()`);
}

test("3D track clicks retain original sample index and scene revision for gate placement", () => {
  assert.deepEqual(JSON.parse(pickScene("track-7-0")), [
    { type: "sampleSelected", trackId: 7, sceneRevision: 42, sampleIndex: 900 }
  ]);
  assert.deepEqual(JSON.parse(pickScene("track-7-0", false)), []);
});

test("3D gate and gate label clicks select the shared gate", () => {
  for (const id of ["gate-abc123", "gate-marker-abc123"])
    assert.deepEqual(JSON.parse(pickScene(id)), [{ type: "gateSelected", gateId: "abc123" }]);
});

test("visibility removes existing hidden geometry without renumbering other tracks", () => {
  const result = vm.runInNewContext(`(() => {
    const sceneState = { altitudeMode: "ground", selectedTrackId: 7, tracks: [
      { id: 3, points: [[1, 2, 0, 0], [2, 3, 0, 1]], isVisible: true, hasAltitude: true },
      { id: 7, points: [[110, 30, 0, 0], [111, 31, 0, 1]], isVisible: true, hasAltitude: true }
    ] };
    const entities = new Map();
    const viewer = { scene: { requestRender() {} }, entities: {
      get values() { return Array.from(entities.values()); },
      getById(id) { return entities.get(id); },
      add(entity) { entities.set(entity.id, entity); return entity; },
      removeById(id) { return entities.delete(id); }
    } };
    let lastAltitudeMode = "ground", lastDatasetKey = "", trackRenderRevision = 0;
    let trackEntityIds = new Set();
    const trackRenderKeys = new Map(), compareCursorPoints = [];
    const cursorPoint = undefined, text = { waiting: "", missing() { return ""; } };
    const Cesium = { Cartesian3: { fromDegreesArrayHeights(points) { return points; } },
      Color: { fromCssColorString(color) { return color; } } };
    function trackUsesGroundHeights() { return true; }
    function trackChunks(points) { return [points]; }
    function displayHeight() { return 0; }
    function fit() {}
    function updateGates() {}
    function setNotice() {}
    ${extractFunction("updateTracks")}
    updateTracks();
    const before = Array.from(entities.keys());
    sceneState.tracks[1].isVisible = false;
    updateTracks();
    const hidden = Array.from(entities.keys());
    sceneState.tracks[1].isVisible = true;
    updateTracks();
    return JSON.stringify({ before, hidden, restored: Array.from(entities.keys()) });
  })()`);
  assert.deepEqual(JSON.parse(result), {
    before: ["track-3-0", "track-7-0"], hidden: ["track-3-0"], restored: ["track-3-0", "track-7-0"]
  });
});

test("hiding a source removes its existing cursor even before another cursor message", () => {
  const result = vm.runInNewContext(`(() => {
    const sceneState = { tracks: [{ id: 7, isVisible: false }] };
    let cursorPoint, cursorEntity = { id: "cursor" }, removed = 0;
    const viewer = { trackedEntity: cursorEntity,
      entities: { remove() { removed++; } }, scene: { requestRender() {} } };
    function clearCompareCursorEntities() {}
    ${extractFunction("setCursor")}
    setCursor({ trackId: 7, sampleIndex: 900 });
    return JSON.stringify({ removed, cursorEntity, trackedEntity: viewer.trackedEntity });
  })()`);
  assert.deepEqual(JSON.parse(result), { removed: 1 });
});

test("map reports ready before a pending terrain request completes", () => {
  const start = source.search(/  try \{\r?\n    if \(!window\.Cesium\)/);
  const end = source.indexOf("  function handleSceneClick", start);
  assert.ok(start >= 0 && end > start);
  const messages = [];
  const event = { addEventListener() {} };
  const scene = {
    postProcessStages: { fxaa: {} }, globe: { tileLoadProgressEvent: event },
    skyBox: {}, sun: {}, moon: {}, skyAtmosphere: {}, fog: {},
    screenSpaceCameraController: {}, canvas: { addEventListener() {} }, requestRender() {}
  };
  const Cesium = {
    EllipsoidTerrainProvider: class {},
    Viewer: class { constructor() { this.scene = scene; this.camera = { moveStart: event, moveEnd: event }; } },
    Color: { fromCssColorString: x => x },
    ScreenSpaceEventHandler: class { setInputAction() {} }, ScreenSpaceEventType: { LEFT_CLICK: 0 },
    ArcGISTiledElevationTerrainProvider: { fromUrl: () => new Promise(() => {}) }
  };
  vm.runInNewContext(`(async () => {
    let viewer, cameraMoving, pendingTrackPayload, sceneState, cursorPoint, followVehicle;
    const compareCursorPoints = [], text = {};
    function setNotice() {} function refreshGateLegend() {} function handleMessage() {}
    function handleSceneClick() {} function setCursor() {} function setCursors() {} function updateTracks() {}
    ${source.slice(start, end)}
  })()`, {
    Cesium, window: { Cesium, addEventListener() {}, chrome: { webview: { addEventListener() {} } } },
    post: (type, message) => messages.push({ type, message }),
    setTimeout: () => 1, clearTimeout() {}
  });
  assert.deepEqual(messages, [{ type: "ready", message: "" }]);
});
