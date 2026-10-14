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
  let lastDatasetKey = "";

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
    return track.baseline + (point[2] - track.baseline) * sceneState.exaggeration;
  }

  function rebuildTracks() {
    if (!viewer) return;
    viewer.entities.removeAll();
    cursorEntity = undefined;

    let drawable = 0;
    let missingAltitude = 0;
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

      viewer.entities.add({
        id: `track-${track.id}`,
        name: track.name,
        polyline: {
          positions: Cesium.Cartesian3.fromDegreesArrayHeights(degrees),
          width: 5,
          clampToGround: sceneState.altitudeMode === "ground" || !track.hasAltitude,
          material: Cesium.Color.fromCssColorString(track.color || "#42A5F5"),
          depthFailMaterial: Cesium.Color.fromCssColorString(track.color || "#42A5F5").withAlpha(.45)
        }
      });

      const first = track.points[0];
      const last = track.points[track.points.length - 1];
      addEndpoint(`start-${track.id}`, "S", first, track, "#49c878");
      addEndpoint(`end-${track.id}`, "E", last, track, "#f0b44d");
    }

    if (!drawable) {
      setNotice(text.waiting);
      return;
    }
    setNotice(missingAltitude ? text.missing(missingAltitude) : "");
    if (datasetKey !== lastDatasetKey) fit();
    lastDatasetKey = datasetKey;
  }

  function addEndpoint(id, text, point, track, color) {
    viewer.entities.add({
      id,
      position: Cesium.Cartesian3.fromDegrees(point[0], point[1], displayHeight(point, track)),
      point: { pixelSize: 9, color: Cesium.Color.fromCssColorString(color), outlineColor: Cesium.Color.BLACK, outlineWidth: 2 },
      label: { text, font: "bold 12px Segoe UI", pixelOffset: new Cesium.Cartesian2(0, -18),
        fillColor: Cesium.Color.WHITE, outlineColor: Cesium.Color.BLACK, outlineWidth: 3,
        style: Cesium.LabelStyle.FILL_AND_OUTLINE, disableDepthTestDistance: Number.POSITIVE_INFINITY }
    });
  }

  function setCursor(point) {
    if (!viewer) return;
    if (!point) {
      if (cursorEntity) viewer.entities.remove(cursorEntity);
      cursorEntity = undefined;
      return;
    }
    const height = sceneState.altitudeMode === "ground" ? 0 : point.altitude;
    const position = Cesium.Cartesian3.fromDegrees(point.longitude, point.latitude, height);
    if (!cursorEntity) {
      cursorEntity = viewer.entities.add({
        id: "cmts-cursor",
        position,
        point: { pixelSize: 14, color: Cesium.Color.GOLD, outlineColor: Cesium.Color.BLACK,
          outlineWidth: 3, disableDepthTestDistance: Number.POSITIVE_INFINITY }
      });
    } else {
      cursorEntity.position = position;
    }
  }

  function fit() {
    if (viewer && viewer.entities.values.length)
      viewer.zoomTo(viewer.entities, new Cesium.HeadingPitchRange(0, Cesium.Math.toRadians(-38), 0));
  }

  function handleMessage(event) {
    const data = event.data || {};
    if (data.type === "tracks") {
      sceneState = data.payload || sceneState;
      rebuildTracks();
    } else if (data.type === "cursor") {
      setCursor(data.payload);
    } else if (data.type === "fit") {
      fit();
    } else if (data.type === "clear") {
      sceneState.tracks = [];
      lastDatasetKey = "";
      viewer.entities.removeAll();
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
      baseLayer: Cesium.ImageryLayer.fromProviderAsync(
        Cesium.ArcGisMapServerImageryProvider.fromUrl(
          "https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer")),
      animation: false, timeline: false, fullscreenButton: false, homeButton: false,
      sceneModePicker: false, baseLayerPicker: false, geocoder: false,
      navigationHelpButton: false, infoBox: false, selectionIndicator: false
    });
    viewer.scene.globe.depthTestAgainstTerrain = true;
    viewer.scene.globe.enableLighting = !terrainFallback;
    const cameraController = viewer.scene.screenSpaceCameraController;
    cameraController.enableCollisionDetection = true;
    cameraController.minimumZoomDistance = 5.0;
    cameraController.maximumTiltAngle = Math.PI / 2.0;
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
