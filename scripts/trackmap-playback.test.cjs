const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const test = require('node:test');
const { performance } = require('node:perf_hooks');
const root = path.resolve(__dirname, '..');
const source = fs.readFileSync(path.join(root, 'Assets/TrackMap3D/trackmap.js'), 'utf8');
function extract(name) {
  const start = source.indexOf(`  function ${name}(`);
  assert.ok(start >= 0, name);
  return source.slice(start, source.indexOf('\n  }', start) + 4);
}
function harness() {
  const context = vm.createContext({ performance });
  vm.runInContext(`
    let adds=0, removes=0, renders=0, targets=[], releases=0, primitiveAdds=0, primitiveRemoves=0;
    const primitives = new Set();
    const viewer = { entities: {add:e=>(adds++,e), remove:()=>removes++},
      camera: {position:{x:100,y:-200,z:100},direction:{x:0,y:1,z:0},up:{x:0,y:0,z:1},right:{x:1,y:0,z:0},lookAt:(target,offset)=>targets.push({target,offset}), lookAtTransform:(target,offset)=>{releases++;viewer.camera.transform=target;if(offset)viewer.camera.position=offset}, cancelFlight(){}},
      scene: {canvas:{clientHeight:800},primitives:{add:p=>(primitiveAdds++,primitives.add(p),p),remove:p=>(primitiveRemoves++,primitives.delete(p))},requestRender:()=>renders++, screenSpaceCameraController:{enableInputs:true,rotateEventTypes:'freeRotate',tiltEventTypes:'freeTilt',lookEventTypes:'freeLook'}, globe:{getHeight:()=>100}} };
    viewer.camera.frustum={fovy:Math.PI/3};
    viewer.camera.moveRight=amount=>viewer.camera.position.x+=amount;
    viewer.camera.moveUp=amount=>viewer.camera.position.z+=amount;
    let cursorPoint, cursorEntity, cursorSolid, cursorOutline, renderedCursorPose, cursorAnimation, compareCursorPoints=[], followVehicle=false, followCameraActive=false, followCameraInputs, followPanStart, followPanLast, followPanDragged=false, chaseVehicle=false, chasePose;
    const compareCursorEntities=new Map();
    const sceneState={altitudeMode:'recorded',tracks:[{id:0,hasAltitude:true,
      points:Array.from({length:20000},(_,i)=>[i/100000,0,100+i/1000,i])}]};
    const cursorBoxLengthM=5,cursorBoxWidthM=2,cursorBoxHeightM=1.5,cursorBoxHalfHeightM=.75;
    const recordedTrackLiftMeters=10,groundTrackLiftMeters=0;
    const color={withAlpha:()=>color};
    const Cesium={CameraEventType:{LEFT_DRAG:0,MIDDLE_DRAG:1,RIGHT_DRAG:2},KeyboardEventModifier:{CTRL:0,SHIFT:1},
      Cartesian2:{clone:(p,r)=>Object.assign(r||{},p),distance:(a,b)=>Math.hypot(a.x-b.x,a.y-b.y)},
      Primitive:class{constructor(options){Object.assign(this,options)}},
      GeometryInstance:class{constructor(options){Object.assign(this,options)}},
      BoxGeometry:{fromDimensions:o=>o},BoxOutlineGeometry:{fromDimensions:o=>o},
      ColorGeometryInstanceAttribute:{fromColor:c=>c},
      PerInstanceColorAppearance:class{constructor(o){Object.assign(this,o)}},Color:{GOLD:color,BLACK:color,TRANSPARENT:{alpha:0}},HeightReference:{NONE:0,RELATIVE_TO_GROUND:1},Matrix4:{IDENTITY:{},fromTranslationQuaternionRotationScale:(position,orientation)=>({position,orientation})},
      Quaternion:class{static slerp(a,b,t){return {t}}},
      Cartesian3:class{constructor(x,y,z){Object.assign(this,{x,y,z})} static clone(p,result){return Object.assign(result||{},p)} static cross(a,b,r){return Object.assign(r,{x:1,y:0,z:0})} static dot(a,b){return a.x*b.x+a.y*b.y+a.z*b.z} static distance(a,b){return Math.abs(a.longitude-b.longitude)*111320}
        static lerp(a,b,t){return {longitude:a.longitude+(b.longitude-a.longitude)*t,latitude:a.latitude+(b.latitude-a.latitude)*t,height:a.height+(b.height-a.height)*t}}
        static fromDegrees(longitude,latitude,height){return {longitude,latitude,height}}},
      Cartographic:{fromDegrees:(longitude,latitude)=>({longitude,latitude})},Math:{toRadians:x=>x*Math.PI/180},
      HeadingPitchRoll:class{},HeadingPitchRange:class{constructor(heading,pitch,range){Object.assign(this,{heading,pitch,range})}},
      Transforms:{headingPitchRollQuaternion:()=>({}),eastNorthUpToFixedFrame:position=>({position})}};
    ${['isGroundMode','trackUsesGroundHeights','displayHeight','trackDisplayHeightNear','cursorTrack',
      'cursorPitch','cursorPosition','cursorOrientation','applyCursorBox','clearCompareCursorEntities',
      'absoluteCursorPosition','createVehiclePrimitive','clearVehicleCursor','applyVehiclePose','advanceCursorAnimation','setCursor','setCursors','releaseFollowCamera','configureFollowInputs','startFollowPan','moveFollowPan','endFollowPan','updateFollowCamera','setFollowVehicle','releaseChaseCamera','updateChaseCamera','setChaseVehicle'].map(extract).join('\n')}
    function point(i,heading=90){return {trackId:0,sampleIndex:i,longitude:i/100000,latitude:0,altitude:100,heading};}
  `, context);
  return code => vm.runInContext(code, context);
}
test('300 playback ticks reuse one vehicle and issue one render per tick', () => {
  // Check the real WPF entry point does not send the destructive empty comparison message.
  const panel=fs.readFileSync(path.join(root,'Controls/TrackMapPanel.xaml.cs'),'utf8');
  const entry=panel.slice(panel.indexOf('public void SetCursorSample('),panel.indexOf('private void FollowVehicleChanged('));
  assert.doesNotMatch(entry,/SetCompareCursors/);
  const run=harness();
  const elapsed=run('const start=performance.now(); for(let i=0;i<300;i++) setCursor(point(i)); performance.now()-start');
  assert.equal(run('adds'),1); assert.equal(run('removes'),0); assert.equal(run('renders'),300);
  console.log('300 ticks / 20,000 points (mock Cesium CPU, not FPS):',elapsed.toFixed(2),'ms');
  assert.equal(run('cursorEntity.position.longitude'),299/100000);
  assert.equal(run('primitiveAdds'),2); assert.equal(run('primitiveRemoves'),0);
  assert.equal(run('cursorEntity.box'),undefined);
  assert.equal(run('cursorEntity.point.color.alpha'),0);
  assert.notEqual(run('cursorEntity.point.show'),false);
  assert.equal(run('cursorSolid.asynchronous'),false);
  assert.equal(run('cursorSolid.appearance.translucent'),false);
  assert.equal(run('cursorSolid.modelMatrix === cursorOutline.modelMatrix'),true);
  assert.equal(run('cursorSolid.modelMatrix.position === cursorEntity.position'),true);
  run('setCursor(null)');
  assert.equal(run('primitives.size'),0);
  run('setCursor(point(1))');
  assert.equal(run('primitives.size'),2);
});
test('indexed heights remain bounded at ends and on backwards seeks', () => {
  const run=harness();
  for(const i of [0,1,10000,19999,20]) {
    assert.ok(run(`cursorTrack(point(${i}),sceneState.tracks[0]).points.length`)<=5);
    assert.ok(Math.abs(run(`cursorPosition(point(${i})).height`)-(110+i/1000+.75))<.001);
  }
});
test('chase follows absolute seek pose, wraps heading, tracks ground and releases controls', () => {
  const run=harness();
  run('setCursor(point(10,359)); setChaseVehicle(true)');
  assert.equal(run('viewer.scene.screenSpaceCameraController.enableInputs'),false);
  for(const [i,h] of [[100,1],[17000,90],[5,270]]) {
    run(`setCursor(point(${i},${h}))`);
    assert.equal(run('targets.at(-1).target.longitude'),i/100000);
    assert.ok(Math.abs(run('targets.at(-1).offset.heading')-h*Math.PI/180)<1e-10);
  }
  run("sceneState.altitudeMode='ground';setCursor(point(5))");
  assert.equal(run('targets.at(-1).target.height'),100.75);
  run('setChaseVehicle(false)');
  assert.equal(run('viewer.scene.screenSpaceCameraController.enableInputs'),true);
  assert.equal(run('chasePose'),undefined);
  run('setChaseVehicle(true);setCursor(null)');
  assert.equal(run('viewer.scene.screenSpaceCameraController.enableInputs'),true);
  run('setCursor(point(4))');
  assert.equal(run('viewer.scene.screenSpaceCameraController.enableInputs'),false);
  run('setCursors([])');
  assert.equal(run('viewer.scene.screenSpaceCameraController.enableInputs'),true);
});

test('playback interpolates car and chase together; seek cancels interpolation immediately', () => {
  const run=harness();
  run('setCursor(point(10,359));setChaseVehicle(true);setCursor(point(12,1),33);advanceCursorAnimation(cursorAnimation.samples[1].time+cursorAnimation.delay/2)');
  assert.equal(run('renderedCursorPose.position.longitude'),11/100000);
  assert.equal(run('renderedCursorPose.point.heading'),360);
  assert.equal(run('chasePose.position === renderedCursorPose.position'),true);
  assert.equal(run('cursorSolid.modelMatrix.position === chasePose.position'),true);
  run('setCursor(point(19000,180),0)');
  assert.equal(run('cursorAnimation'),undefined);
  assert.equal(run('renderedCursorPose.position.longitude'),19000/100000);
  run('setCursor(point(19001,180),33);advanceCursorAnimation(cursorAnimation.samples.at(-1).time+cursorAnimation.delay+1)');
  assert.equal(run('cursorAnimation'),undefined);
  assert.equal(run('renderedCursorPose.position.longitude'),19001/100000);
});

test('Follow preserves entry view, updates the reference frame with the rendered pose and retains user zoom', () => {
  const run=harness();
  run('setCursor(point(1));setFollowVehicle(true);assertEntry=viewer.camera.position.y;setCursor(point(3),33);advanceCursorAnimation(cursorAnimation.samples[1].time+cursorAnimation.delay/2)');
  assert.equal(run('assertEntry'),-200);
  assert.equal(run('viewer.trackedEntity'),undefined);
  assert.equal(run('viewer.camera.transform.position === renderedCursorPose.position'),true);
  run('viewer.camera.position={x:2,y:-40,z:30};setCursor(point(4))');
  assert.equal(run('viewer.camera.position.y'),-40);
  assert.equal(run('viewer.camera.transform.position === cursorSolid.modelMatrix.position'),true);
  run('setFollowVehicle(false)');
  assert.equal(run('followCameraActive'),false);
  assert.equal(run('viewer.camera.transform === Cesium.Matrix4.IDENTITY'),true);
  run('setFollowVehicle(true);setCursor(null)');
  assert.equal(run('followCameraActive'),false);
});

test('timestamp buffer remains linear across irregular host delivery gaps', () => {
  const run=harness();
  // A linear input at 1 sample per ms; irregular delivery is buffered, not eased again.
  run(`setCursor(point(0)); cursorAnimation = {delay:66,samples:[
    {time:0,pose:{point:point(0),position:Cesium.Cartesian3.fromDegrees(0,0,100),orientation:{}}},
    {time:23,pose:{point:point(23),position:Cesium.Cartesian3.fromDegrees(23,0,100),orientation:{}}},
    {time:66,pose:{point:point(66),position:Cesium.Cartesian3.fromDegrees(66,0,100),orientation:{}}},
    {time:95,pose:{point:point(95),position:Cesium.Cartesian3.fromDegrees(95,0,100),orientation:{}}}
  ]}`);
  for(const wall of [76,92,108,124,140]) {
    run(`advanceCursorAnimation(${wall})`);
    assert.ok(Math.abs(run('renderedCursorPose.position.longitude')-(wall-66))<1e-9);
  }
  run('setCursor(point(99),0)');
  assert.equal(run('cursorAnimation'),undefined);
});

test('Follow separates click from pan, retains the lock and restores free camera gestures', () => {
  const run=harness();
  run('setCursor(point(1));setFollowVehicle(true);startFollowPan({position:{x:100,y:100}});moveFollowPan({endPosition:{x:102,y:101}})');
  assert.equal(run('followPanDragged'),false);
  assert.equal(run('viewer.camera.position.x'),100);
  run('moveFollowPan({endPosition:{x:140,y:120}});endFollowPan()');
  assert.equal(run('followPanDragged'),true);
  assert.equal(run('followVehicle'),true);
  assert.ok(run('viewer.camera.position.x')<100);
  assert.ok(run('viewer.camera.position.z')>100);
  assert.equal(run('viewer.scene.screenSpaceCameraController.tiltEventTypes'),undefined);
  assert.equal(run('viewer.scene.screenSpaceCameraController.lookEventTypes'),undefined);
  assert.equal(run('viewer.scene.screenSpaceCameraController.rotateEventTypes[0]'),1);
  run('setFollowVehicle(false)');
  assert.equal(run('viewer.scene.screenSpaceCameraController.rotateEventTypes'),'freeRotate');
  assert.equal(run('viewer.scene.screenSpaceCameraController.tiltEventTypes'),'freeTilt');
  assert.equal(run('viewer.scene.screenSpaceCameraController.lookEventTypes'),'freeLook');
  assert.equal(run('followPanStart'),undefined);
  run('setFollowVehicle(true);setCursor(null)');
  assert.equal(run('viewer.scene.screenSpaceCameraController.rotateEventTypes'),'freeRotate');
});
