#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Simulador de frota Ressoa — demonstra os fluxos da API de telemetria.
// Uso (.NET 10, app de arquivo único):
//   dotnet run tools/simulador.cs -- [urlJava] [urlDotnet] [email] [senha]
// Padrões: http://localhost:8080  http://localhost:5080  gestor@ressoa.app  $SEED_SENHA_PADRAO (ou Ressoa@2026)
//
// Cenários:
//   1. Dia normal: cada aparelho envia o status diário (201).
//   2. Reenvio por rede instável: mesma Idempotency-Key -> 200 + Idempotency-Replayed (sem duplicar).
//   3. Bateria baixa: status do aparelho vira BATERIA_BAIXA e o app mostra o alerta.
//   4. Volta do sinal: leituras acumuladas offline (horas atrás) são aceitas sem regredir o status.
//   5. Rajada: excede o limite por minuto -> HTTP 429 com Retry-After.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

var urlJava = args.ElementAtOrDefault(0) ?? "http://localhost:8080";
var urlDotnet = args.ElementAtOrDefault(1) ?? "http://localhost:5080";
var email = args.ElementAtOrDefault(2) ?? "gestor@ressoa.app";
var senha = args.ElementAtOrDefault(3) ?? Environment.GetEnvironmentVariable("SEED_SENHA_PADRAO") ?? "Ressoa@2026";
string[] aparelhos = ["RSA-2026-0001", "RSA-2026-0002", "RSA-2026-0003"];

using var http = new HttpClient();

Titulo($"Login na API Java como {email}");
var login = await http.PostAsJsonAsync($"{urlJava}/api/auth/login", new { email, senha });
if (!login.IsSuccessStatusCode)
{
    Console.WriteLine($"Falha no login ({(int)login.StatusCode}). Confira a URL da API Java e as credenciais.");
    return 1;
}
var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
Console.WriteLine("Token JWT recebido; o mesmo token vale na API .NET.");

Titulo("1. Dia normal: status diário de cada aparelho");
foreach (var serie in aparelhos)
{
    await Enviar(new { numeroSerie = serie, bateriaPct = Random.Shared.Next(60, 100), temperaturaAgua = 24.5, pulsoOk = true });
}

Titulo("2. Rede instável: o aparelho reenvia a mesma leitura (mesma Idempotency-Key)");
var chave = Guid.NewGuid().ToString();
var leitura = new { numeroSerie = aparelhos[0], bateriaPct = 77, temperaturaAgua = 25.1, pulsoOk = true };
await Enviar(leitura, chave);
await Enviar(leitura, chave);

Titulo("3. Bateria baixa no aparelho da caixa d'água");
await Enviar(new { numeroSerie = aparelhos[1], bateriaPct = 9, temperaturaAgua = 27.8, pulsoOk = false });
await MostrarStatus(2);

Titulo("4. Sinal voltou: leituras acumuladas offline");
for (var h = 30; h >= 6; h -= 12)
{
    await Enviar(new { numeroSerie = aparelhos[2], bateriaPct = 70, temperaturaAgua = 24.0, pulsoOk = true, registradaEm = DateTime.UtcNow.AddHours(-h) });
}
await MostrarStatus(3);

Titulo("5. Rajada de envios: rate limiting");
for (var i = 1; i <= 12; i++)
{
    var r = await Enviar(new { numeroSerie = aparelhos[0], bateriaPct = 80, temperaturaAgua = 24.0, pulsoOk = true }, silencioso: true);
    Console.Write($"{(int)r.StatusCode} ");
    if ((int)r.StatusCode == 429)
    {
        Console.WriteLine($"\n-> 429 Too Many Requests. Retry-After: {r.Headers.RetryAfter}s");
        break;
    }
}

Titulo("Saúde da API .NET");
Console.WriteLine(await http.GetStringAsync($"{urlDotnet}/health"));
return 0;

async Task<HttpResponseMessage> Enviar(object corpo, string? idempotencyKey = null, bool silencioso = false)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, $"{urlDotnet}/api/v1/leituras") { Content = JsonContent.Create(corpo) };
    req.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
    var resp = await http.SendAsync(req);
    if (!silencioso)
    {
        var replay = resp.Headers.TryGetValues("Idempotency-Replayed", out var v) ? $" (Idempotency-Replayed: {v.First()})" : "";
        Console.WriteLine($"POST /api/v1/leituras -> {(int)resp.StatusCode} {resp.StatusCode}{replay}");
        Console.WriteLine($"   {await resp.Content.ReadAsStringAsync()}");
    }
    return resp;
}

async Task MostrarStatus(long id)
{
    Console.WriteLine($"GET /api/v1/dispositivos/{id}/status");
    Console.WriteLine($"   {await http.GetStringAsync($"{urlDotnet}/api/v1/dispositivos/{id}/status")}");
}

static void Titulo(string texto) => Console.WriteLine($"\n=== {texto} ===");
