using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHttpClient(nameof(SomeBackgroundService), o => o.BaseAddress = new Uri("https://localhost:7070"))
    .AddHttpMessageHandler<SomeDelegatingHandler>();
builder.Services.AddHostedService<SomeBackgroundService>();
builder.Services
    .AddGrpcClient<Greeter.GreeterClient>(o => o.Address = new Uri("https://localhost:7070"))
    .AddInterceptor<SomeGrpcInterceptor>()
    .AddHttpMessageHandler<SomeDelegatingHandler>();

builder.Services.AddSingleton<SomeGrpcInterceptor>();
builder.Services.AddTransient<SomeDelegatingHandler>();

var app = builder.Build();

app.MapGet("/", async (HttpContext ctx,HttpClient client, ILogger<Program> logger) =>
{
    var receivedTraceParent = ctx.Request.Headers["traceparent"].FirstOrDefault();
    logger.LogInformation("received traceparent is {TraceParent}", receivedTraceParent ?? "null");
    logger.LogInformation("activity id is {ActivityId}", Activity.Current?.Id ?? "null");

    // out of the box, will hit the server app, including the traceparent header with sampled flag set to false
    // so the server app will not send the trace to the collector (unless we force always sampling on the server app)
    var response = await client.GetAsync("https://localhost:7070");
    var content = await response.Content.ReadAsStringAsync();
    return "Got response: " + content;
});

app.Run();

public class SomeBackgroundService(
    IHttpClientFactory clientFactory,
    IServiceScopeFactory scopeFactory, 
    ILogger<SomeBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("activity id is {ActivityId}", Activity.Current?.Id ?? "null");

        // out of the box, will hit the server app, not including the traceparent header (because there's no activity in this scope)
        // so the server app will send the trace to the collector
        var client = clientFactory.CreateClient(nameof(SomeBackgroundService));
        _ = await client.GetAsync("https://localhost:7070", stoppingToken);

        // out of the box, will hit the server app, including the traceparent header with sampled flag set to false
        // so the server app will not send the trace to the collector (unless we force always sampling on the server app)
        // the traceparent header is sent in this case and not the above, because somewhere along the way
        // some activity is created
        using var scope = scopeFactory.CreateScope();
        var grpcClient = scope.ServiceProvider.GetRequiredService<Greeter.GreeterClient>();
        _ = await grpcClient.SayHelloAsync(new HelloRequest { Name = "Tester" }, cancellationToken: stoppingToken);
    }
}

public class SomeGrpcInterceptor(ILogger<SomeGrpcInterceptor> logger) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        logger.LogInformation("activity id is {ActivityId}", Activity.Current?.Id ?? "null");
        return base.AsyncUnaryCall(request, context, continuation);
    }
}

public class SomeDelegatingHandler(ILogger<SomeDelegatingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        logger.LogInformation("activity id is {ActivityId}", Activity.Current?.Id ?? "null");
        return await base.SendAsync(request, cancellationToken);
    }
}