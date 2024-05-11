using Grpc.Core;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resourceBuilder => resourceBuilder.AddService(
        serviceName: "server-app",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        serviceInstanceId: Environment.MachineName))
    .WithTracing(traceBuilder => traceBuilder
        //.SetSampler(new AlwaysOnSampler())
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri("http://localhost:4317")));

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<GreeterService>();

app.MapGet("/", (HttpContext ctx) => "traceparent is " + ctx.Request.Headers["traceparent"]);

app.Run();

public class GreeterService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HelloReply
        {
            Message = $"traceparent is {context.RequestHeaders.Get("traceparent")?.Value ?? "null"}"
        });
    }
}