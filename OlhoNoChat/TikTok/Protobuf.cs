using System.IO;
using System.Text;

namespace OlhoNoChat.TikTok;

/// <summary>
/// Uma mensagem em protobuf (o formato binário do TikTok) lida campo a campo, sem o esquema: só os campos que o leitor
/// usa, pelo número; os outros são ignorados (o TikTok muda o esquema com frequência). Um campo que falta devolve o
/// vazio (0, "", null), e dados que não são protobuf saem como <see cref="InvalidDataException"/>.
/// </summary>
public sealed class Protobuf
{
    // Cada campo: o número, e o número (varint, fixo) ou os bytes (texto, mensagem, lista)
    private readonly List<(int Numero, ulong Valor, ReadOnlyMemory<byte>? Bytes)> _campos = [];

    public Protobuf(ReadOnlyMemory<byte> dados)
    {
        ReadOnlySpan<byte> b = dados.Span;
        int i = 0;
        while (i < b.Length)
        {
            ulong chave = Varint(b, ref i);
            int numero = (int)(chave >> 3);
            if (numero == 0)
                throw new InvalidDataException("Campo 0 no protobuf.");
            switch (chave & 7)
            {
                case 0:
                    _campos.Add((numero, Varint(b, ref i), null));
                    break;
                case 1:
                    _campos.Add((numero, Fixo(b, ref i, 8), null));
                    break;
                case 2:
                    ulong tamanho = Varint(b, ref i);
                    if (tamanho > (ulong)(b.Length - i))
                        throw new InvalidDataException("Protobuf cortado.");
                    _campos.Add((numero, 0, dados.Slice(i, (int)tamanho)));
                    i += (int)tamanho;
                    break;
                case 5:
                    _campos.Add((numero, Fixo(b, ref i, 4), null));
                    break;
                default:
                    throw new InvalidDataException("Tipo de campo desconhecido no protobuf.");
            }
        }
    }

    public static Protobuf Ler(ReadOnlyMemory<byte> dados) => new(dados);

    /// <summary>Um número (o primeiro com esse número de campo), ou 0.</summary>
    public ulong Numero(int campo)
    {
        foreach (var c in _campos)
        {
            if (c.Numero == campo && c.Bytes == null)
                return c.Valor;
        }
        return 0;
    }

    public bool Sim(int campo) => Numero(campo) != 0;

    /// <summary>Os bytes de um campo (o primeiro), ou null.</summary>
    public ReadOnlyMemory<byte>? Bytes(int campo)
    {
        foreach (var c in _campos)
        {
            if (c.Numero == campo && c.Bytes != null)
                return c.Bytes;
        }
        return null;
    }

    /// <summary>Um texto, ou "".</summary>
    public string Texto(int campo) => Bytes(campo) is { } b ? Encoding.UTF8.GetString(b.Span) : string.Empty;

    /// <summary>Uma mensagem dentro desta (a primeira), ou null.</summary>
    public Protobuf? Mensagem(int campo) => Bytes(campo) is { } b ? new Protobuf(b) : null;

    /// <summary>Todas as mensagens de um campo repetido.</summary>
    public IEnumerable<Protobuf> Mensagens(int campo)
    {
        foreach (var c in _campos)
        {
            if (c.Numero == campo && c.Bytes is { } b)
                yield return new Protobuf(b);
        }
    }

    /// <summary>Todos os números de um campo repetido, soltos ou juntos num bloco ("packed").</summary>
    public IEnumerable<ulong> Numeros(int campo)
    {
        var numeros = new List<ulong>();
        foreach (var c in _campos)
        {
            if (c.Numero != campo)
                continue;
            if (c.Bytes is not { } b)
            {
                numeros.Add(c.Valor);
                continue;
            }
            ReadOnlySpan<byte> bloco = b.Span;
            int i = 0;
            while (i < bloco.Length)
                numeros.Add(Varint(bloco, ref i));
        }
        return numeros;
    }

    private static ulong Varint(ReadOnlySpan<byte> b, ref int i)
    {
        ulong valor = 0;
        for (int deslocamento = 0; deslocamento < 70; deslocamento += 7)
        {
            if (i >= b.Length)
                throw new InvalidDataException("Protobuf cortado.");
            byte atual = b[i++];
            valor |= (ulong)(atual & 0x7F) << deslocamento;
            if ((atual & 0x80) == 0)
                return valor;
        }
        throw new InvalidDataException("Número comprido demais no protobuf.");
    }

    private static ulong Fixo(ReadOnlySpan<byte> b, ref int i, int tamanho)
    {
        if (i + tamanho > b.Length)
            throw new InvalidDataException("Protobuf cortado.");
        ulong valor = 0;
        for (int k = tamanho - 1; k >= 0; k--)
            valor = (valor << 8) | b[i + k];
        i += tamanho;
        return valor;
    }
}

/// <summary>Monta uma mensagem em protobuf, campo a campo (os poucos quadros que o leitor manda ao TikTok).</summary>
public sealed class ProtobufNovo
{
    private readonly MemoryStream _bytes = new();

    public ProtobufNovo Numero(int campo, ulong valor)
    {
        Varint((ulong)campo << 3);
        Varint(valor);
        return this;
    }

    public ProtobufNovo Bytes(int campo, ReadOnlySpan<byte> valor)
    {
        Varint(((ulong)campo << 3) | 2);
        Varint((ulong)valor.Length);
        _bytes.Write(valor);
        return this;
    }

    public ProtobufNovo Texto(int campo, string valor) => Bytes(campo, Encoding.UTF8.GetBytes(valor));

    public byte[] ParaBytes() => _bytes.ToArray();

    private void Varint(ulong valor)
    {
        while (valor >= 0x80)
        {
            _bytes.WriteByte((byte)(valor | 0x80));
            valor >>= 7;
        }
        _bytes.WriteByte((byte)valor);
    }
}
