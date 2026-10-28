import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { FBXLoader } from 'three/addons/loaders/FBXLoader.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';

/* =========================================================================
   스포츠별 3D 모델 및 리깅(애니메이션) 에셋 레지스트리
   (추후 볼링, 검술 모델/리깅 FBX 또는 GLB 파일이 준비되면 아래 경로만 수정하면 즉시 적용됩니다)
========================================================================= */
export const SPORT_MODELS_REGISTRY = {
  tennis: {
    name: '테니스 (Tennis)',
    characterMotion: {
      reap_swing: { path: 'assets/models/character/character.glb', type: 'gltf' },
      spin_attack: { path: 'assets/models/character/spin_attack.glb', type: 'gltf' },
      slash_swing: { path: 'assets/models/character/slash_swing.glb', type: 'gltf' },
      warrior_idle: { path: 'assets/models/character/warrior_idle.fbx', type: 'fbx' },
      tennis_idle: { path: 'assets/models/character/tennis_idle.fbx', type: 'fbx' }
    },
    gear: {
      path: 'assets/models/racket/Tennis_Racket.fbx',
      type: 'fbx'
    },
    ball: {
      path: 'assets/models/ball/tennis_ball.fbx',
      type: 'fbx'
    },
    court: {
      path: 'assets/models/court/Tennis_.fbx',
      type: 'fbx'
    }
  },
  bowling: {
    name: '볼링 (Bowling) - 에셋 파일 준비 시 경로 교체',
    characterMotion: {
      bowling_throw: { path: 'assets/models/character/character.glb', type: 'gltf' }
    },
    gear: { path: 'assets/models/ball/tennis_ball.fbx', type: 'fbx' }
  },
  swordplay: {
    name: '검술 (Chambara) - 에셋 파일 준비 시 경로 교체',
    characterMotion: {
      sword_slash: { path: 'assets/models/character/slash_swing.glb', type: 'gltf' }
    },
    gear: { path: 'assets/models/racket/Tennis_Racket.fbx', type: 'fbx' }
  }
};

/* =========================================================================
   3D Model Showcase Inspector (마우스 드래그 뷰어 모달)
========================================================================= */
export class ModelInspectModalViewer {
  constructor(canvasContainerId) {
    this.container = document.getElementById(canvasContainerId);
    if (!this.container) return;

    this.clock = new THREE.Clock();
    this.textureLoader = new THREE.TextureLoader();
    this.currentObject = null;
    this.mixer = null;
    this.currentModelType = 'character';
    this.racketMesh = null;
    this.ballMesh = null;
    this.isBouncing = true;
    this.ballTime = 0;

    this.initScene();
    this.initLights();
    this.initControls();

    this.animate = this.animate.bind(this);
    requestAnimationFrame(this.animate);
  }

  initScene() {
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0xf0f9ff);

    const w = this.container.clientWidth || 600;
    const h = this.container.clientHeight || 450;

    this.camera = new THREE.PerspectiveCamera(45, w / h, 0.1, 500);
    this.camera.position.set(0, 1.5, 4);

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
    this.renderer.setSize(w, h);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    // [그래픽 업그레이드] 영화 같은 톤매핑 + 스튜디오 IBL 환경광
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;

    const pmrem = new THREE.PMREMGenerator(this.renderer);
    this.scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;

    // 텍스처 sRGB 색공간 보정 + 이방성 필터링
    const maxAniso = this.renderer.capabilities.getMaxAnisotropy();
    const rawLoad = this.textureLoader.load.bind(this.textureLoader);
    this.textureLoader.load = (url, onLoad, onProgress, onError) => {
      const tex = rawLoad(url, onLoad, onProgress, onError);
      if (!/normal/i.test(String(url))) tex.colorSpace = THREE.SRGBColorSpace;
      tex.anisotropy = maxAniso;
      return tex;
    };

    this.container.innerHTML = '';
    this.container.appendChild(this.renderer.domElement);

    const grid = new THREE.GridHelper(16, 16, 0x0284c7, 0xbae6fd);
    grid.position.y = -0.01;
    this.scene.add(grid);
  }

  initLights() {
    const amb = new THREE.AmbientLight(0xffffff, 0.45);
    this.scene.add(amb);

    const dir = new THREE.DirectionalLight(0xfff3e0, 2.2);
    dir.position.set(5, 10, 8);
    dir.castShadow = true;
    dir.shadow.mapSize.set(2048, 2048);
    dir.shadow.camera.left = -8;
    dir.shadow.camera.right = 8;
    dir.shadow.camera.top = 8;
    dir.shadow.camera.bottom = -8;
    dir.shadow.camera.near = 1;
    dir.shadow.camera.far = 40;
    dir.shadow.radius = 3;
    dir.shadow.bias = -0.0004;
    dir.shadow.normalBias = 0.03;
    this.scene.add(dir);

    // 림 라이트: 모델 윤곽을 또렷하게
    const fill = new THREE.DirectionalLight(0x7dd3fc, 1.1);
    fill.position.set(-5, 5, -5);
    this.scene.add(fill);

    // 바닥 접지 그림자 (clearModel에서 제거되지 않도록 keep 표시)
    const shadowPlane = new THREE.Mesh(
      new THREE.PlaneGeometry(40, 40),
      new THREE.ShadowMaterial({ opacity: 0.28 })
    );
    shadowPlane.rotation.x = -Math.PI / 2;
    shadowPlane.position.y = 0.002;
    shadowPlane.receiveShadow = true;
    shadowPlane.userData.keep = true;
    this.scene.add(shadowPlane);
  }

  initControls() {
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.maxPolarAngle = Math.PI / 2 - 0.01;
  }

  fitCamera(obj, offset = 1.3) {
    const box = new THREE.Box3().setFromObject(obj);
    const size = box.getSize(new THREE.Vector3());
    const center = box.getCenter(new THREE.Vector3());

    let maxDim = Math.max(size.x, size.y, size.z);
    if (!isFinite(maxDim) || maxDim <= 0.001) maxDim = 2;

    obj.position.x -= center.x;
    obj.position.y -= box.min.y;
    obj.position.z -= center.z;

    const fitDist = offset * (maxDim / (2 * Math.atan((Math.PI * this.camera.fov) / 360)));
    const dir = new THREE.Vector3(1, 0.6, 1.2).normalize();
    this.camera.position.copy(dir.multiplyScalar(fitDist));
    this.camera.position.y += (size.y || 1) * 0.4;
    this.controls.target.set(0, (size.y || 1) * 0.45, 0);
    this.controls.minDistance = Math.max(0.2, maxDim * 0.2);
    this.controls.maxDistance = Math.max(15, maxDim * 6);
    this.controls.update();
  }

  clearModel() {
    this.currentLoadId = (this.currentLoadId || 0) + 1;
    if (this.currentObject) {
      this.scene.remove(this.currentObject);
      this.currentObject = null;
    }
    // 기존에 씬에 남아있을 수 있는 모델(T-포즈 잔여물 등) 완전 스윕 제거
    const toRemove = [];
    this.scene.children.forEach((child) => {
      if (!child.isLight && !child.isGridHelper && !child.userData.keep) {
        toRemove.push(child);
      }
    });
    toRemove.forEach((obj) => this.scene.remove(obj));

    if (this.mixer) {
      this.mixer.stopAllAction();
      this.mixer = null;
    }
    this.racketMesh = null;
    this.ballMesh = null;
  }

  loadModel(type) {
    this.currentModelType = type;
    this.clearModel();
    const thisLoadId = this.currentLoadId;

    const fbxLoader = new FBXLoader();

    if (type === 'character') {
      this.loadCharacterMotion('reap_swing');
    } else if (type === 'racket') {
      this.racketMeshes = [];
      fbxLoader.load('assets/models/racket/Tennis_Racket.fbx', (racket) => {
        if (thisLoadId !== this.currentLoadId) return;
        this.currentObject = racket;
        const frameTex = this.textureLoader.load('assets/models/racket/Texture/racket_blue.png');
        const strTex = this.textureLoader.load('assets/models/racket/Texture/Tennis_Racket_Tennis_Racket_Strings_AlbedoTransparency.png');

        racket.traverse((child) => {
          if (child.isMesh) {
            child.castShadow = true;
            const name = (child.name || '').toLowerCase();
            if (name.includes('string')) {
              child.material = new THREE.MeshStandardMaterial({
                map: strTex,
                transparent: true,
                alphaTest: 0.3,
                side: THREE.DoubleSide
              });
            } else {
              this.racketMeshes.push(child);
              child.material = new THREE.MeshStandardMaterial({
                map: frameTex,
                roughness: 0.35,
                metalness: 0.2,
                side: THREE.DoubleSide
              });
            }
          }
        });
        this.scene.add(racket);
        this.fitCamera(racket, 1.4);
      });
    } else if (type === 'ball') {
      this.isBouncing = false;
      fbxLoader.load('assets/models/ball/tennis_ball.fbx', (ball) => {
        if (thisLoadId !== this.currentLoadId) return;
        this.currentObject = ball;
        this.ballMesh = ball;

        const ballTex = this.textureLoader.load('assets/models/ball/Texture/tennis_ball_tennis_ball1_AlbedoTransparency.png');
        const ballNorm = this.textureLoader.load('assets/models/ball/Texture/tennis_ball_tennis_ball1_Normal.png');

        ball.traverse((child) => {
          if (child.isMesh) {
            child.castShadow = true;
            child.material = new THREE.MeshStandardMaterial({
              map: ballTex,
              normalMap: ballNorm,
              roughness: 0.85,
              metalness: 0.05
            });
          }
        });
        this.scene.add(ball);
        this.fitCamera(ball, 1.4);
      });
    } else if (type === 'court') {
      fbxLoader.load('assets/models/court/Tennis_.fbx', (court) => {
        if (thisLoadId !== this.currentLoadId) return;
        this.currentObject = court;
        court.rotation.y = Math.PI / 2;
        const courtTex = this.textureLoader.load('assets/models/court/Texture/Tennis__Tennis_AlbedoTransparency.png');
        court.traverse((c) => {
          if (c.isMesh) {
            c.receiveShadow = true;
            const name = (c.name || '').toLowerCase();
            const isFloor = !name.includes('chair') && !name.includes('extra') && !name.includes('net') && !name.includes('post');
            c.castShadow = !isFloor;
            c.material = new THREE.MeshStandardMaterial({ map: courtTex, roughness: 0.65, side: THREE.DoubleSide });
          }
        });
        this.scene.add(court);
        this.fitCamera(court, 1.25);
      });
    }
  }

  /* 캐릭터 5가지 역동적인 모션 교체 로더 */
  loadCharacterMotion(motionKey = 'reap_swing') {
    this.clearModel();
    this.currentModelType = 'character';
    const thisLoadId = this.currentLoadId;

    const motionMap = {
      reap_swing: { path: 'assets/models/character/character.glb', type: 'gltf' },
      spin_attack: { path: 'assets/models/character/spin_attack.glb', type: 'gltf' },
      slash_swing: { path: 'assets/models/character/slash_swing.glb', type: 'gltf' },
      warrior_idle: { path: 'assets/models/character/warrior_idle.fbx', type: 'fbx' },
      tennis_idle: { path: 'assets/models/character/tennis_idle.fbx', type: 'fbx' }
    };

    const target = motionMap[motionKey] || motionMap.reap_swing;

    if (target.type === 'gltf') {
      const gltfLoader = new GLTFLoader();
      gltfLoader.load(target.path, (gltf) => {
        if (thisLoadId !== this.currentLoadId) return; // 이전 비동기 호출 잔여물 무시
        const char = gltf.scene;
        this.currentObject = char;
        char.traverse((c) => {
          if (c.isMesh) {
            c.castShadow = true;
            if (c.material) c.material.side = THREE.DoubleSide;
          }
        });
        this.scene.add(char);
        this.fitCamera(char, 2.2);

        if (gltf.animations && gltf.animations.length > 0) {
          this.mixer = new THREE.AnimationMixer(char);
          const act = this.mixer.clipAction(gltf.animations[0]);
          act.play();
        }
      });
    } else {
      const fbxLoader = new FBXLoader();
      fbxLoader.load(target.path, (charFbx) => {
        if (thisLoadId !== this.currentLoadId) return;
        this.currentObject = charFbx;
        charFbx.scale.setScalar(0.015);
        charFbx.traverse((c) => {
          if (c.isMesh) {
            c.castShadow = true;
            if (c.material) c.material.side = THREE.DoubleSide;
          }
        });
        this.scene.add(charFbx);
        this.fitCamera(charFbx, 2.2);

        if (charFbx.animations && charFbx.animations.length > 0) {
          this.mixer = new THREE.AnimationMixer(charFbx);
          const act = this.mixer.clipAction(charFbx.animations[0]);
          act.play();
        }
      });
    }
  }

  setRacketColor(colorName) {
    if (!this.racketMeshes || this.racketMeshes.length === 0) return;
    const colorMap = {
      blue: 'assets/models/racket/Texture/racket_blue.png',
      red: 'assets/models/racket/Texture/racket_red.png',
      green: 'assets/models/racket/Texture/racket_green.png',
      orange: 'assets/models/racket/Texture/racket_orange.png',
      pink: 'assets/models/racket/Texture/racket_pink.png',
      yellow: 'assets/models/racket/Texture/racket_yellow.png'
    };
    const path = colorMap[colorName] || colorMap.blue;
    const tex = this.textureLoader.load(path);
    tex.colorSpace = THREE.SRGBColorSpace;

    this.racketMeshes.forEach((mesh) => {
      mesh.material.map = tex;
      mesh.material.needsUpdate = true;
    });
  }

  resize() {
    if (!this.container || !this.renderer || !this.camera) return;
    const w = this.container.clientWidth || 600;
    const h = this.container.clientHeight || 450;
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h);
  }

  animate() {
    requestAnimationFrame(this.animate);
    const delta = this.clock.getDelta();

    if (this.mixer) {
      this.mixer.update(delta);
    }

    if (this.currentModelType === 'ball' && this.ballMesh && this.isBouncing) {
      this.ballTime += delta * 4;
      this.ballMesh.position.y = Math.abs(Math.sin(this.ballTime)) * 0.8;
      this.ballMesh.rotation.x += delta * 2;
    }

    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  }
}
