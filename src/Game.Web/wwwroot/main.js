import { dotnet } from './_framework/dotnet.js'

const canvas = document.getElementById('screen');
const ctx = canvas.getContext('2d');
const statusEl = document.getElementById('status');

let audioEl = null;

const { setModuleImports, getAssemblyExports, runMain } = await dotnet.create();

setModuleImports('main.js', {
    env: {
        getBaseUri: () => document.baseURI
    },
    canvas: {
        paint: (width, height, pixelsView) => {
            const bytes = pixelsView.slice();
            const imageData = new ImageData(new Uint8ClampedArray(bytes.buffer), width, height);
            ctx.putImageData(imageData, 0, 0);
        }
    },
    audio: {
        play: (url, repeat, volume) => {
            if (audioEl) {
                audioEl.pause();
            }
            audioEl = new Audio(url);
            audioEl.loop = repeat;
            audioEl.volume = volume;
            audioEl.play().catch(err => console.error('audio play failed', err));
        },
        setPaused: (paused) => {
            if (!audioEl) return;
            if (paused) audioEl.pause();
            else audioEl.play().catch(err => console.error('audio resume failed', err));
        },
        setVolume: (volume) => {
            if (audioEl) audioEl.volume = volume;
        },
        stop: () => {
            if (audioEl) {
                audioEl.pause();
                audioEl.currentTime = 0;
                audioEl = null;
            }
        }
    },
    app: {
        reportStatus: (status) => {
            statusEl.innerText = status;
        }
    }
});

const exports = await getAssemblyExports('Game.Web');
const gameApp = exports.Game.Web.GameApp;
const inputBackend = exports.Game.Web.WebInputBackend;

canvas.addEventListener('mousemove', e => {
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    const x = (e.clientX - rect.left) * scaleX;
    const y = (e.clientY - rect.top) * scaleY;
    inputBackend.OnMouseMove(x, y, canvas.width, canvas.height);
});
canvas.addEventListener('mouseleave', () => inputBackend.OnMouseLeave());
canvas.addEventListener('mousedown', e => {
    if (e.button === 0) inputBackend.OnMouseButton(true);
});
window.addEventListener('mouseup', e => {
    if (e.button === 0) inputBackend.OnMouseButton(false);
});

const handledKeyCodes = new Set([
    'Space', 'Enter', 'NumpadEnter', 'Backspace', 'Tab',
    'Digit0', 'Digit1', 'Digit2', 'Digit3', 'Digit4', 'Digit5', 'Digit6', 'Digit7', 'Digit8', 'Digit9',
    'Numpad0', 'Numpad1', 'Numpad2', 'Numpad3', 'Numpad4', 'Numpad5', 'Numpad6', 'Numpad7', 'Numpad8', 'Numpad9'
]);
window.addEventListener('keydown', e => {
    if (handledKeyCodes.has(e.code)) e.preventDefault();
    inputBackend.OnKeyDown(e.code);
});
window.addEventListener('keyup', e => {
    if (handledKeyCodes.has(e.code)) e.preventDefault();
    inputBackend.OnKeyUp(e.code);
});

// run the C# Main() method, which keeps the runtime process running for further API calls
const runMainPromise = runMain();

await gameApp.Boot();

function frame(timestampMs) {
    gameApp.Tick(timestampMs);
    requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

await runMainPromise;
