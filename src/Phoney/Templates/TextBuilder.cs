using System.Buffers;
using System.Runtime.CompilerServices;

namespace Phoney.Templates;

/// <summary>
/// Builds one string from many parts on a stack buffer, renting from <see cref="ArrayPool{T}"/> only when it outgrows it. Templates write
/// nested templates and the parts of built-in functions straight into it, so a street address made of four templates is one string, not five.
/// </summary>
internal ref struct TextBuilder
{
    private char[]? _rented;
    private Span<char> _chars;
    private int _length;

    public TextBuilder(Span<char> initial)
    {
        _rented = null;
        _chars = initial;
        _length = 0;
    }

    /// <summary>The number of characters written so far.</summary>
    public int Length
    {
        readonly get => _length;
        set => _length = value; // only ever shortened, to drop a part that failed halfway
    }

    /// <summary>The characters written so far, which the caller may rewrite in place.</summary>
    public readonly Span<char> Written => _chars[.._length];

    /// <summary>Appends <paramref name="text"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(scoped ReadOnlySpan<char> text)
    {
        if (text.Length > _chars.Length - _length)
            Grow(text.Length);
        text.CopyTo(_chars[_length..]);
        _length += text.Length;
    }

    /// <summary>Appends <paramref name="c"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char c)
    {
        if (_length == _chars.Length)
            Grow(1);
        _chars[_length++] = c;
    }

    /// <summary>Reserves <paramref name="count"/> characters at the end and returns them for the caller to fill.</summary>
    public Span<char> AppendSpan(int count)
    {
        if (count > _chars.Length - _length)
            Grow(count);
        var span = _chars.Slice(_length, count);
        _length += count;
        return span;
    }

    /// <summary>The text written so far, as a string; returns the rented buffer, so the builder must not be used afterwards.</summary>
    public string ToStringAndDispose()
    {
        var text = _chars[.._length].ToString();
        Dispose();
        return text;
    }

    /// <summary>Returns the rented buffer, if any.</summary>
    public void Dispose()
    {
        var rented = _rented;
        this = default;
        if (rented is not null)
            ArrayPool<char>.Shared.Return(rented);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int needed)
    {
        var next = ArrayPool<char>.Shared.Rent(Math.Max(_length + needed, _chars.Length * 2));
        _chars[.._length].CopyTo(next);
        var old = _rented;
        _chars = _rented = next;
        if (old is not null)
            ArrayPool<char>.Shared.Return(old);
    }
}
