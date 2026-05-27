using GrpcHttp3Demo.Composition;

var builder = WebApplication.CreateBuilder(args);

// 组合根：模块注册（一级顺序在这里显式体现）
builder.ConfigureModules();

var app = builder.Build();

// 组合根：管道与路由（一级顺序在这里显式体现）
app.ConfigurePipeline();

app.Run();

