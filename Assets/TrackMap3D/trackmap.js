(async function () {
  "use strict";

  const notice = document.getElementById("notice");
  const badge = document.getElementById("badge");
  const isZh = (navigator.language || "").toLowerCase().startsWith("zh");
  const text = isZh ? {
    badge: "3D · 在线影像 · 真实地形",
    badgeFallback: "3D · 在线影像 · 平面降级",
    terrainLoading: "正在加载在线 DEM 地形…",
    terrainError: "在线 DEM 加载失败，已降级为平面地球。正式发布前请配置 Cesium ion Token。",
    waiting: "等待 GPS 数据",
    missing: count => `${count} 条轨迹没有有效高度，已降级为贴地显示。`,
    cesiumError: "无法下载 Cesium，请检查网络连接。"
  } : {
    badge: "3D · online imagery · world terrain",
    badgeFallback: "3D · online imagery · flat fallback",
    terrainLoading: "Loading online DEM terrain…",
    terrainError: "Online DEM failed to load; using the flat ellipsoid fallback. Configure a Cesium ion token before release.",
    waiting: "Waiting for GPS data",
    missing: count => `${count} track(s) have no usable altitude and are shown on the ground.`,
    cesiumError: "Cesium could not be downloaded. Check the network connection."
  };
  badge.textContent = text.badge;
  let viewer;
  let sceneState = { tracks: [], altitudeMode: "recorded", exaggeration: 1 };
  let cursorEntity;
  let cursorPoint;
  let compareCursorPoints = [];
  const compareCursorEntities = new Map();
  let lastDatasetKey = "";
  let trackEntityIds = new Set();
  let cameraMoving = false;
  let pendingTrackPayload;
  let imageryGeneration = 0;
  const recordedTrackLiftMeters = 1.5;

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
    // Keep the visual line just above the DEM. Recorded GNSS altitude and the
    // terrain mesh often differ by less than a metre, which otherwise causes
    // depth-buffer z-fighting while orbiting the camera.
    return track.baseline + (point[2] - track.baseline) * sceneState.exaggeration
      + recordedTrackLiftMeters;
  }

  function updateTracks() {
    if (!viewer) return;

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
      let entity = viewer.entities.getById(trackId);
      if (!entity) {
        entity = viewer.entities.add({ id: trackId, name: track.name, polyline: {} });
      }
      entity.name = track.name;
      entity.polyline.positions = Cesium.Cartesian3.fromDegreesArrayHeights(degrees);
      entity.polyline.width = 5;
      entity.polyline.clampToGround = sceneState.altitudeMode === "ground" || !track.hasAltitude;
      entity.polyline.material = color;
      entity.polyline.depthFailMaterial = color;
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
    viewer.scene.requestRender();
  }

  function upsertEndpoint(id, labelText, point, track, color) {
    const position = Cesium.Cartesian3.fromDegrees(point[0], point[1], displayHeight(point, track));
    let entity = viewer.entities.getById(id);
    if (!entity) {
      entity = viewer.entities.add({
        id,
        position,
        point: { pixelSize: 9, color: Cesium.Color.fromCssColorString(color), outlineColor: Cesium.Color.BLACK, outlineWidth: 2 },
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
        ? track.baseline + (point.altitude - track.baseline) * sceneState.exaggeration
          + recordedTrackLiftMeters
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
        ? track.baseline + (point.altitude - track.baseline) * sceneState.exaggeration
          + recordedTrackLiftMeters
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
      if (cameraMoving) {
        pendingTrackPayload = data.payload;
      } else {
        sceneState = data.payload || sceneState;
        updateTracks();
      }
    } else if (data.type === "imagery") {
      setImagery(data.payload);
    } else if (data.type === "cursor") {
      setCursor(data.payload);
    } else if (data.type === "cursors") {
      setCursors(data.payload);
    } else if (data.type === "fit") {
      fit();
    } else if (data.type === "clear") {
      sceneState.tracks = [];
      lastDatasetKey = "";
      trackEntityIds.clear();
      viewer.entities.removeAll();
      cursorEntity = undefined;
      cursorPoint = undefined;
      compareCursorPoints = [];
      compareCursorEntities.clear();
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
    viewer.scene.globe.depthTestAgainstTerrain = true;
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
    badge.textContent = terrainFallback ? text.badgeFallback : text.badge;
    setNotice(terrainFallback ? text.terrainError : "");
    window.chrome.webview.addEventListener("message", handleMessage);
    post("ready", "");
  } catch (error) {
    const message = error && error.message ? error.message : String(error);
    setNotice(message);
    post("status", message);
  }
})();
