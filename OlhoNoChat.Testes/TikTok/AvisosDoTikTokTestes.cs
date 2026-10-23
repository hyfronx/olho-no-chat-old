using System.Text.Json;
using System.Text.RegularExpressions;
using OlhoNoChat.Chat;
using OlhoNoChat.Configuracoes;
using OlhoNoChat.TikTok;
using static OlhoNoChat.Testes.TikTok.AmostrasDoTikTok;

namespace OlhoNoChat.Testes.TikTok;

/// <summary>
/// Os avisos do TikTok: a leitura nos quadros (presentes, seguiu, compartilhou, Superfã), o juntador de combos (uma linha
/// por combo, como a pesquisa de 2026-10-23 viu nas lives), a lista de nomes em português e o texto para a página.
/// </summary>
public class AvisosDoTikTokTestes
{
    private static readonly DateTimeOffset Zero = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static LoteDoTikTok Ler(byte[] quadro) => QuadrosDoTikTok.Ler(quadro, IdDoDono, comHistorico: false);

    // --- Quadros ---------------------------------------------------------------------------------------------

    [Fact]
    public void Presente_LidoComTudo()
    {
        EnvioDePresente envio = Assert.Single(Ler(Quadro(1, Presente(9, "viewer1", 5655, "Rose", 1, combo: 77, contagem: 3, fim: true))).Presentes);

        Assert.Equal(new EnvioDePresente("9", "111", "viewer1", "viewer1", 5655, "Rose", ImagemDaRosa, 1, 1, 77, 3, true), envio);
    }

    [Theory]
    [InlineData(1ul, TipoDeAvisoDoTikTok.Seguiu)]
    [InlineData(3ul, TipoDeAvisoDoTikTok.Compartilhou)]
    public void Social_SeguiuOuCompartilhou(ulong acao, TipoDeAvisoDoTikTok tipo)
    {
        AvisoDoTikTok aviso = Assert.Single(Ler(Quadro(1, Social(5, "viewer3", acao))).Avisos);
        Assert.Equal(new AvisoDoTikTok("5", "222", "viewer3", "viewer3", tipo), aviso);
    }

    [Fact]
    public void Repostou_FicaDeFora()
    {
        Assert.Empty(Ler(Quadro(1, Social(5, "viewer3", 4))).Avisos);
    }

    [Fact]
    public void Superfa_PelaFaixaOuPelaAssinatura()
    {
        LoteDoTikTok lote = Ler(Quadro(1,
            Faixa(1, "viewer4", "ttlive_superFan_commentNotif_someoneBecameSuperFan"),
            Faixa(2, "viewer5", "ttlive_superFan_commentNotif_superFanJoined"), // só entrou: não conta
            Faixa(3, "viewer6", "pm_mt_alp_join_animation"),
            Assinatura(4, "viewer1"),
            Assinatura(5, "viewer2", tipo: 1))); // lembrete ao dono: não conta

        Assert.Equal(["viewer4", "viewer1"], lote.Avisos.Select(a => a.Login));
        Assert.All(lote.Avisos, a => Assert.Equal(TipoDeAvisoDoTikTok.Superfa, a.Tipo));
    }

    // --- Juntador --------------------------------------------------------------------------------------------

    private static EnvioDePresente Envio(string usuario, int contagem, bool fim = false, int tipo = 1, ulong combo = 7, long presente = 5655,
        int moedas = 1) =>
        new("id-" + usuario + contagem, usuario, "login" + usuario, "Nome " + usuario, presente, "Rose", ImagemDaRosa, moedas, tipo, combo,
            contagem, fim);

    private static JuntadorDePresentes Juntador() => new(id => id == 5655 ? "Rosa" : null);

    [Fact]
    public void Combo_SaiSoNoFim_ComAContagemFinal()
    {
        var juntador = Juntador();

        Assert.Null(juntador.Receber(Envio("1", 1), Zero));
        Assert.Null(juntador.Receber(Envio("1", 4), Zero.AddSeconds(1))); // o TikTok pula números quando junta toques
        AvisoDoTikTok? aviso = juntador.Receber(Envio("1", 5, fim: true), Zero.AddSeconds(4));

        Assert.NotNull(aviso);
        Assert.Equal("id-11", aviso.Id); // o id do primeiro
        Assert.Equal(new PresenteDoTikTok("Rosa", ImagemDaRosa, 5, 5), aviso.Presente);
        Assert.Empty(juntador.Vencidos(Zero.AddMinutes(1)));
    }

    [Fact]
    public void UmaRosaSo_InicioEFim_UmAviso()
    {
        var juntador = Juntador();
        Assert.Null(juntador.Receber(Envio("1", 1), Zero));
        Assert.Equal(1, juntador.Receber(Envio("1", 1, fim: true), Zero.AddSeconds(3))?.Presente?.Quantidade);
    }

    [Fact]
    public void SoOFim_ConectouNoMeioDoCombo()
    {
        Assert.Equal(8, Juntador().Receber(Envio("1", 8, fim: true), Zero)?.Presente?.Quantidade);
    }

    [Fact]
    public void ComboSemFim_SaiDepoisDoPrazo()
    {
        var juntador = Juntador();
        juntador.Receber(Envio("1", 3), Zero);

        Assert.Empty(juntador.Vencidos(Zero + JuntadorDePresentes.SemFinal - TimeSpan.FromSeconds(1)));
        Assert.Equal(3, Assert.Single(juntador.Vencidos(Zero + JuntadorDePresentes.SemFinal)).Presente?.Quantidade);
    }

    [Fact]
    public void SemCombo_SomaOsEnviosAteOSilencio()
    {
        var juntador = Juntador();
        for (int i = 0; i < 9; i++)
            Assert.Null(juntador.Receber(Envio("1", 1, tipo: 2, presente: 5660, moedas: 100), Zero.AddSeconds(i)));

        Assert.Empty(juntador.Vencidos(Zero.AddSeconds(8) + JuntadorDePresentes.SilencioEntreEnvios - TimeSpan.FromMilliseconds(1)));
        AvisoDoTikTok aviso = Assert.Single(juntador.Vencidos(Zero.AddSeconds(8) + JuntadorDePresentes.SilencioEntreEnvios));
        Assert.Equal(new PresenteDoTikTok("Rose", ImagemDaRosa, 9, 900), aviso.Presente); // sem nome em português: o da mensagem
    }

    [Fact]
    public void PessoasECombosDiferentes_SeparadosELimpar()
    {
        var juntador = Juntador();
        juntador.Receber(Envio("1", 1), Zero);
        juntador.Receber(Envio("2", 1), Zero);
        juntador.Receber(Envio("1", 1, combo: 8), Zero);

        Assert.Equal(3, juntador.Vencidos(Zero.AddMinutes(1)).Count);
        juntador.Receber(Envio("1", 1), Zero);
        juntador.Limpar();
        Assert.Empty(juntador.Vencidos(Zero.AddMinutes(1)));
    }

    // --- Lista de presentes ----------------------------------------------------------------------------------

    [Fact]
    public void Lista_NomesPorId_SemRepetir()
    {
        IReadOnlyDictionary<long, string> nomes = ListaDePresentes.Ler(
            """{"data":{"gifts":[{"id":5655,"name":"Rosa"},{"id":5655,"name":"Outra"},{"id":6064,"name":"Bom jogo "},{"name":"sem id"}]}}""");

        Assert.Equal(new Dictionary<long, string> { [5655] = "Rosa", [6064] = "Bom jogo" }, nomes);
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"status_code":10011}""")]
    public void Lista_RespostaEstranha_Vazia(string json)
    {
        Assert.Empty(ListaDePresentes.Ler(json));
    }

    [Fact]
    public void Lista_EnderecoEmPortuguesComASala()
    {
        Assert.EndsWith("&webcast_language=pt-BR&room_id=123", ListaDePresentes.Endereco("123"));
    }

    // --- Para a página ---------------------------------------------------------------------------------------

    [Fact]
    public void Pagina_Presente_ComImagemQuantidadeEMoedas()
    {
        var aviso = new AvisoDoTikTok("9", "111", "viewer1", "Viewer Um", TipoDeAvisoDoTikTok.Presente,
            new PresenteDoTikTok("Rosa", "https://exemplo.com/r.png", 5, 1500));

        Assert.Equal(
            """window.oncChat && window.oncChat.addTikTok && window.oncChat.addTikTok({"id":"9","login":"viewer1","name":"Viewer Um","role":"","kind":"gift","parts":[],"notice":["enviou ",{"value":"5x"}," ",{"image":"https://exemplo.com/r.png","name":"Rosa"},{"value":"Rosa"}," (1.500 moedas)"]});""",
            ContratoComAPagina.AdicionarAvisoDoTikTok(aviso));
    }

    [Fact]
    public void Pagina_UmaRosa_SemXEUmaMoeda()
    {
        var aviso = new AvisoDoTikTok("9", "111", "viewer1", "viewer1", TipoDeAvisoDoTikTok.Presente, new PresenteDoTikTok("Rosa", null, 1, 1));

        Assert.Contains("""["enviou ",{"value":"Rosa"}," (1 moeda)"]""", ContratoComAPagina.AdicionarAvisoDoTikTok(aviso));
    }

    [Theory]
    [InlineData(TipoDeAvisoDoTikTok.Seguiu, "follow", """["seguiu o criador de LIVE"]""")]
    [InlineData(TipoDeAvisoDoTikTok.Compartilhou, "share", """["compartilhou a LIVE"]""")]
    [InlineData(TipoDeAvisoDoTikTok.Superfa, "superfan", """["acabou de se tornar ",{"value":"Superfã"},"!"]""")]
    public void Pagina_OutrosAvisos(TipoDeAvisoDoTikTok tipo, string kind, string partes)
    {
        string script = ContratoComAPagina.AdicionarAvisoDoTikTok(new AvisoDoTikTok("1", "2", "viewer1", "viewer1", tipo));

        Assert.Contains($"\"kind\":\"{kind}\"", script);
        Assert.Contains($"\"notice\":{partes}", Regex.Unescape(script));
    }

    // --- Estado para o aviso de conexão da página (Chat Multiplataforma) --------------------------------------

    [Theory]
    [InlineData(EstadoDoTikTok.Procurando, "connecting")]
    [InlineData(EstadoDoTikTok.Conectado, "connected")]
    [InlineData(EstadoDoTikTok.EsperandoALive, "waiting")]
    [InlineData(EstadoDoTikTok.CanalNaoExiste, "notfound")]
    [InlineData(EstadoDoTikTok.SemConexao, "failed")]
    [InlineData(EstadoDoTikTok.Desligado, "off")]
    public void EstadoParaAPagina(EstadoDoTikTok estado, string esperado)
    {
        Assert.Equal(esperado, ContratoComAPagina.EstadoParaAPagina(estado));
        Assert.Equal($"window.oncChat && window.oncChat.platformState && window.oncChat.platformState(\"tiktok\", \"{esperado}\");",
            ContratoComAPagina.EstadoDaPlataforma("tiktok", esperado));
    }

    // --- Opções: o "Esconder avisos" antigo vira os interruptores da Twitch e do YouTube ---------------------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EsconderAvisosAntigo_ViraOsDaTwitchEDoYouTube(bool escondia, bool mostra)
    {
        Opcoes opcoes = ArquivoDeConfiguracoes.DeJson($$"""{"Versao":1,"EsconderAvisos":{{(escondia ? "true" : "false")}}}""");

        Assert.Equal(mostra, opcoes.MostrarAvisosDaTwitch);
        Assert.Equal(mostra, opcoes.MostrarAvisosDoYouTube);
        Assert.True(opcoes.MostrarPresentesDoTikTok); // os do TikTok são novos: o padrão
        Assert.Null(opcoes.EsconderAvisosAntigo);
        Assert.DoesNotContain("EsconderAvisos", ArquivoDeConfiguracoes.ParaJson(opcoes));
    }

    [Fact]
    public void Padrao_TudoLigadoMenosCompartilhamentos()
    {
        var opcoes = new Opcoes();

        Assert.True(opcoes.MostrarAvisosDaTwitch && opcoes.MostrarAvisosDoYouTube && opcoes.MostrarPresentesDoTikTok
                    && opcoes.MostrarSeguidoresDoTikTok && opcoes.MostrarSuperfasDoTikTok);
        Assert.False(opcoes.MostrarCompartilhamentosDoTikTok);
        using JsonDocument json = JsonDocument.Parse(ArquivoDeConfiguracoes.ParaJson(opcoes));
        Assert.False(json.RootElement.TryGetProperty("EsconderAvisos", out _));
    }
}
