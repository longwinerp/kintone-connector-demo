using System.Text.Encodings.Web;
using System.Text.Unicode;
using KintoneConnector.Web.Endpoints;
using KintoneConnector.Web.Options;
using KintoneConnector.Web.Security;
using KintoneConnector.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── 1. 設定 ─────────────────────────────────────────────────────────
builder.Services
    .AddOptions<KintoneOptions>()
    .Bind(builder.Configuration.GetSection(KintoneOptions.SectionName));
builder.Services
    .AddOptions<ConnectorSecurityOptions>()
    .Bind(builder.Configuration.GetSection(ConnectorSecurityOptions.SectionName));

// 中文直接輸出成 UTF-8，不要變成 \uXXXX，方便外部程式與人眼閱讀。
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(
        UnicodeRanges.BasicLatin,
        UnicodeRanges.CjkUnifiedIdeographs,
        UnicodeRanges.CjkSymbolsandPunctuation,
        UnicodeRanges.HalfwidthandFullwidthForms,
        UnicodeRanges.Hiragana,
        UnicodeRanges.Katakana,
        UnicodeRanges.HangulSyllables));

// ── 2. 服務 ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<IKintoneConnectionResolver, KintoneConnectionResolver>();
builder.Services.AddHttpClient<KintoneClient>(client =>
{
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-TW");
});

// Demo 版：示範 profile 走內建假資料，其餘連線照舊打真正的 Kintone。
builder.Services.AddSingleton<IDemoDataSource, DemoDataSource>();
builder.Services.AddTransient<IKintoneClient>(provider => new DemoAwareKintoneClient(
    provider.GetRequiredService<KintoneClient>(),
    provider.GetRequiredService<IDemoDataSource>()));
builder.Services.AddSingleton<GatewayKeyFilter>();

var app = builder.Build();

// ── 3. 管線 ─────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
// 開發時一律不快取，改了 JS／CSS 重新整理就會生效；正式環境才讓瀏覽器長期快取。
var isDevelopment = app.Environment.IsDevelopment();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var isIndex = context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase);
        context.Context.Response.Headers.CacheControl = isDevelopment || isIndex
            ? "no-cache"
            : "public,max-age=604800";
    }
});

// ── 4. 端點 ─────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    service = "KintoneConnector",
    utc = DateTimeOffset.UtcNow
}));

app.MapKintoneEndpoints();
app.MapGet("/error", () => Results.Json(
    new { ok = false, error = "UNEXPECTED_ERROR", message = "伺服器發生未預期錯誤。" },
    statusCode: StatusCodes.Status500InternalServerError));

app.Run();

public partial class Program;
