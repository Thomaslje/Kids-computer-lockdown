using KioskAgent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "KioskAgent");
builder.Services.AddHostedService<AgentWorker>();
builder.Build().Run();
