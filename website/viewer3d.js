import * as THREE from 'three';
import { FBXLoader } from 'three/addons/loaders/FBXLoader.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';
import { Sky } from 'three/addons/objects/Sky.js';
import {
  createNintendoGrassTexture,
  createResortWaterTexture,
  createStylizedCloud,
  createGlowSpriteTexture,
  addGrandAceDomeShell,
  createFloatingBalloonsGroup,
  createFestiveFireworksSystem,
  createSwayingGrassSystem,
  createResortPathwaysSystem
} from './procedural-assets.js';
import { ModelInspectModalViewer } from './model-inspector.js';

/* =========================================================================
   TennisIslandCinematicViewer: 메가 테니스 아일랜드 & 3대 경기장 시네마틱 뷰어
========================================================================= */
class TennisIslandCinematicViewer {
  constructor(canvasContainerId) {
    this.container = document.getElementById(canvasContainerId);
    if (!this.container) return;

    this.clock = new THREE.Clock();
    this.textureLoader = new THREE.TextureLoader();

    this.scene = null;
    this.camera = null;
    this.renderer = null;

    // Groups
    this.islandGroup = new THREE.Group();
    this.leftStadiumGroup = new THREE.Group();
    this.topRightStadiumGroup = new THREE.Group();
    this.bottomRightStadiumGroup = new THREE.Group();

    // 3개 경기장 각각의 중심 좌표 (테니스, 볼링, 검술)
    this.tennisStadiumCenter = new THREE.Vector3(-160, 1.2, 0);
    this.bowlingStadiumCenter = new THREE.Vector3(130, 1.2, -150);
    this.swordStadiumCenter = new THREE.Vector3(130, 1.2, 150);
    this.leftStadiumCenter = this.tennisStadiumCenter;

    // 3개 경기장 전체가 여유롭게 프레임에 안착하는 와이드 시네마틱 앵글
    this.skyCamPos = new THREE.Vector3(25, 205, 435);
    this.skyLookAt = new THREE.Vector3(-30, 20, -10);

    this.currentCamPos = new THREE.Vector3().copy(this.skyCamPos);
    this.targetCamPos = new THREE.Vector3().copy(this.skyCamPos);
    this.currentLookAt = new THREE.Vector3().copy(this.skyLookAt);
    this.targetLookAt = new THREE.Vector3().copy(this.skyLookAt);

    this.scrollProgress = 0;
    this.particles = null;
    this.balloonsSystem = null;
    this.fireworksSystem = null;

    // Time of Day (시간대) 상태 관리
    this.currentTod = 'day'; // 'day' | 'sunset' | 'night'
    this.hemiLight = null;
    this.sunLight = null;
    this.fillLight = null;
    this.skyCanvas = null;
    this.skyCtx = null;
    this.skyTexture = null;

    // 경기장 투광 조명 및 은은한 렌즈 글로우 스프라이트 리스트
    this.stadiumFloodLights = [];
    this.stadiumGlowSprites = [];
    this.stadiumCrownGlowSprites = [];
    this.stadiumCoreLights = [];
    this.stadiumBulbMaterial = null;
    this.stadiumLightMaterials = [];
    this.resortPathways = null;

    this.initScene();
    this.initEmotionalSky();
    this.initVastOceanAndMegaIsland();
    this.initAtmosphereParticles();
    this.initLights();
    this.loadThreeStadiums();
    this.setTimeOfDay('day');
    this.initEventListeners();

    this.animate = this.animate.bind(this);
    requestAnimationFrame(this.animate);
  }

  initScene() {
    this.scene = new THREE.Scene();
    this.scene.add(this.islandGroup);

    // 닌텐도 스위치 스포츠 감성의 청량하고 부드러운 파스텔 원경 안개
    this.scene.fog = new THREE.Fog(0x9bdcff, 600, 4200);

    const width = window.innerWidth;
    const height = window.innerHeight;

    this.camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 5000);
    this.camera.position.copy(this.skyCamPos);
    this.camera.lookAt(this.skyLookAt);

    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
    this.renderer.setSize(width, height);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.28;

    this.container.innerHTML = '';
    this.container.appendChild(this.renderer.domElement);

    // [그래픽 업그레이드] 텍스처 sRGB 색공간 보정 + 이방성 필터링
    const maxAniso = this.renderer.capabilities.getMaxAnisotropy();
    const rawLoad = this.textureLoader.load.bind(this.textureLoader);
    this.textureLoader.load = (url, onLoad, onProgress, onError) => {
      const tex = rawLoad(url, onLoad, onProgress, onError);
      if (!/normal/i.test(String(url))) tex.colorSpace = THREE.SRGBColorSpace;
      tex.anisotropy = maxAniso;
      return tex;
    };

    // [그래픽 업그레이드] 후처리: MSAA 4x + HDR 블룸 + 톤매핑/sRGB 출력
    const rt = new THREE.WebGLRenderTarget(width, height, {
      type: THREE.HalfFloatType,
      samples: 4
    });
    this.composer = new EffectComposer(this.renderer, rt);
    this.composer.setPixelRatio(this.renderer.getPixelRatio());
    this.composer.setSize(width, height);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    this.bloomPass = new UnrealBloomPass(new THREE.Vector2(width, height), 0.3, 0.7, 1.0);
    this.composer.addPass(this.bloomPass);

    // [그래픽 업그레이드] 시간대별 컬러 그레이딩 (섀도 틴트 / 채도 / 대비 / 비네트)
    this.gradePass = new ShaderPass({
      uniforms: {
        tDiffuse: { value: null },
        tint: { value: new THREE.Vector3(1, 1, 1) },
        shadowTint: { value: new THREE.Vector3(0, 0, 0) },
        sat: { value: 1.0 },
        contrast: { value: 1.0 },
        vignette: { value: 0.0 }
      },
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
      fragmentShader: `
        uniform sampler2D tDiffuse;
        uniform vec3 tint;
        uniform vec3 shadowTint;
        uniform float sat;
        uniform float contrast;
        uniform float vignette;
        varying vec2 vUv;
        void main() {
          vec3 c = texture2D(tDiffuse, vUv).rgb;
          float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
          c = mix(vec3(l), c, sat);
          c += shadowTint * (1.0 - smoothstep(0.0, 0.7, l));
          c *= tint;
          c = max((c - 0.18) * contrast + 0.18, 0.0);
          float d = length(vUv - 0.5);
          c *= 1.0 - vignette * smoothstep(0.30, 0.85, d);
          gl_FragColor = vec4(c, 1.0);
        }`
    });
    this.composer.addPass(this.gradePass);
    this.gradeTargets = {
      day:    { tint: [1.0, 1.0, 1.0],    shadow: [0, 0, 0],          sat: 1.0,  contrast: 1.0,  vig: 0.0 },
      sunset: { tint: [1.0, 1.0, 1.0],    shadow: [0, 0, 0],          sat: 1.0,  contrast: 1.0,  vig: 0.0 },
      night:  { tint: [0.92, 1.02, 1.14], shadow: [0.0, 0.02, 0.06],  sat: 1.08, contrast: 1.08, vig: 0.42 }
    };
    this.composer.addPass(new OutputPass());

    // 시간대별 블룸 세기 (낮은 은은하게, 밤은 조명/폭죽이 화사하게)
    this.bloomTargets = { day: 0.28, sunset: 0.45, night: 0.95 };
    this.bloomThresholds = { day: 1.0, sunset: 1.0, night: 1.0 };
  }

  /* 닌텐도 스위치 스포츠 감성 + 골든 아워 샴페인 틴트 시네마틱 스카이 */
  initEmotionalSky() {
    const canvas = document.createElement('canvas');
    canvas.width = 512;
    canvas.height = 512;
    const ctx = canvas.getContext('2d');

    const grad = ctx.createLinearGradient(0, 0, 0, 512);
    grad.addColorStop(0, '#004ea8');    // Deep Vivid Nintendo Cobalt
    grad.addColorStop(0.32, '#0284c7'); // Mediterranean Azure
    grad.addColorStop(0.65, '#38bdf8'); // Sparkling Tropical Cyan
    grad.addColorStop(0.86, '#bae6fd'); // Soft Pastel Horizon
    grad.addColorStop(0.94, '#fed7aa'); // Warm Golden Sunset Tint
    grad.addColorStop(1, '#ffffff');    // Brilliant Horizon

    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, 512, 512);

    this.skyCanvas = canvas;
    this.skyCtx = ctx;
    this.skyTexture = new THREE.CanvasTexture(canvas);
    // [그래픽 업그레이드] 대기 산란(Preetham) 기반 절차적 하늘 + 별밭
    this.scene.background = null;
    this.sky = new Sky();
    this.sky.scale.setScalar(4200);
    this.scene.add(this.sky);

    // 반사/IBL용 환경맵(PMREM): 동일 파라미터의 별도 Sky를 캡처
    this.pmrem = new THREE.PMREMGenerator(this.renderer);
    this.skyEnvScene = new THREE.Scene();
    this.skyEnv = new Sky();
    this.skyEnv.scale.setScalar(4200);
    this.skyEnvScene.add(this.skyEnv);
    this.envRT = null;
    this.sunDir = new THREE.Vector3();

    // 밤하늘 별
    const starCount = 3500;
    const starPos = new Float32Array(starCount * 3);
    for (let i = 0; i < starCount; i++) {
      const u = Math.random(), v = Math.random() * 0.92 + 0.04;
      const th = u * Math.PI * 2, ph = Math.acos(v);
      const r = 3600;
      starPos[i * 3] = r * Math.sin(ph) * Math.cos(th);
      starPos[i * 3 + 1] = r * Math.cos(ph);
      starPos[i * 3 + 2] = r * Math.sin(ph) * Math.sin(th);
    }
    const starGeo = new THREE.BufferGeometry();
    starGeo.setAttribute('position', new THREE.BufferAttribute(starPos, 3));
    this.starMat = new THREE.PointsMaterial({
      color: 0xffffff, size: 2.6, sizeAttenuation: false,
      transparent: true, opacity: 0, depthWrite: false, fog: false
    });
    this.stars = new THREE.Points(starGeo, this.starMat);
    this.scene.add(this.stars);

    // 푹신푹신한 볼륨감의 닌텐도 카툰 솜사탕 구름 클러스터
    this.cloudsGroup = new THREE.Group();
    const cloudCount = 0; // 인공적인 로우폴리 구름 제거

    for (let i = 0; i < cloudCount; i++) {
      const cloud = createStylizedCloud();
      const angle = (i / cloudCount) * Math.PI * 2 + (Math.random() - 0.5) * 0.2;
      const radius = 560 + Math.random() * 260;
      const height = 110 + Math.random() * 85;

      cloud.position.set(
        Math.cos(angle) * radius,
        height,
        Math.sin(angle) * radius
      );
      const scale = 2.2 + Math.random() * 1.6;
      cloud.scale.set(scale, scale * 0.75, scale * 1.1);
      this.cloudsGroup.add(cloud);
    }
    this.scene.add(this.cloudsGroup);
  }

  /* [그래픽 업그레이드] 하늘 환경맵을 바다/섬/경기장 재질에 은은한 반사광으로 적용 */
  applyEnvironmentDetail() {
    if (!this.envRT) return;
    const env = this.envRT.texture;
    if (this.waterMesh) {
      this.waterMesh.material.envMap = env;
      this.waterMesh.material.envMapIntensity = this.currentTod === 'day' ? 0.1 : 0.3;
      this.waterMesh.material.needsUpdate = true;
    }
    this.islandGroup.traverse((o) => {
      if (!o.isMesh || !o.material) return;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      mats.forEach((m) => {
        if (m.isMeshStandardMaterial || m.isMeshPhysicalMaterial) {
          m.envMap = env;
          m.envMapIntensity = 0.35;
          m.needsUpdate = true;
        } else if (m.isMeshPhongMaterial || m.isMeshLambertMaterial) {
          if (m.isMeshPhongMaterial) {
            m.envMap = env;
            m.reflectivity = 0.06;
            m.needsUpdate = true;
          }
        }
      });
    });
  }

  /* 타일링되는 절차적 물결 노말맵 생성 */
  createWaterNormalMap() {
    const N = 256;
    const h = new Float32Array(N * N);
    const waves = [
      [3, 2, 1.0, 0.0], [5, 7, 0.6, 1.3], [9, 4, 0.35, 2.1],
      [12, 11, 0.2, 0.7], [2, 9, 0.5, 3.4], [17, 6, 0.12, 4.2]
    ];
    for (let y = 0; y < N; y++) {
      for (let x = 0; x < N; x++) {
        let v = 0;
        for (const [fx, fy, a, p] of waves) {
          v += a * Math.sin((x / N) * Math.PI * 2 * fx + (y / N) * Math.PI * 2 * fy + p);
        }
        h[y * N + x] = v;
      }
    }
    const canvas = document.createElement('canvas');
    canvas.width = N;
    canvas.height = N;
    const ctx = canvas.getContext('2d');
    const img = ctx.createImageData(N, N);
    const k = 6.0;
    for (let y = 0; y < N; y++) {
      for (let x = 0; x < N; x++) {
        const dx = h[y * N + ((x + 1) % N)] - h[y * N + ((x - 1 + N) % N)];
        const dy = h[((y + 1) % N) * N + x] - h[((y - 1 + N) % N) * N + x];
        let nx = -dx * k / N * 10, ny = -dy * k / N * 10, nz = 1;
        const len = Math.hypot(nx, ny, nz);
        nx /= len; ny /= len; nz /= len;
        const i = (y * N + x) * 4;
        img.data[i] = (nx * 0.5 + 0.5) * 255;
        img.data[i + 1] = (ny * 0.5 + 0.5) * 255;
        img.data[i + 2] = (nz * 0.5 + 0.5) * 255;
        img.data[i + 3] = 255;
      }
    }
    ctx.putImageData(img, 0, 0);
    const tex = new THREE.CanvasTexture(canvas);
    tex.wrapS = THREE.RepeatWrapping;
    tex.wrapT = THREE.RepeatWrapping;
    tex.anisotropy = 8;
    return tex;
  }

  /* 화사하고 생생한 닌텐도 리조트 메가 아일랜드 & 맑은 바다 & 산호초 군도 */
  initVastOceanAndMegaIsland() {
    // 1. 에메랄드빛 카툰 수면 텍스처 바다
    const oceanGeo = new THREE.PlaneGeometry(10000, 10000, 64, 64);
    const waterTex = createResortWaterTexture();
    // [그래픽 업그레이드] 절차적 노말맵 + 하늘 반사 + 클리어코트의 물리 기반 바다
    const waterNormal = this.createWaterNormalMap();
    waterNormal.repeat.copy(waterTex.repeat);
    waterNormal.repeat.multiplyScalar(2);
    const oceanMat = new THREE.MeshPhysicalMaterial({
      color: 0x0284c7,
      map: waterTex,
      normalMap: waterNormal,
      normalScale: new THREE.Vector2(0.55, 0.55),
      roughness: 0.1,
      metalness: 0.0,
      clearcoat: 0.35,
      clearcoatRoughness: 0.05,
      envMapIntensity: 0.3,
      transparent: true,
      opacity: 0.95
    });
    this.waterNormalMap = waterNormal;
    this.waterMesh = new THREE.Mesh(oceanGeo, oceanMat);
    this.waterMesh.rotation.x = -Math.PI / 2;
    this.waterMesh.position.y = -0.8;
    this.scene.add(this.waterMesh);

    // 유기적인 곡선 섬 외곽 형태를 생성하는 헬퍼
    const createOrganicShape = (baseRadius, noiseScale = 0.12, pointsCount = 120, seed = 0) => {
      const shape = new THREE.Shape();
      for (let i = 0; i <= pointsCount; i++) {
        const theta = (i / pointsCount) * Math.PI * 2;
        const wave = 
          Math.sin(theta * 3 + seed) * 0.18 +
          Math.cos(theta * 5 + seed * 1.5) * 0.10 +
          Math.sin(theta * 7 - seed) * 0.05;
        const r = baseRadius * (1 + wave * noiseScale);
        const x = Math.cos(theta) * r;
        const y = Math.sin(theta) * r;
        if (i === 0) shape.moveTo(x, y);
        else shape.lineTo(x, y);
      }
      return shape;
    };

    // 2. 햇살 가득한 황금빛 모래사장 비치 (잔디보다 확실히 아래 y: -0.55로 배치하여 번쩍임 완벽 제거)
    const sandShape = createOrganicShape(415, 0.18, 140, 2);
    const sandGeo = new THREE.ShapeGeometry(sandShape);
    const sandTex = this.textureLoader.load('assets/textures/sand_diffuse.jpg');
    sandTex.wrapS = THREE.RepeatWrapping;
    sandTex.wrapT = THREE.RepeatWrapping;
    sandTex.repeat.set(32, 32);
    const sandMat = new THREE.MeshStandardMaterial({
      map: sandTex,
      bumpMap: sandTex,
      bumpScale: 2.2,
      roughness: 0.92,
      metalness: 0.0
    });
    const sandMesh = new THREE.Mesh(sandGeo, sandMat);
    sandMesh.rotation.x = -Math.PI / 2;
    sandMesh.position.y = -0.55;
    sandMesh.receiveShadow = true;
    this.islandGroup.add(sandMesh);

    // 3. 싱그러운 메인 잔디 평원 (산뜻하고 싱그러운 리조트 잔디 그린 & 밑바닥 겹침/번쩍임 완전 차단)
    const grassShape = createOrganicShape(390, 0.15, 140, 3);
    const grassGeo = new THREE.ShapeGeometry(grassShape);
    const grassTex = this.textureLoader.load('assets/textures/grass_diffuse.jpg');
    grassTex.wrapS = THREE.RepeatWrapping;
    grassTex.wrapT = THREE.RepeatWrapping;
    grassTex.repeat.set(22, 22);
    const grassNormal = this.textureLoader.load('assets/textures/grass_normal.jpg');
    grassNormal.wrapS = THREE.RepeatWrapping;
    grassNormal.wrapT = THREE.RepeatWrapping;
    grassNormal.repeat.set(22, 22);
    const grassMat = new THREE.MeshStandardMaterial({
      color: 0x62dc3e, // 쨍한 여름 잔디 그린
      map: grassTex,
      normalMap: grassNormal,
      normalScale: new THREE.Vector2(0.8, 0.8),
      roughness: 0.78,
      metalness: 0.0
    });
    const grassMesh = new THREE.Mesh(grassGeo, grassMat);
    grassMesh.rotation.x = -Math.PI / 2;
    grassMesh.position.y = 0.0;
    grassMesh.receiveShadow = true;
    this.islandGroup.add(grassMesh);

    // 3.5 주황색 리조트 연결 도로 & 가로등 시스템 (경기장 간 연결 길목 & 가로수)
    this.resortPathways = createResortPathwaysSystem();
    this.islandGroup.add(this.resortPathways.group);

    // 4. 바람에 살랑살랑 흔들리는 조그마한 3D 잔디 풀밭 (도로 위 침범 방지)
    const stadiumAreas = [
      { x: -160, z: 0, radius: 96 },
      { x: 130, z: -150, radius: 94 },
      { x: 130, z: 150, radius: 94 }
    ];
    this.swayingGrassSystem = createSwayingGrassSystem(
      stadiumAreas,
      (x, z) => this.resortPathways.isPointOnRoad(x, z, 2.0)
    );
    this.islandGroup.add(this.swayingGrassSystem.mesh);

    // 5. 주변을 둘러싼 아기자기한 열대 부속 섬 5개 (모래 -> 잔디)
    const subIslands = [
      { x: -440, z: -260, r: 80 },
      { x: -390, z: 310, r: 95 },
      { x: 380, z: -320, r: 105 },
      { x: 430, z: 270, r: 85 },
      { x: 20, z: 460, r: 75 },
    ];

    subIslands.forEach((isle, idx) => {
      const subSand = new THREE.Mesh(
        new THREE.ShapeGeometry(createOrganicShape(isle.r * 1.15, 0.12, 60, idx + 4)),
        sandMat
      );
      subSand.rotation.x = -Math.PI / 2;
      subSand.position.set(isle.x, -0.4, isle.z);
      this.islandGroup.add(subSand);

      const subGrass = new THREE.Mesh(
        new THREE.ShapeGeometry(createOrganicShape(isle.r, 0.1, 60, idx + 6)),
        grassMat
      );
      subGrass.rotation.x = -Math.PI / 2;
      subGrass.position.set(isle.x, 0.0, isle.z);
      subGrass.receiveShadow = true;
      this.islandGroup.add(subGrass);
    });

    // 6. 리얼 3D 열대 야자수 가로수 및 숲 모델링 (GLB 3D 에셋)
    const gltfLoader = new GLTFLoader();
    gltfLoader.load('assets/models/trees/palm_tree.glb', (gltf) => {
      const palmTemplate = gltf.scene;
      palmTemplate.traverse((child) => {
        if (child.isMesh) {
          child.castShadow = true;
          child.receiveShadow = true;
          if (child.material) {
            child.material.roughness = 0.55;
            child.material.side = THREE.DoubleSide;
          }
        }
      });

      // 6-A. 가는 길목 주변에 가로수 야자수 정렬 배치 ("길주변에는 나무가 있어")
      if (this.resortPathways && this.resortPathways.roadsideTreePositions) {
        this.resortPathways.roadsideTreePositions.forEach((pos) => {
          // 경기장 돔과의 겹침 방지
          const distToLeft = Math.hypot(pos.x - (-160), pos.z);
          const distToTopRight = Math.hypot(pos.x - 130, pos.z - (-150));
          const distToBottomRight = Math.hypot(pos.x - 130, pos.z - 150);
          if (distToLeft < 86 || distToTopRight < 84 || distToBottomRight < 84) return;

          const treeInstance = palmTemplate.clone(true);
          const scale = 2.4 + Math.random() * 1.1; // 아늑한 휴양지 가로수 크기
          treeInstance.scale.set(scale, scale * (0.9 + Math.random() * 0.25), scale);
          treeInstance.position.set(pos.x, 0.0, pos.z);
          treeInstance.rotation.y = Math.random() * Math.PI * 2;
          treeInstance.rotation.z = (Math.random() - 0.5) * 0.10;

          this.islandGroup.add(treeInstance);
        });
      }

      // 6-B. 섬 전역의 자연스러운 야자수 숲 (도로 위에는 절대 심지 않음 - "길에는 나무가 없고")
      for (let i = 0; i < 95; i++) {
        const angle = Math.random() * Math.PI * 2;
        const r = 180 + Math.random() * 195;
        const tx = Math.cos(angle) * r;
        const tz = Math.sin(angle) * r;

        // 돔 스타디움과의 충돌 방지
        const distToLeft = Math.hypot(tx - (-160), tz);
        const distToTopRight = Math.hypot(tx - 130, tz - (-150));
        const distToBottomRight = Math.hypot(tx - 130, tz - 150);
        if (distToLeft < 98 || distToTopRight < 95 || distToBottomRight < 95) continue;

        // 도로와의 충돌 방지 (길 위 생성 차단)
        if (this.resortPathways && this.resortPathways.isPointOnRoad(tx, tz, 8.5)) continue;

        const treeInstance = palmTemplate.clone(true);
        const scale = 2.6 + Math.random() * 1.6; // 높이 13 ~ 21 유닛의 자연스러운 야자수
        treeInstance.scale.set(scale, scale * (0.9 + Math.random() * 0.25), scale);
        treeInstance.position.set(tx, 0.0, tz);
        treeInstance.rotation.y = Math.random() * Math.PI * 2;
        treeInstance.rotation.z = (Math.random() - 0.5) * 0.12;

        this.islandGroup.add(treeInstance);
      }
    }, undefined, (err) => {
      console.error('Failed to load palm_tree.glb:', err);
    });

    // 7. 먼 바다의 귀여운 리조트 세일보트 (요트)
    this.boatsGroup = new THREE.Group();
    const boatHullGeo = new THREE.BoxGeometry(7, 3, 14);
    const boatHullMat = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.3 });
    const boatSailGeo = new THREE.ConeGeometry(5, 12, 3);
    const boatSailMat = new THREE.MeshStandardMaterial({ color: 0xff5533, roughness: 0.4 });

    const boatCoords = [
      { x: -320, z: 360, rot: 0.5 },
      { x: 380, z: -80, rot: -1.2 },
      { x: -80, z: -420, rot: 2.1 }
    ];

    boatCoords.forEach((bc) => {
      const boat = new THREE.Group();
      const hull = new THREE.Mesh(boatHullGeo, boatHullMat);
      hull.position.y = 0.5;
      const sail = new THREE.Mesh(boatSailGeo, boatSailMat);
      sail.position.set(0, 7.5, 0);
      sail.scale.set(0.3, 1, 1);
      boat.add(hull);
      boat.add(sail);
      boat.position.set(bc.x, -0.2, bc.z);
      boat.rotation.y = bc.rot;
      this.boatsGroup.add(boat);
    });
    this.scene.add(this.boatsGroup);
  }

  initAtmosphereParticles() {
    // 1. 닌텐도 스위치 스포츠 감성 알록달록 하늘 풍선 (Floating Balloons)
    this.balloonsSystem = createFloatingBalloonsGroup();
    this.islandGroup.add(this.balloonsSystem.group);

    // 2. 하늘에 쉼 없이 펑펑 터지는 축제 폭죽 (Continuous Fireworks)
    this.fireworksSystem = createFestiveFireworksSystem();
    this.scene.add(this.fireworksSystem.group);
  }

  /* 닌텐도 스위치 스포츠 감성: 화사한 앰비언트 스카이라이트 & 쨍한 선샤인 라이팅 */
  initLights() {
    const hemiLight = new THREE.HemisphereLight(0x7dd3fc, 0x86efac, 1.5);
    this.scene.add(hemiLight);
    this.hemiLight = hemiLight;

    const sunLight = new THREE.DirectionalLight(0xfff5e6, 2.9);
    sunLight.position.set(220, 380, 260);
    sunLight.castShadow = true;
    sunLight.shadow.mapSize.width = 4096;
    sunLight.shadow.mapSize.height = 4096;
    sunLight.shadow.radius = 2.5;
    sunLight.shadow.camera.near = 50;
    sunLight.shadow.camera.far = 850;
    sunLight.shadow.bias = -0.0006;
    sunLight.shadow.normalBias = 0.05;
    const d = 320;
    sunLight.shadow.camera.left = -d;
    sunLight.shadow.camera.right = d;
    sunLight.shadow.camera.top = d;
    sunLight.shadow.camera.bottom = -d;
    this.scene.add(sunLight);
    this.sunLight = sunLight;

    const fillLight = new THREE.DirectionalLight(0x38bdf8, 0.95);
    fillLight.position.set(-220, 160, -220);
    this.scene.add(fillLight);
    this.fillLight = fillLight;

    // 조명 전구 발광 머티리얼
    this.stadiumBulbMaterial = new THREE.MeshStandardMaterial({
      color: 0xffffff,
      emissive: new THREE.Color(0xffffff),
      emissiveIntensity: 0.1,
      roughness: 0.1,
      metalness: 0.1
    });
  }

  /* =========================================================================
     스타디움 조명 타워 & 렌즈 글로우 스프라이트 (Fake Glow Sprite)
     - 부자연스러운 스팟라이트/빛기둥/바닥 스팟 경계선 제거!
     - 램프 헤드에 은은한 글로우 스프라이트로 실제 불빛이 켜진 듯한 자연스러운 효과
     - 부드러운 무지향성 주변광(PointLight)으로 관중석과 코트를 화사하게 유지
  ========================================================================= */
  addStadiumLightingSystem(stadiumUnit) {
    const towerConfigs = [
      { x: -24, z: -33, rotY: Math.PI * 0.25 },
      { x:  24, z: -33, rotY: -Math.PI * 0.25 },
      { x: -24, z:  33, rotY: Math.PI * 0.75 },
      { x:  24, z:  33, rotY: -Math.PI * 0.75 },
    ];

    const poleGeo = new THREE.CylinderGeometry(0.38, 0.62, 18, 12);
    const metalMat = new THREE.MeshStandardMaterial({
      color: 0x334155,
      roughness: 0.35,
      metalness: 0.75
    });

    const headGeo = new THREE.BoxGeometry(4.4, 2.2, 1.3);
    const headMat = new THREE.MeshStandardMaterial({
      color: 0x1e293b,
      roughness: 0.3,
      metalness: 0.8
    });

    const visorGeo = new THREE.BoxGeometry(4.6, 0.35, 1.6);
    const visorMat = new THREE.MeshStandardMaterial({
      color: 0x0f172a,
      roughness: 0.2,
      metalness: 0.9
    });

    const bulbGeo = new THREE.SphereGeometry(0.55, 12, 12);
    const glowTex = createGlowSpriteTexture();

    // 0. 스타디움 돔 상부 오큘러스 크라운 글로우 스프라이트 (멀리서 봐도 어둡지 않은 몽환적인 아레나 헤일로)
    const crownGlowMat = new THREE.SpriteMaterial({
      map: glowTex,
      color: 0x67e8f9,
      transparent: true,
      opacity: 0.0,
      blending: THREE.AdditiveBlending,
      depthWrite: false
    });
    const crownGlowSprite = new THREE.Sprite(crownGlowMat);
    crownGlowSprite.position.set(0, 52, 0);
    crownGlowSprite.scale.set(45.0, 45.0, 1.0);
    stadiumUnit.add(crownGlowSprite);
    this.stadiumCrownGlowSprites.push({ sprite: crownGlowSprite, mat: crownGlowMat });

    // 0.5. 스타디움 내부 코트 코어 조명 (PointLight: 오큘러스 천장 위로 빛이 솟아오름)
    const coreLight = new THREE.PointLight(0xfffae6, 0.1, 150, 1.0);
    coreLight.position.set(0, 22, 0);
    stadiumUnit.add(coreLight);
    this.stadiumCoreLights.push(coreLight);

    towerConfigs.forEach((cfg) => {
      const tower = new THREE.Group();
      tower.position.set(cfg.x, 8, cfg.z);
      tower.rotation.y = cfg.rotY;

      // 1. 견고한 수직 라이트 마운트 폴 (y: 0 ~ 18)
      const pole = new THREE.Mesh(poleGeo, metalMat);
      pole.position.y = 9;
      pole.castShadow = true;
      tower.add(pole);

      // 2. 조명 타워 헤드 (y: 18 위치, 코트 안쪽을 향해 38도 아래로 숙임)
      const headGroup = new THREE.Group();
      headGroup.position.y = 18;
      headGroup.rotation.x = 0.38;

      const headBox = new THREE.Mesh(headGeo, headMat);
      headGroup.add(headBox);

      // 상단 바이저 (차광판 엣지 캡)
      const visor = new THREE.Mesh(visorGeo, visorMat);
      visor.position.set(0, 1.15, 0.15);
      headGroup.add(visor);

      // 3. 고휘도 4구 LED 램프 전구
      const bulbPositions = [
        { x: -1.3, y: 0.45 },
        { x:  1.3, y: 0.45 },
        { x: -1.3, y: -0.45 },
        { x:  1.3, y: -0.45 },
      ];

      bulbPositions.forEach((bp) => {
        const bulb = new THREE.Mesh(bulbGeo, this.stadiumBulbMaterial);
        bulb.position.set(bp.x, bp.y, 0.65);
        headGroup.add(bulb);
      });

      // 4. 부드러운 옴니 주변광 PointLight (경계선 없는 자연스러운 광원)
      const floodLight = new THREE.PointLight(0xfffae6, 0.05, 110, 1.2);
      floodLight.position.set(0, 0, 1.5);
      headGroup.add(floodLight);
      this.stadiumFloodLights.push(floodLight);

      // 5. ★★★ 렌즈 글로우 스프라이트 (Fake Glow Sprite) ★★★
      // 카메라 각도에 상관없이 조명 램프가 영롱하게 켜진 느낌을 주는 부드러운 글로우
      const glowMat = new THREE.SpriteMaterial({
        map: glowTex,
        color: 0xffffff,
        transparent: true,
        opacity: 0.0,
        blending: THREE.AdditiveBlending,
        depthWrite: false
      });
      const glowSprite = new THREE.Sprite(glowMat);
      glowSprite.position.set(0, 0, 1.8);
      glowSprite.scale.set(6.0, 6.0, 1.0);
      headGroup.add(glowSprite);
      this.stadiumGlowSprites.push({ sprite: glowSprite, mat: glowMat });

      tower.add(headGroup);
      stadiumUnit.add(tower);
    });
  }

  /* 시간대 (Time of Day) 3단 순환 전환 시스템: day ➔ sunset ➔ night ➔ day */
  /* =========================================================================
     시간대 부드러운 전환 (기본 2초)
     - 최종 값은 기존 _applyTimeOfDay 가 계산하고, 그 직전/직후 값을 스냅샷으로 찍어
       animate() 에서 from → to 로 보간한다. (하늘/태양/조명/안개/바다/경기장 불빛 등)
  ========================================================================= */
  _collectTweenables() {
    const list = [];
    const num = (o, p) => { if (o && typeof o[p] === 'number') list.push({ t: 'n', o, p }); };
    const col = (c) => { if (c && c.isColor) list.push({ t: 'c', c }); };
    const vec = (v) => { if (v && v.isVector3) list.push({ t: 'v', v }); };

    if (this.sky) {
      const u = this.sky.material.uniforms;
      num(u.turbidity, 'value'); num(u.rayleigh, 'value');
      num(u.mieCoefficient, 'value'); num(u.mieDirectionalG, 'value');
      vec(u.sunPosition.value);
    }
    if (this.renderer) num(this.renderer, 'toneMappingExposure');
    if (this.starMat) num(this.starMat, 'opacity');

    if (this.hemiLight) { col(this.hemiLight.color); col(this.hemiLight.groundColor); num(this.hemiLight, 'intensity'); }
    [this.sunLight, this.fillLight].forEach((l) => {
      if (!l) return;
      col(l.color); num(l, 'intensity'); vec(l.position);
    });
    if (this.scene.fog) col(this.scene.fog.color);
    if (this.waterMesh) {
      const m = this.waterMesh.material;
      col(m.color); num(m, 'roughness'); num(m, 'specularIntensity');
    }

    const light = (l) => { col(l.color); num(l, 'intensity'); num(l, 'distance'); };
    const glow = (it) => { col(it.mat.color); num(it.mat, 'opacity'); vec(it.sprite.scale); };
    this.stadiumFloodLights.forEach(light);
    this.stadiumCoreLights.forEach(light);
    this.stadiumGlowSprites.forEach(glow);
    this.stadiumCrownGlowSprites.forEach(glow);

    if (this.resortPathways) {
      const lb = this.resortPathways.lampBulbMaterial;
      if (lb) { col(lb.emissive); num(lb, 'emissiveIntensity'); }
      (this.resortPathways.streetLamps || []).forEach((lamp) => {
        if (lamp.glowMat) { col(lamp.glowMat.color); num(lamp.glowMat, 'opacity'); }
        if (lamp.sprite) vec(lamp.sprite.scale);
        if (lamp.light) light(lamp.light);
      });
    }
    if (this.stadiumBulbMaterial) { col(this.stadiumBulbMaterial.emissive); num(this.stadiumBulbMaterial, 'emissiveIntensity'); }
    this.stadiumLightMaterials.forEach((m) => { col(m.emissive); num(m, 'emissiveIntensity'); });
    this.islandGroup.traverse((child) => {
      if (child.isMesh && (child.name || '').toLowerCase().includes('stadium_led_ring')) col(child.material.color);
    });
    return list;
  }

  _snapshotTweenables(list) {
    return list.map((it) => (it.t === 'n' ? it.o[it.p] : it.t === 'c' ? it.c.clone() : it.v.clone()));
  }

  _writeTweenables(list, a, b, k) {
    for (let i = 0; i < list.length; i++) {
      const it = list[i];
      if (it.t === 'n') it.o[it.p] = a[i] + (b[i] - a[i]) * k;
      else if (it.t === 'c') it.c.copy(a[i]).lerp(b[i], k);
      else it.v.copy(a[i]).lerp(b[i], k);
    }
  }

  setTimeOfDay(mode = 'day', duration = 2.0) {
    // 최초 1회(초기화)는 즉시 적용
    if (!this._todReady) {
      this._applyTimeOfDay(mode);
      this._todReady = true;
      return;
    }
    if (mode === this.currentTod && !this._todTween) return;

    const items = this._collectTweenables();
    const from = this._snapshotTweenables(items); // 진행 중인 전환이 있으면 그 중간값이 시작점
    this._todTween = null;

    this._applyTimeOfDay(mode);                   // 최종값 계산 (환경맵/별/그림자 방향 등 포함)
    const to = this._snapshotTweenables(items);

    if (duration <= 0) return;
    this._writeTweenables(items, from, to, 0);    // 시작점으로 되돌리고 서서히 이동
    this._todTween = { items, from, to, t: 0, dur: duration };
  }

  cycleTimeOfDay() {
    const modes = ['day', 'sunset', 'night'];
    const nextIdx = (modes.indexOf(this.currentTod) + 1) % modes.length;
    this.setTimeOfDay(modes[nextIdx]);
    return this.currentTod;
  }

  _applyTimeOfDay(mode = 'day') {
    this.currentTod = mode;

    // 0. 대기 산란 하늘 파라미터 (시간대별 태양 고도/방위)
    if (this.sky) {
      const P = {
        day:    { elev: 38, azim: 150, turb: 1.6,  ray: 3.6, mie: 0.0015, g: 0.80, exp: 0.52 },
        sunset: { elev: 3,  azim: 200, turb: 8,    ray: 2.4, mie: 0.008, g: 0.90, exp: 0.40 },
        night:  { elev: -5, azim: 150, turb: 1.0,  ray: 0.55, mie: 0.002, g: 0.70, exp: 0.50 }
      }[mode] || null;
      if (P) {
        const phi = THREE.MathUtils.degToRad(90 - P.elev);
        const theta = THREE.MathUtils.degToRad(P.azim);
        this.sunDir.setFromSphericalCoords(1, phi, theta);
        [this.sky, this.skyEnv].forEach((sk) => {
          const u = sk.material.uniforms;
          u.turbidity.value = P.turb;
          u.rayleigh.value = P.ray;
          u.mieCoefficient.value = P.mie;
          u.mieDirectionalG.value = P.g;
          u.sunPosition.value.copy(this.sunDir);
        });
        if (this.renderer) this.renderer.toneMappingExposure = P.exp * 2.4;
        if (this.starMat) this.starMat.opacity = mode === 'night' ? 0.95 : 0;

        // 태양 광원 방향을 하늘의 태양과 일치 (낮/노을). 밤은 달빛이 위에서
        if (this.sunLight) {
          if (mode === 'night') {
            this.sunLight.position.set(-120, 230, -380);
            if (this.fillLight) this.fillLight.position.set(200, 160, 350);
          }
          else {
            this.sunLight.position.copy(this.sunDir).multiplyScalar(460);
            if (this.fillLight) this.fillLight.position.set(-220, 160, -220);
          }
        }

        if (this.pmrem) {
          if (this.envRT) this.envRT.dispose();
          this.envRT = this.pmrem.fromScene(this.skyEnvScene, 0.02);
          this.applyEnvironmentDetail();
        }
      }
    }

    // 1. 스카이 캔버스 그라디언트 갱신 (레거시, 대기 하늘 사용 시 미표시)
    if (false && this.skyCanvas && this.skyCtx && this.skyTexture) {
      const ctx = this.skyCtx;
      const grad = ctx.createLinearGradient(0, 0, 0, 512);

      if (mode === 'day') {
        grad.addColorStop(0, '#004ea8');    // Deep Vivid Nintendo Cobalt
        grad.addColorStop(0.32, '#0284c7'); // Mediterranean Azure
        grad.addColorStop(0.65, '#38bdf8'); // Sparkling Tropical Cyan
        grad.addColorStop(0.86, '#bae6fd'); // Soft Pastel Horizon
        grad.addColorStop(0.94, '#fed7aa'); // Warm Golden Sunset Tint
        grad.addColorStop(1, '#ffffff');    // Brilliant Horizon
      } else if (mode === 'sunset') {
        grad.addColorStop(0, '#2e0854');    // Deep Twilight Violet
        grad.addColorStop(0.25, '#581c87'); // Royal Purple
        grad.addColorStop(0.52, '#c026d3'); // Radiant Magenta Sunset
        grad.addColorStop(0.76, '#ea580c'); // Fiery Orange
        grad.addColorStop(0.90, '#f59e0b'); // Golden Amber Horizon
        grad.addColorStop(1, '#fef08a');    // Glowing Warm Sunlight
      } else if (mode === 'night') {
        grad.addColorStop(0, '#020617');    // Deep Space Midnight Navy
        grad.addColorStop(0.28, '#0f172a'); // Rich Slate Midnight
        grad.addColorStop(0.58, '#1e1b4b'); // Electric Indigo Night
        grad.addColorStop(0.82, '#0284c7'); // Vibrant Cyberpunk Horizon Glow
        grad.addColorStop(1, '#38bdf8');    // Neon Cyan Horizon Rim
      }

      ctx.fillStyle = grad;
      ctx.fillRect(0, 0, 512, 512);
      this.skyTexture.needsUpdate = true;

      // 반사용 환경맵 갱신 (바다에 하늘이 비치도록)
      if (this.pmrem && this.skyEnvScene) {
        if (this.envRT) this.envRT.dispose();
        this.envRT = this.pmrem.fromScene(this.skyEnvScene, 0.02);
        if (this.waterMesh) {
          this.waterMesh.material.envMap = this.envRT.texture;
          this.waterMesh.material.needsUpdate = true;
        }
      }
    }

    // 2. 환경 조명 & 앰비언스 & 포그 갱신
    if (this.hemiLight && this.sunLight && this.fillLight) {
      if (mode === 'day') {
        this.hemiLight.color.setHex(0x7dd3fc);
        this.hemiLight.groundColor.setHex(0x86efac);
        this.hemiLight.intensity = 2.2;

        this.sunLight.color.setHex(0xfff2d6);
        this.sunLight.intensity = 3.6;

        this.fillLight.color.setHex(0x38bdf8);
        this.fillLight.intensity = 0.95;

        if (this.scene.fog) this.scene.fog.color.setHex(0x9bdcff);
                if (this.waterMesh) { this.waterMesh.material.color.setHex(0x1ec8ff); this.waterMesh.material.roughness = 0.1; this.waterMesh.material.specularIntensity = 1.0; }
      } else if (mode === 'sunset') {
        this.hemiLight.color.setHex(0xf472b6);
        this.hemiLight.groundColor.setHex(0xd97706);
        this.hemiLight.intensity = 1.55;

        this.sunLight.color.setHex(0xff7033);
        this.sunLight.intensity = 3.3;

        this.fillLight.color.setHex(0xc084fc);
        this.fillLight.intensity = 1.2;

        if (this.scene.fog) this.scene.fog.color.setHex(0xfbcfe8);
                if (this.waterMesh) { this.waterMesh.material.color.setHex(0x0369a1); this.waterMesh.material.roughness = 0.1; this.waterMesh.material.specularIntensity = 1.0; }
      } else if (mode === 'night') {
        this.hemiLight.color.setHex(0x5b7cff);
        this.hemiLight.groundColor.setHex(0x1b2f5a);
        this.hemiLight.intensity = 1.6;

        this.sunLight.color.setHex(0x9ec5ff);
        this.sunLight.intensity = 3.3;

        this.fillLight.color.setHex(0x6d7cff);
        this.fillLight.intensity = 1.7;

        if (this.scene.fog) this.scene.fog.color.setHex(0x0b1736);
                if (this.waterMesh) { this.waterMesh.material.color.setHex(0x06407f); this.waterMesh.material.roughness = 0.14; this.waterMesh.material.specularIntensity = 1.0; }
      }
    }

    // 3. 조명탑 주변광 & 렌즈 글로우 스프라이트 & 스타디움 돔 크라운 글로우 제어
    if (mode === 'day') {
      this.stadiumFloodLights.forEach((light) => {
        light.color.setHex(0xfffae6);
        light.intensity = 0.05;
        light.distance = 90;
      });
      this.stadiumGlowSprites.forEach((item) => {
        item.mat.opacity = 0.0;
        item.sprite.scale.set(6.0, 6.0, 1.0);
      });
      this.stadiumCrownGlowSprites.forEach((item) => {
        item.mat.opacity = 0.0;
      });
      this.stadiumCoreLights.forEach((light) => {
        light.color.setHex(0xfffae6);
        light.intensity = 0.1;
      });
    } else if (mode === 'sunset') {
      this.stadiumFloodLights.forEach((light) => {
        light.color.setHex(0xffaa44);
        light.intensity = 0.45;
        light.distance = 130;
      });
      this.stadiumGlowSprites.forEach((item) => {
        item.mat.color.setHex(0xffaa44);
        item.mat.opacity = 0.55;
        item.sprite.scale.set(10.0, 10.0, 1.0);
      });
      this.stadiumCrownGlowSprites.forEach((item) => {
        item.mat.color.setHex(0xf59e0b);
        item.mat.opacity = 0.28;
        item.sprite.scale.set(55.0, 55.0, 1.0);
      });
      this.stadiumCoreLights.forEach((light) => {
        light.color.setHex(0xffa834);
        light.intensity = 0.7;
        light.distance = 150;
      });
    } else if (mode === 'night') {
      // 밤 시간대: 멀리서 봐도 너무 어둡지 않게 은은하고 영롱한 스타디움 돔 글로우 & 코어 조명 강화
      this.stadiumFloodLights.forEach((light) => {
        light.color.setHex(0xffe2b0);
        light.intensity = 2.6;
        light.distance = 200;
      });
      this.stadiumGlowSprites.forEach((item) => {
        item.mat.color.setHex(0xe2f1ff);
        item.mat.opacity = 0.90; // 조명탑 렌즈 글로우
        item.sprite.scale.set(16.0, 16.0, 1.0);
      });
      this.stadiumCrownGlowSprites.forEach((item) => {
        item.mat.color.setHex(0x67e8f9); // 스타디움 돔 상공의 소프트 스카이블루 아레나 헤일로
        item.mat.opacity = 0.52; // 과하지 않고 자연스러운 원경 조명 효과
        item.sprite.scale.set(70.0, 70.0, 1.0);
      });
      this.stadiumCoreLights.forEach((light) => {
        light.color.setHex(0x38bdf8);
        light.intensity = 3.2;
        light.distance = 210;
      });
    }

    // 3.5 길목 가로등 (Street Lamps) 밤 시간대 발광 & 조명 제어 ("가는 길목 주변에 가로등도 있어서 밤에는 밝게 빛나도록")
    if (this.resortPathways && this.resortPathways.lampBulbMaterial) {
      if (mode === 'day') {
        this.resortPathways.lampBulbMaterial.emissive.setHex(0xfffae6);
        this.resortPathways.lampBulbMaterial.emissiveIntensity = 0.05;
        this.resortPathways.streetLamps.forEach((lamp) => {
          if (lamp.glowMat) lamp.glowMat.opacity = 0.0;
          if (lamp.light) lamp.light.intensity = 0.0;
        });
      } else if (mode === 'sunset') {
        this.resortPathways.lampBulbMaterial.emissive.setHex(0xffaa44);
        this.resortPathways.lampBulbMaterial.emissiveIntensity = 0.75;
        this.resortPathways.streetLamps.forEach((lamp) => {
          if (lamp.glowMat) {
            lamp.glowMat.color.setHex(0xffaa44);
            lamp.glowMat.opacity = 0.55;
            lamp.sprite.scale.set(8.0, 8.0, 1.0);
          }
          if (lamp.light) {
            lamp.light.color.setHex(0xffaa44);
            lamp.light.intensity = 0.65;
            lamp.light.distance = 42;
          }
        });
      } else if (mode === 'night') {
        // 밤에는 가로등이 영롱한 온백색/골든 앰버로 밝게 빛남
        this.resortPathways.lampBulbMaterial.emissive.setHex(0xfff3b0);
        this.resortPathways.lampBulbMaterial.emissiveIntensity = 2.0;
        this.resortPathways.streetLamps.forEach((lamp) => {
          if (lamp.glowMat) {
            lamp.glowMat.color.setHex(0xffe680);
            lamp.glowMat.opacity = 0.95;
            lamp.sprite.scale.set(12.5, 12.5, 1.0);
          }
          if (lamp.light) {
            lamp.light.color.setHex(0xffd27a);
            lamp.light.intensity = 3.0;
            lamp.light.distance = 70;
          }
        });
      }
    }

    // 4. 고휘도 LED 전구 머티리얼 발광 조율
    if (this.stadiumBulbMaterial) {
      if (mode === 'day') {
        this.stadiumBulbMaterial.emissive.setHex(0xffffff);
        this.stadiumBulbMaterial.emissiveIntensity = 0.1;
      } else if (mode === 'sunset') {
        this.stadiumBulbMaterial.emissive.setHex(0xffa834);
        this.stadiumBulbMaterial.emissiveIntensity = 0.45;
      } else if (mode === 'night') {
        this.stadiumBulbMaterial.emissive.setHex(0xffffff);
        this.stadiumBulbMaterial.emissiveIntensity = 1.35;
      }
    }

    // 5. FBX 모델 내 Light 메쉬 머티리얼 발광 조율
    this.stadiumLightMaterials.forEach((mat) => {
      if (mode === 'day') {
        mat.emissive.setHex(0xfffae6);
        mat.emissiveIntensity = 0.1;
      } else if (mode === 'sunset') {
        mat.emissive.setHex(0xff9922);
        mat.emissiveIntensity = 0.45;
      } else if (mode === 'night') {
        mat.emissive.setHex(0xffffff);
        mat.emissiveIntensity = 1.2;
      }
    });

    // 6. 돔 LED 링 발광 색상 조율
    this.islandGroup.traverse((child) => {
      if (child.isMesh) {
        const name = (child.name || '').toLowerCase();
        if (name.includes('stadium_led_ring')) {
          if (mode === 'day') child.material.color.setHex(0x38bdf8);
          else if (mode === 'sunset') child.material.color.setHex(0xfbbf24);
          else if (mode === 'night') child.material.color.setHex(0x00f5ff);
        }
      }
    });
  }

  /* 3대 경기장 배치 및 로드 */
  loadThreeStadiums() {
    const fbxLoader = new FBXLoader();

    const courtTex = this.textureLoader.load('assets/models/court/Texture/Tennis__Tennis_AlbedoTransparency.png');
    const chairMainTex = this.textureLoader.load('assets/models/court/Texture/Tennis__Chair_Main1_AlbedoTransparency.png');
    const chairRearTex = this.textureLoader.load('assets/models/court/Texture/Tennis__Chair_rear_AlbedoTransparency.png');
    const extraTex = this.textureLoader.load('assets/models/court/Texture/Tennis__Extra1_AlbedoTransparency.png');

    const back1Tex = this.textureLoader.load('assets/models/court/Texture/Tennis_Back_Back_1_AlbedoTransparency.png');
    const back2Tex = this.textureLoader.load('assets/models/court/Texture/Tennis_Back_Back_2_AlbedoTransparency.png');
    const lightTex = this.textureLoader.load('assets/models/court/Texture/Tennis_Back_Light_AlbedoTransparency.png');
    const opTex = this.textureLoader.load('assets/models/court/Texture/Tennis_Back_Back_1_OP_AlbedoTransparency.png');

    const buildStadiumUnit = (onReady) => {
      const stadiumUnit = new THREE.Group();

      fbxLoader.load('assets/models/court/Tennis_.fbx', (courtFbx) => {
        courtFbx.rotation.y = Math.PI / 2;
        courtFbx.traverse((child) => {
          if (child.isMesh) {
            child.receiveShadow = true;
            const name = (child.name || '').toLowerCase();
            const isFloor = !name.includes('chair') && !name.includes('extra') && !name.includes('net') && !name.includes('post');
            child.castShadow = !isFloor;

            if (name.includes('chair_main')) {
              child.material = new THREE.MeshStandardMaterial({ map: chairMainTex, roughness: 0.5, side: THREE.DoubleSide });
            } else if (name.includes('chair_rear')) {
              child.material = new THREE.MeshStandardMaterial({ map: chairRearTex, roughness: 0.5, side: THREE.DoubleSide });
            } else if (name.includes('extra')) {
              child.material = new THREE.MeshStandardMaterial({ map: extraTex, roughness: 0.5, side: THREE.DoubleSide });
            } else {
              child.material = new THREE.MeshStandardMaterial({
                map: courtTex,
                roughness: 0.65,
                side: THREE.DoubleSide,
                polygonOffset: true,
                polygonOffsetFactor: -1.0,
                polygonOffsetUnits: -1.0
              });
            }
          }
        });
        const box = new THREE.Box3().setFromObject(courtFbx);
        const center = box.getCenter(new THREE.Vector3());
        courtFbx.position.x -= center.x;
        courtFbx.position.y -= box.min.y;
        courtFbx.position.y += 0.06; // 코트 바닥이 아래층 바닥/지형과 겹치거나 Z-fighting 생기지 않도록 미세 부양
        courtFbx.position.z -= center.z;
        stadiumUnit.add(courtFbx);

        fbxLoader.load('assets/models/court/Tennis_Back.fbx', (standsFbx) => {
          standsFbx.traverse((child) => {
            if (child.isMesh) {
              child.castShadow = true;
              child.receiveShadow = true;
              const name = (child.name || '').toLowerCase();
              const matName = (child.material && child.material.name ? child.material.name : '').toLowerCase();
              const isLight = name.includes('light') || matName.includes('light');

              if (isLight) {
                const lightMat = new THREE.MeshStandardMaterial({
                  map: lightTex,
                  emissive: new THREE.Color(0xfffae6),
                  emissiveIntensity: 0.35,
                  roughness: 0.2,
                  metalness: 0.1,
                  side: THREE.DoubleSide
                });
                child.material = lightMat;
                this.stadiumLightMaterials.push(lightMat);
              } else if (name.includes('back_2') || name.includes('back2')) {
                child.material = new THREE.MeshStandardMaterial({ map: back2Tex, roughness: 0.6, side: THREE.DoubleSide });
              } else if (name.includes('op')) {
                child.material = new THREE.MeshStandardMaterial({ map: opTex, transparent: true, alphaTest: 0.2, side: THREE.DoubleSide });
              } else {
                child.material = new THREE.MeshStandardMaterial({ map: back1Tex, roughness: 0.6, side: THREE.DoubleSide });
              }
            }
          });
          // 경기장 내부(관중석: Tennis_Back.fbx) 90도 회전 및 사이즈 약간 확대
          standsFbx.rotation.y = Math.PI / 2;
          standsFbx.scale.set(1.08, 1.05, 1.08);

          const b = new THREE.Box3().setFromObject(standsFbx);
          const c = b.getCenter(new THREE.Vector3());
          standsFbx.position.x -= c.x;
          standsFbx.position.y -= b.min.y;
          standsFbx.position.z -= c.z;
          stadiumUnit.add(standsFbx);

          // 1. ACE DOME: 거대 돔형 외벽 껍데기 래퍼
          addGrandAceDomeShell(stadiumUnit);

          // 2. 스타디움 조명 타워 & 렌즈 글로우 스프라이트 장착
          this.addStadiumLightingSystem(stadiumUnit);

          onReady(stadiumUnit);
        });
      });
    };

    // 1. Left Stadium (Main focus stadium at x: -160, y: 1.2, z: 0)
    buildStadiumUnit((unit) => {
      this.leftStadiumGroup.add(unit);
      this.leftStadiumGroup.position.copy(this.leftStadiumCenter);
      this.islandGroup.add(this.leftStadiumGroup);

      const preloader = document.getElementById('initial-preloader');
      if (preloader) {
        preloader.classList.add('fade-out');
        setTimeout(() => preloader.remove(), 800);
      }
    });

    // 2. Top-Right Stadium (Bowling at x: 130, y: 1.2, z: -150 - 테니스와 동일한 y: 1.2 높이로 잔디 바닥 겹침 제거)
    buildStadiumUnit((unit) => {
      this.topRightStadiumGroup.add(unit);
      this.topRightStadiumGroup.position.copy(this.bowlingStadiumCenter);
      this.topRightStadiumGroup.rotation.y = -Math.PI / 4;
      this.islandGroup.add(this.topRightStadiumGroup);
    });

    // 3. Bottom-Right Stadium (Swordplay at x: 130, y: 1.2, z: 150 - 테니스와 동일한 y: 1.2 높이로 잔디 바닥 겹침 제거)
    buildStadiumUnit((unit) => {
      this.bottomRightStadiumGroup.add(unit);
      this.bottomRightStadiumGroup.position.copy(this.swordStadiumCenter);
      this.bottomRightStadiumGroup.rotation.y = Math.PI / 4;
      this.islandGroup.add(this.bottomRightStadiumGroup);
    });
  }

  /* =========================================================================
     3대 경기장 순차 완결 투어 시네마틱 카메라 궤적:
     - Step 0 (p: 0.00 ~ 0.04): 첫 화면 와이드 스카이뷰 (낮)
     - Step 1 ~ 3 (p: 0.04 ~ 0.28): 🎾 테니스 경기장 내부 3스텝 완결 공전 (낮)
     - 전환 1 (p: 0.28 ~ 0.40): 테니스 ➔ 볼링 경기장 상공 완만한 시네마틱 활공 (노을로 전환)
     - Step 4 ~ 6 (p: 0.40 ~ 0.62): 🎳 볼링 경기장 내부 3스텝 완결 공전 (노을)
     - 전환 2 (p: 0.62 ~ 0.74): 볼링 ➔ 검술 경기장 상공 완만한 시네마틱 활공 (밤으로 전환)
     - Step 7 ~ 9 (p: 0.74 ~ 0.92): ⚔️ 검술 경기장 내부 3스텝 완결 공전 (밤)
     - 전환 3 (p: 0.92 ~ 1.00): 검술 ➔ 3대 경기장 전체 조망 항공뷰로 완만하게 상승
     - Step 10 (p: 1.00): 🏆 3대 경기장 전체 조망 항공뷰 피날레 (밤)
  ========================================================================= */
  updateCameraForScroll(p) {
    this.scrollProgress = Math.max(0, Math.min(1, p));
    const orbit = this._orbitTime || 0;

    // -------------------------------------------------------------
    // 1. 오프닝 스카이뷰 -> Step 1: 🎾 테니스 경기장 외경 인트로 (p: 0.00 ~ 0.08)
    // -------------------------------------------------------------
    if (this.scrollProgress <= 0.08) {
      const f = this.scrollProgress / 0.08;
      const easeF = f * f * (3 - 2 * f);

      const center = this.tennisStadiumCenter;
      // 스카이뷰(Hero)에서 보이는 방향과 정확히 일치하는 직선 줌인 벡터
      // skyCamPos(25, 205, 435) -> tennisStadiumCenter(-160, 1.2, 0)
      // dx: -185, dy: -203.8, dz: -435 (각도 불변 일직선 줌인)
      const introPos = new THREE.Vector3(
        center.x + 68.0,  // -92.0
        78.0,
        center.z + 160.0  // +160.0
      );
      const introLook = new THREE.Vector3(center.x, center.y + 6.0, center.z);

      this.targetCamPos.lerpVectors(this.skyCamPos, introPos, easeF);
      this.targetLookAt.lerpVectors(this.skyLookAt, introLook, easeF);
    }
    // -------------------------------------------------------------
    // 2. 🎾 테니스 경기장 내부 완결 진입 & 공전 (Step 2 ~ 4, p: 0.08 ~ 0.28)
    // -------------------------------------------------------------
    else if (this.scrollProgress <= 0.28) {
      const t = (this.scrollProgress - 0.08) / 0.20;
      const angle = t * Math.PI * 1.5 - 0.2;
      const radius = 24.5 - t * 2.5;
      const height = 7.8 + Math.sin(t * Math.PI) * 3.8;

      const camX = this.tennisStadiumCenter.x + Math.sin(angle) * radius;
      const camZ = this.tennisStadiumCenter.z + Math.cos(angle) * radius;

      this.targetCamPos.set(camX, height, camZ);
      this.targetLookAt.copy(this.tennisStadiumCenter);
    }
    // -------------------------------------------------------------
    // 3. 전환 1: 테니스 ➔ Step 5: 🎳 볼링 경기장 외경 인트로 (p: 0.28 ~ 0.44)
    // -------------------------------------------------------------
    else if (this.scrollProgress <= 0.44) {
      const rawT = (this.scrollProgress - 0.28) / 0.16;
      const s = rawT * rawT * (3 - 2 * rawT);

      const p0 = new THREE.Vector3(-176, 14, 15);
      const p1 = new THREE.Vector3(-20, 82, -80);

      // 볼링 외경 인트로 목표 위치 (더 멀리 떨어지고 더 높은 뷰에서 천천히 선회)
      const centerB = this.bowlingStadiumCenter;
      const angleB = -1.0; // 회전 없이 안정적인 뷰
      const p2 = new THREE.Vector3(
        centerB.x + Math.sin(angleB) * 135.0,
        78.0,
        centerB.z + Math.cos(angleB) * 135.0
      );

      const oneMinusS = 1 - s;
      const posX = oneMinusS * oneMinusS * p0.x + 2 * oneMinusS * s * p1.x + s * s * p2.x;
      const posY = oneMinusS * oneMinusS * p0.y + 2 * oneMinusS * s * p1.y + s * s * p2.y;
      const posZ = oneMinusS * oneMinusS * p0.z + 2 * oneMinusS * s * p1.z + s * s * p2.z;
      this.targetCamPos.set(posX, posY, posZ);

      const look0 = this.tennisStadiumCenter;
      const look1 = new THREE.Vector3(0, 15, -75);
      const look2 = new THREE.Vector3(centerB.x, centerB.y + 4.0, centerB.z);
      const lookX = oneMinusS * oneMinusS * look0.x + 2 * oneMinusS * s * look1.x + s * s * look2.x;
      const lookY = oneMinusS * oneMinusS * look0.y + 2 * oneMinusS * s * look1.y + s * s * look2.y;
      const lookZ = oneMinusS * oneMinusS * look0.z + 2 * oneMinusS * s * look1.z + s * s * look2.z;
      this.targetLookAt.set(lookX, lookY, lookZ);
    }
    // -------------------------------------------------------------
    // 4. 🎳 볼링 경기장 내부 완결 진입 & 공전 (Step 6 ~ 8, p: 0.44 ~ 0.64)
    // -------------------------------------------------------------
    else if (this.scrollProgress <= 0.64) {
      const t = (this.scrollProgress - 0.44) / 0.20;
      const angle = t * Math.PI * 1.5 - Math.PI * 0.35;
      const radius = 25.0 - t * 2.0;
      const height = 8.0 + Math.sin(t * Math.PI) * 3.6;

      const camX = this.bowlingStadiumCenter.x + Math.sin(angle) * radius;
      const camZ = this.bowlingStadiumCenter.z + Math.cos(angle) * radius;

      this.targetCamPos.set(camX, height, camZ);
      this.targetLookAt.copy(this.bowlingStadiumCenter);
    }
    // -------------------------------------------------------------
    // 5. 전환 2: 볼링 ➔ Step 9: ⚔️ 검술 경기장 외경 인트로 (p: 0.64 ~ 0.78)
    // -------------------------------------------------------------
    else if (this.scrollProgress <= 0.78) {
      const rawT = (this.scrollProgress - 0.64) / 0.14;
      const s = rawT * rawT * (3 - 2 * rawT);

      const p0 = new THREE.Vector3(144, 15, -135);
      const p1 = new THREE.Vector3(185, 84, 0);

      // 검술 외경 인트로 목표 위치 (더 멀리 떨어지고 더 높은 뷰에서 천천히 선회)
      const centerS = this.swordStadiumCenter;
      const angleS = 0.8; // 회전 없이 안정적인 뷰
      const p2 = new THREE.Vector3(
        centerS.x + Math.sin(angleS) * 135.0,
        78.0,
        centerS.z + Math.cos(angleS) * 135.0
      );

      const oneMinusS = 1 - s;
      const posX = oneMinusS * oneMinusS * p0.x + 2 * oneMinusS * s * p1.x + s * s * p2.x;
      const posY = oneMinusS * oneMinusS * p0.y + 2 * oneMinusS * s * p1.y + s * s * p2.y;
      const posZ = oneMinusS * oneMinusS * p0.z + 2 * oneMinusS * s * p1.z + s * s * p2.z;
      this.targetCamPos.set(posX, posY, posZ);

      const look0 = this.bowlingStadiumCenter;
      const look1 = new THREE.Vector3(145, 12, 0);
      const look2 = new THREE.Vector3(centerS.x, centerS.y + 4.0, centerS.z);
      const lookX = oneMinusS * oneMinusS * look0.x + 2 * oneMinusS * s * look1.x + s * s * look2.x;
      const lookY = oneMinusS * oneMinusS * look0.y + 2 * oneMinusS * s * look1.y + s * s * look2.y;
      const lookZ = oneMinusS * oneMinusS * look0.z + 2 * oneMinusS * s * look1.z + s * s * look2.z;
      this.targetLookAt.set(lookX, lookY, lookZ);
    }
    // -------------------------------------------------------------
    // 6. ⚔️ 검술 경기장 내부 완결 진입 & 공전 (Step 10 ~ 12, p: 0.78 ~ 0.94)
    // -------------------------------------------------------------
    else if (this.scrollProgress <= 0.94) {
      const t = (this.scrollProgress - 0.78) / 0.16;
      const angle = t * Math.PI * 1.5 + Math.PI * 0.22;
      const radius = 24.5 - t * 2.0;
      const height = 8.0 + Math.sin(t * Math.PI) * 3.6;

      const camX = this.swordStadiumCenter.x + Math.sin(angle) * radius;
      const camZ = this.swordStadiumCenter.z + Math.cos(angle) * radius;

      this.targetCamPos.set(camX, height, camZ);
      this.targetLookAt.copy(this.swordStadiumCenter);
    }
    // -------------------------------------------------------------
    // 7. 전환 3: Step 13 🏆 3대 경기장 항공뷰 피날레 (p: 0.94 ~ 1.00)
    // -------------------------------------------------------------
    else {
      const rawT = (this.scrollProgress - 0.94) / 0.06;
      const s = rawT * rawT * (3 - 2 * rawT);

      const p0 = new THREE.Vector3(142, 18, 160);
      const p1 = new THREE.Vector3(80, 140, 330);
      const p2 = this.skyCamPos;

      const oneMinusS = 1 - s;
      const posX = oneMinusS * oneMinusS * p0.x + 2 * oneMinusS * s * p1.x + s * s * p2.x;
      const posY = oneMinusS * oneMinusS * p0.y + 2 * oneMinusS * s * p1.y + s * s * p2.y;
      const posZ = oneMinusS * oneMinusS * p0.z + 2 * oneMinusS * s * p1.z + s * s * p2.z;
      this.targetCamPos.set(posX, posY, posZ);

      const look0 = this.swordStadiumCenter;
      const look1 = new THREE.Vector3(0, 30, 0);
      const look2 = this.skyLookAt;
      const lookX = oneMinusS * oneMinusS * look0.x + 2 * oneMinusS * s * look1.x + s * s * look2.x;
      const lookY = oneMinusS * oneMinusS * look0.y + 2 * oneMinusS * s * look1.y + s * s * look2.y;
      const lookZ = oneMinusS * oneMinusS * look0.z + 2 * oneMinusS * s * look1.z + s * s * look2.z;
      this.targetLookAt.set(lookX, lookY, lookZ);
    }
  }

  initEventListeners() {
    window.addEventListener('resize', () => {
      if (!this.renderer || !this.camera) return;
      const w = window.innerWidth;
      const h = window.innerHeight;
      this.camera.aspect = w / h;
      this.camera.updateProjectionMatrix();
      this.renderer.setSize(w, h);
      if (this.composer) {
        this.composer.setSize(w, h);
        this.bloomPass.setSize(w, h);
      }
    });
  }

  animate() {
    requestAnimationFrame(this.animate);
    const delta = this.clock.getDelta();
    const elapsed = this.clock.getElapsedTime();
    this._orbitTime = (this._orbitTime || 0) + delta;

    // 1. 솜사탕 구름 부드러운 자전
    if (this.cloudsGroup) {
      this.cloudsGroup.rotation.y += delta * 0.007;
    }

    // 2. 카툰 에메랄드 수면 파도 웨이브 & 텍스처 흐름 애니메이션
    if (this.waterMesh) {
      if (this.waterMesh.material && this.waterMesh.material.map) {
        this.waterMesh.material.map.offset.x = (this.waterMesh.material.map.offset.x + delta * 0.012) % 1;
        this.waterMesh.material.map.offset.y = (this.waterMesh.material.map.offset.y + delta * 0.008) % 1;
      }
      this.waterMesh.material.opacity = 0.93 + Math.sin(elapsed * 1.5) * 0.02;
      if (this.waterNormalMap) {
        this.waterNormalMap.offset.x = (this.waterNormalMap.offset.x - delta * 0.018) % 1;
        this.waterNormalMap.offset.y = (this.waterNormalMap.offset.y + delta * 0.011) % 1;
      }
    }

    // 3. 리조트 세일보트 잔물결 롤링/피칭
    if (this.boatsGroup) {
      this.boatsGroup.children.forEach((boat, i) => {
        boat.position.y = -0.2 + Math.sin(elapsed * 1.8 + i * 2) * 0.25;
        boat.rotation.z = Math.sin(elapsed * 1.2 + i * 1.5) * 0.04;
      });
    }

    // 4. 알록달록 하늘 풍선 유유히 둥둥 부유
    if (this.balloonsSystem) {
      this.balloonsSystem.update(elapsed);
    }

    // 5. 밤낮 가리지 않고 하늘에 계속 펑펑 터지는 축제 폭죽
    if (this.fireworksSystem) {
      this.fireworksSystem.update(delta);
    }

    // 6. 바람에 살랑살랑 흔들리는 조그마한 3D 잔디 풀밭 애니메이션
    if (this.swayingGrassSystem) {
      this.swayingGrassSystem.update(elapsed);
    }

    // 경기장 간 이동 시 어지럽지 않도록 여유롭고 부드러운 시네마틱 댐핑 (0.042)
    const lerpFactor = this.scrollProgress <= 0.04 ? 0.065 : 0.042;
    this.currentCamPos.lerp(this.targetCamPos, lerpFactor);
    this.currentLookAt.lerp(this.targetLookAt, lerpFactor);

    this.camera.position.copy(this.currentCamPos);
    this.camera.lookAt(this.currentLookAt);

    // 시간대 전환 보간 (2초, ease in-out)
    if (this._todTween) {
      const tw = this._todTween;
      tw.t += delta;
      const p = Math.min(tw.t / tw.dur, 1);
      const k = p * p * (3 - 2 * p);
      this._writeTweenables(tw.items, tw.from, tw.to, k);
      if (p >= 1) this._todTween = null;
    }

    // 경기장 FBX가 비동기로 늘어나므로 로드 후 한 번 더 환경 반사 적용
    this._envFrames = (this._envFrames || 0) + 1;
    if (this._envFrames === 240 || this._envFrames === 900) this.applyEnvironmentDetail();

    if (this.composer) {
      const kb = 1 - Math.exp(-delta * 1.6);
      const tb = this.bloomTargets[this.currentTod] ?? 0.3;
      this.bloomPass.strength += (tb - this.bloomPass.strength) * kb;
      const g = this.gradeTargets[this.currentTod];
      if (g && this.gradePass) {
        const u = this.gradePass.uniforms;
        u.tint.value.lerp(new THREE.Vector3(...g.tint), kb);
        u.shadowTint.value.lerp(new THREE.Vector3(...g.shadow), kb);
        u.sat.value += (g.sat - u.sat.value) * kb;
        u.contrast.value += (g.contrast - u.contrast.value) * kb;
        u.vignette.value += (g.vig - u.vignette.value) * kb;
      }
      const th = this.bloomThresholds[this.currentTod] ?? 1.0;
      this.bloomPass.threshold += (th - this.bloomPass.threshold) * kb;
      this.composer.render(delta);
    } else {
      this.renderer.render(this.scene, this.camera);
    }
  }
}

export { TennisIslandCinematicViewer, ModelInspectModalViewer };
