using System.Text.RegularExpressions;

namespace OlhoNoChat.TikTok;

/// <summary>
/// O canal do TikTok digitado na faixa do canal: "nome", "@nome" ou o link do perfil ou da live
/// ("tiktok.com/@nome", "https://www.tiktok.com/@nome/live"). Gravado nas opções como o nome, sem o @ e em minúsculas
/// (o TikTok não diferencia); a faixa mostra com o @, como o TikTok.
/// </summary>
public static partial class CanalDoTikTok
{
    public const string DicaInvalido = "Use o @ do perfil no TikTok (ou o link da live): só letras, números, _ e ponto.";

    [GeneratedRegex("^[A-Za-z0-9_.]{2,24}$")]
    private static partial Regex Nome();

    /// <summary>O nome do canal no que foi digitado (ou gravado); null se vazio ou se não dá para entender.</summary>
    public static string? Ler(string? digitado)
    {
        string texto = (digitado ?? string.Empty).Trim();

        int site = texto.IndexOf("tiktok.com", StringComparison.OrdinalIgnoreCase);
        if (site >= 0)
        {
            // Só o link do perfil ou da live tem o nome (os links curtos, vm.tiktok.com/..., não)
            string caminho = texto[(site + "tiktok.com".Length)..].Split('?', '#')[0];
            texto = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (!texto.StartsWith('@'))
                return null;
        }

        string nome = texto.StartsWith('@') ? texto[1..] : texto;
        return Nome().IsMatch(nome) ? nome.ToLowerInvariant() : null;
    }
}
