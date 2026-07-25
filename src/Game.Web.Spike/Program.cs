using System.Threading.Tasks;

System.Console.WriteLine("Hello, Browser! Starting render-backend + threading spike.");

ThreadPumpSpike.Start();
RenderLoop.Run();

// Give the async loop the process; JSExport-callable methods stay reachable from JS meanwhile.
await Task.Delay(-1);
