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
  let sceneState = { tracks: [], altitudeMode: "recorded" };
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
  // Lift tracks above DEM so GNSS/DEM mismatch does not bury polylines.
  const recordedTrackLiftMeters = 10;
  const gatePostHeightMeters = 40;

  function post(type, message) {
    if (window.chrome && window.chrome.webview)
      window.chrome.webview.postMessage({ type, message });
  }

  function setNotice(message) {
    notice.textContent = message || "";
    notice.style.display = message ? "block" : "none";
  }

  function displayHeight(point, track) {
    if (sceneState.altitudeMode === "ground" || !track.hasAltitude)
      return 0;
    return track.baseline + (point[2] - track.baseline) + recordedTrackLiftMeters;
  }

  function clearTrackEntities() {
    if (!viewer) return;
    for (const id of trackEntityIds)
      viewer.entities.removeById(id);
    trackEntityIds = new Set();
  }

  function updateTracks() {
    if (!viewer) return;

    viewer.scene.verticalExaggeration = 1;
    viewer.scene.verticalExaggerationRelativeHeight = 0;

    const mode = sceneState.altitudeMode === "ground" ? "ground" : "recorded";
    if (lastAltitudeMode !== null && lastAltitudeMode !== mode)
      clearTrackEntities();
    lastAltitudeMode = mode;

    let drawable = 0;
    let missingAltitude = 0;
    const nextEntityIds = new Set();
    const datasetKey = (sceneState.tracks || []).map(track => {
      const first = track.points && track.points.length ? track.points[0] : [];
      return `${track.id}:${first[0]}:${first[1]}`;
    }).join("|");
    for (const track of sceneState.tracks || []) {
      if (!track.points || track.points.length < 2) continue;
      drawable++;
      if (!track.hasAltitude) missingAltitude++;
      const degrees = [];
      for (const point of track.points)
        degrees.push(point[0], point[1], displayHeight(point, track));

      const trackId = `track-${track.id}`;
      const color = Cesium.Color.fromCssColorString(track.color || "#42A5F5");
      const positions = Cesium.Cartesian3.fromDegreesArrayHeights(degrees);
      const clampToGround = mode === "ground" || !track.hasAltitude;
      let entity = viewer.entities.getById(trackId);
      // Recreate when clamp mode flips: Cesium often ignores in-place clampToGround changes.
      if (entity) {
        const wasClamped = !!(entity.polyline && entity.polyline.clampToGround && entity.polyline.clampToGround.getValue && entity.polyline.clampToGround.getValue());
        if (wasClamped !== clampToGround) {
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
            clampToGround,
            material: color,
            depthFailMaterial: color
          }
        });
      } else {
        entity.name = track.name;
        entity.polyline.positions = positions;
        entity.polyline.width = 8;
        entity.polyline.clampToGround = clampToGround;
        entity.polyline.material = color;
        entity.polyline.depthFailMaterial = color;
      }
      nextEntityIds.add(trackId);

      const first = track.points[0];
      const last = track.points[track.points.length - 1];
      upsertEndpoint(`start-${track.id}`, "S", first, track, "#49c878");
      upsertEndpoint(`end-${track.id}`, "E", last, track, "#f0b44d");
      nextEntityIds.add(`start-${track.id}`);
      nextEntityIds.add(`end-${track.id}`);
    }

    for (const id of trackEntityIds) {
      if (!nextEntityIds.has(id)) viewer.entities.removeById(id);
    }
    trackEntityIds = nextEntityIds;

    if (!drawable) {
      setNotice(text.waiting);
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

  function upsertEndpoint(id, labelText, point, track, color) {
    const position = Cesium.Cartesian3.fromDegrees(point[0], point[1], displayHeight(point, track));
    let entity = viewer.entities.getById(id);
    if (!entity) {
      entity = viewer.entities.add({
        id,
        position,
        point: { pixelSize: 9, color: Cesium.Color.fromCssColorString(color), outlineColor: Cesium.Color.BLACK, outlineWidth: 2,
          disableDepthTestDistance: Number.POSITIVE_INFINITY },
        label: { text: labelText, font: "bold 12px Segoe UI", pixelOffset: new Cesium.Cartesian2(0, -18),
          fillColor: Cesium.Color.WHITE, outlineColor: Cesium.Color.BLACK, outlineWidth: 3,
          style: Cesium.LabelStyle.FILL_AND_OUTLINE, disableDepthTestDistance: Number.POSITIVE_INFINITY }
      });
    } else {
      entity.position = position;
    }
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

  function gateHeight(alt) {
    if (sceneState.altitudeMode === "ground")
      return 0;
    const value = Number.isFinite(alt) ? alt : 0;
    return value + recordedTrackLiftMeters;
  }

  function upsertGatePolyline(id, name, positions, width, color, clampToGround) {
    let entity = viewer.entities.getById(id);
    if (!entity) {
      entity = viewer.entities.add({
        id,
        name,
        polyline: {
          positions,
          width,
          clampToGround,
          material: color
        }
      });
    } else {
      entity.name = name;
      entity.polyline.positions = positions;
      entity.polyline.width = width;
      entity.polyline.clampToGround = clampToGround;
      entity.polyline.material = color;
    }
  }

  function upsertGateMarker(id, lon, lat, height, labelText) {
    // Label-only: no round point marker (user request).
    const position = Cesium.Cartesian3.fromDegrees(lon, lat, height);
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
          showBackground: true,
          backgroundColor: Cesium.Color.fromCssColorString("#0A0E14").withAlpha(0.72)
        }
      });
    } else {
      entity.position = position;
      if (entity.label) {
        entity.label.text = labelText || "";
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
    const clampToGround = sceneState.altitudeMode === "ground";
    for (const gate of gateState || []) {
      if (!gate) continue;
      const h0 = gateHeight(gate.alt0);
      const h1 = gateHeight(gate.alt1);
      const color = Cesium.Color.fromCssColorString(gate.colorHex || "#3FBF6F");
      const width = gate.selected ? 10 : 6;
      const name = gate.name || "Gate";

      // One continuous U polyline (top0 -> base0 -> base1 -> top1) updated in place
      // so frequent track refreshes do not remove/re-add and flicker.
      const barLift = clampToGround ? 2 : 0;
      const postH = gatePostHeightMeters;
      const lineId = `gate-${gate.id}`;
      const positions = Cesium.Cartesian3.fromDegreesArrayHeights([
        gate.lon0, gate.lat0, h0 + barLift + postH,
        gate.lon0, gate.lat0, h0 + barLift,
        gate.lon1, gate.lat1, h1 + barLift,
        gate.lon1, gate.lat1, h1 + barLift + postH
      ]);
      upsertGatePolyline(lineId, name, positions, width, color, false);
      next.add(lineId);

      const midLon = (Number(gate.lon0) + Number(gate.lon1)) * 0.5;
      const midLat = (Number(gate.lat0) + Number(gate.lat1)) * 0.5;
      const midH = (h0 + h1) * 0.5 + barLift + postH * 0.55;
      const markerId = `gate-marker-${gate.id}`;
      upsertGateMarker(markerId, midLon, midLat, midH, name);
      next.add(markerId);
    }
    for (const id of gateEntityIds) {
      if (!next.has(id)) viewer.entities.removeById(id);
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

  function setCursor(point) {
    if (!viewer) return;
    cursorPoint = point;
    clearCompareCursorEntities();
    if (!point) {
      if (cursorEntity) viewer.entities.remove(cursorEntity);
      cursorEntity = undefined;
      return;
    }
    const track = (sceneState.tracks || []).find(item => item.id === point.trackId);
    const useGround = sceneState.altitudeMode === "ground" || (track && !track.hasAltitude);
    const height = useGround
      ? 0
      : track
        ? track.baseline + (point.altitude - track.baseline) + recordedTrackLiftMeters
        : point.altitude + recordedTrackLiftMeters;
    const position = Cesium.Cartesian3.fromDegrees(point.longitude, point.latitude, height);
    if (!cursorEntity) {
      cursorEntity = viewer.entities.add({
        id: "cmts-cursor",
        position,
        point: { pixelSize: 14, color: Cesium.Color.GOLD, outlineColor: Cesium.Color.BLACK,
          outlineWidth: 3, disableDepthTestDistance: Number.POSITIVE_INFINITY,
          heightReference: useGround ? Cesium.HeightReference.CLAMP_TO_GROUND : Cesium.HeightReference.NONE }
      });
    } else {
      cursorEntity.position = position;
      cursorEntity.point.heightReference = useGround
        ? Cesium.HeightReference.CLAMP_TO_GROUND
        : Cesium.HeightReference.NONE;
    }
    viewer.scene.requestRender();
  }

  function cursorPosition(point) {
    const track = (sceneState.tracks || []).find(item => item.id === point.trackId);
    const useGround = sceneState.altitudeMode === "ground" || (track && !track.hasAltitude);
    const height = useGround
      ? 0
      : track
        ? track.baseline + (point.altitude - track.baseline) + recordedTrackLiftMeters
        : point.altitude + recordedTrackLiftMeters;
    return { position: Cesium.Cartesian3.fromDegrees(point.longitude, point.latitude, height), useGround };
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
      const color = Cesium.Color.fromCssColorString(point.color || "#FFD54F");
      let entity = compareCursorEntities.get(id);
      if (!entity) {
        entity = viewer.entities.add({
          id,
          position: resolved.position,
          point: { pixelSize: 15, color, outlineColor: Cesium.Color.BLACK,
            outlineWidth: 3, disableDepthTestDistance: Number.POSITIVE_INFINITY,
            heightReference: resolved.useGround ? Cesium.HeightReference.CLAMP_TO_GROUND : Cesium.HeightReference.NONE }
        });
        compareCursorEntities.set(id, entity);
      } else {
        entity.position = resolved.position;
        entity.point.color = color;
        entity.point.heightReference = resolved.useGround
          ? Cesium.HeightReference.CLAMP_TO_GROUND : Cesium.HeightReference.NONE;
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