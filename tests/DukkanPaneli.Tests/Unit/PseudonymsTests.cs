using DukkanPaneli.Api.Features.Assistant;

namespace DukkanPaneli.Tests.Unit;

public class PseudonymsTests
{
    [Fact]
    public void Ayni_ad_ayni_takma_adi_alir_numaralar_onek_basina_sayilir()
    {
        var p = new Pseudonyms();
        Assert.Equal("Müşteri-1", p.Alias("Ayşe", "Müşteri"));
        Assert.Equal("Müşteri-2", p.Alias("Mehmet", "Müşteri"));
        Assert.Equal("Müşteri-1", p.Alias(" Ayşe ", "Müşteri"));
        Assert.Equal("Çalışan-1", p.Alias("Fatma", "Çalışan"));
    }

    [Fact]
    public void Kelime_icindeki_ad_degismez_uzun_ad_once_degisir()
    {
        var p = new Pseudonyms();
        p.Alias("Ali", "Müşteri");        // Müşteri-1
        p.Alias("Ali Veli", "Müşteri");   // Müşteri-2
        Assert.Equal("Müşteri-2 ve Müşteri-1 geldi, Alişan gelmedi.", p.Hide("Ali Veli ve Ali geldi, Alişan gelmedi."));
    }

    [Fact]
    public void Geri_cevirmede_numaralar_karismaz()
    {
        var p = new Pseudonyms();
        for (var i = 1; i <= 12; i++) p.Alias($"Kişi {i}", "Müşteri");
        Assert.Equal("Kişi 1, Kişi 12.", p.Reveal("Müşteri-1, Müşteri-12."));
    }
}
