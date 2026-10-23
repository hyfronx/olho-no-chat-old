using OlhoNoChat.TikTok;

namespace OlhoNoChat.Testes.TikTok;

public class CanalDoTikTokTestes
{
    [Theory]
    [InlineData("fulano", "fulano")]
    [InlineData("  Fulano ", "fulano")]
    [InlineData("@Fulano", "fulano")]
    [InlineData("fulano.live_1", "fulano.live_1")]
    [InlineData("tiktok.com/@fulano", "fulano")]
    [InlineData("https://www.tiktok.com/@Fulano/live", "fulano")]
    [InlineData("https://www.tiktok.com/@fulano/live?lang=pt-BR&enter_from=share", "fulano")]
    [InlineData("https://m.tiktok.com/@fulano/video/7123456789", "fulano")]
    public void Le_ONome(string digitado, string esperado)
    {
        Assert.Equal(esperado, CanalDoTikTok.Ler(digitado));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("nome com espaço")]
    [InlineData("nome-com-traco")]
    [InlineData("não!")]
    [InlineData("https://www.tiktok.com/")]
    [InlineData("https://vm.tiktok.com/ZMabc123/")]
    [InlineData("https://www.tiktok.com/live")]
    [InlineData("um_nome_grande_demais_pro_tiktok")]
    public void NaoEntende_Nulo(string? digitado)
    {
        Assert.Null(CanalDoTikTok.Ler(digitado));
    }
}
