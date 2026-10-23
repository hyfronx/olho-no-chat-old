using System.Text.Json;

namespace OlhoNoChat.TikTok;

/// <summary>
/// A resposta do <c>api-live/user/room</c> do site do TikTok (sem login nem assinatura): se o perfil existe, se está ao
/// vivo, o id da sala da live e o id do dono (para reconhecer as mensagens dele no chat).
/// </summary>
public sealed record SalaDoTikTok(bool Existe, bool AoVivo, string IdDaSala, string IdDoDono)
{
    /// <summary>O código que o TikTok devolve quando o perfil não existe.</summary>
    public const long PerfilNaoExiste = 19881007;

    public static string Caminho(string canal) =>
        "/api-live/user/room/?aid=1988&app_name=tiktok_web&device_platform=web_pc&sourceType=54&uniqueId=" + Uri.EscapeDataString(canal);

    /// <exception cref="JsonException">A resposta não é a esperada (o TikTok mudou, ou pediu para esperar).</exception>
    public static SalaDoTikTok Ler(string json)
    {
        using JsonDocument documento = JsonDocument.Parse(json);
        JsonElement raiz = documento.RootElement;
        long codigo = raiz.TryGetProperty("statusCode", out JsonElement c) && c.TryGetInt64(out long n) ? n : 0;
        if (codigo == PerfilNaoExiste)
            return new SalaDoTikTok(false, false, string.Empty, string.Empty);
        if (codigo != 0 || !raiz.TryGetProperty("data", out JsonElement dados) || !dados.TryGetProperty("user", out JsonElement usuario)
            || usuario.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Resposta inesperada do TikTok (código {codigo}).");

        string sala = Texto(usuario, "roomId");
        // liveRoom.status: 2 = ao vivo, 4 = acabou
        bool aoVivo = sala.Length > 0 && sala != "0" && dados.TryGetProperty("liveRoom", out JsonElement live)
                      && live.ValueKind == JsonValueKind.Object && live.TryGetProperty("status", out JsonElement status)
                      && status.TryGetInt32(out int s) && s == 2;
        return new SalaDoTikTok(true, aoVivo, sala, Texto(usuario, "id"));
    }

    private static string Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out JsonElement valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() ?? string.Empty : string.Empty;
}
