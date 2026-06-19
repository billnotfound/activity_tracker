// WinActivityTracker.Web — production static file server for the Vue SPA.
//
// In development, this is NOT used. Instead, run:
//   cd WinActivityTracker.Web && pnpm dev
// That starts the Vite dev server with HMR and API proxy to :32579.
//
// In production, run:
//   cd WinActivityTracker.Web && pnpm build
//   dotnet run
// This serves the built files from wwwroot/ with SPA fallback (all routes → index.html).
//
// The Service project can also serve the frontend directly from its DashboardServer
// (on-demand Kestrel), so this standalone host is optional and only needed if you
// want to serve the SPA separately from the monitoring backend.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Serve static files (JS, CSS, assets) from wwwroot/
app.UseDefaultFiles();
app.UseStaticFiles();

// SPA fallback: any unmatched route serves index.html so Vue Router handles it.
// This is essential — without it, refreshing /history would 404.
app.MapFallbackToFile("index.html");

await app.RunAsync();
