using System.Text;

namespace TPVOne.LegacyAccess.Core.Data;

public sealed class PipeDelimitedRecordReader : IDisposable
{
    private readonly StreamReader _reader;
    private readonly char _delimiter;
    private bool _disposed;

    public PipeDelimitedRecordReader(Stream stream, Encoding encoding, char delimiter = '|')
    {
        _reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        _delimiter = delimiter;
    }

    public IReadOnlyList<string>? ReadHeader()
    {
        var fields = ReadRawRecord(expectedFieldCount: null);
        if (fields is null)
        {
            return null;
        }

        if (fields.Count > 0 && fields[^1].Length == 0)
        {
            return fields.Take(fields.Count - 1).ToArray();
        }

        return fields;
    }

    public IReadOnlyList<string>? ReadRecord(int expectedFieldCount)
    {
        while (true)
        {
            var fields = ReadRawRecord(expectedFieldCount);
            if (fields is null)
            {
                return null;
            }

            if (fields.Count == 0)
            {
                continue;
            }

            if (fields.Count == expectedFieldCount + 1 && fields[^1].Length == 0)
            {
                return fields.Take(expectedFieldCount).ToArray();
            }

            if (fields.Count != expectedFieldCount)
            {
                throw new InvalidDataException(
                    $"El registro tiene {fields.Count} campos y se esperaban {expectedFieldCount}.");
            }

            return fields;
        }
    }

    private IReadOnlyList<string>? ReadRawRecord(int? expectedFieldCount)
    {
        if (_reader.EndOfStream)
        {
            return null;
        }

        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;

        while (true)
        {
            var next = _reader.Read();
            if (next < 0)
            {
                if (!started && fields.Count == 0 && current.Length == 0)
                {
                    return null;
                }

                fields.Add(current.ToString());
                return fields;
            }

            started = true;
            var ch = (char)next;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    var peek = _reader.Peek();
                    if (peek == '"')
                    {
                        _reader.Read();
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch == '"' && current.Length == 0)
            {
                inQuotes = true;
                continue;
            }

            if (ch == _delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
                if (expectedFieldCount is not null &&
                    fields.Count == expectedFieldCount)
                {
                    ConsumeRecordTerminator();
                    return fields;
                }

                continue;
            }

            if (ch is '\r' or '\n')
            {
                if (ch == '\r' && _reader.Peek() == '\n')
                {
                    _reader.Read();
                }

                if (expectedFieldCount is not null && fields.Count < expectedFieldCount - 1)
                {
                    current.Append('\n');
                    continue;
                }

                if (current.Length == 0 && fields.Count == 0)
                {
                    return [];
                }

                if (current.Length > 0 || fields.Count > 0)
                {
                    fields.Add(current.ToString());
                }

                return fields;
            }

            current.Append(ch);
        }
    }

    private void ConsumeRecordTerminator()
    {
        var peek = _reader.Peek();
        if (peek == '\r')
        {
            _reader.Read();
            if (_reader.Peek() == '\n')
            {
                _reader.Read();
            }
        }
        else if (peek == '\n')
        {
            _reader.Read();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _reader.Dispose();
        _disposed = true;
    }
}
