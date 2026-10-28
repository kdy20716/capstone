import { TennisIslandCinematicViewer, ModelInspectModalViewer } from './viewer3d.js';

function initApp() {
  let islandViewer = null;
  let inspectViewer = null;

  // 1. Initialize Background Island 3D Viewer
  const islandCanvas = document.getElementById('island-canvas-container');
  if (islandCanvas) {
    try {
      islandViewer = new TennisIslandCinematicViewer('island-canvas-container');
    } catch (e) {
      console.error('Failed to init TennisIslandCinematicViewer:', e);
    }
  }

  // (확인용) ?tod=day|sunset|night 로 시간대 강제 지정
  const stepParam = new URLSearchParams(location.search).get('step');
  if (stepParam !== null) {
    const initIdx = parseInt(stepParam, 10);
    setTimeout(() => {
      if (typeof goToStep === 'function') {
        goToStep(initIdx, true);
        targetProgress = stepProgressMap[initIdx] || 0;
        currentAnimatedProgress = targetProgress;
        if (islandViewer) {
          islandViewer.updateCameraForScroll(targetProgress);
          if (islandViewer.targetCamPos) islandViewer.currentCamPos.copy(islandViewer.targetCamPos);
          if (islandViewer.targetLookAt) islandViewer.currentLookAt.copy(islandViewer.targetLookAt);
        }
      }
    }, 500);
  }
  const todParam = new URLSearchParams(location.search).get('tod');
  if (islandViewer && todParam) islandViewer.setTimeOfDay(todParam);

  // 2. Initialize Model Inspect Modal Viewer
  const inspectCanvas = document.getElementById('inspect-canvas-container');
  if (inspectCanvas) {
    try {
      inspectViewer = new ModelInspectModalViewer('inspect-canvas-container');
    } catch (e) {
      console.error('Failed to init ModelInspectModalViewer:', e);
    }
  }

  // Web Audio for bubbly popping feedback
  const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
  let soundEnabled = true;

  function playSound(type = 'pop') {
    if (!soundEnabled || !audioCtx) return;
    if (audioCtx.state === 'suspended') {
      audioCtx.resume();
    }
    const osc = audioCtx.createOscillator();
    const gain = audioCtx.createGain();
    osc.connect(gain);
    gain.connect(audioCtx.destination);

    const now = audioCtx.currentTime;
    if (type === 'whoosh') {
      osc.type = 'sine';
      osc.frequency.setValueAtTime(320, now);
      osc.frequency.exponentialRampToValueAtTime(680, now + 0.12);
      gain.gain.setValueAtTime(0.2, now);
      gain.gain.exponentialRampToValueAtTime(0.01, now + 0.12);
      osc.start(now);
      osc.stop(now + 0.12);
    } else {
      osc.type = 'triangle';
      osc.frequency.setValueAtTime(540, now);
      osc.frequency.exponentialRampToValueAtTime(220, now + 0.08);
      gain.gain.setValueAtTime(0.15, now);
      gain.gain.exponentialRampToValueAtTime(0.01, now + 0.08);
      osc.start(now);
      osc.stop(now + 0.08);
    }
  }

  // =======================================================
  // Background Music (BGM): assets/music/메인테마곡.mp3 연결
  // =======================================================
  const bgm = new Audio('assets/music/메인테마곡.mp3');
  bgm.loop = true;
  bgm.volume = 0.5;
  let isMusicPlaying = false;

  const soundBtn = document.getElementById('btn-sound-toggle');

  function startBgm() {
    if (isMusicPlaying) return;
    bgm.play()
      .then(() => {
        isMusicPlaying = true;
        if (soundBtn) soundBtn.innerHTML = '🔊 ON';
      })
      .catch((e) => {
        console.log('Autoplay deferred until user interaction:', e);
      });
  }

  function toggleMusic() {
    if (isMusicPlaying) {
      bgm.pause();
      isMusicPlaying = false;
      if (soundBtn) soundBtn.innerHTML = '🔇 OFF';
      soundEnabled = false;
    } else {
      bgm.play().then(() => {
        isMusicPlaying = true;
        if (soundBtn) soundBtn.innerHTML = '🔊 ON';
        soundEnabled = true;
      });
    }
  }

  soundBtn?.addEventListener('click', () => {
    toggleMusic();
    playSound('pop');
  });

  // =======================================================
  // Time of Day (시간대) 토글: 낮 ➔ 노을 저녁 ➔ 밤 순환
  // =======================================================
  const todBtn = document.getElementById('btn-tod-toggle');
  if (todBtn && islandViewer) {
    todBtn.addEventListener('click', () => {
      const nextMode = islandViewer.cycleTimeOfDay();
      playSound('whoosh');

      if (nextMode === 'day') {
        todBtn.innerHTML = '🌅 노을 모드';
        todBtn.classList.remove('tod-sunset', 'tod-night');
      } else if (nextMode === 'sunset') {
        todBtn.innerHTML = '🌙 나이트 모드';
        todBtn.classList.remove('tod-night');
        todBtn.classList.add('tod-sunset');
      } else if (nextMode === 'night') {
        todBtn.innerHTML = '☀️ 데이 모드';
        todBtn.classList.remove('tod-sunset');
        todBtn.classList.add('tod-night');
      }
    });
  }

  // 사용자의 첫 인터랙션(클릭, 키 입력 등) 시 BGM 자연스러운 시작 시도
  const handleFirstInteraction = () => {
    startBgm();
    window.removeEventListener('click', handleFirstInteraction);
    window.removeEventListener('wheel', handleFirstInteraction);
    window.removeEventListener('keydown', handleFirstInteraction);
  };
  window.addEventListener('click', handleFirstInteraction, { once: true });
  window.addEventListener('wheel', handleFirstInteraction, { once: true });
  window.addEventListener('keydown', handleFirstInteraction, { once: true });

  // =======================================================
  // Toast Message Cards: 살짝만 굴려도 슉슉 반응하는 스냅 스크롤 제어
  // =======================================================
  const progressBar = document.getElementById('scroll-progress-indicator');
  const skyHint = document.getElementById('sky-hint-pill');
  const heroLogo = document.getElementById('hero-brand-container');

  let currentActiveIndex = 0;
  const TOTAL_STEPS = 14; // 0: 하늘(낮), 1: 테니스 인트로, 2~4: 테니스 설명, 5: 볼링 인트로(노을), 6~8: 볼링 설명, 9: 검술 인트로(밤), 10~12: 검술 설명, 13: 피날레
  let isTransitioning = false;
  let targetProgress = 0;
  let currentAnimatedProgress = 0;

  // Step별 카메라 시네마틱 위치 매핑
  const stepProgressMap = [
    0.0,   // Step 0: 오프닝 스카이뷰 (낮)
    0.08,  // Step 1: 🎾 테니스 경기장 외경 인트로 & 테마 일러스트 (낮)
    0.15,  // Step 2: 🎾 테니스 코트 전경 (낮)
    0.21,  // Step 3: 🎾 테니스 랠리 & 피직스 (낮)
    0.27,  // Step 4: 🎾 테니스 라켓 & 장비 (낮)
    0.44,  // Step 5: 🎳 볼링 경기장 외경 인트로 & 테마 일러스트 (노을)
    0.51,  // Step 6: 🎳 볼링 돔 전경 (노을)
    0.57,  // Step 7: 🎳 볼링 스핀 훅 & 레인 (노을)
    0.63,  // Step 8: 🎳 볼링 프로 볼 & 장비 (노을)
    0.78,  // Step 9: ⚔️ 검술 경기장 외경 인트로 & 테마 일러스트 (밤)
    0.84,  // Step 10: ⚔️ 검술 아레나 전경 (밤)
    0.89,  // Step 11: ⚔️ 검술 공방 & 패링 (밤)
    0.94,  // Step 12: ⚔️ 검술 네온 블레이드 & 장비 (밤)
    1.00   // Step 13: 🏆 3대 경기장 항공뷰 피날레 (밤)
  ];

  // 경기장별 시간대 매핑: 테니스(0~4)=낮, 볼링(5~8)=노을, 검술/항공뷰(9~13)=밤
  const stepTimeMap = {
    0: 'day',
    1: 'day',
    2: 'day',
    3: 'day',
    4: 'day',
    5: 'sunset',
    6: 'sunset',
    7: 'sunset',
    8: 'sunset',
    9: 'night',
    10: 'night',
    11: 'night',
    12: 'night',
    13: 'night'
  };

  let enterStadiumTimer = null;

  function goToStep(newIndex, isForward = true) {
    if (newIndex < 0 || newIndex >= TOTAL_STEPS) return;
    if (newIndex === currentActiveIndex) return;

    clearTimeout(enterStadiumTimer);

    const oldCard = document.querySelector(`.floating-story-card[data-step="${currentActiveIndex}"]`);
    const newCard = document.querySelector(`.floating-story-card[data-step="${newIndex}"]`);

    if (oldCard) {
      oldCard.classList.remove('active', 'slide-out-up', 'slide-out-down');
      oldCard.classList.add(isForward ? 'slide-out-up' : 'slide-out-down');
    }

    // 경기장 간 이동(테니스->볼링, 볼링->검술, 검술->항공뷰) 시
    // 카메라가 여유롭게 날아간 뒤 카드가 뜨도록 부드러운 타이밍 연출
    const isCrossFlight = (
      (newIndex === 1 && currentActiveIndex === 0) ||
      (currentActiveIndex <= 4 && newIndex >= 5) ||
      (currentActiveIndex >= 5 && currentActiveIndex <= 8 && newIndex >= 9) ||
      (currentActiveIndex <= 12 && newIndex === 13) ||
      (newIndex === 0)
    );

    if (isCrossFlight && newIndex > 0) {
      if (newCard) {
        newCard.classList.remove('active', 'slide-out-up', 'slide-out-down');
      }
      enterStadiumTimer = setTimeout(() => {
        if (currentActiveIndex === newIndex && newCard) {
          newCard.classList.remove('slide-out-up', 'slide-out-down');
          newCard.classList.add('active');
          playSound('whoosh');
        }
      }, 550);
    } else {
      if (newCard) {
        newCard.classList.remove('slide-out-up', 'slide-out-down');
        newCard.classList.add('active');
      }
      playSound('whoosh');
    }

    currentActiveIndex = newIndex;
    targetProgress = stepProgressMap[newIndex] || 0;

    // 경기장 이동에 따른 시간대(Time of Day) 자동 전환: 테니스=낮, 볼링=노을, 검술/항공뷰=밤
    const targetTod = stepTimeMap[newIndex] || 'day';
    if (islandViewer && islandViewer.currentTod !== targetTod) {
      islandViewer.setTimeOfDay(targetTod);

      // 상단 시간대 토글 버튼 텍스트 & 스타일 동기화
      if (todBtn) {
        if (targetTod === 'day') {
          todBtn.innerHTML = '🌅 노을 모드';
          todBtn.classList.remove('tod-sunset', 'tod-night');
        } else if (targetTod === 'sunset') {
          todBtn.innerHTML = '🌙 나이트 모드';
          todBtn.classList.remove('tod-night');
          todBtn.classList.add('tod-sunset');
        } else if (targetTod === 'night') {
          todBtn.innerHTML = '☀️ 데이 모드';
          todBtn.classList.remove('tod-sunset');
          todBtn.classList.add('tod-night');
        }
      }
    }

    // 상단 얇은 프로그레스 바 갱신
    if (progressBar) {
      progressBar.style.width = newIndex === 0 ? '0%' : `${(newIndex / (TOTAL_STEPS - 1)) * 100}%`;
    }

    // 첫 스텝 벗어나면 하늘 힌트 및 중앙 메인 로고 스르륵 숨기기, 첫 스텝(0) 복귀 시 뿅 나타나기
    if (heroLogo) {
      if (newIndex > 0) {
        heroLogo.classList.add('hidden');
      } else {
        heroLogo.classList.remove('hidden');
      }
    }

    if (skyHint) {
      if (newIndex > 0) skyHint.classList.add('hidden');
      else skyHint.classList.remove('hidden');
    }
  }

  // 휠을 살짝만 돌려도 즉각 즉각 전환되는 고감도 인터셉터
  let wheelTimeout = null;
  window.addEventListener(
    'wheel',
    (e) => {
      // 3D 검사 모달이 열려있을 때는 모달 내부 줌/조작을 위해 페이지 전환 스킵
      if (inspectModal && inspectModal.classList.contains('active')) return;

      e.preventDefault();

      if (isTransitioning) return;

      const threshold = 12; // 살짝만 움직여도 반응
      if (Math.abs(e.deltaY) < threshold) return;

      isTransitioning = true;
      if (e.deltaY > 0) {
        // 휠 내림 -> 다음 스텝
        goToStep(currentActiveIndex + 1, true);
      } else {
        // 휠 올림 -> 이전 스텝
        goToStep(currentActiveIndex - 1, false);
      }

      // 380ms 쿨다운 후 다음 휠 입력 허용 (부드러운 전환감 보장)
      clearTimeout(wheelTimeout);
      wheelTimeout = setTimeout(() => {
        isTransitioning = false;
      }, 380);
    },
    { passive: false }
  );

  // 키보드 방향키 제어
  window.addEventListener('keydown', (e) => {
    if (inspectModal && inspectModal.classList.contains('active')) return;
    if (['ArrowDown', 'PageDown', ' '].includes(e.key)) {
      e.preventDefault();
      goToStep(currentActiveIndex + 1, true);
    } else if (['ArrowUp', 'PageUp'].includes(e.key)) {
      e.preventDefault();
      goToStep(currentActiveIndex - 1, false);
    }
  });

  // 모바일 터치 스와이프 제어
  let touchStartY = 0;
  window.addEventListener(
    'touchstart',
    (e) => {
      if (e.touches.length > 0) touchStartY = e.touches[0].clientY;
    },
    { passive: true }
  );

  window.addEventListener(
    'touchend',
    (e) => {
      if (inspectModal && inspectModal.classList.contains('active')) return;
      if (e.changedTouches.length > 0) {
        const touchEndY = e.changedTouches[0].clientY;
        const diffY = touchStartY - touchEndY;
        if (Math.abs(diffY) > 30) {
          if (diffY > 0) goToStep(currentActiveIndex + 1, true);
          else goToStep(currentActiveIndex - 1, false);
        }
      }
    },
    { passive: true }
  );

  // 카메라 프로그레스 부드러운 애니메이션 루프 (더 천천히 부드럽게 활공하도록 0.052 적용)
  function animateCameraStep() {
    requestAnimationFrame(animateCameraStep);
    const diff = targetProgress - currentAnimatedProgress;
    if (Math.abs(diff) > 0.0005) {
      currentAnimatedProgress += diff * 0.052;
      if (islandViewer) {
        islandViewer.updateCameraForScroll(currentAnimatedProgress);
      }
    }
  }
  requestAnimationFrame(animateCameraStep);

  // =======================================================
  // 3D Model Inspect Modal Window ("슉 생기는 파트")
  // =======================================================
  const inspectModal = document.getElementById('model-inspect-modal');
  const btnCloseInspect = document.getElementById('btn-close-inspect');
  const btnOpenModal = document.getElementById('btn-open-3d-modal');
  const btnTriggerViewer = document.getElementById('btn-trigger-3d-viewer');
  const btnReInspect = document.getElementById('btn-re-inspect');
  const btnRestartTour = document.getElementById('btn-restart-tour');

  function openInspectModal(initialModel = 'character') {
    if (!inspectModal) return;
    inspectModal.classList.add('active');
    playSound('whoosh');

    if (inspectViewer) {
      setTimeout(() => {
        inspectViewer.resize();
        inspectViewer.loadModel(initialModel);
        updateInspectTabs(initialModel);
      }, 50);
    }
  }

  function closeInspectModal() {
    if (!inspectModal) return;
    inspectModal.classList.remove('active');
    playSound('pop');
  }

  btnOpenModal?.addEventListener('click', () => openInspectModal('character'));
  btnTriggerViewer?.addEventListener('click', () => openInspectModal('character'));
  btnReInspect?.addEventListener('click', () => openInspectModal('character'));
  btnCloseInspect?.addEventListener('click', closeInspectModal);

  // 각 스토리 카드의 "3D 모델 둘러보기" 버튼들 일괄 연동
  const inspectSportBtns = document.querySelectorAll('.btn-open-sport-inspect');
  inspectSportBtns.forEach((btn) => {
    btn.addEventListener('click', () => {
      openInspectModal('character');
    });
  });

  // 피날레 카드의 "처음부터 다시 둘러보기" 버튼
  btnRestartTour?.addEventListener('click', () => {
    goToStep(0, false);
    playSound('pop');
  });

  inspectModal?.addEventListener('click', (e) => {
    if (e.target === inspectModal) closeInspectModal();
  });

  // Modal Model Tabs (Character, Racket 6-colors, Ball, Court)
  const tabBtns = document.querySelectorAll('.inspect-tab-btn');
  const characterMotionRow = document.getElementById('character-motion-row');
  const racketColorRow = document.getElementById('racket-color-row');
  const modelTipText = document.getElementById('inspect-model-tip');

  const tipsMap = {
    character: '마우스 드래그로 360° 회전하고, 아래 모션 버튼으로 5가지 동작을 감상하세요!',
    racket: '아래 6가지 컬러 버튼을 눌러 라켓의 컬러와 텍스처를 실시간으로 바꿔보세요!',
    ball: '공인구 텍스처와 노멀맵이 적용된 테니스 볼을 마우스 드래그로 자유롭게 둘러보세요!',
    court: '마우스 드래그로 챔피언십 테니스 코트 라인과 네트를 둘러보세요!'
  };

  function updateInspectTabs(type) {
    tabBtns.forEach((b) => b.classList.toggle('active', b.getAttribute('data-model') === type));
    if (characterMotionRow) {
      characterMotionRow.style.display = type === 'character' ? 'flex' : 'none';
    }
    if (racketColorRow) {
      racketColorRow.style.display = type === 'racket' ? 'flex' : 'none';
    }
    if (modelTipText && tipsMap[type]) {
      modelTipText.innerText = tipsMap[type];
    }
  }

  tabBtns.forEach((btn) => {
    btn.addEventListener('click', () => {
      const model = btn.getAttribute('data-model');
      if (inspectViewer && model) {
        playSound('pop');
        inspectViewer.loadModel(model);
        updateInspectTabs(model);
      }
    });
  });

  // Character Motion Buttons (5개 모션 실시간 전환)
  const motionBtns = document.querySelectorAll('.motion-btn');
  motionBtns.forEach((btn) => {
    btn.addEventListener('click', () => {
      const motion = btn.getAttribute('data-motion');
      if (inspectViewer && motion) {
        motionBtns.forEach((b) => b.classList.remove('active'));
        btn.classList.add('active');
        playSound('pop');
        inspectViewer.loadCharacterMotion(motion);
      }
    });
  });

  // 6 Racket Color Buttons (텍스처 실시간 교체)
  const paletteBtns = document.querySelectorAll('.palette-btn');
  paletteBtns.forEach((btn) => {
    btn.addEventListener('click', () => {
      const color = btn.getAttribute('data-color');
      if (inspectViewer && color) {
        paletteBtns.forEach((b) => b.classList.remove('active'));
        btn.classList.add('active');
        playSound('pop');
        inspectViewer.setRacketColor(color);
      }
    });
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initApp);
} else {
  initApp();
}
