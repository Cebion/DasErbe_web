using System.Threading.Tasks;

// Keep the WASM module's process alive so JS can keep invoking GameApp's [JSExport] entry points
// (Boot/Tick) after Main returns - the runtime loop itself lives on its own dedicated Thread.
await Task.Delay(-1);
