using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DukkanPaneli.Api.Features.Assistant;
using DukkanPaneli.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DukkanPaneli.Tests.Api;

/// <summary>
/// Asistan uç noktaları. Gerçek Claude API'si yerine <see cref="FakeModel"/> kullanılır:
/// modele ne gönderildiğini kaydeder ve önceden belirlenen cevabı döndürür.
/// </summary>
public sealed class AssistantTests : IDisposable
{
    private sealed class FakeModel : IAssistantModel
    {
        public bool IsConfigured { get; set; } = true;
        public string ModelName => "sahte-model";
        public string Answer { get; set; } = "Tamam.";
        public bool Refuse { get; set; }
        public string? LastSystem { get; private set; }
        public IReadOnlyList<AssistantTurn> LastTurns { get; private set; } = [];

        public Task<AssistantModelReply> CompleteAsync(string system, IReadOnlyList<AssistantTurn> turns, CancellationToken ct)
        {
            LastSystem = system;
            LastTurns = turns;
            return Task.FromResult(new AssistantModelReply(Refuse ? "" : Answer, Refuse));
        }
    }

    private readonly TestApp _base = new();
    private readonly WebApplicationFactory<Program> _app;
    private readonly FakeModel _model = new();
    private readonly HttpClient _http;

    public AssistantTests()
    {
        _app = _base.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IAssistantModel>(_model)));
        _http = Client();
    }

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
        _base.Dispose();
    }

    private HttpClient Client()
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.Add("X-Requested-With", "dukkan");
        return c;
    }

    private async Task Setup()
    {
        (await _http.PostAsJsonAsync("/api/auth/setup", new { shopName = "Test", displayName = "Ahmet Sahip", userName = "sahip", password = "gizli-sifre-1" })).EnsureSuccessStatusCode();
        (await _http.PostAsJsonAsync("/api/users", new { userName = "fatma", displayName = "Fatma Kaya", role = "Calisan", password = "fatma-sifre-1" })).EnsureSuccessStatusCode();
        (await _http.PostAsJsonAsync("/api/debts", new { kind = "Alacak", party = "Ayşe Yılmaz", amount = 500m, date = "2019-12-01", dueDate = "2020-01-01" })).EnsureSuccessStatusCode();
        (await _http.PostAsJsonAsync("/api/products", new { name = "Çay", category = "", unit = "adet", stock = 3, capacity = 50, minStock = 5, cost = 60, price = 100, vatRate = 20 })).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> Ask(string question, object? history = null) =>
        await _http.PostAsJsonAsync("/api/assistant/ask", new { question, history });

    [Fact]
    public async Task Ozet_ve_soru_modele_gider_kisisel_adlar_takma_adla_gider_cevapta_geri_gelir()
    {
        await Setup();
        _model.Answer = "Müşteri-1 ödemesini geciktirdi; Çalışan-1 bu ay satış yapmadı.";

        var r = await Ask("Kimden alacağım var?");
        r.EnsureSuccessStatusCode();
        var answer = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answer").GetString();
        Assert.Equal("Ayşe Yılmaz ödemesini geciktirdi; Fatma Kaya bu ay satış yapmadı.", answer);

        var sent = Assert.Single(_model.LastTurns);
        Assert.Equal("user", sent.Role);
        Assert.Contains("## Stok", sent.Text);
        Assert.Contains("Çay [KRİTİK]", sent.Text);          // ürün adları gider
        Assert.Contains("Müşteri-1", sent.Text);
        Assert.Contains("Kimden alacağım var?", sent.Text);
        Assert.DoesNotContain("Ayşe", sent.Text);              // müşteri adı gitmez
        Assert.DoesNotContain("Fatma", sent.Text);             // çalışan adı gitmez
        Assert.DoesNotContain("Ahmet", sent.Text);             // sahibin adı da gitmez
        Assert.Equal(AssistantService.SystemPrompt, _model.LastSystem);
    }

    [Fact]
    public async Task Onceki_konusma_sirayla_ve_takma_adlarla_gonderilir()
    {
        await Setup();
        var history = new[]
        {
            new { role = "user", text = "Ayşe Yılmaz ne kadar borçlu?" },
            new { role = "assistant", text = "Ayşe Yılmaz 500 ₺ borçlu." },
        };
        (await Ask("Ne zamandan beri?", history)).EnsureSuccessStatusCode();

        Assert.Equal(["user", "assistant", "user"], _model.LastTurns.Select(t => t.Role));
        Assert.StartsWith("# Dükkan özeti", _model.LastTurns[0].Text);  // özet ilk mesajda
        Assert.EndsWith("Müşteri-1 ne kadar borçlu?", _model.LastTurns[0].Text);
        Assert.Equal("Müşteri-1 500 ₺ borçlu.", _model.LastTurns[1].Text);
        Assert.Equal("Ne zamandan beri?", _model.LastTurns[2].Text);
        Assert.DoesNotContain(_model.LastTurns, t => t.Text.Contains("Ayşe"));
    }

    [Fact]
    public async Task Anahtar_yoksa_asistan_kapali_ve_soru_reddedilir()
    {
        await Setup();
        _model.IsConfigured = false;
        var status = await _http.GetFromJsonAsync<JsonElement>("/api/assistant/status");
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Ask("Merhaba")).StatusCode);
    }

    [Fact]
    public async Task Bos_soru_ve_model_reddi_anlasilir_mesaj_verir()
    {
        await Setup();
        Assert.Equal(HttpStatusCode.BadRequest, (await Ask("   ")).StatusCode);

        _model.Refuse = true;
        var r = await Ask("Bir soru");
        r.EnsureSuccessStatusCode();
        Assert.Contains("cevap veremiyorum", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("answer").GetString());
    }

    [Fact]
    public async Task Calisan_asistani_kullanamaz()
    {
        await Setup();
        var staff = Client();
        (await staff.PostAsJsonAsync("/api/auth/login", new { userName = "fatma", password = "fatma-sifre-1" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/assistant/ask", new { question = "Ciro ne?" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/assistant/context")).StatusCode);
    }

    [Fact]
    public async Task Gonderilen_ozet_goruntulenebilir()
    {
        await Setup();
        var ctx = await _http.GetFromJsonAsync<JsonElement>("/api/assistant/context");
        var text = ctx.GetProperty("text").GetString()!;
        Assert.Contains("## Kasa", text);
        Assert.Contains("gecikmiş", text);
        Assert.DoesNotContain("Ayşe", text);
    }
}
