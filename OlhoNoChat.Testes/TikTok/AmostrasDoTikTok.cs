using System.IO;
using System.IO.Compression;
using OlhoNoChat.TikTok;

namespace OlhoNoChat.Testes.TikTok;

/// <summary>
/// Respostas e quadros do TikTok montados à mão, com os mesmos campos dos de verdade (nomes genéricos, como
/// viewer1…viewer6).
/// </summary>
public static class AmostrasDoTikTok
{
    public const string IdDaSala = "7699571068760001302";
    public const string IdDoDono = "6812345678901234567";

    public static string Sala(bool aoVivo) => $$$"""
        {"data":{"user":{"id":"{{{IdDoDono}}}","nickname":"Fulano","roomId":"{{{IdDaSala}}}","uniqueId":"fulano"},
         "liveRoom":{"status":{{{(aoVivo ? 2 : 4)}}},"title":"live"}},"extra":{},"message":"","statusCode":0}
        """;

    public const string SalaQueNaoExiste = """{"data":null,"message":"user_not_found","extra":{"id":"x"},"statusCode":19881007}""";

    /// <summary>Uma mensagem do lote: o tipo, o corpo e, se for o caso, marcada como histórico.</summary>
    public sealed record Bruta(string Tipo, byte[] Corpo, ulong IdNoEnvelope = 0, bool Historico = false);

    /// <summary>Um quadro do WebSocket com as mensagens (em gzip, como o TikTok manda).</summary>
    public static byte[] Quadro(ulong idDoQuadro, params Bruta[] mensagens) => Quadro(idDoQuadro, gzip: true, mensagens);

    public static byte[] Quadro(ulong idDoQuadro, bool gzip, params Bruta[] mensagens)
    {
        var lote = new ProtobufNovo();
        foreach (Bruta m in mensagens)
        {
            var bruta = new ProtobufNovo().Texto(1, m.Tipo).Bytes(2, m.Corpo);
            if (m.IdNoEnvelope != 0)
                bruta.Numero(3, m.IdNoEnvelope);
            if (m.Historico)
                bruta.Numero(6, 1);
            lote.Bytes(1, bruta.ParaBytes());
        }
        byte[] dados = lote.ParaBytes();
        if (gzip)
            dados = Compactar(dados);
        var quadro = new ProtobufNovo();
        if (idDoQuadro != 0)
            quadro.Numero(2, idDoQuadro);
        return quadro.Texto(6, "pb").Texto(7, "msg").Bytes(8, dados).ParaBytes();
    }

    /// <summary>Um quadro que não é de mensagens ("hb", "im_enter_room_resp").</summary>
    public static byte[] QuadroSemMensagens(string tipo) => new ProtobufNovo().Texto(6, "pb").Texto(7, tipo).ParaBytes();

    public static Bruta MensagemDoChat(ulong id, string login, string texto, ulong idDoUsuario = 111, string? nome = null, bool moderador = false,
        bool assinante = false, bool historico = false, params (ulong Posicao, string Imagem)[] emotes)
    {
        var corpo = new ProtobufNovo()
            .Bytes(1, Cabecalho(id))
            .Bytes(2, Usuario(idDoUsuario, login, nome ?? login))
            .Texto(3, texto);
        foreach (var (posicao, imagem) in emotes)
            corpo.Bytes(13, new ProtobufNovo().Numero(1, posicao).Bytes(2, Emote(imagem)).ParaBytes());
        corpo.Bytes(18, Identidade(moderador, assinante));
        return new Bruta("WebcastChatMessage", corpo.ParaBytes(), Historico: historico);
    }

    public static Bruta SoEmotes(ulong id, string login, params string[] imagens)
    {
        var corpo = new ProtobufNovo().Bytes(1, Cabecalho(id)).Bytes(2, Usuario(111, login, login));
        foreach (string imagem in imagens)
            corpo.Bytes(3, Emote(imagem));
        return new Bruta("WebcastEmoteChatMessage", corpo.ParaBytes());
    }

    public static Bruta Apagar(ulong[] ids, ulong[] usuarios)
    {
        var corpo = new ProtobufNovo();
        foreach (ulong id in ids)
            corpo.Numero(2, id);
        // os ids das pessoas juntos num bloco ("packed"), como o TikTok manda
        var bloco = new MemoryStream();
        foreach (ulong usuario in usuarios)
            bloco.Write(Varint(usuario));
        if (usuarios.Length > 0)
            corpo.Bytes(3, bloco.ToArray());
        return new Bruta("WebcastImDeleteMessage", corpo.ParaBytes());
    }

    /// <summary>A live acabou (3) ou foi suspensa (4); outras ações não.</summary>
    public static Bruta Controle(ulong acao) => new("WebcastControlMessage", new ProtobufNovo().Numero(2, acao).ParaBytes());

    public const string ImagemDaRosa = "https://p16-webcast.tiktokcdn.com/img/rosa~tplv-obj.png";

    /// <summary>
    /// Uma mensagem de presente: quem (7), o presente (2; 15: 1.1 imagem, 11 tipo, 12 moedas, 16 nome), a contagem (5), o
    /// fim do combo (9) e o id do combo (11). Tipo 1 = de combo (a Rosa); 2 e 4 = um envio por mensagem.
    /// </summary>
    public static Bruta Presente(ulong id, string login, ulong idDoPresente, string nome, ulong moedas, ulong tipo = 1, ulong combo = 1,
        ulong contagem = 1, bool fim = false, ulong idDoUsuario = 111)
    {
        byte[] presente = new ProtobufNovo()
            .Bytes(1, new ProtobufNovo().Texto(1, ImagemDaRosa).ParaBytes())
            .Numero(11, tipo).Numero(12, moedas).Texto(16, nome).ParaBytes();
        var corpo = new ProtobufNovo()
            .Bytes(1, Cabecalho(id))
            .Numero(2, idDoPresente)
            .Numero(5, contagem)
            .Bytes(7, Usuario(idDoUsuario, login, login));
        if (fim)
            corpo.Numero(9, 1);
        return new Bruta("WebcastGiftMessage", corpo.Numero(11, combo).Bytes(15, presente).ParaBytes());
    }

    /// <summary>Seguiu (1), compartilhou (3) ou repostou (4) — o campo 4.</summary>
    public static Bruta Social(ulong id, string login, ulong acao) =>
        new("WebcastSocialMessage", new ProtobufNovo().Bytes(1, Cabecalho(id)).Bytes(2, Usuario(222, login, login)).Numero(4, acao).ParaBytes());

    /// <summary>A faixa de destaque com a chave dada (a de quem virou Superfã: ttlive_superFan_..._someoneBecameSuperFan).</summary>
    public static Bruta Faixa(ulong id, string login, string chave) =>
        new("WebcastBarrageMessage", new ProtobufNovo().Bytes(1, Cabecalho(id)).Numero(3, 100)
            .Bytes(5, new ProtobufNovo().Texto(1, chave).ParaBytes()).Bytes(50, Usuario(333, login, login)).ParaBytes());

    /// <summary>O aviso antigo de assinatura (campo 7: 0 = assinou).</summary>
    public static Bruta Assinatura(ulong id, string login, ulong tipo = 0) =>
        new("WebcastSubNotifyMessage", new ProtobufNovo().Bytes(1, Cabecalho(id)).Bytes(2, Usuario(444, login, login)).Numero(7, tipo).ParaBytes());

    public static Bruta Curtida() => new("WebcastLikeMessage", new ProtobufNovo().Numero(2, 5).ParaBytes());

    private static byte[] Cabecalho(ulong id) => new ProtobufNovo().Texto(1, "WebcastChatMessage").Numero(2, id).ParaBytes();

    private static byte[] Usuario(ulong id, string login, string nome) =>
        new ProtobufNovo().Numero(1, id).Texto(3, nome).Texto(38, login).ParaBytes();

    private static byte[] Identidade(bool moderador, bool assinante)
    {
        var identidade = new ProtobufNovo();
        if (assinante)
            identidade.Numero(2, 1);
        if (moderador)
            identidade.Numero(5, 1);
        return identidade.ParaBytes();
    }

    private static byte[] Emote(string imagem) =>
        new ProtobufNovo().Texto(1, "emote1").Bytes(2, new ProtobufNovo().Texto(1, imagem).Texto(1, imagem + "?2").ParaBytes()).ParaBytes();

    private static byte[] Varint(ulong valor)
    {
        var bytes = new List<byte>();
        while (valor >= 0x80)
        {
            bytes.Add((byte)(valor | 0x80));
            valor >>= 7;
        }
        bytes.Add((byte)valor);
        return [.. bytes];
    }

    private static byte[] Compactar(byte[] dados)
    {
        using var saida = new MemoryStream();
        using (var gzip = new GZipStream(saida, CompressionLevel.Fastest))
            gzip.Write(dados);
        return saida.ToArray();
    }
}
