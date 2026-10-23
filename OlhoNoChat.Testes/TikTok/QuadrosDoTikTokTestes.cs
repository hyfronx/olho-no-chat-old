using System.IO;
using OlhoNoChat.TikTok;
using static OlhoNoChat.Testes.TikTok.AmostrasDoTikTok;

namespace OlhoNoChat.Testes.TikTok;

/// <summary>Os quadros do WebSocket do TikTok: os que o app manda e a leitura dos lotes de mensagens.</summary>
public class QuadrosDoTikTokTestes
{
    private const string Imagem = "https://p16-webcast.tiktokcdn.com/emote1.webp";

    private static LoteDoTikTok Ler(byte[] quadro, bool comHistorico = false) =>
        QuadrosDoTikTok.Ler(quadro, IdDoDono, comHistorico);

    [Fact]
    public void EntrarNaSala_ComoEspectador()
    {
        var quadro = Protobuf.Ler(QuadrosDoTikTok.EntrarNaSala(IdDaSala));
        Assert.Equal("pb", quadro.Texto(6));
        Assert.Equal("im_enter_room", quadro.Texto(7));
        Assert.Equal(0ul, quadro.Numero(2));

        var entrar = quadro.Mensagem(8)!;
        Assert.Equal(ulong.Parse(IdDaSala), entrar.Numero(1));
        Assert.Equal(12ul, entrar.Numero(4));
        Assert.Equal("audience", entrar.Texto(5));
    }

    [Fact]
    public void Batida_ComASala()
    {
        var quadro = Protobuf.Ler(QuadrosDoTikTok.Batida(IdDaSala));
        Assert.Equal("hb", quadro.Texto(7));
        Assert.Equal(ulong.Parse(IdDaSala), quadro.Mensagem(8)!.Numero(1));
    }

    [Fact]
    public void Confirmar_ComOIdDoQuadroEUmTraco()
    {
        var quadro = Protobuf.Ler(QuadrosDoTikTok.Confirmar(98765));
        Assert.Equal("ack", quadro.Texto(7));
        Assert.Equal(98765ul, quadro.Numero(2));
        Assert.Equal("-", quadro.Texto(8));
    }

    [Fact]
    public void Chat_ComONomeOArrobaEOTexto()
    {
        LoteDoTikTok lote = Ler(Quadro(55, MensagemDoChat(1001, "Viewer1", "boa noite!", idDoUsuario: 42, nome: "Viewer Um")));

        ItemDoTikTok item = Assert.Single(lote.Mensagens);
        Assert.Equal(new ItemDoTikTok("1001", "42", "viewer1", "Viewer Um", PapelNoTikTok.Nenhum, item.Partes), item);
        Assert.Equal([new ParteDoTikTok("boa noite!")], item.Partes);
        Assert.Equal(55ul, lote.Confirmar);
        Assert.False(lote.LiveAcabou);
    }

    [Fact]
    public void Chat_SemApelido_UsaOArroba()
    {
        ItemDoTikTok item = Assert.Single(Ler(Quadro(1, MensagemDoChat(1, "viewer2", "oi", nome: ""))).Mensagens);
        Assert.Equal("viewer2", item.Nome);
    }

    [Fact]
    public void Papel_DonoModeradorEAssinante()
    {
        LoteDoTikTok lote = Ler(Quadro(1,
            MensagemDoChat(1, "fulano", "a", idDoUsuario: ulong.Parse(IdDoDono), moderador: true),
            MensagemDoChat(2, "viewer1", "b", moderador: true, assinante: true),
            MensagemDoChat(3, "viewer2", "c", assinante: true),
            MensagemDoChat(4, "viewer3", "d")));

        Assert.Equal([PapelNoTikTok.Dono, PapelNoTikTok.Moderador, PapelNoTikTok.Assinante, PapelNoTikTok.Nenhum],
            lote.Mensagens.Select(m => m.Papel));
    }

    [Fact]
    public void Emotes_NaPosicaoDeles_ContandoOEmojiComoUmCaractere()
    {
        // "😀 oi" tem 4 caracteres (o emoji são dois no UTF-16): um emote depois do emoji e outro no fim
        ItemDoTikTok item = Assert.Single(Ler(Quadro(1, MensagemDoChat(1, "viewer1", "😀 oi", emotes: [(1, Imagem), (99, Imagem)]))).Mensagens);

        Assert.Equal([new ParteDoTikTok("😀"), new ParteDoTikTok("", Imagem), new ParteDoTikTok(" oi"), new ParteDoTikTok("", Imagem)],
            item.Partes);
    }

    [Fact]
    public void SoEmotes_ViramImagens()
    {
        ItemDoTikTok item = Assert.Single(Ler(Quadro(1, SoEmotes(7, "viewer4", Imagem, Imagem))).Mensagens);

        Assert.Equal("7", item.Id);
        Assert.Equal([new ParteDoTikTok("", Imagem), new ParteDoTikTok("", Imagem)], item.Partes);
    }

    [Fact]
    public void EmoteSemImagem_EMensagemVazia_Ignorados()
    {
        Assert.Empty(Ler(Quadro(1, SoEmotes(7, "viewer4", "nao-e-endereco"), MensagemDoChat(8, "viewer5", ""))).Mensagens);
    }

    [Fact]
    public void Historico_SoComAOpcao()
    {
        byte[] quadro = Quadro(1, MensagemDoChat(1, "viewer1", "antiga", historico: true), MensagemDoChat(2, "viewer1", "nova"));

        Assert.Equal(["2"], Ler(quadro).Mensagens.Select(m => m.Id));
        Assert.Equal(["1", "2"], Ler(quadro, comHistorico: true).Mensagens.Select(m => m.Id));
    }

    [Fact]
    public void Apagadas_PeloIdDaMensagemEDaPessoa()
    {
        LoteDoTikTok lote = Ler(Quadro(1, Apagar([1001, 1002], [42, 300])));

        Assert.Equal(["1001", "1002"], lote.Apagadas);
        Assert.Equal(["42", "300"], lote.UsuariosApagados);
    }

    [Theory]
    [InlineData(3ul, true)]
    [InlineData(4ul, true)]
    [InlineData(1ul, false)]
    public void Controle_LiveAcabou(ulong acao, bool acabou)
    {
        Assert.Equal(acabou, Ler(Quadro(1, Controle(acao))).LiveAcabou);
    }

    [Fact]
    public void OutrosTipos_Ignorados_SemGzipTambem()
    {
        LoteDoTikTok lote = Ler(Quadro(9, gzip: false, Curtida(), MensagemDoChat(1, "viewer1", "oi")));

        Assert.Single(lote.Mensagens);
        Assert.Equal(9ul, lote.Confirmar);
    }

    [Theory]
    [InlineData("hb")]
    [InlineData("im_enter_room_resp")]
    public void QuadroSemMensagens_Vazio(string tipo)
    {
        Assert.Same(LoteDoTikTok.Vazio, Ler(QuadroSemMensagens(tipo)));
    }

    [Fact]
    public void IdSoNoEnvelope_UsaODoEnvelope()
    {
        var corpo = new ProtobufNovo().Bytes(2, new ProtobufNovo().Numero(1, 5).Texto(38, "viewer6").ParaBytes()).Texto(3, "oi").ParaBytes();
        ItemDoTikTok item = Assert.Single(Ler(Quadro(1, new Bruta("WebcastChatMessage", corpo, IdNoEnvelope: 777))).Mensagens);

        Assert.Equal("777", item.Id);
    }

    [Fact]
    public void QuadroQueNaoEhProtobuf_Erro()
    {
        Assert.Throws<InvalidDataException>(() => Ler([0x0A, 0x50, 0x01]));
    }
}
