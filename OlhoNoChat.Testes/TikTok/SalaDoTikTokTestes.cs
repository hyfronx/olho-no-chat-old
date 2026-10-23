using System.Text.Json;
using OlhoNoChat.TikTok;

namespace OlhoNoChat.Testes.TikTok;

/// <summary>A resposta do api-live/user/room, nos formatos que o TikTok devolveu em 2026-10.</summary>
public class SalaDoTikTokTestes
{
    [Fact]
    public void AoVivo_ComASalaEODono()
    {
        SalaDoTikTok sala = SalaDoTikTok.Ler(AmostrasDoTikTok.Sala(aoVivo: true));

        Assert.Equal(new SalaDoTikTok(true, true, AmostrasDoTikTok.IdDaSala, AmostrasDoTikTok.IdDoDono), sala);
    }

    [Fact]
    public void ForaDoAr_ExisteMasNaoEstaAoVivo()
    {
        SalaDoTikTok sala = SalaDoTikTok.Ler(AmostrasDoTikTok.Sala(aoVivo: false));

        Assert.True(sala.Existe);
        Assert.False(sala.AoVivo);
    }

    [Fact]
    public void PerfilQueNaoExiste()
    {
        SalaDoTikTok sala = SalaDoTikTok.Ler(AmostrasDoTikTok.SalaQueNaoExiste);

        Assert.False(sala.Existe);
        Assert.False(sala.AoVivo);
    }

    [Theory]
    [InlineData("""{"data":null,"message":"params_error","statusCode":10011}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("<html>")]
    public void RespostaEstranha_Erro(string json)
    {
        Assert.ThrowsAny<JsonException>(() => SalaDoTikTok.Ler(json));
    }

    [Fact]
    public void Caminho_ComONomeEscapado()
    {
        Assert.EndsWith("&uniqueId=fulano.live", SalaDoTikTok.Caminho("fulano.live"));
    }
}
