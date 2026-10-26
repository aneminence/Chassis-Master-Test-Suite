(async function () {
  "use strict";

  const notice = document.getElementById("notice");
  const gateLegend = document.getElementById("gateLegend");
  const gateLegendItems = document.getElementById("gateLegendItems");
  const isZh = (navigator.language || "").toLowerCase().startsWith("zh");
  const text = isZh ? {
    terrainLoading: "正在加载在线 DEM 地形…",
    terrainError: "在线 DEM 加载失败，已降级为平面地球。正式发布前请配置 Cesium ion Token。",
    terrainSampling: "正在采样地形高度…",
    waiting: "等待 GPS 数据",
    missing: count => `${count} 条轨迹没有有效高度，已降级为贴地显示。`,
    cesiumError: "无法下载 Cesium，请检查网络连接。",
    gatesTitle: "Gates"
  } : {
    terrainLoading: "Loading online DEM terrain…",
    terrainError: "Online DEM failed to load; using the flat ellipsoid fallback. Configure a Cesium ion token before release.",
    terrainSampling: "Sampling terrain heights…",
    waiting: "Waiting for GPS data",
    missing: count => `${count} track(s) have no usable altitude and are shown on the ground.`,
    cesiumError: "Cesium could not be downloaded. Check the network connection.",
    gatesTitle: "Gates"
  };
  let viewer;
  let sceneState = { tracks: [], altitudeMode: "ground" };
  let lastAltitudeMode = null;
  let gateState = [];
  let gateEntityIds = new Set();
  let cursorEntity;
  let cursorPoint;
  let compareCursorPoints = [];
  const compareCursorEntities = new Map();
  let lastDatasetKey = "";
  let trackEntityIds = new Set();
  let cameraMoving = false;
  let pendingTrackPayload;
  let imageryGeneration = 0;
  // Lift recorded tracks above DEM so GNSS/DEM mismatch does not bury polylines.
  const recordedTrackLiftMeters = 10;
  // Ground mode: sample DEM heights and lift slightly (do NOT use clampToGround on dense tracks).
  const groundTrackLiftMeters = 2.5;
  const maxTerrainSamplesPerTrack = 900;
  let terrainSampleGen = 0;
  // trackId -> { key, heights: number[] } absolute ellipsoid heights (DEM + lift)
  const groundHeightByTrack = new Map();
  // "lon,lat" -> absolute height for gate endpoints in ground mode
  const gateGroundHeightCache = new Map();
  // Vehicle cursor box: length(along heading) x width x height in meters.
  const cursorBoxLengthM = 5.0;
  const cursorBoxWidthM = 2.0;
  const cursorBoxHeightM = 1.8;
  const cursorBoxHalfHeightM = cursorBoxHeightM * 0.5;

  function post(type, message) {
    if (window.chrome && window.chrome.webview)
      window.chrome.webview.postMessage({ type, message });
  }

  function setNotice(message) {
    notice.textContent = message || "";
    notice.style.display = message ? "block" : "none";
  }

  function isGroundMode() {
    return sceneState.altitudeMode === "ground";
  }

  function trackUsesGroundHeights(track) {
    return isGroundMode() || !track.hasAltitude;
  }

  function terrainCacheKey(track) {
    const points = track.points || [];
    const n = points.length;
    if (!n) return "0";
    const a = points[0];
    const b = points[n - 1];
    return `${n}:${a[0]}:${a[1]}:${b[0]}:${b[1]}:${a[2]}:${b[2]}`;
  }

  function pickSampleIndices(count, maxSamples) {
    if (count <= 0) return [];
    if (count <= maxSamples) {
      const all = new Array(count);
      for (let i = 0; i < count; i++) all[i] = i;
      return all;
    }
    const stride = Math.ceil(count / maxSamples);
    const indices = [];
    for (let i = 0; i < count; i += stride) indices.push(i);
    if (indices[indices.length - 1] !== count - 1) indices.push(count - 1);
    return indices;
  }

  function displayHeight(point, track, index) {
    if (trackUsesGroundHeights(track)) {
      const cached = groundHeightByTrack.get(track.id);
      if (cached && cached.heights && Number.isFinite(cached.heights[index]))
        return cached.heights[index];
      // Pending DEM sample: absolute lift above ellipsoid (visible with depthTest off).
      return groundTrackLiftMeters;
    }
    return track.baseline + (point[2] - track.baseline) + recordedTrackLiftMeters;
  }

  function gateLonLatKey(lon, lat) {
    return `${Number(lon).toFixed(6)},${Number(lat).toFixed(6)}`;
  }

  function sampleGroundHeightSync(lon, lat) {
    const key = gateLonLatKey(lon, lat);
    if (gateGroundHeightCache.has(key))
      return gateGroundHeightCache.get(key);
    if (!viewer || !viewer.scene || !viewer.scene.globe) return groundTrackLiftMeters;
    const carto = Cesium.Cartographic.fromDegrees(lon, lat);
    const h = viewer.scene.globe.getHeight(carto);
    if (Number.isFinite(h)) {
      const value = h + groundTrackLiftMeters;
      gateGroundHeightCache.set(key, value);
      return value;
    }
    return groundTrackLiftMeters;
  }

  async function sampleHeightsAlongTrack(track) {
    const points = track.points || [];
    const n = points.length;
    if (n < 2) return null;
    const indices = pickSampleIndices(n, maxTerrainSamplesPerTrack);
    const cartographics = indices.map(i =>
      Cesium.Cartographic.fromDegrees(points[i][0], points[i][1]));
    try {
      await Cesium.sampleTerrainMostDetailed(viewer.terrainProvider, cartographics);
    } catch (error) {
      for (const carto of cartographics) {
        const h = viewer.scene.globe.getHeight(carto);
        carto.height = Number.isFinite(h) ? h : 0;
      }
    }
    const sampleHeights = cartographics.map(carto =>
      (Number.isFinite(carto.height) ? carto.height : 0) + groundTrackLiftMeters);
    const heights = new Array(n);
    for (let s = 0; s < indices.length - 1; s++) {
      const i0 = indices[s];
      const i1 = indices[s + 1];
      const h0 = sampleHeights[s];
      const h1 = sampleHeights[s + 1];
      const span = i1 - i0;
      for (let i = i0; i <= i1; i++) {
        const t = span === 0 ? 0 : (i - i0) / span;
        heights[i] = h0 + (h1 - h0) * t;
      }
    }
    heights[indices[0]] = sampleHeights[0];
    heights[indices[indices.length - 1]] = sampleHeights[sampleHeights.length - 1];
    return heights;
  }

  async function refreshGateGroundCache() {
    const carts = [];
    const keys = [];
    const seen = new Set();
    for (const gate of gateState || []) {
      if (!gate) continue;
      for (const pair of [[gate.lon0, gate.lat0], [gate.lon1, gate.lat1]]) {
        const key = gateLonLatKey(pair[0], pair[1]);
        if (seen.has(key)) continue;
        seen.add(key);
        keys.push(key);
        carts.push(Cesium.Cartographic.fromDegrees(pair[0], pair[1]));
      }
    }
    if (!carts.length) return;
    try {
      await Cesium.sampleTerrainMostDetailed(viewer.terrainProvider, carts);
      for (let i = 0; i < carts.length; i++) {
        const h = (Number.isFinite(carts[i].height) ? carts[i].height : 0) + groundTrackLiftMeters;
        gateGroundHeightCache.set(keys[i], h);
      }
    } catch (error) {
      for (let i = 0; i < carts.length; i++) {
        const h = viewer.scene.globe.getHeight(carts[i]);
        gateGroundHeightCache.set(keys[i],
          (Number.isFinite(h) ? h : 0) + groundTrackLiftMeters);
      }
    }
  }

  async function ensureGroundHeights(tracksNeedingSample) {
    const gen = ++terrainSampleGen;
    try {
      setNotice(text.terrainSampling);
      for (const track of tracksNeedingSample) {
        if (gen !== terrainSampleGen) return;
        const heights = await sampleHeightsAlongTrack(track);
        if (gen !== terrainSampleGen) return;
        if (!heights) continue;
        groundHeightByTrack.set(track.id, { key: terrainCacheKey(track), heights });
      }
      if (gen !== terrainSampleGen) return;
      await refreshGateGroundCache();
      if (gen !== terrainSampleGen) return;
      // Redraw with sampled absolute heights (still no clampToGround).
      updateTracks(true);
    } catch (error) {
      if (gen === terrainSampleGen)
        setNotice(error && error.message ? error.message : String(error));
    }
  }

  function clearTrackEntities() {
    if (!viewer) return;
    for (const id of trackEntityIds)
      viewer.entities.removeById(id);
    trackEntityIds = new Set();
  }

  function updateTracks(fromTerrainSample) {
    if (!viewer) return;

    viewer.scene.verticalExaggeration = 1;
    viewer.scene.verticalExaggerationRelativeHeight = 0;

    const mode = sceneState.altitudeMode === "ground" ? "ground" : "recorded";
    if (lastAltitudeMode !== null && lastAltitudeMode !== mode) {
      clearTrackEntities();
      groundHeightByTrack.clear();
      gateGroundHeightCache.clear();
      terrainSampleGen++;
    }
    lastAltitudeMode = mode;

    let drawable = 0;
    let missingAltitude = 0;
    const nextEntityIds = new Set();
    const needSample = [];
    const datasetKey = (sceneState.tracks || []).map(track => {
      const first = track.points && track.points.length ? track.points[0] : [];
      return `${track.id}:${first[0]}:${first[1]}`;
    }).join("|");
    for (const track of sceneState.tracks || []) {
      if (!track.points || track.points.length < 2) continue;
      drawable++;
      if (!track.hasAltitude) missingAltitude++;
      if (trackUsesGroundHeights(track)) {
        const key = terrainCacheKey(track);
        const cached = groundHeightByTrack.get(track.id);
        if (!cached || cached.key !== key)
          needSample.push(track);
      }
      const degrees = [];
      for (let i = 0; i < track.points.length; i++) {
        const point = track.points[i];
        degrees.push(point[0], point[1], displayHeight(point, track, i));
      }

      const trackId = `track-${track.id}`;
      const color = Cesium.Color.fromCssColorString(track.color || "#42A5F5");
      const positions = Cesium.Cartesian3.fromDegreesArrayHeights(degrees);
      // Never clampToGround for long GNSS polylines — they often vanish on Cesium.
      // Ground mode uses sampled DEM heights + small lift instead.
      const clampToGround = false;
      let entity = viewer.entities.getById(trackId);
      // Drop leftover clampToGround entities from older builds.
      if (entity) {
        const wasClamped = !!(entity.polyline && entity.polyline.clampToGround &&
          entity.polyline.clampToGround.getValue && entity.polyline.clampToGround.getValue());
        if (wasClamped) {
          viewer.entities.remove(entity);
          entity = undefined;
        }
      }
      if (!entity) {
        entity = viewer.entities.add({
          id: trackId,
          name: track.name,
          polyline: {
            positions,
            width: 8,
            clampToGround: false,
            material: color,
            depthFailMaterial: color
          }
        });
      } else {
        entity.name = track.name;
        entity.polyline.positions = positions;
        entity.polyline.width = 8;
        entity.polyline.clampToGround = false;
        entity.polyline.material = color;
        entity.polyline.depthFailMaterial = color;
      }
      nextEntityIds.add(trackId);
    }

    for (const id of trackEntityIds) {
      if (!nextEntityIds.has(id)) viewer.entities.removeById(id);
    }
    // Remove leftover S/E endpoint markers from prior builds.
    const staleEnds = [];
    for (const entity of viewer.entities.values) {
      if (!entity || !entity.id) continue;
      const id = String(entity.id);
      if (id.indexOf("start-") === 0 || id.indexOf("end-") === 0) staleEnds.push(id);
    }
    for (const id of staleEnds) {
      viewer.entities.removeById(id);
      nextEntityIds.delete(id);
    }
    trackEntityIds = nextEntityIds;

    if (!drawable) {
      setNotice(text.waiting);
      return;
    }
    if (!fromTerrainSample && needSample.length) {
      setNotice(text.terrainSampling);
      ensureGroundHeights(needSample);
    } else {
      setNotice(missingAltitude ? text.missing(missingAltitude) : "");
    }
    if (datasetKey !== lastDatasetKey) fit();
    lastDatasetKey = datasetKey;
    if (cursorPoint) setCursor(cursorPoint);
    if (compareCursorPoints.length) setCursors(compareCursorPoints);
    updateGates();
    viewer.scene.requestRender();
  }


  function quadKey(x, y, level) {
    let result = "";
    for (let i = level; i > 0; i--) {
      let digit = 0;
      const mask = 1 << (i - 1);
      if ((x & mask) !== 0) digit++;
      if ((y & mask) !== 0) digit += 2;
      result += digit;
    }
    return result;
  }

  // Match track displayHeight: recorded uses alt+lift; ground uses sampled DEM + lift.
  function gateHeight(alt, track) {
    if (sceneState.altitudeMode === "ground" || (track && !track.hasAltitude))
      return groundTrackLiftMeters;
    const value = Number.isFinite(alt) ? alt : 0;
    if (track && Number.isFinite(track.baseline))
      return track.baseline + (value - track.baseline) + recordedTrackLiftMeters;
    return value + recordedTrackLiftMeters;
  }

  // Nearest track sample altitude at lon/lat (same idea as C# ResolveGateAltitudeMeters).
  function sampleTrackAltitudeNear(lon, lat) {
    let bestAlt = NaN;
    let bestDist = Infinity;
    const mPerDegLat = 111320.0;
    const mPerLon = mPerDegLat * Math.cos((lat || 0) * Math.PI / 180.0);
    for (const track of sceneState.tracks || []) {
      if (!track.points || !track.points.length) continue;
      for (const point of track.points) {
        const dx = (point[0] - lon) * mPerLon;
        const dy = (point[1] - lat) * mPerDegLat;
        const d2 = dx * dx + dy * dy;
        if (d2 >= bestDist) continue;
        bestDist = d2;
        bestAlt = point[2];
      }
    }
    return bestAlt;
  }

  function resolveGateEndpointHeight(lon, lat, altFallback) {
    const tracks = sceneState.tracks || [];
    const track = tracks.find(t => t.hasAltitude) || tracks[0];
    if (isGroundMode() || (track && !track.hasAltitude))
      return sampleGroundHeightSync(lon, lat);
    let alt = Number.isFinite(altFallback) ? altFallback : NaN;
    if (!Number.isFinite(alt)) {
      const sampled = sampleTrackAltitudeNear(lon, lat);
      if (Number.isFinite(sampled)) alt = sampled;
    } else {
      // Prefer nearby track sample so bar matches ribbon when DTO alt is a coarse center estimate.
      const sampled = sampleTrackAltitudeNear(lon, lat);
      if (Number.isFinite(sampled)) alt = sampled;
    }
    return gateHeight(alt, track);
  }

  function upsertGatePolyline(id, name, positions, width, color, clampToGround) {
    let entity = viewer.entities.getById(id);
    if (entity) {
      const wasClamped = !!(entity.polyline && entity.polyline.clampToGround &&
        entity.polyline.clampToGround.getValue && entity.polyline.clampToGround.getValue());
      if (wasClamped !== !!clampToGround) {
        viewer.entities.remove(entity);
        entity = undefined;
      }
    }
    if (!entity) {
      entity = viewer.entities.add({
        id,
        name,
        polyline: {
          positions,
          width,
          clampToGround,
          material: color,
          depthFailMaterial: color
        }
      });
    } else {
      entity.name = name;
      entity.polyline.positions = positions;
      entity.polyline.width = width;
      entity.polyline.clampToGround = clampToGround;
      entity.polyline.material = color;
      entity.polyline.depthFailMaterial = color;
    }
  }

  function upsertGateMarker(id, lon, lat, height, labelText, clampToGround) {
    // Label-only: no round point marker (user request).
    const position = Cesium.Cartesian3.fromDegrees(lon, lat, height);
    const heightReference = clampToGround
      ? Cesium.HeightReference.RELATIVE_TO_GROUND
      : Cesium.HeightReference.NONE;
    let entity = viewer.entities.getById(id);
    if (!entity) {
      entity = viewer.entities.add({
        id,
        position,
        label: {
          text: labelText || "",
          font: "bold 11px Segoe UI",
          pixelOffset: new Cesium.Cartesian2(0, -12),
          fillColor: Cesium.Color.WHITE,
          outlineColor: Cesium.Color.BLACK,
          outlineWidth: 3,
          style: Cesium.LabelStyle.FILL_AND_OUTLINE,
          disableDepthTestDistance: Number.POSITIVE_INFINITY,
          heightReference,
          showBackground: true,
          backgroundColor: Cesium.Color.fromCssColorString("#0A0E14").withAlpha(0.72)
        }
      });
    } else {
      entity.position = position;
      if (entity.label) {
        entity.label.text = labelText || "";
        entity.label.heightReference = heightReference;
      }
    }
  }

  function refreshGateLegend() {
    if (!gateLegend || !gateLegendItems) return;
    const title = gateLegend.querySelector(".gate-legend-title");
    if (title) title.textContent = text.gatesTitle;
    gateLegendItems.innerHTML = "";
    const gates = gateState || [];
    if (!gates.length) {
      gateLegend.style.display = "none";
      return;
    }
    gateLegend.style.display = "block";
    for (const gate of gates) {
      if (!gate) continue;
      const row = document.createElement("div");
      row.className = "gate-legend-row";
      const swatch = document.createElement("span");
      swatch.className = "gate-legend-swatch";
      swatch.style.background = gate.colorHex || "#3FBF6F";
      const label = document.createElement("span");
      label.className = "gate-legend-label" + (gate.selected ? " selected" : "");
      label.textContent = gate.name || "Gate";
      row.appendChild(swatch);
      row.appendChild(label);
      gateLegendItems.appendChild(row);
    }
  }

  function updateGates() {
    if (!viewer) return;
    const next = new Set();
    // Polylines use absolute sampled/recorded heights — never clampToGround.
    const tracks = sceneState.tracks || [];
    const anyHasAltitude = tracks.some(t => t && t.hasAltitude);
    const useGroundHeights = isGroundMode() || !anyHasAltitude;
    const clampToGround = false;
    // Labels: RELATIVE_TO_GROUND in ground mode; absolute mid+lift in recorded.
    const labelLift = 3;
    for (const gate of gateState || []) {
      if (!gate) continue;
      const h0 = resolveGateEndpointHeight(gate.lon0, gate.lat0, gate.alt0);
      const h1 = resolveGateEndpointHeight(gate.lon1, gate.lat1, gate.alt1);
      const color = Cesium.Color.fromCssColorString(gate.colorHex || "#3FBF6F");
      const width = gate.selected ? 12 : 8;
      const name = gate.name || "Gate";

      const lineId = `gate-${gate.id}`;
      const positions = Cesium.Cartesian3.fromDegreesArrayHeights([
        gate.lon0, gate.lat0, h0,
        gate.lon1, gate.lat1, h1
      ]);
      upsertGatePolyline(lineId, name, positions, width, color, clampToGround);
      next.add(lineId);

      const midLon = (Number(gate.lon0) + Number(gate.lon1)) * 0.5;
      const midLat = (Number(gate.lat0) + Number(gate.lat1)) * 0.5;
      const midH = useGroundHeights ? labelLift : (h0 + h1) * 0.5 + labelLift;
      const markerId = `gate-marker-${gate.id}`;
      upsertGateMarker(markerId, midLon, midLat, midH, name, useGroundHeights);
      next.add(markerId);
    }
    for (const id of gateEntityIds) {
      if (!next.has(id)) viewer.entities.removeById(id);
    }
    // Sweep leftover post / old U entity ids from prior builds (F5 without full clear).
    const toRemove = [];
    for (const entity of viewer.entities.values) {
      if (!entity || !entity.id) continue;
      const id = String(entity.id);
      if (id.indexOf("gate-post") === 0 || id.indexOf("gate-top-") === 0 || id.indexOf("gate-base-") === 0) {
        toRemove.push(id);
      }
    }
    for (const id of toRemove) {
      viewer.entities.removeById(id);
      next.delete(id);
    }
    gateEntityIds = next;
    refreshGateLegend();
    viewer.scene.requestRender();
  }

  async function setImagery(source) {
    if (!viewer || !source || !source.url) return;
    const generation = ++imageryGeneration;
    try {
      const provider = new Cesium.UrlTemplateImageryProvider({
        url: source.url,
        subdomains: ["0", "1", "2", "3"],
        maximumLevel: source.maximumLevel || 19,
        credit: source.attribution || "",
        customTags: { q: (_provider, x, y, level) => quadKey(x, y, level) }
      });
      if (generation !== imageryGeneration) return;
      viewer.imageryLayers.removeAll(true);
      viewer.imageryLayers.addImageryProvider(provider);
      viewer.scene.requestRender();
    } catch (error) {
      setNotice(error && error.message ? error.message : String(error));
    }
  }

  function cursorPosition(point) {
    const track = (sceneState.tracks || []).find(item => item.id === point.trackId);
    const useGround = sceneState.altitudeMode === "ground" || (track && !track.hasAltitude);
    let trackHeight;
    if (useGround) {
      trackHeight = 0;
    } else if (track) {
      trackHeight = track.baseline + (point.altitude - track.baseline) + recordedTrackLiftMeters;
    } else {
      trackHeight = point.altitude + recordedTrackLiftMeters;
    }
    // Box center sits half-height above the track ribbon so the bottom rests on it.
    const height = useGround ? cursorBoxHalfHeightM : trackHeight + cursorBoxHalfHeightM;
    const position = Cesium.Cartesian3.fromDegrees(point.longitude, point.latitude, height);
    return { position, useGround, height };
  }

  function cursorOrientation(position, point) {
    const headingDeg = Number.isFinite(point.heading) ? point.heading : 0;
    // Cesium HeadingPitchRoll: heading clockwise from north (same as GPS travel heading).
    return Cesium.Transforms.headingPitchRollQuaternion(
      position,
      new Cesium.HeadingPitchRoll(Cesium.Math.toRadians(headingDeg), 0, 0)
    );
  }

  function applyCursorBox(entity, point, resolved, color) {
    const heightReference = resolved.useGround
      ? Cesium.HeightReference.RELATIVE_TO_GROUND
      : Cesium.HeightReference.NONE;
    entity.position = resolved.position;
    entity.orientation = cursorOrientation(resolved.position, point);
    if (entity.point) entity.point = undefined;
    if (!entity.box) {
      entity.box = {
        dimensions: new Cesium.Cartesian3(cursorBoxLengthM, cursorBoxWidthM, cursorBoxHeightM),
        material: color,
        outline: true,
        outlineColor: Cesium.Color.BLACK,
        heightReference
      };
    } else {
      entity.box.dimensions = new Cesium.Cartesian3(cursorBoxLengthM, cursorBoxWidthM, cursorBoxHeightM);
      entity.box.material = color;
      entity.box.outline = true;
      entity.box.outlineColor = Cesium.Color.BLACK;
      entity.box.heightReference = heightReference;
    }
  }

  function setCursor(point) {
    if (!viewer) return;
    cursorPoint = point;
    clearCompareCursorEntities();
    if (!point) {
      if (cursorEntity) viewer.entities.remove(cursorEntity);
      cursorEntity = undefined;
      return;
    }
    const resolved = cursorPosition(point);
    const color = Cesium.Color.GOLD.withAlpha(0.92);
    if (cursorEntity && cursorEntity.point) {
      viewer.entities.remove(cursorEntity);
      cursorEntity = undefined;
    }
    if (!cursorEntity) {
      cursorEntity = viewer.entities.add({
        id: "cmts-cursor",
        position: resolved.position,
        orientation: cursorOrientation(resolved.position, point),
        box: {
          dimensions: new Cesium.Cartesian3(cursorBoxLengthM, cursorBoxWidthM, cursorBoxHeightM),
          material: color,
          outline: true,
          outlineColor: Cesium.Color.BLACK,
          heightReference: resolved.useGround
            ? Cesium.HeightReference.RELATIVE_TO_GROUND
            : Cesium.HeightReference.NONE
        }
      });
    } else {
      applyCursorBox(cursorEntity, point, resolved, color);
    }
    viewer.scene.requestRender();
  }

  function clearCompareCursorEntities() {
    for (const entity of compareCursorEntities.values())
      viewer.entities.remove(entity);
    compareCursorEntities.clear();
    compareCursorPoints = [];
  }

  function setCursors(points) {
    if (!viewer) return;
    compareCursorPoints = Array.isArray(points) ? points : [];
    cursorPoint = undefined;
    if (cursorEntity) {
      viewer.entities.remove(cursorEntity);
      cursorEntity = undefined;
    }
    const keep = new Set();
    compareCursorPoints.forEach((point, index) => {
      const id = `cmts-compare-cursor-${point.runNumber || index}`;
      keep.add(id);
      const resolved = cursorPosition(point);
      const color = Cesium.Color.fromCssColorString(point.color || "#FFD54F").withAlpha(0.88);
      let entity = compareCursorEntities.get(id);
      if (entity && entity.point) {
        viewer.entities.remove(entity);
        compareCursorEntities.delete(id);
        entity = undefined;
      }
      if (!entity) {
        entity = viewer.entities.add({
          id,
          position: resolved.position,
          orientation: cursorOrientation(resolved.position, point),
          box: {
            dimensions: new Cesium.Cartesian3(cursorBoxLengthM, cursorBoxWidthM, cursorBoxHeightM),
            material: color,
            outline: true,
            outlineColor: Cesium.Color.BLACK,
            heightReference: resolved.useGround
              ? Cesium.HeightReference.RELATIVE_TO_GROUND
              : Cesium.HeightReference.NONE
          }
        });
        compareCursorEntities.set(id, entity);
      } else {
        applyCursorBox(entity, point, resolved, color);
      }
    });
    for (const [id, entity] of compareCursorEntities) {
      if (keep.has(id)) continue;
      viewer.entities.remove(entity);
      compareCursorEntities.delete(id);
    }
    viewer.scene.requestRender();
  }

  function fit() {
    if (viewer && viewer.entities.values.length)
      viewer.zoomTo(viewer.entities, new Cesium.HeadingPitchRange(0, Cesium.Math.toRadians(-38), 0));
  }

  function handleMessage(event) {
    const data = event.data || {};
    if (data.type === "tracks") {
      const next = data.payload || sceneState;
      const modeChanged = !!(next && sceneState && next.altitudeMode !== sceneState.altitudeMode);
      // Altitude-mode switches must apply immediately; do not defer behind camera motion.
      if (cameraMoving && !modeChanged) {
        pendingTrackPayload = next;
      } else {
        sceneState = next;
        pendingTrackPayload = undefined;
        updateTracks();
      }
    } else if (data.type === "imagery") {
      setImagery(data.payload);
    } else if (data.type === "cursor") {
      setCursor(data.payload);
    } else if (data.type === "cursors") {
      setCursors(data.payload);
    } else if (data.type === "gates") {
      gateState = Array.isArray(data.payload) ? data.payload : [];
      if (isGroundMode()) {
        refreshGateGroundCache().then(() => updateGates()).catch(() => updateGates());
      } else {
        updateGates();
      }
    } else if (data.type === "fit") {
      fit();
    } else if (data.type === "clear") {
      sceneState.tracks = [];
      gateState = [];
      lastDatasetKey = "";
      lastAltitudeMode = null;
      terrainSampleGen++;
      groundHeightByTrack.clear();
      gateGroundHeightCache.clear();
      trackEntityIds.clear();
      gateEntityIds.clear();
      viewer.entities.removeAll();
      cursorEntity = undefined;
      cursorPoint = undefined;
      compareCursorPoints = [];
      compareCursorEntities.clear();
      refreshGateLegend();
      setNotice(text.waiting);
    }
  }

  try {
    if (!window.Cesium) throw new Error(text.cesiumError);
    setNotice(text.terrainLoading);
    let terrainProvider;
    let terrainFallback = false;
    try {
      terrainProvider = await Cesium.ArcGISTiledElevationTerrainProvider.fromUrl(
        "https://elevation3d.arcgis.com/arcgis/rest/services/WorldElevation3D/Terrain3D/ImageServer");
    } catch (terrainError) {
      terrainProvider = new Cesium.EllipsoidTerrainProvider();
      terrainFallback = true;
    }
    viewer = new Cesium.Viewer("cesiumContainer", {
      terrainProvider,
      baseLayer: false,
      animation: false, timeline: false, fullscreenButton: false, homeButton: false,
      sceneModePicker: false, baseLayerPicker: false, geocoder: false,
      navigationHelpButton: false, infoBox: false, selectionIndicator: false,
      requestRenderMode: true,
      maximumRenderTimeChange: Number.POSITIVE_INFINITY
    });
    // Prefer visibility over terrain occlusion: GNSS alts often sit below DEM.
    viewer.scene.globe.depthTestAgainstTerrain = false;
    viewer.scene.globe.enableLighting = false;
    viewer.scene.backgroundColor = Cesium.Color.fromCssColorString("#8FC7F2");
    viewer.scene.skyBox.show = false;
    viewer.scene.sun.show = false;
    viewer.scene.moon.show = false;
    viewer.scene.skyAtmosphere.show = true;
    viewer.scene.fog.enabled = true;
    const cameraController = viewer.scene.screenSpaceCameraController;
    cameraController.enableCollisionDetection = true;
    cameraController.minimumZoomDistance = 5.0;
    cameraController.maximumTiltAngle = Math.PI / 2.0;
    viewer.camera.moveStart.addEventListener(() => { cameraMoving = true; });
    viewer.camera.moveEnd.addEventListener(() => {
      cameraMoving = false;
      if (pendingTrackPayload) {
        sceneState = pendingTrackPayload;
        pendingTrackPayload = undefined;
        updateTracks();
      }
    });
    const clickHandler = new Cesium.ScreenSpaceEventHandler(viewer.scene.canvas);
    clickHandler.setInputAction(movement => {
      const picked = viewer.scene.pick(movement.position);
      const entityId = picked && picked.id && picked.id.id;
      if (typeof entityId !== "string" || !entityId.startsWith("track-")) return;

      const trackId = Number(entityId.substring("track-".length));
      const track = (sceneState.tracks || []).find(item => item.id === trackId);
      if (!track || !track.points || !track.points.length) return;

      const ray = viewer.camera.getPickRay(movement.position);
      const world = ray && viewer.scene.globe.pick(ray, viewer.scene);
      if (!world) return;
      const cartographic = Cesium.Cartographic.fromCartesian(world);
      const longitude = Cesium.Math.toDegrees(cartographic.longitude);
      const latitude = Cesium.Math.toDegrees(cartographic.latitude);
      const cosLatitude = Math.cos(cartographic.latitude);
      let nearest = track.points[0];
      let nearestDistance = Number.POSITIVE_INFINITY;
      for (const point of track.points) {
        const dx = (point[0] - longitude) * cosLatitude;
        const dy = point[1] - latitude;
        const distance = dx * dx + dy * dy;
        if (distance < nearestDistance) {
          nearestDistance = distance;
          nearest = point;
        }
      }
      window.chrome.webview.postMessage({
        type: "sampleSelected",
        trackId,
        sampleIndex: Math.round(nearest[3])
      });
    }, Cesium.ScreenSpaceEventType.LEFT_CLICK);
    setNotice(terrainFallback ? text.terrainError : "");
    refreshGateLegend();
    window.chrome.webview.addEventListener("message", handleMessage);
    post("ready", "");
  } catch (error) {
    const message = error && error.message ? error.message : String(error);
    setNotice(message);
    post("status", message);
  }
})();