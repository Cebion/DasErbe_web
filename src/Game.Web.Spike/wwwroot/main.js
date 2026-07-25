// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { dotnet } from './_framework/dotnet.js'

const { setModuleImports, runMain } = await dotnet
    .create();

const canvas = document.getElementById('screen');
const ctx = canvas.getContext('2d');
const statsEl = document.getElementById('stats');
const threadsEl = document.getElementById('threads');

setModuleImports('main.js', {
    canvas: {
        paint: (width, height, pixelsView) => {
            const bytes = pixelsView.slice();
            const imageData = new ImageData(new Uint8ClampedArray(bytes.buffer), width, height);
            ctx.putImageData(imageData, 0, 0);
        },
        reportStats: (frameCount, fps, workerPulses) => {
            statsEl.innerText =
                `frames: ${frameCount}, avg fps: ${fps.toFixed(1)}, background-thread pulses: ${workerPulses}`;
        },
        reportThreadStatus: (status) => {
            threadsEl.innerText =
                `crossOriginIsolated: ${crossOriginIsolated}, SharedArrayBuffer: ${typeof SharedArrayBuffer !== 'undefined'}, background thread: ${status}`;
        }
    }
});

// run the C# Main() method and keep the runtime process running and executing further API calls
await runMain();
