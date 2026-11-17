// Real Cesium/WebGL regression, not a WebView2 or whole-WPF FPS benchmark.
// Run: node scripts/trackmap-browser-probe.cjs [--baseline path/to/trackmap.js]
// --motion measures per-frame motion and Follow interactions in regular/jitter+tile cases.
// --baseline-only runs only the --baseline source; each source is snapshotted and hashed.
// --orbit-only checks native gestures against the Follow frame without performance reruns.
// --orbit-remap adds experimental mouse-only rotation mapping in the test page.
// Optional env: PLAYWRIGHT_MODULE (existing package path), EDGE_EXECUTABLE.
// Outputs screenshots and JSON under tools/trackmap-probe; no user profile is opened.
const fs=require('node:fs'), http=require('node:http'),path=require('node:path'),crypto=require('node:crypto');
// PLAYWRIGHT_MODULE may point at an existing Playwright package; no install is performed.
const {chromium}=require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const root=path.resolve(__dirname,'..'),base=path.join(root,'tools','trackmap-probe'),assets=path.join(root,'Assets','TrackMap3D');
fs.mkdirSync(base,{recursive:true});
const baselineIndex=process.argv.indexOf('--baseline');
if (baselineIndex>=0 && !process.argv[baselineIndex+1]) throw new Error('--baseline requires a JavaScript file path');
const baselinePath=baselineIndex>=0?path.resolve(process.argv[baselineIndex+1]):null;
const versions=baselinePath?(process.argv.includes('--baseline-only')?['baseline']:['baseline','new']):['new'];
const motionMode=process.argv.includes('--motion');
const orbitOnly=process.argv.includes('--orbit-only');
// Snapshot sources once so a concurrent editor cannot mix implementations between cases.
const sources={new:fs.readFileSync(path.join(assets,'trackmap.js'),'utf8')};
if(baselinePath)sources.baseline=fs.readFileSync(baselinePath,'utf8');
const hashes=Object.fromEntries(Object.entries(sources).map(([k,v])=>[k,crypto.createHash('sha256').update(v).digest('hex')]));
const injection=`
window.probeViewer=viewer;
window.probeSceneState=()=>sceneState;
window.probeState=()=>({cursorEntity,followVehicle,chaseVehicle,followCameraActive:typeof followCameraActive === "undefined"?false:followCameraActive,solid:typeof cursorSolid==='undefined'?null:cursorSolid,outline:typeof cursorOutline==='undefined'?null:cursorOutline});
window.probeCounts={speeds:[],add:0,remove:0,frames:0,fillFrames:0,outlineFrames:0,dt:[],centerErrors:[],relativeErrors:[],longFrames:[]};
const pc=Cesium.PrimitiveCollection.prototype,add=pc.add,remove=pc.remove;
pc.add=function(p,...args){if(p instanceof Cesium.Primitive) window.probeCounts.add++;return add.call(this,p,...args)};
pc.remove=function(p,...args){if(p instanceof Cesium.Primitive) window.probeCounts.remove++;return remove.call(this,p,...args)};
window.probeLongTasks=[];new PerformanceObserver(list=>window.probeLongTasks.push(...list.getEntries().map(e=>({startMs:e.startTime,durationMs:e.duration})))).observe({entryTypes:["longtask"]});
let lastFrame, lastPosition, lastPositionTime; window.probeReset=()=>{lastFrame=lastPosition=lastPositionTime=undefined;window.probeCounts={speeds:[],add:0,remove:0,frames:0,fillFrames:0,outlineFrames:0,dt:[],centerErrors:[],relativeErrors:[],longFrames:[]};};
viewer.scene.postRender.addEventListener(()=>{
const c=window.probeCounts,now=performance.now();c.frames++;if(lastFrame){const delta=now-lastFrame;c.dt.push(delta);if(delta>50)c.longFrames.push({atMs:now,deltaMs:delta,dataSourceReady:viewer.dataSourceDisplay.ready,tilesLoaded:viewer.scene.globe.tilesLoaded,heapBytes:performance.memory?.usedJSHeapSize});}lastFrame=now;
const list=[];function visit(p){if(p instanceof Cesium.Primitive){const ids=p._instanceIds||[];if(ids.some(i=>i&&i.id==='cmts-cursor'))list.push(p)}else if(p&&typeof p.length==='number'&&p.get){for(let i=0;i<p.length;i++)visit(p.get(i))}}
visit(viewer.scene.primitives);
const own=window.probeState();
if(own.cursorEntity){const position=own.cursorEntity.position.getValue(viewer.clock.currentTime);if(lastPosition&&now>lastPositionTime)c.speeds.push(Cesium.Cartesian3.distance(lastPosition,position)/((now-lastPositionTime)/1000));lastPosition=Cesium.Cartesian3.clone(position);lastPositionTime=now;const xy=Cesium.SceneTransforms.worldToWindowCoordinates(viewer.scene,position);if(xy){c.centerErrors.push(Math.hypot(xy.x-viewer.scene.canvas.clientWidth/2,xy.y-viewer.scene.canvas.clientHeight/2));if(window.probeReferenceScreen)c.relativeErrors.push(Math.hypot(xy.x-probeReferenceScreen.x,xy.y-probeReferenceScreen.y));}}
if(own.solid){if(own.solid.ready&&own.solid.show)c.fillFrames++;if(own.outline.ready&&own.outline.show)c.outlineFrames++}else{for(const p of list){if(p.ready&&p.show){if(p.appearance&&p.appearance.flat)c.outlineFrames++;else c.fillFrames++;}}}
});
`;
const server=http.createServer((req,res)=>{
const u=new URL(req.url,'http://localhost');
if(u.pathname==='/trackmap.js'){
let s=sources[u.searchParams.get('version')==='baseline'?'baseline':'new'];
s=s.replace('    // FXAA avoids',injection+'\n    // FXAA avoids');
// Reproducible CPU/GPU geometry workload: no live DEM or imagery/network during runs.
s=s.replace(/viewer\.terrainProvider = await Promise\.race\(\[[\s\S]*?\n      \]\);/, 'viewer.terrainProvider = new Cesium.EllipsoidTerrainProvider();');
s=s.replace(/Cesium\.ArcGISTiledElevationTerrainProvider\.fromUrl/g, '(() => Promise.resolve(new Cesium.EllipsoidTerrainProvider()))');
res.setHeader('Content-Type','text/javascript');res.end(s);return;}
if(u.pathname==='/styles.css'){res.setHeader('Content-Type','text/css');res.end(fs.readFileSync(path.join(assets,'styles.css')));return;}
res.setHeader('Content-Type','text/html');res.end(`<!doctype html><html><head><meta charset="utf-8"><script src="https://cesium.com/downloads/cesiumjs/releases/1.146/Build/Cesium/Cesium.js"></script><link rel="stylesheet" href="https://cesium.com/downloads/cesiumjs/releases/1.146/Build/Cesium/Widgets/widgets.css"><link rel="stylesheet" href="/styles.css"></head><body><div id="cesiumContainer"></div><div id="gateLegend"><div id="gateLegendItems"></div></div><div id="notice"></div><div id="terrainStatus" hidden><span id="terrainStatusText"></span><button id="terrainRetry">Retry</button></div><script>window.chrome=window.chrome||{};window.chrome.webview={postMessage:m=>{if(m.type==='ready')window.probeReady=true;},addEventListener:(type,fn)=>window.probeSend=data=>fn({data})};</script><script src="/trackmap.js?version=${u.searchParams.get('version')}"></script></body></html>`)
});
async function checkInteractions(page) {
  await page.evaluate(()=>{const c=probeViewer.scene.screenSpaceCameraController;c.inertiaZoom=c.inertiaSpin=c.inertiaTranslate=0;probeSend({type:'chaseVehicle',enabled:false});probeSend({type:'followVehicle',enabled:true});});
  await page.waitForTimeout(100);
  const before=await page.evaluate(()=>Cesium.Cartesian3.magnitude(probeViewer.camera.position));
  await page.mouse.move(640,400);await page.mouse.wheel(0,180);await page.waitForTimeout(500);
  const zoom=await page.evaluate(()=>({distance:Cesium.Cartesian3.magnitude(probeViewer.camera.position),follow:probeState().followVehicle}));
  const preserved=await page.evaluate(()=>{probeSend({type:'cursor',payload:{trackId:0,sampleIndex:400,longitude:116.0012,latitude:39,altitude:0,heading:90}});return Cesium.Cartesian3.magnitude(probeViewer.camera.position);});
  await page.mouse.down();await page.mouse.move(700,430,{steps:6});await page.mouse.up();await page.waitForTimeout(100);
  const drag=await page.evaluate(()=>({follow:probeState().followVehicle,identity:Cesium.Matrix4.equals(probeViewer.camera.transform,Cesium.Matrix4.IDENTITY),inputs:probeViewer.scene.screenSpaceCameraController.enableInputs}));
  const afterPan=await page.evaluate(()=>{const p=probeState().cursorEntity.position.getValue(probeViewer.clock.currentTime),before=Cesium.SceneTransforms.worldToWindowCoordinates(probeViewer.scene,p);probeSend({type:'cursor',payload:{trackId:0,sampleIndex:450,longitude:116.00135,latitude:39,altitude:0,heading:90}});const q=probeState().cursorEntity.position.getValue(probeViewer.clock.currentTime),after=Cesium.SceneTransforms.worldToWindowCoordinates(probeViewer.scene,q);return {before,after,driftPx:Math.hypot(after.x-before.x,after.y-before.y)};});
  const switches=await page.evaluate(()=>{probeSend({type:'followVehicle',enabled:true});probeSend({type:'followVehicle',enabled:false});probeSend({type:'chaseVehicle',enabled:true});const chaseInputs=probeViewer.scene.screenSpaceCameraController.enableInputs;probeSend({type:'chaseVehicle',enabled:false});probeSend({type:'followVehicle',enabled:true});return {chaseInputs,followInputs:probeViewer.scene.screenSpaceCameraController.enableInputs};});
  return {zoomBefore:before,zoomAfter:zoom.distance,followAfterZoom:zoom.follow,zoomPreservedAfterCursor:preserved,drag,afterPan,switches};
}
async function checkOrbit(page) {
  await page.evaluate(()=>{
    const points=Array.from({length:1000},(_,i)=>[116+i*0.000003,39,0,i]);
    probeSend({type:'tracks',payload:{tracks:[{id:0,hasAltitude:true,isVisible:true,color:'#ff3333',points}],altitudeMode:'ground',selectedTrackId:0}});
    probeSend({type:'cursor',payload:{trackId:0,sampleIndex:0,longitude:116,latitude:39,altitude:0,heading:90}});
    const controller=probeViewer.scene.screenSpaceCameraController;
    controller.inertiaZoom=controller.inertiaSpin=controller.inertiaTranslate=0;
    window.orbitInputSnapshot=()=>{const c=probeViewer.scene.screenSpaceCameraController;return JSON.stringify({rotate:c.rotateEventTypes,tilt:c.tiltEventTypes,look:c.lookEventTypes,zoom:c.zoomEventTypes});};
    window.orbitOriginalInputs=orbitInputSnapshot();
    window.orbitSnapshot=()=>{
      const camera=probeViewer.camera,C=Cesium.Cartesian3;
      const target=probeState().cursorEntity.position.getValue(probeViewer.clock.currentTime);
      const origin=Cesium.Matrix4.getTranslation(camera.transform,new C());
      return {follow:probeState().followVehicle,range:C.distance(camera.positionWC,target),originError:C.distance(origin,target),position:C.clone(camera.position),direction:C.clone(camera.direction),up:C.clone(camera.up),screen:Cesium.SceneTransforms.worldToWindowCoordinates(probeViewer.scene,target)};
    };
  });
  const results=[];
  for(const gesture of ['middle','left','shift-left','ctrl-left','ctrl-right']){
    await page.evaluate(remap=>{
      probeSend({type:'followVehicle',enabled:false});
      probeSend({type:'cursor',payload:{trackId:0,sampleIndex:0,longitude:116,latitude:39,altitude:0,heading:90}});
      const target=probeState().cursorEntity.position.getValue(probeViewer.clock.currentTime);
      probeViewer.camera.lookAt(target,new Cesium.HeadingPitchRange(.8,-.5,60));
      probeViewer.camera.lookAtTransform(Cesium.Matrix4.IDENTITY);
      probeViewer.camera.lookRight(.1);
      probeSend({type:'followVehicle',enabled:true});
      if(remap){const c=probeViewer.scene.screenSpaceCameraController,E=Cesium.CameraEventType,K=Cesium.KeyboardEventModifier;c.rotateEventTypes=[E.LEFT_DRAG,E.MIDDLE_DRAG,{eventType:E.LEFT_DRAG,modifier:K.SHIFT},{eventType:E.LEFT_DRAG,modifier:K.CTRL},{eventType:E.RIGHT_DRAG,modifier:K.CTRL}];c.tiltEventTypes=c.lookEventTypes=undefined;}
      probeViewer.scene.requestRender();
    },process.argv.includes('--orbit-remap'));
    await page.waitForTimeout(200);
    const before=await page.evaluate(()=>orbitSnapshot());
    await page.mouse.move(640,400);
    const modifier=gesture==='shift-left'?'Shift':gesture.startsWith('ctrl-')?'Control':null;
    const button=gesture==='middle'?'middle':gesture==='ctrl-right'?'right':'left';
    if(modifier)await page.keyboard.down(modifier);
    await page.mouse.down({button});
    for(let i=1;i<=8;i++){await page.mouse.move(640+i*20,400+i*7.5);await page.waitForTimeout(35);}
    await page.mouse.up({button});
    if(modifier)await page.keyboard.up(modifier);
    await page.waitForTimeout(250);
    const after=await page.evaluate(()=>orbitSnapshot());
    const next=await page.evaluate(()=>{probeSend({type:'cursor',payload:{trackId:0,sampleIndex:300,longitude:116.0009,latitude:39,altitude:0,heading:90}});return orbitSnapshot();});
    const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);
    const angle=(a,b)=>Math.acos(Math.min(1,Math.max(-1,(a.x*b.x+a.y*b.y+a.z*b.z)/(Math.hypot(a.x,a.y,a.z)*Math.hypot(b.x,b.y,b.z)))))*180/Math.PI;
    results.push({gesture,before,after,next,orbitAngleDegrees:angle(before.position,after.position),viewAngleDegrees:angle(before.direction,after.direction),rangeDeltaM:after.range-before.range,gestureScreenDriftPx:Math.hypot(after.screen.x-before.screen.x,after.screen.y-before.screen.y),cursorScreenDriftPx:Math.hypot(next.screen.x-after.screen.x,next.screen.y-after.screen.y),localPositionAfterCursorDeltaM:distance(next.position,after.position),localDirectionAfterCursorDelta:distance(next.direction,after.direction)});
  }
  const clickBefore=await page.evaluate(()=>orbitSnapshot());
  await page.mouse.click(640,400);await page.waitForTimeout(100);
  const clickAfter=await page.evaluate(()=>orbitSnapshot());
  await page.mouse.wheel(0,180);await page.waitForTimeout(300);
  const zoom=await page.evaluate(()=>{const before=orbitSnapshot();probeSend({type:'cursor',payload:{trackId:0,sampleIndex:600,longitude:116.0018,latitude:39,altitude:0,heading:90}});return {before,next:orbitSnapshot()};});
  const release=await page.evaluate(()=>{
    const same=()=>orbitInputSnapshot()===orbitOriginalInputs;
    const before=Cesium.Cartesian3.clone(probeViewer.camera.positionWC);
    probeSend({type:'followVehicle',enabled:false});
    const disabled={restored:same(),worldJumpM:Cesium.Cartesian3.distance(before,probeViewer.camera.positionWC)};
    probeSend({type:'followVehicle',enabled:true});probeSend({type:'chaseVehicle',enabled:true});const chaseRestored=same();
    probeSend({type:'chaseVehicle',enabled:false});probeSend({type:'followVehicle',enabled:true});probeSend({type:'cursor',payload:null});const nullRestored=same();
    probeSend({type:'cursor',payload:{trackId:0,sampleIndex:10,longitude:116.00003,latitude:39,altitude:0,heading:90}});probeSend({type:'followVehicle',enabled:true});probeSend({type:'clear'});const clearRestored=same();
    return {disabled,chaseRestored,nullRestored,clearRestored};
  });
  return {gestures:results,click:{before:clickBefore,after:clickAfter},zoom,release};
}
(async()=>{
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const browser=await chromium.launch({executablePath:process.env.EDGE_EXECUTABLE || path.join(process.env['ProgramFiles(x86)']||process.env.ProgramFiles,'Microsoft','Edge','Application','msedge.exe'),headless:true});
const context=await browser.newContext({viewport:{width:1280,height:800}}), results=[];
await context.route('https://elevation3d.arcgis.com/**',route=>route.abort());
try{
if(orbitOnly){
  const page=await context.newPage(),errors=[];page.on('pageerror',e=>errors.push(String(e)));
  await page.goto(`http://127.0.0.1:${server.address().port}/?version=new`,{waitUntil:'domcontentloaded',timeout:60000});
  await page.waitForFunction(()=>window.probeReady,{timeout:60000});
  const result={sourceSha256:hashes.new,experimentalRemap:process.argv.includes('--orbit-remap'),...await checkOrbit(page),errors};
  fs.writeFileSync(path.join(base,process.argv.includes('--orbit-remap')?'results-orbit-remap.json':'results-orbit.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify(result));await page.close();return;
}
for(const version of versions)for(const mode of (motionMode?['ground']:['ground','recorded']))for(const camera of ['follow','chase'])for(const scenario of (motionMode?['regular','jitter-tiles']:['regular'])){
const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(String(e)));page.on('console',m=>{if(m.type()==='error')errors.push(m.text())});
await page.goto(`http://127.0.0.1:${server.address().port}/?version=${version}`,{waitUntil:'domcontentloaded',timeout:60000});
await page.waitForFunction(()=>window.probeReady,{timeout:60000});
await page.evaluate(({mode,camera})=>{const points=Array.from({length:1000},(_,i)=>[116+i*0.000003,39,0,i]);probeSend({type:'tracks',payload:{tracks:[{id:0,hasAltitude:true,isVisible:true,color:'#ff3333',points}],altitudeMode:mode,selectedTrackId:0}});probeSend({type:'cursor',payload:{trackId:0,sampleIndex:0,longitude:116,latitude:39,altitude:0,heading:90}});if(camera==='follow'){const target=probeState().cursorEntity.position.getValue(probeViewer.clock.currentTime);probeViewer.camera.lookAt(target,new Cesium.HeadingPitchRange(.8,-.5,60));probeViewer.camera.lookAtTransform(Cesium.Matrix4.IDENTITY);probeViewer.camera.lookRight(.1);}
const beforePosition=Cesium.Cartesian3.clone(probeViewer.camera.positionWC),beforeDirection=Cesium.Cartesian3.clone(probeViewer.camera.directionWC),beforeUp=Cesium.Cartesian3.clone(probeViewer.camera.upWC);
probeSend({type:camera==='follow'?'followVehicle':'chaseVehicle',enabled:true});
window.probeActivation={positionJumpM:Cesium.Cartesian3.distance(beforePosition,probeViewer.camera.positionWC),directionChange:Cesium.Cartesian3.distance(beforeDirection,probeViewer.camera.directionWC),upChange:Cesium.Cartesian3.distance(beforeUp,probeViewer.camera.upWC)};
const anchorPosition=probeState().cursorEntity.position.getValue(probeViewer.clock.currentTime);window.probeReferenceScreen=Cesium.SceneTransforms.worldToWindowCoordinates(probeViewer.scene,anchorPosition);
}, {mode,camera});
// Warm the real data-source/worker/camera pipeline with 60 playback ticks.
// Waiting only on an idle requestRenderMode scene is insufficient for this test.
const warmup=await page.evaluate(async()=>{
const start=performance.now();let i=0;await new Promise(resolve=>{const timer=setInterval(()=>{probeSend({type:'cursor',transitionMs:33,payload:{trackId:0,sampleIndex:++i,longitude:116+i*.000003,latitude:39,altitude:0,heading:90}});if(i===60){clearInterval(timer);resolve()}},33)});
const anchor=probeState().cursorEntity,sphere=new Cesium.BoundingSphere(),sphereState=probeViewer.dataSourceDisplay.getBoundingSphere(anchor,false,sphere),pos=anchor.position.getValue(probeViewer.clock.currentTime),screen=Cesium.SceneTransforms.worldToWindowCoordinates(probeViewer.scene,pos);
return {warmupMs:performance.now()-start,dataSourceReady:probeViewer.dataSourceDisplay.ready,sphereState,screen};
});
const result=await page.evaluate(async({motionMode,scenario})=>{
window.probeReset();const t0=performance.now();
let ticks=0;
if(motionMode){await new Promise(resolve=>{const delays=scenario==='regular'?[33]:[23,43,29,37,18,48];const send=()=>{const elapsed=performance.now()-t0,i=60+elapsed/33;ticks++;probeSend({type:'cursor',transitionMs:33,payload:{trackId:0,sampleIndex:i,longitude:116+i*0.000003,latitude:39,altitude:0,heading:90}});if(scenario==='jitter-tiles'&&ticks%4===0)probeViewer.scene.globe.tileLoadProgressEvent.raiseEvent(0);if(elapsed>=8000)resolve();else setTimeout(send,delays[ticks%delays.length]);};setTimeout(send,33);});}
else{await new Promise(resolve=>{const timer=setInterval(()=>{const i=60+(++ticks);probeSend({type:'cursor',transitionMs:33,payload:{trackId:0,sampleIndex:i,longitude:116+i*0.000003,latitude:39,altitude:0,heading:90}});if(ticks>=300){clearInterval(timer);resolve()}},33)});}
const totalMs=performance.now()-t0, c={...probeCounts}; const dt=c.dt.sort((a,b)=>a-b);delete c.dt;const centerErrors=c.centerErrors.sort((a,b)=>a-b);delete c.centerErrors;
const relativeErrors=c.relativeErrors.sort((a,b)=>a-b);delete c.relativeErrors;
const speeds=c.speeds.filter(Number.isFinite); delete c.speeds;
const speedMean=speeds.reduce((a,b)=>a+b,0)/speeds.length;
const speedStd=Math.sqrt(speeds.reduce((a,b)=>a+(b-speedMean)**2,0)/speeds.length);
const sortedSpeeds=[...speeds].sort((a,b)=>a-b);
const gl=probeViewer.scene.context._gl,ext=gl.getExtension('WEBGL_debug_renderer_info');
const anchor=probeState().cursorEntity, sphere=new Cesium.BoundingSphere(), sphereState=probeViewer.dataSourceDisplay.getBoundingSphere(anchor,false,sphere), pos=anchor.position.getValue(probeViewer.clock.currentTime), screen=Cesium.SceneTransforms.worldToWindowCoordinates(probeViewer.scene,pos); return {activation:probeActivation,relativeP95Px:relativeErrors[Math.floor(relativeErrors.length*.95)],relativeMaxPx:relativeErrors.at(-1),speedMean,speedStd,speedCv:speedStd/speedMean,speedP05:sortedSpeeds[Math.floor(speeds.length*.05)],speedP95:sortedSpeeds[Math.floor(speeds.length*.95)],longTasks:probeLongTasks.filter(t=>t.startMs>=t0),tracked:probeViewer.trackedEntity?.id,sphereState,sphere,screen,point:!!anchor.point,centerP95Px:centerErrors[Math.floor(centerErrors.length*.95)],centerMaxPx:centerErrors.at(-1),totalMs,ticks,...c,p50Ms:dt[Math.floor(dt.length*.5)],p95Ms:dt[Math.floor(dt.length*.95)],p99Ms:dt[Math.floor(dt.length*.99)],over50ms:dt.filter(t=>t>50).length,maxMs:dt.at(-1),fps:c.frames/(totalMs/1000),gpu:ext?gl.getParameter(ext.UNMASKED_RENDERER_WEBGL):'unknown',anchorBox:!!probeState().cursorEntity.box};
}, {motionMode,scenario});
await page.screenshot({path:path.join(base,`${version}-${mode}-${camera}-${scenario}.png`)});
const interactions=motionMode?await checkInteractions(page):null;
const lifecycle=await page.evaluate(async()=>{const before=probeViewer.scene.primitives.length;probeSend({type:'cursor',payload:null});const noTarget={entity:!!probeState().cursorEntity,solid:!!probeState().solid,inputs:probeViewer.scene.screenSpaceCameraController.enableInputs};probeSend({type:'cursor',payload:{trackId:0,sampleIndex:10,longitude:116.00003,latitude:39,altitude:0,heading:90}});const scene=probeSceneState();probeSend({type:'tracks',payload:{...scene,tracks:scene.tracks.map(t=>({...t,isVisible:false}))}});await new Promise(r=>setTimeout(r,100));const hidden={entity:!!probeState().cursorEntity,solid:!!probeState().solid,inputs:probeViewer.scene.screenSpaceCameraController.enableInputs};probeSend({type:'clear'});return {before,noTarget,hidden,afterClear:{entity:!!probeState().cursorEntity,solid:!!probeState().solid,inputs:probeViewer.scene.screenSpaceCameraController.enableInputs}}});
results.push({version,sourceSha256:hashes[version],mode,camera,scenario,interactions,warmup,viewport:'1280x800',terrain:'ellipsoid (no live DEM)',imagery:'none',hostLoadUncontrolled:true,...result,lifecycle,errors});fs.writeFileSync(path.join(base,motionMode?(process.argv.includes('--baseline-only')?'results-motion-baseline.json':'results-motion.json'):'results-browser-probe-warmup.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results.at(-1)));await page.close();
}
}finally{await browser.close();server.close()}
})().catch(e=>{console.error(e);server.close();process.exitCode=1});
