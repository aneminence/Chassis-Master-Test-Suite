(async function () {
  "use strict";

  const notice = document.getElementById("notice");
  const gateLegend = document.getElementById("gateLegend");
  const gateLegendItems = document.getElementById("gateLegendItems");
  const isZh = (navigator.language || "").toLowerCase().startsWith("zh");
  const text = isZh ? {
    terrainLoading: "正在加载在线 DEM 地形…",
    terrainError: "在线 DEM 加载失败，已降级为平面地球。正式发布前请配置 Cesium ion Token。",
    waiting: "等待 GPS 数据",
    missing: count => `${count} 条轨迹没有有效高度，已降级为贴地显示。`,
    cesiumError: "无法下载 Cesium，请检查网络连接。",
    gatesTitle: "Gates"
  } : {
    terrainLoading: "Loading online DEM terrain…",
    terrainError: "Online DEM failed to load; using the flat ellipsoid fallback. Configure a Cesium ion token before release.",
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
  // Ground geometry is draped by Cesium, without a vertical offset or DEM resampling.
  const groundTrackLiftMeters = 0;
  const maxGroundChunkPoints = 512;
  const trackRenderKeys = new Map();
  let trackRenderRevision = 0;
  let lastGateRenderKey = "";
  // Vehicle cursor box: length(along heading) x width x height in meters.
  const cursorBoxLengthM = 5.0;
  const cursorBoxWidthM = 2.0;
  const cursorBoxHeightM = 1.5;
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

  function displayHeight(point, track, index) {
    return trackUsesGroundHeights(track) ? groundTrackLiftMeters : point[2] + recordedTrackLiftMeters;
  }

  function trackChunks(points, ground) {
    // Adjacent chunks share one endpoint, preserving every original segment.
    // Exact duplicate positions are ignored: they form degenerate ground geometry.
    const clean = points.filter((p, i) => i === 0 || p[0] !== points[i - 1][0]
      || p[1] !== points[i - 1][1] || (!ground && p[2] !== points[i - 1][2]));
    const chunks = [];
    const size = ground ? maxGroundChunkPoints : Math.max(2, clean.length);
    for (let start = 0; start < clean.length - 1; start += size - 1)
      chunks.push(clean.slice(start, start + size));
    return chunks;
  }

  function clearTrackEntities() {
    if (!viewer) return;
    for (const id of trackEntityIds)
      viewer.entities.removeById(id);
    trackEntityIds = new Set();
    trackRenderKeys.clear();
  }

  function updateTracks() {
    if (!viewer) return;

    viewer.scene.verticalExaggeration = 1;
    viewer.scene.verticalExaggerationRelativeHeight = 0;

    const mode = sceneState.altitudeMode === "ground" ? "ground" : "recorded";
    if (lastAltitudeMode !== null && lastAltitudeMode !== mode) {
      clearTrackEntities();
    }
    lastAltitudeMode = mode;

    let drawable = 0;
    let missingAltitude = 0;
    const nextEntityIds = new Set();
    let geometryChanged = false;
    const datasetKey = (sceneState.tracks || []).map(track => {
      const first = track.points && track.points.length ? track.points[0] : [];
      return `${track.id}:${first[0]}:${first[1]}`;
    }).join("|");
    for (const track of sceneState.tracks || []) {
      if (!track.points || track.points.length < 2) continue;
      drawable++;
      if (!track.hasAltitude) missingAltitude++;
      const ground = trackUsesGroundHeights(track);
      const chunks = trackChunks(track.points, ground);
      chunks.forEach((points, chunkIndex) => {
        const trackId = `track-${track.id}-${chunkIndex}`;
        nextEntityIds.add(trackId);
        const key = JSON.stringify([ground, track.color, points]);
        if (trackRenderKeys.get(trackId) === key) return;
        geometryChanged = true;
        const degrees = points.flatMap((point, index) =>
          [point[0], point[1], displayHeight(point, track, index)]);
        const positions = Cesium.Cartesian3.fromDegreesArrayHeights(degrees);
        const color = Cesium.Color.fromCssColorString(track.color || "#42A5F5");
        let entity = viewer.entities.getById(trackId);
        if (!entity) {
          entity = viewer.entities.add({
            id: trackId, name: track.name,
            polyline: {
              positions, width: 3, clampToGround: ground, zIndex: 0,
              material: color
            }
          });
        } else {
          entity.name = track.name;
          entity.polyline.positions = positions;
          entity.polyline.clampToGround = ground;
          entity.polyline.material = color;
        }
        trackRenderKeys.set(trackId, key);
      });
    }

    for (const id of trackEntityIds) {
      if (!nextEntityIds.has(id)) {
        viewer.entities.removeById(id);
        trackRenderKeys.delete(id);
        geometryChanged = true;
      }
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
    if (geometryChanged) trackRenderRevision++;

    if (!drawable) {
      setNotice(text.waiting);
      updateGates();
      viewer.scene.requestRender();
      return;
    }
    setNotice(missingAltitude ? text.missing(missingAltitude) : "");
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

  // Recorded-mode gates/cursors interpolate the displayed track altitude.
  function trackDisplayHeightNear(lon, lat, tracks = sceneState.tracks || []) {
    let height;
    let bestDistance = Infinity;
    const cosLat = Math.cos(lat * Math.PI / 180);
    for (const track of tracks) {
      const points = track.points || [];
      for (let i = 1; i < points.length; i++) {
        const a = points[i - 1], b = points[i];
        const ax = (a[0] - lon) * cosLat, ay = a[1] - lat;
        const dx = (b[0] - a[0]) * cosLat, dy = b[1] - a[1];
        const lengthSquared = dx * dx + dy * dy;
        const t = lengthSquared > 0
          ? Math.max(0, Math.min(1, -(ax * dx + ay * dy) / lengthSquared)) : 0;
        const distance = (ax + t * dx) ** 2 + (ay + t * dy) ** 2;
        if (distance >= bestDistance) continue;
        bestDistance = distance;
        const h0 = displayHeight(a, track, i - 1);
        height = h0 + (displayHeight(b, track, i) - h0) * t;
      }
    }
    return height;
  }

  function upsertGatePolyline(id, name, positions, width, color, clampToGround) {
    const material = new Cesium.PolylineOutlineMaterialProperty({
      color, outlineColor: Cesium.Color.BLACK, outlineWidth: 0.5
    });
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
          zIndex: 10,
          material
        }
      });
    } else {
      entity.name = name;
      entity.polyline.positions = positions;
      entity.polyline.width = width;
      entity.polyline.clampToGround = clampToGround;
      entity.polyline.material = material;
      entity.polyline.zIndex = 10;
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
    const key = JSON.stringify([gateState, sceneState.altitudeMode, trackRenderRevision]);
    if (key === lastGateRenderKey) return;
    lastGateRenderKey = key;
    const next = new Set();
    const clampToGround = isGroundMode() || !(sceneState.tracks || []).some(t => t.hasAltitude);
    // Labels share the absolute bar height in both altitude modes.
    const labelLift = 3;
    for (const gate of gateState || []) {
      if (!gate) continue;
      const midLon = (Number(gate.lon0) + Number(gate.lon1)) * 0.5;
      const midLat = (Number(gate.lat0) + Number(gate.lat1)) * 0.5;
      const fallback = isGroundMode() ? groundTrackLiftMeters
        : (Number.isFinite(gate.alt0) ? gate.alt0 : 0) + recordedTrackLiftMeters;
      // Ground ordering is handled by zIndex. Recorded bars have a tiny offset
      // to avoid coplanar depth fighting with the track at their crossing.
      const h0 = clampToGround ? 0 : (trackDisplayHeightNear(midLon, midLat) ?? fallback) + 0.05;
      const h1 = h0;
      const color = Cesium.Color.fromCssColorString(gate.colorHex || "#3FBF6F");
      const width = gate.selected ? 5 : 4;
      const name = gate.name || "Gate";

      const lineId = `gate-${gate.id}`;
      const positions = Cesium.Cartesian3.fromDegreesArrayHeights([
        gate.lon0, gate.lat0, h0,
        gate.lon1, gate.lat1, h1
      ]);
      upsertGatePolyline(lineId, name, positions, width, color, clampToGround);
      next.add(lineId);

      const midH = h0 + labelLift;
      const markerId = `gate-marker-${gate.id}`;
      upsertGateMarker(markerId, midLon, midLat, midH, name, clampToGround);
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

  function cursorPitch(point, track, useGround) {
    const heading = (Number.isFinite(point.heading) ? point.heading : 0) * Math.PI / 180;
    // Measure a vehicle-length baseline. Ground uses the currently rendered DEM
    // tiles, avoiding network requests or full-track terrain sampling while dragging.
    const halfSpan = cursorBoxLengthM * 0.5;
    const latitudeScale = 111320;
    const longitudeScale = latitudeScale * Math.cos(point.latitude * Math.PI / 180);
    if (Math.abs(longitudeScale) < 1) return 0;
    const dx = Math.sin(heading) * halfSpan / longitudeScale;
    const dy = Math.cos(heading) * halfSpan / latitudeScale;
    const heightAt = (lon, lat) => useGround
      ? viewer.scene.globe.getHeight(Cesium.Cartographic.fromDegrees(lon, lat))
      : track && trackDisplayHeightNear(lon, lat, [track]);
    const back = heightAt(point.longitude - dx, point.latitude - dy);
    const front = heightAt(point.longitude + dx, point.latitude + dy);
    if (!Number.isFinite(back) || !Number.isFinite(front)) return 0;
    return Math.atan2(front - back, halfSpan * 2);
  }

  function cursorPosition(point) {
    const track = (sceneState.tracks || []).find(item => item.id === point.trackId);
    const useGround = sceneState.altitudeMode === "ground" || (track && !track.hasAltitude);
    // Ground box positions are relative to the very same terrain as the draped
    // track. Recorded boxes use the rendered segment, not a separate altitude.
    const trackHeight = useGround ? 0
      : (track && trackDisplayHeightNear(point.longitude, point.latitude, [track]))
        ?? (point.altitude + recordedTrackLiftMeters);
    // Cesium BoxGeometryUpdater adds half the box height for non-NONE height
    // references. Relative height therefore specifies the bottom, not the center.
    const pitch = cursorPitch(point, track, useGround);
    // Keep the tilted bottom plane on the local track tangent. At fixed lon/lat
    // the center needs halfHeight / cos(pitch), not the horizontal-box halfHeight.
    const centerLift = cursorBoxHalfHeightM / Math.cos(pitch);
    const height = useGround ? centerLift - cursorBoxHalfHeightM : trackHeight + centerLift;
    const position = Cesium.Cartesian3.fromDegrees(point.longitude, point.latitude, height);
    return { position, useGround, height, pitch };
  }

  function cursorOrientation(position, point, pitch = 0) {
    const headingDeg = Number.isFinite(point.heading) ? point.heading : 0;
    // ENU local X points east; GPS heading is clockwise from north.
    // Rotate the box length (X) from east to north before applying GPS heading.
    return Cesium.Transforms.headingPitchRollQuaternion(
      position,
      new Cesium.HeadingPitchRoll(Cesium.Math.toRadians(headingDeg - 90), pitch, 0)
    );
  }

  function applyCursorBox(entity, point, resolved, color) {
    const heightReference = resolved.useGround
      ? Cesium.HeightReference.RELATIVE_TO_GROUND
      : Cesium.HeightReference.NONE;
    entity.position = resolved.position;
    entity.orientation = cursorOrientation(resolved.position, point, resolved.pitch);
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
        orientation: cursorOrientation(resolved.position, point, resolved.pitch),
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
          orientation: cursorOrientation(resolved.position, point, resolved.pitch),
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
      updateGates();
    } else if (data.type === "fit") {
      fit();
    } else if (data.type === "clear") {
      sceneState.tracks = [];
      gateState = [];
      lastDatasetKey = "";
      lastAltitudeMode = null;
      trackRenderKeys.clear();
      lastGateRenderKey = "";
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
      // Limit redraw work while moving; idle scenes still render only on demand.
      targetFrameRate: 30,
      useBrowserRecommendedResolution: true,
      msaaSamples: 1,
      requestRenderMode: true,
      maximumRenderTimeChange: Number.POSITIVE_INFINITY
    });
    // FXAA avoids the GPU bandwidth cost of multisample rendering.
    viewer.scene.postProcessStages.fxaa.enabled = true;
    // Ground tracks/gates are draped; recorded tracks keep their absolute altitude.
    viewer.scene.globe.depthTestAgainstTerrain = false;
    viewer.scene.globe.enableLighting = false;
    viewer.scene.backgroundColor = Cesium.Color.fromCssColorString("#8FC7F2");
    viewer.scene.skyBox.show = false;
    viewer.scene.sun.show = false;
    viewer.scene.moon.show = false;
    viewer.scene.skyAtmosphere.show = true;
    viewer.scene.fog.enabled = true;
    // Re-evaluate slope when terrain tiles finish loading under a stationary cursor.
    viewer.scene.globe.tileLoadProgressEvent.addEventListener(pending => {
      if (pending !== 0) return;
      if (cursorPoint) setCursor(cursorPoint);
      else if (compareCursorPoints.length) setCursors(compareCursorPoints);
    });
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

      const trackId = Number(entityId.split("-")[1]);
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