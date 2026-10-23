using System.Text.Json;

namespace OlhoNoChat.TikTok;

/// <summary>Os avisos do TikTok que o app mostra no chat (cada um com a sua opção em Configurações > Chat).</summary>
public enum TipoDeAvisoDoTikTok
{
    /// <summary>Mandou um presente (um combo inteiro numa linha só, ver <see cref="JuntadorDePresentes"/>).</summary>
    Presente,

    /// <summary>Seguiu o criador da live.</summary>
    Seguiu,

    /// <summary>Compartilhou a live.</summary>
    Compartilhou,

    /// <summary>Virou Superfã (a assinatura do TikTok).</summary>
    Superfa,
}

/// <summary>Um presente já juntado: o nome (em português, quando a lista do TikTok tem), a imagem, quantos e o valor total.</summary>
public sealed record PresenteDoTikTok(string Nome, string? Imagem, int Quantidade, long Moedas);

/// <summary>Um aviso do TikTok para o chat: quem fez, o quê e, num presente, qual.</summary>
public sealed record AvisoDoTikTok(string Id, string IdDoUsuario, string Login, string Nome, TipoDeAvisoDoTikTok Tipo,
    PresenteDoTikTok? Presente = null);

/// <summary>
/// Uma mensagem de presente como o TikTok manda (WebcastGiftMessage). Nos presentes de combo (<see cref="TipoDoPresente"/> 1:
/// a Rosa e os baratos) vem uma mensagem a cada toque, com a <see cref="Contagem"/> acumulada, e uma final com
/// <see cref="Fim"/>; nos outros, uma mensagem por envio, sem final.
/// </summary>
public sealed record EnvioDePresente(
    string Id,
    string IdDoUsuario,
    string Login,
    string Nome,
    long IdDoPresente,
    string NomeDoPresente,
    string? Imagem,
    int MoedasPorUnidade,
    int TipoDoPresente,
    ulong IdDoCombo,
    int Contagem,
    bool Fim);

/// <summary>
/// Junta as mensagens de um presente numa linha só, como o TikTok mostra ("enviou Rosa x5"): o combo da Rosa sai quando
/// chega a mensagem final (uns 3 s depois do último toque); os presentes sem combo repetidos pela mesma pessoa saem depois
/// de <see cref="SilencioEntreEnvios"/> sem outro igual. Se a final de um combo não chegar (conexão caiu), sai com a última
/// contagem depois de <see cref="SemFinal"/>. Usado por duas tarefas (a leitura e o relógio): tem trava.
/// </summary>
public sealed class JuntadorDePresentes
{
    public static readonly TimeSpan SilencioEntreEnvios = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SemFinal = TimeSpan.FromSeconds(8);

    private readonly Func<long, string?> _nomeEmPortugues;
    private readonly object _trava = new();
    private readonly Dictionary<string, (EnvioDePresente Primeiro, int Quantidade, DateTimeOffset Ultimo)> _abertos = [];

    /// <param name="nomeEmPortugues">O nome do presente na lista do TikTok em pt-BR, ou null (fica o nome da mensagem).</param>
    public JuntadorDePresentes(Func<long, string?> nomeEmPortugues) => _nomeEmPortugues = nomeEmPortugues;

    /// <summary>Uma mensagem de presente; devolve o aviso quando o combo terminou, ou null.</summary>
    public AvisoDoTikTok? Receber(EnvioDePresente envio, DateTimeOffset agora)
    {
        lock (_trava)
        {
            bool deCombo = envio.TipoDoPresente == 1;
            string chave = deCombo ? $"{envio.IdDoUsuario}|{envio.IdDoPresente}|{envio.IdDoCombo}" : $"{envio.IdDoUsuario}|{envio.IdDoPresente}";
            if (deCombo)
            {
                // A contagem já é a do combo inteiro
                EnvioDePresente primeiro = _abertos.TryGetValue(chave, out var aberto) ? aberto.Primeiro : envio;
                int quantidade = Math.Max(envio.Contagem, 1);
                if (envio.Fim)
                {
                    _abertos.Remove(chave);
                    return Aviso(primeiro, quantidade);
                }
                _abertos[chave] = (primeiro, quantidade, agora);
                return null;
            }

            // Sem combo: cada mensagem é um envio, somado aos da mesma pessoa até um silêncio
            int soma = Math.Max(envio.Contagem, 1);
            _abertos[chave] = _abertos.TryGetValue(chave, out var anterior)
                ? (anterior.Primeiro, anterior.Quantidade + soma, agora)
                : (envio, soma, agora);
            return null;
        }
    }

    /// <summary>Os presentes que já esperaram o bastante (chamado de segundo em segundo).</summary>
    public IReadOnlyList<AvisoDoTikTok> Vencidos(DateTimeOffset agora)
    {
        lock (_trava)
        {
            var prontos = new List<AvisoDoTikTok>();
            foreach (var (chave, aberto) in _abertos.ToList())
            {
                TimeSpan espera = aberto.Primeiro.TipoDoPresente == 1 ? SemFinal : SilencioEntreEnvios;
                if (agora - aberto.Ultimo < espera)
                    continue;
                _abertos.Remove(chave);
                prontos.Add(Aviso(aberto.Primeiro, aberto.Quantidade));
            }
            return prontos;
        }
    }

    /// <summary>Esquece os combos abertos (outra live, ou a conexão caiu e o TikTok vai mandar de novo).</summary>
    public void Limpar()
    {
        lock (_trava)
            _abertos.Clear();
    }

    private AvisoDoTikTok Aviso(EnvioDePresente envio, int quantidade)
    {
        string nome = _nomeEmPortugues(envio.IdDoPresente) is { Length: > 0 } emPortugues ? emPortugues : envio.NomeDoPresente;
        var presente = new PresenteDoTikTok(nome, envio.Imagem, quantidade, (long)envio.MoedasPorUnidade * quantidade);
        return new AvisoDoTikTok(envio.Id, envio.IdDoUsuario, envio.Login, envio.Nome, TipoDeAvisoDoTikTok.Presente, presente);
    }
}

/// <summary>
/// A lista de presentes do TikTok em português (<c>webcast/gift/list</c> com <c>webcast_language=pt-BR</c>; com o id da
/// sala, vêm também os presentes próprios do criador): o id de cada presente e o nome dele.
/// </summary>
public static class ListaDePresentes
{
    public static string Endereco(string idDaSala) =>
        "https://webcast.tiktok.com/webcast/gift/list/?aid=1988&app_name=tiktok_web&device_platform=web_pc&webcast_language=pt-BR&room_id="
        + Uri.EscapeDataString(idDaSala);

    /// <summary>Os nomes por id; vazio se a resposta não for a esperada (os avisos ficam com o nome em inglês).</summary>
    public static IReadOnlyDictionary<long, string> Ler(string json)
    {
        var nomes = new Dictionary<long, string>();
        try
        {
            using JsonDocument documento = JsonDocument.Parse(json);
            if (!documento.RootElement.TryGetProperty("data", out JsonElement dados) || dados.ValueKind != JsonValueKind.Object
                || !dados.TryGetProperty("gifts", out JsonElement presentes) || presentes.ValueKind != JsonValueKind.Array)
                return nomes;
            foreach (JsonElement presente in presentes.EnumerateArray())
            {
                if (presente.ValueKind == JsonValueKind.Object
                    && presente.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long numero)
                    && presente.TryGetProperty("name", out JsonElement nome) && nome.ValueKind == JsonValueKind.String
                    && nome.GetString() is { Length: > 0 } texto)
                    nomes.TryAdd(numero, texto.Trim());
            }
        }
        catch (JsonException)
        {
        }
        return nomes;
    }
}
