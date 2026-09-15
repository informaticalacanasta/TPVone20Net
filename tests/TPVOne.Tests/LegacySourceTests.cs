using System.Text;
using TPVOne.LegacyAccess.Core.Binary;
using TPVOne.LegacyAccess.Core.Data;
using TPVOne.LegacyAccess.Core.Exceptions;
using TPVOne.LegacyAccess.Core.Import;
using TPVOne.LegacyAccess.Core.Mapping;
using TPVOne.LegacyAccess.Core.Models;
using TPVOne.LegacyAccess.Core.Parsing;
using TPVOne.LegacyAccess.Core.Planning;
using TPVOne.LegacyAccess.Core.Schema;
using TPVOne.LegacyAccess.Core.Sources;
using TPVOne.LegacyAccess.Core.Utilities;

namespace TPVOne.Tests;

public sealed class LegacySourceTests
{
    private const string AlergenosTxt = """
        Nombre Tabla=alergenos
        Estructura:
        Nombre Campo=id_alergeno Tipo=dbLong  Entero largo, size=4
        Nombre Campo=DESCRIPCIO Tipo=dbText Texto, size=30
        Nombre Campo=FOTO_activado Tipo=dbLongBinary    Objeto OLE / binario largo
        Indices:
        Nombre Indice: id_alergeno
         Es Primary: False
         Es Unique: True
         Campos:
            - id_alergeno
        """;

    private readonly TxtTableStructureParser _parser = new();
    private readonly DaoToSqlTypeMapper _mapper = new();

    [Fact]
    public void Scanner_FindsTxtSchemaSources()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        File.WriteAllText(Path.Combine(directory.Path, "familias.txt"), TableTxt("familias"));

        var result = Scan(directory.Path);

        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(["alergenos", "familias"], result.Sources.Select(source => source.LogicalName));
    }

    [Fact]
    public void Scanner_AllowsTxtWithoutCsv()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);

        var result = Scan(directory.Path);

        Assert.Single(result.Sources);
        Assert.Null(result.Sources[0].DataSource);
        Assert.Empty(result.OrphanDataFiles);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Scanner_AssociatesCsvWhenPresent()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.csv"), "id_alergeno|DESCRIPCIO|FOTO_activado|\n");

        var result = Scan(directory.Path);

        Assert.NotNull(result.Sources[0].DataSource);
        Assert.EndsWith("alergenos.csv", result.Sources[0].DataSource!.Location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scanner_RejectsCsvWithoutTxt()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "a|b|\n");

        var result = Scan(directory.Path);

        Assert.Empty(result.Sources);
        Assert.Single(result.OrphanDataFiles);
        Assert.Contains(result.Errors, error => error.Contains("huérfano", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StructureParser_ReadsTableName()
    {
        Assert.Equal("alergenos", _parser.Parse(AlergenosTxt).Name);
    }

    [Fact]
    public void StructureParser_ReadsDbLong()
    {
        var column = _parser.Parse(AlergenosTxt).Columns[0];
        Assert.Equal("id_alergeno", column.Name);
        Assert.Equal("dbLong", column.SourceTypeName);
        Assert.Equal(4, column.Size);
    }

    [Fact]
    public void StructureParser_ReadsDbTextSize()
    {
        var column = _parser.Parse(AlergenosTxt).Columns[1];
        Assert.Equal("DESCRIPCIO", column.Name);
        Assert.Equal("dbText", column.SourceTypeName);
        Assert.Equal(30, column.Size);
    }

    [Fact]
    public void StructureParser_ReadsDbLongBinary()
    {
        var column = _parser.Parse(AlergenosTxt).Columns[2];
        Assert.Equal("FOTO_activado", column.Name);
        Assert.Equal("dbLongBinary", column.SourceTypeName);
    }

    [Fact]
    public void StructureParser_ReadsUniqueNonPrimaryIndex()
    {
        var index = Assert.Single(_parser.Parse(AlergenosTxt).Indexes);
        Assert.Equal("id_alergeno", index.Name);
        Assert.False(index.IsPrimaryKey);
        Assert.True(index.IsUnique);
        Assert.Equal("id_alergeno", index.Columns[0].Name);
    }

    [Fact]
    public void DaoMapper_DbLongMapsToInt()
    {
        Assert.Equal("int", _mapper.Map(Column("id_alergeno", "dbLong", 4)).ToSql());
    }

    [Fact]
    public void DaoMapper_DbText30MapsToNvarchar30()
    {
        Assert.Equal("nvarchar(30)", _mapper.Map(Column("DESCRIPCIO", "dbText", 30)).ToSql());
    }

    [Fact]
    public void DaoMapper_DbLongBinaryMapsToVarbinaryMax()
    {
        Assert.Equal("varbinary(max)", _mapper.Map(Column("FOTO_activado", "dbLongBinary")).ToSql());
    }

    [Fact]
    public void DaoMapper_DbMemoMapsToNvarcharMax()
    {
        Assert.Equal("nvarchar(max)", _mapper.Map(Column("COMPOSICIO", "dbMemo")).ToSql());
    }

    [Fact]
    public void Converter_DbMemoClrTypeIsString()
    {
        Assert.Equal(typeof(string), new LegacyValueConverter().GetClrType(Column("COMPOSICIO", "dbMemo")));
    }

    [Fact]
    public void Converter_DbTextClrTypeRemainsString()
    {
        Assert.Equal(typeof(string), new LegacyValueConverter().GetClrType(Column("DESCRIPCIO", "dbText", 30)));
    }

    [Fact]
    public void Converter_DbTextDoesNotDecodeBase64()
    {
        var result = new LegacyValueConverter().ConvertValue("SG9sYQ==", Column("DESCRIPCIO", "dbText", 30));
        Assert.Equal("SG9sYQ==", result);
    }

    [Fact]
    public void Converter_DbMemoDecodesUtf16LeBase64ToText()
    {
        var original = "Ingredientes";
        var result = new LegacyValueConverter().ConvertValue(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(original)),
            Column("COMPOSICIO", "dbMemo"));
        Assert.Equal(original, result);
    }

    [Fact]
    public void Converter_DbMemoPreservesSpanishCharacters()
    {
        var original = "jamón de york untado con paté";
        var result = new LegacyValueConverter().ConvertValue(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(original)),
            Column("COMPOSICIO", "dbMemo"));
        Assert.Equal(original, result);
    }

    [Fact]
    public void Converter_DbMemoPreservesLineBreaks()
    {
        var original = "PAN DE CENTENO\r\n\r\nPan de molde de CENTENO.\r\n\r\nIdeal con platos de caza.";
        var result = new LegacyValueConverter().ConvertValue(
            Convert.ToBase64String(Encoding.Unicode.GetBytes(original)),
            Column("COMPOSICIO", "dbMemo"));
        Assert.Equal(original, result);
    }

    [Fact]
    public void Converter_DbMemoEmptyIsDbNull()
    {
        Assert.Same(DBNull.Value, new LegacyValueConverter().ConvertValue("", Column("COMPOSICIO", "dbMemo")));
    }

    [Fact]
    public void Converter_DbMemoInvalidBase64Throws()
    {
        var exception = Assert.Throws<DataImportException>(
            () => new LegacyValueConverter().ConvertValue("NO_ES_BASE64", Column("COMPOSICIO", "dbMemo")));
        Assert.Contains("COMPOSICIO", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dbMemo", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NO_ES_BASE64", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Converter_DbMemoOddLengthLegacyUtf16Le_DecodesMissingHighByte()
    {
        var encoded = Convert.ToBase64String([0x41, 0x00, 0x42]);
        var result = new LegacyValueConverter().ConvertValue(encoded, Column("COMPOSICIO", "dbMemo"));
        Assert.Equal("AB", result);
    }

    [Fact]
    public void Converter_DbLongBinaryStillReturnsBytes()
    {
        var payload = "BMPDATA"u8.ToArray();
        var result = new LegacyValueConverter().ConvertValue(
            Convert.ToBase64String(payload),
            Column("FOTO_activado", "dbLongBinary"));
        Assert.Equal(payload, Assert.IsType<byte[]>(result));
    }

    [Fact]
    public async Task SchemaOnly_CreatesTableWithoutCsv()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        var sql = new FakeSql();
        var copy = new FakeCopy();
        var result = await Import(directory.Path, sql, copy);

        Assert.True(sql.Created.ContainsKey("alergenos"));
        Assert.Equal(0, copy.Calls);
        var table = Assert.Single(result.Tables);
        Assert.Equal(SchemaStatus.Created, table.SchemaStatus);
        Assert.Equal("int", sql.Created["alergenos"].Columns[0].SourceTypeName == "dbLong"
            ? _mapper.Map(sql.Created["alergenos"].Columns[0]).ToSql()
            : null);
    }

    [Fact]
    public async Task SchemaOnly_CreatesIndexesWithoutCsv()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        var sql = new FakeSql();
        await Import(directory.Path, sql, new FakeCopy());

        Assert.Contains("alergenos", sql.IndexedTables);
    }

    [Fact]
    public async Task SchemaOnly_IsSuccessWithoutData()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        var result = await Import(directory.Path, new FakeSql(), new FakeCopy());
        var table = Assert.Single(result.Tables);

        Assert.Equal(SchemaStatus.Created, table.SchemaStatus);
        Assert.Equal(DataStatus.NotAvailable, table.DataStatus);
        Assert.Equal(0, table.ImportedRowCount);
        Assert.Equal(ImportStatus.Success, table.Status);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task MissingSqlTable_CreatesAndImportsCsv()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var sql = new FakeSql();
        var copy = new FakeCopy();
        var interaction = FakeInteraction.Unexpected();
        var result = await Import(directory.Path, sql, copy, interaction: interaction);

        Assert.Empty(interaction.AskedTables);
        Assert.Empty(sql.Dropped);
        Assert.Equal(1, sql.CreateCalls);
        Assert.Equal(1, copy.Calls);
        Assert.Contains("__s", copy.Destinations[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("productos", sql.IndexedTables);
        Assert.Equal(SchemaStatus.Created, result.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.Imported, result.Tables[0].DataStatus);
        Assert.Equal(1, result.Tables[0].ImportedRowCount);
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public async Task Utf16LegacyBinaryColumn_IsCreatedAsNvarcharWithoutNameHardcode()
    {
        using var directory = new TempDir();
        File.WriteAllText(
            Path.Combine(directory.Path, "articulos.txt"),
            """
            Nombre Tabla=articulos
            Estructura:
            Nombre Campo=id Tipo=dbLong Entero largo, size=4
            Nombre Campo=COMPOSICIO Tipo=dbLongBinary
            Nombre Campo=FOTO Tipo=dbLongBinary
            """);
        var text = Convert.ToBase64String(Encoding.Unicode.GetBytes("PAN DE CENTENO ALEMAN"));
        var foto = Convert.ToBase64String(MinimalBmp());
        File.WriteAllText(
            Path.Combine(directory.Path, "articulos.csv"),
            $"id|COMPOSICIO|FOTO|\n1|{text}|{foto}|\n");
        var sql = new FakeSql();
        var interaction = FakeInteraction.Unexpected();
        var result = await Import(directory.Path, sql, new FakeCopy(), interaction: interaction);

        Assert.True(result.IsSuccessful);
        Assert.Equal("dbMemo", sql.Created["articulos"].Columns[1].SourceTypeName);
        Assert.Equal("nvarchar(max)", _mapper.Map(sql.Created["articulos"].Columns[1]).ToSql());
        Assert.Equal("dbLongBinary", sql.Created["articulos"].Columns[2].SourceTypeName);
        Assert.Equal("varbinary(max)", _mapper.Map(sql.Created["articulos"].Columns[2]).ToSql());
        Assert.Contains(
            interaction.Messages,
            message => message.Contains("COMPOSICIO", StringComparison.OrdinalIgnoreCase)
                && message.Contains("nvarchar(max)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            interaction.Messages,
            message => message.Contains("FOTO", StringComparison.OrdinalIgnoreCase)
                && message.Contains("varbinary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reader_Utf16Memo_ExposesStringAndFotoExposesBytes()
    {
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "articulos.csv");
        var text = Convert.ToBase64String(Encoding.Unicode.GetBytes("Hola"));
        File.WriteAllText(path, $"COMPOSICIO|FOTO|\n{text}||\n");
        var schema = new LegacyTableSchema(
            "articulos",
            [
                new("COMPOSICIO", "dbMemo", null, null, null, 0, true, false),
                new("FOTO", "dbLongBinary", null, null, null, 1, true, false)
            ],
            []);
        using var reader = new CsvLegacyDataSource(path).OpenReader(schema);

        Assert.Equal(typeof(string), reader.GetFieldType(0));
        Assert.Equal(typeof(byte[]), reader.GetFieldType(1));
        Assert.True(reader.Read());
        Assert.Equal("Hola", reader.GetString(0));
    }

    [Fact]
    public void CsvHeader_IgnoresTrailingEmptyField()
    {
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "t.csv");
        File.WriteAllText(path, "id_alergeno|DESCRIPCIO|FOTO_activado|\n");
        var header = new CsvLegacyDataSource(path).ReadHeader();

        Assert.Equal(["id_alergeno", "DESCRIPCIO", "FOTO_activado"], header);
    }

    [Fact]
    public void CsvHeader_MatchesStructure()
    {
        var schema = _parser.Parse(AlergenosTxt);
        CsvHeaderValidator.EnsureMatches(
            ["id_alergeno", "DESCRIPCIO", "FOTO_activado"],
            schema);
    }

    [Fact]
    public void CsvHeader_MismatchDoesNotInvalidateParsedSchema()
    {
        var schema = _parser.Parse(AlergenosTxt);
        Assert.Equal(3, schema.Columns.Count);
        Assert.Throws<CsvHeaderMismatchException>(() =>
            CsvHeaderValidator.EnsureMatches(["id_alergeno", "OTRO"], schema));
        Assert.Equal("alergenos", schema.Name);
        Assert.Equal("dbLong", schema.Columns[0].SourceTypeName);
    }

    [Fact]
    public void CsvReader_Windows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "nombres.csv");
        var encoding = Encoding.GetEncoding("windows-1252");
        File.WriteAllBytes(path, encoding.GetBytes("nombre|\nEspaña|\n"));
        var source = new CsvLegacyDataSource(path);
        var schema = new LegacyTableSchema(
            "nombres",
            [Column("nombre", "dbText", 30)],
            []);
        using var reader = source.OpenReader(schema);

        Assert.True(reader.Read());
        Assert.Equal("España", reader.GetString(0));
    }

    [Fact]
    public void BinaryDecoder_DecodesBase64()
    {
        var original = "BMPDATA"u8.ToArray();
        var encoded = Convert.ToBase64String(original);
        Assert.Equal(original, Base64Decoder.Decode(encoded));
    }

    [Fact]
    public void BinaryDecoder_ExtractsValidBmp()
    {
        var bmp = MinimalBmp();
        var wrapped = new byte[20 + bmp.Length];
        wrapped[0] = 0x6C;
        wrapped[1] = 0x74;
        Buffer.BlockCopy(bmp, 0, wrapped, 20, bmp.Length);

        var extracted = LegacyBinaryPayloadInterpreter.Interpret(wrapped);

        Assert.Equal(bmp, extracted);
        Assert.Equal(0x42, extracted[0]);
        Assert.Equal(0x4D, extracted[1]);
    }

    [Fact]
    public void BinaryDecoder_DoesNotCorruptGenericBinary()
    {
        var payload = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
        Assert.Equal(payload, LegacyBinaryPayloadInterpreter.Interpret(payload));
    }

    [Fact]
    public void StructureHash_IsStable()
    {
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "alergenos.txt");
        File.WriteAllText(path, AlergenosTxt);
        Assert.Equal(
            FileHashCalculator.CalculateSha256(path),
            FileHashCalculator.CalculateSha256(path));
    }

    [Fact]
    public void DataHash_IsStable()
    {
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "alergenos.csv");
        File.WriteAllText(path, "id_alergeno|DESCRIPCIO|FOTO_activado|\n1|GLUTEN||\n");
        Assert.Equal(
            FileHashCalculator.CalculateSha256(path),
            FileHashCalculator.CalculateSha256(path));
    }

    [Fact]
    public void StructureHash_ChangesWhenTxtChanges()
    {
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "alergenos.txt");
        File.WriteAllText(path, AlergenosTxt);
        var first = FileHashCalculator.CalculateSha256(path);
        File.WriteAllText(path, AlergenosTxt + "\n");
        Assert.NotEqual(first, FileHashCalculator.CalculateSha256(path));
    }

    [Fact]
    public void DataHash_ChangesWhenCsvChanges()
    {
        using var directory = new TempDir();
        var path = Path.Combine(directory.Path, "alergenos.csv");
        File.WriteAllText(path, "id_alergeno|DESCRIPCIO|FOTO_activado|\n1|GLUTEN||\n");
        var first = FileHashCalculator.CalculateSha256(path);
        File.WriteAllText(path, "id_alergeno|DESCRIPCIO|FOTO_activado|\n2|HUEVOS||\n");
        Assert.NotEqual(first, FileHashCalculator.CalculateSha256(path));
    }

    [Fact]
    public async Task CsvAppearsLater_AsksOverwriteAndRecreatesWhenAccepted()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        var sql = new FakeSql();
        var copy = new FakeCopy();
        await Import(directory.Path, sql, copy);
        Assert.Equal(1, sql.CreateCalls);
        Assert.Equal(0, copy.Calls);

        File.WriteAllText(
            Path.Combine(directory.Path, "productos.csv"),
            "id|nombre|\n1|pan|\n");
        sql.Existing["productos"] = ToSql(sql.Created["productos"]);
        sql.Created.Clear();
        var interaction = FakeInteraction.With(true);
        await Import(directory.Path, sql, copy, interaction: interaction);

        Assert.Equal(["productos"], interaction.AskedTables);
        Assert.DoesNotContain("productos", sql.Dropped, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(sql.Dropped, name => name.Contains("__b", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, sql.CreateCalls);
        Assert.Equal(1, copy.Calls);
        Assert.Contains("productos", sql.Created.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(SchemaStatus.Replaced, copy.LastResult?.SchemaStatus);
    }

    [Fact]
    public async Task ExistingTable_UserDeclinesOverwrite_KeepsTable()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var sql = new FakeSql();
        var copy = new FakeCopy();
        var history = new FakeHistory();
        var first = await Import(directory.Path, sql, copy, history);
        sql.Existing["productos"] = ToSql(sql.Created["productos"]);
        sql.RowCounts["productos"] = 1;
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|leche|\n");
        var interaction = FakeInteraction.With(false);
        var second = await Import(directory.Path, sql, copy, history, interaction);

        Assert.Equal(DataStatus.Imported, first.Tables[0].DataStatus);
        Assert.Equal(["productos"], interaction.AskedTables);
        Assert.Empty(sql.Dropped);
        Assert.Equal(1, sql.CreateCalls);
        Assert.Equal(1, copy.Calls);
        Assert.Equal(SchemaStatus.AlreadyExists, second.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.NotProcessed, second.Tables[0].DataStatus);
        Assert.Equal(ImportStatus.SkippedByUser, second.Tables[0].Status);
        Assert.Contains(
            interaction.Messages,
            message => message.Contains("Se conserva la tabla 'productos'", StringComparison.Ordinal));
        Assert.True(second.IsSuccessful);
    }

    [Fact]
    public async Task ExistingTable_UserAcceptsOverwrite_ReplacesViaStaging()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var sql = new FakeSql();
        var copy = new FakeCopy();
        var history = new FakeHistory();
        await Import(directory.Path, sql, copy, history);
        sql.Existing["productos"] = ToSql(sql.Created["productos"]);
        sql.RowCounts["productos"] = 1;
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|leche|\n");
        var interaction = FakeInteraction.With(true);
        var overwritten = await Import(directory.Path, sql, copy, history, interaction);

        Assert.DoesNotContain("productos", sql.Dropped, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, sql.CreateCalls);
        Assert.Equal(2, copy.Calls);
        Assert.Contains("productos", sql.Created.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(SchemaStatus.Replaced, overwritten.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.Imported, overwritten.Tables[0].DataStatus);
        Assert.Equal(ImportStatus.Success, overwritten.Tables[0].Status);
    }

    [Fact]
    public async Task ExistingTable_SameHash_IsSkipped()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var sql = new FakeSql();
        var copy = new FakeCopy();
        var history = new FakeHistory();
        await Import(directory.Path, sql, copy, history);
        sql.Existing["productos"] = ToSql(sql.Created["productos"]);
        sql.RowCounts["productos"] = 1;
        Assert.True(await history.WasDataImportedAsync(
            "productos",
            FileHashCalculator.CalculateSha256(Path.Combine(directory.Path, "productos.csv")),
            CancellationToken.None));

        var skipped = await Import(
            directory.Path,
            sql,
            copy,
            history,
            FakeInteraction.Unexpected());

        Assert.Equal(1, copy.Calls);
        Assert.Equal(DataStatus.AlreadyImported, skipped.Tables[0].DataStatus);
        Assert.Equal(ImportStatus.SkippedAlreadyImported, skipped.Tables[0].Status);
        Assert.Equal(SchemaStatus.AlreadyExists, skipped.Tables[0].SchemaStatus);
    }

    [Fact]
    public async Task ExistingTable_SameHash_ForceImportReimports()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var sql = new FakeSql();
        var copy = new FakeCopy();
        var history = new FakeHistory();
        await Import(directory.Path, sql, copy, history);
        sql.Existing["productos"] = ToSql(sql.Created["productos"]);
        sql.RowCounts["productos"] = 1;

        var overwritten = await Import(
            directory.Path,
            sql,
            copy,
            history,
            FakeInteraction.With(true),
            forceImport: true);

        Assert.Equal(2, copy.Calls);
        Assert.Equal(DataStatus.Imported, overwritten.Tables[0].DataStatus);
        Assert.Equal(SchemaStatus.Replaced, overwritten.Tables[0].SchemaStatus);
    }

    [Fact]
    public async Task BulkFailure_LeavesOriginalTable()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var original = ToSql(_parser.Parse(TableTxt("productos")));
        var sql = new FakeSql
        {
            Existing = { ["productos"] = original },
            RowCounts = { ["productos"] = 4 }
        };
        var copy = new FakeCopy
        {
            ThrowOnCopy = new DataImportException("Error importando datos en staging de 'productos'.")
        };
        var result = await Import(directory.Path, sql, copy, interaction: FakeInteraction.With(true));

        Assert.Equal(ImportStatus.Failed, result.Tables[0].Status);
        Assert.Equal(DataStatus.Failed, result.Tables[0].DataStatus);
        Assert.Equal(original, sql.Existing["productos"]);
        Assert.Equal(4, sql.RowCounts["productos"]);
        Assert.DoesNotContain("productos", sql.Dropped, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("staging", result.Tables[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndexFailure_LeavesOriginalTable()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var original = ToSql(_parser.Parse(TableTxt("productos")));
        var sql = new FakeSql
        {
            Existing = { ["productos"] = original },
            RowCounts = { ["productos"] = 4 },
            ThrowOnCreateIndexes = new InvalidOperationException("duplicate key")
        };
        var result = await Import(
            directory.Path,
            sql,
            new FakeCopy(),
            interaction: FakeInteraction.With(true));

        Assert.Equal(ImportStatus.Failed, result.Tables[0].Status);
        Assert.Equal(original, sql.Existing["productos"]);
        Assert.DoesNotContain("productos", sql.Dropped, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("índices", result.Tables[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwapFailure_LeavesOriginalTable()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var original = ToSql(_parser.Parse(TableTxt("productos")));
        var sql = new FakeSql
        {
            Existing = { ["productos"] = original },
            RowCounts = { ["productos"] = 4 },
            ThrowOnSwap = true
        };
        var result = await Import(
            directory.Path,
            sql,
            new FakeCopy(),
            interaction: FakeInteraction.With(true));

        Assert.Equal(ImportStatus.Failed, result.Tables[0].Status);
        Assert.Equal(original, sql.Existing["productos"]);
        Assert.DoesNotContain("productos", sql.Dropped, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("sustituyendo", result.Tables[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_LeavesOriginalTable()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|nombre|\n1|pan|\n");
        var original = ToSql(_parser.Parse(TableTxt("productos")));
        var sql = new FakeSql
        {
            Existing = { ["productos"] = original },
            RowCounts = { ["productos"] = 4 }
        };
        var copy = new FakeCopy { ThrowOnCopy = new OperationCanceledException() };
        var result = await Import(directory.Path, sql, copy, interaction: FakeInteraction.With(true));

        Assert.Equal(ImportStatus.Failed, result.Tables[0].Status);
        Assert.NotEqual(DataStatus.Imported, result.Tables[0].DataStatus);
        Assert.Equal(original, sql.Existing["productos"]);
        Assert.DoesNotContain("productos", sql.Dropped, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingIncompatibleSchema_OverwriteRecreatesFromTxt()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        File.WriteAllText(
            Path.Combine(directory.Path, "alergenos.csv"),
            "id_alergeno|DESCRIPCIO|FOTO_activado|\n1|GLUTEN||\n");
        var sql = new FakeSql
        {
            Existing =
            {
                ["alergenos"] = new SqlTableSchema(
                    "alergenos",
                    [
                        new("id_alergeno", new("int"), true, false, 0),
                        new("DESCRIPCIO", new("int"), true, false, 1)
                    ],
                    [])
            },
            RowCounts = { ["alergenos"] = 8 }
        };
        var copy = new FakeCopy();
        var result = await Import(
            directory.Path,
            sql,
            copy,
            interaction: FakeInteraction.With(true));

        Assert.False(sql.Compared);
        Assert.DoesNotContain("alergenos", sql.Dropped, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, sql.CreateCalls);
        Assert.Equal(1, copy.Calls);
        Assert.Equal(SchemaStatus.Replaced, result.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.Imported, result.Tables[0].DataStatus);
        Assert.Equal(3, sql.Created["alergenos"].Columns.Count);
    }

    [Fact]
    public async Task ExistingIncompatibleSchema_DeclineKeepsOriginal()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        File.WriteAllText(
            Path.Combine(directory.Path, "alergenos.csv"),
            "id_alergeno|DESCRIPCIO|FOTO_activado|\n1|GLUTEN||\n");
        var original = new SqlTableSchema(
            "alergenos",
            [
                new("id_alergeno", new("int"), true, false, 0),
                new("DESCRIPCIO", new("int"), true, false, 1)
            ],
            []);
        var sql = new FakeSql
        {
            Existing = { ["alergenos"] = original },
            RowCounts = { ["alergenos"] = 8 }
        };
        var copy = new FakeCopy();
        var result = await Import(
            directory.Path,
            sql,
            copy,
            interaction: FakeInteraction.With(false));

        Assert.Empty(sql.Dropped);
        Assert.Equal(0, sql.CreateCalls);
        Assert.Equal(0, copy.Calls);
        Assert.False(sql.Compared);
        Assert.Equal(original, sql.Existing["alergenos"]);
        Assert.Equal(8, sql.RowCounts["alergenos"]);
        Assert.Equal(ImportStatus.SkippedByUser, result.Tables[0].Status);
    }

    [Fact]
    public async Task ExistingTable_MissingCsv_DoesNotAskOrDrop()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        var schema = _parser.Parse(AlergenosTxt);
        var interaction = FakeInteraction.Unexpected();
        var sql = new FakeSql
        {
            Existing = { ["alergenos"] = ToSql(schema) },
            RowCounts = { ["alergenos"] = 4 }
        };
        var copy = new FakeCopy();
        var result = await Import(directory.Path, sql, copy, interaction: interaction);

        Assert.Empty(interaction.AskedTables);
        Assert.Empty(sql.Dropped);
        Assert.Equal(0, copy.Calls);
        Assert.Equal(DataStatus.NotAvailable, result.Tables[0].DataStatus);
        Assert.Equal(4, sql.RowCounts["alergenos"]);
    }

    [Fact]
    public async Task ExistingTable_InvalidCsvHeader_DoesNotAskOrDrop()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "productos.txt"), TableTxt("productos"));
        File.WriteAllText(Path.Combine(directory.Path, "productos.csv"), "id|otro|\n1|x|\n");
        var sql = new FakeSql
        {
            Existing = { ["productos"] = ToSql(_parser.Parse(TableTxt("productos"))) },
            RowCounts = { ["productos"] = 3 }
        };
        var interaction = FakeInteraction.Unexpected();
        var copy = new FakeCopy();
        var result = await Import(directory.Path, sql, copy, interaction: interaction);

        Assert.Empty(interaction.AskedTables);
        Assert.Empty(sql.Dropped);
        Assert.Equal(0, copy.Calls);
        Assert.Equal(DataStatus.Failed, result.Tables[0].DataStatus);
        Assert.Equal(3, sql.RowCounts["productos"]);
        Assert.Contains("cabecera", result.Tables[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProtectedInfrastructureTable_IsNotDropped()
    {
        using var directory = new TempDir();
        File.WriteAllText(
            Path.Combine(directory.Path, "SchemaMigrations.txt"),
            TableTxt("SchemaMigrations"));
        File.WriteAllText(
            Path.Combine(directory.Path, "SchemaMigrations.csv"),
            "id|nombre|\n1|x|\n");
        var interaction = FakeInteraction.Unexpected();
        var sql = new FakeSql
        {
            Existing =
            {
                ["SchemaMigrations"] = ToSql(_parser.Parse(TableTxt("SchemaMigrations")))
            },
            RowCounts = { ["SchemaMigrations"] = 5 }
        };
        var copy = new FakeCopy();
        var result = await Import(directory.Path, sql, copy, interaction: interaction);

        Assert.Empty(interaction.AskedTables);
        Assert.Empty(sql.Dropped);
        Assert.Equal(0, copy.Calls);
        Assert.Equal(SchemaStatus.Failed, result.Tables[0].SchemaStatus);
        Assert.Contains("infraestructura interna", result.Tables[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, sql.RowCounts["SchemaMigrations"]);
    }

    [Fact]
    public async Task ExistingSqlTable_IsComparedEvenWithoutCsv()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        var schema = _parser.Parse(AlergenosTxt);
        var sql = new FakeSql
        {
            Existing =
            {
                ["alergenos"] = ToSql(schema)
            }
        };
        var result = await Import(directory.Path, sql, new FakeCopy());

        Assert.Equal(0, sql.CreateCalls);
        Assert.True(sql.Compared);
        Assert.Equal(SchemaStatus.AlreadyExists, result.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.NotAvailable, result.Tables[0].DataStatus);
    }

    [Fact]
    public async Task ExistingIncompatibleSchema_BlocksEvenWithoutCsv()
    {
        using var directory = new TempDir();
        File.WriteAllText(Path.Combine(directory.Path, "alergenos.txt"), AlergenosTxt);
        var sql = new FakeSql
        {
            Existing =
            {
                ["alergenos"] = new SqlTableSchema(
                    "alergenos",
                    [
                        new("id_alergeno", new("int"), true, false, 0),
                        new("DESCRIPCIO", new("int"), true, false, 1),
                        new("FOTO_activado", new("varbinary", -1), true, false, 2)
                    ],
                    [])
            }
        };
        var copy = new FakeCopy();
        var result = await Import(directory.Path, sql, copy);

        Assert.Equal(SchemaStatus.Conflict, result.Tables[0].SchemaStatus);
        Assert.Equal(DataStatus.NotProcessed, result.Tables[0].DataStatus);
        Assert.Equal(ImportStatus.Conflict, result.Tables[0].Status);
        Assert.Equal(0, copy.Calls);
        Assert.False(result.IsSuccessful);
    }

    [Fact]
    public void UniqueIndex_DoesNotBecomePrimaryKey()
    {
        var schema = _parser.Parse(AlergenosTxt);
        var sql = new LegacySqlDdlBuilder(_mapper).BuildCreateIndexSql(schema, "alergenos");
        var statement = Assert.Single(sql);
        Assert.Contains("UNIQUE INDEX", statement, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIMARY KEY", statement, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LogicalName_MismatchIsError()
    {
        Assert.Throws<SchemaNameMismatchException>(() =>
            LegacyLogicalNameValidator.EnsureMatchesFile("alergenos.txt", "productos"));
    }

    private static LegacyImportCoordinator Coordinator(
        string sourceDirectory,
        FakeSql sql,
        FakeCopy copy,
        FakeHistory? history = null,
        ILegacyImportInteraction? interaction = null,
        bool forceImport = false)
    {
        var options = new LegacyImportOptions
        {
            SourceDirectory = sourceDirectory,
            ForceImport = forceImport
        };
        var mapper = new DaoToSqlTypeMapper();
        copy.History = history ?? new FakeHistory();
        copy.CoordinatorResults = null;
        return new LegacyImportCoordinator(
            new FileLegacyTableSourceScanner(options),
            mapper,
            new SchemaComparisonService(mapper),
            sql,
            copy,
            copy.History,
            options,
            interaction ?? FakeInteraction.Unexpected());
    }

    private static async Task<PipelineResult> Import(
        string sourceDirectory,
        FakeSql sql,
        FakeCopy copy,
        FakeHistory? history = null,
        ILegacyImportInteraction? interaction = null,
        bool forceImport = false)
    {
        var coordinator = Coordinator(
            sourceDirectory,
            sql,
            copy,
            history,
            interaction,
            forceImport);
        var result = await coordinator.ImportAsync();
        copy.LastResult = result.Tables.LastOrDefault();
        return result;
    }

    private static LegacyDiscoveryResult Scan(string directory)
    {
        return new FileLegacyTableSourceScanner(new LegacyImportOptions())
            .Discover(directory);
    }

    private static LegacyColumnSchema Column(string name, string type, int? size = null)
    {
        return new(name, type, size, null, null, 0, true, false);
    }

    private static string TableTxt(string name)
    {
        return $"""
            Nombre Tabla={name}
            Estructura:
            Nombre Campo=id Tipo=dbLong Entero largo, size=4
            Nombre Campo=nombre Tipo=dbText Texto, size=30
            """;
    }

    private static SqlTableSchema ToSql(LegacyTableSchema schema)
    {
        var mapper = new DaoToSqlTypeMapper();
        return new(
            schema.Name,
            schema.Columns.Select(column =>
                new SqlColumnSchema(
                    column.Name,
                    mapper.Map(column),
                    column.IsNullable,
                    column.IsAutoIncrement,
                    column.Ordinal)).ToArray(),
            schema.Indexes);
    }

    private static byte[] MinimalBmp()
    {
        var data = new byte[58];
        data[0] = 0x42;
        data[1] = 0x4D;
        BitConverter.GetBytes(58).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(1).CopyTo(data, 18);
        BitConverter.GetBytes(1).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        return data;
    }

    private sealed class FakeInteraction : ILegacyImportInteraction
    {
        private readonly Queue<bool> _answers;
        private readonly bool _throwIfAsked;

        private FakeInteraction(bool throwIfAsked, params bool[] answers)
        {
            _throwIfAsked = throwIfAsked;
            _answers = new Queue<bool>(answers);
        }

        public List<string> Messages { get; } = [];

        public List<string> AskedTables { get; } = [];

        public static FakeInteraction Unexpected()
        {
            return new(throwIfAsked: true);
        }

        public static FakeInteraction With(params bool[] answers)
        {
            return new(throwIfAsked: false, answers);
        }

        public void Inform(string message)
        {
            Messages.Add(message);
        }

        public bool ConfirmOverwrite(string tableName)
        {
            AskedTables.Add(tableName);
            if (_throwIfAsked || _answers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No se esperaba confirmación para '{tableName}'.");
            }

            return _answers.Dequeue();
        }
    }

    private sealed class FakeSql : ILegacySqlSchemaPort
    {
        public Dictionary<string, LegacyTableSchema> Created { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SqlTableSchema> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IndexedTables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> RowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Dropped { get; } = [];
        public int CreateCalls { get; private set; }
        public bool Compared { get; private set; }
        public bool ThrowOnSwap { get; set; }
        public Exception? ThrowOnCreateIndexes { get; set; }

        public Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
        {
            return Task.FromResult(Existing.ContainsKey(tableName) || Created.ContainsKey(tableName));
        }

        public Task DropTableAsync(string tableName, CancellationToken cancellationToken)
        {
            Dropped.Add(tableName);
            Existing.Remove(tableName);
            Created.Remove(tableName);
            IndexedTables.Remove(tableName);
            RowCounts[tableName] = 0;
            return Task.CompletedTask;
        }

        public Task CreateTableAsync(
            LegacyTableSchema table,
            string destinationTableName,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            Created[destinationTableName] = table;
            RowCounts[destinationTableName] = 0;
            return Task.CompletedTask;
        }

        public Task CreateIndexesAsync(
            LegacyTableSchema table,
            string destinationTableName,
            string? uniqueNameSuffix,
            CancellationToken cancellationToken)
        {
            if (ThrowOnCreateIndexes is not null)
            {
                throw ThrowOnCreateIndexes;
            }

            IndexedTables.Add(destinationTableName);
            return Task.CompletedTask;
        }

        public Task SwapAtomicAsync(
            string destinationTableName,
            string stagingTableName,
            string? backupTableName,
            IReadOnlyList<LegacyIndexSchema> indexes,
            string? uniqueNameSuffix,
            CancellationToken cancellationToken)
        {
            if (ThrowOnSwap)
            {
                throw new InvalidOperationException("swap failed");
            }

            if (backupTableName is not null)
            {
                Transfer(destinationTableName, backupTableName);
            }

            Transfer(stagingTableName, destinationTableName);
            return Task.CompletedTask;
        }

        public Task<SqlTableSchema> ReadTableSchemaAsync(string tableName, CancellationToken cancellationToken)
        {
            Compared = true;
            return Task.FromResult(Existing[tableName]);
        }

        public Task<long> CountRowsAsync(string tableName, CancellationToken cancellationToken)
        {
            if (Created.TryGetValue(tableName, out var created) && created.RowCount > 0)
            {
                return Task.FromResult(created.RowCount);
            }

            return Task.FromResult(RowCounts.GetValueOrDefault(tableName));
        }

        private void Transfer(string from, string to)
        {
            if (Created.TryGetValue(from, out var created))
            {
                Created[to] = created;
                Created.Remove(from);
            }

            if (Existing.TryGetValue(from, out var existing))
            {
                Existing[to] = existing with { Name = to };
                Existing.Remove(from);
            }

            if (IndexedTables.Remove(from))
            {
                IndexedTables.Add(to);
            }

            RowCounts[to] = RowCounts.GetValueOrDefault(from);
            RowCounts.Remove(from);
        }
    }

    private sealed class FakeCopy : ILegacyDataCopyPort
    {
        public int Calls { get; private set; }
        public List<string> Destinations { get; } = [];
        public TableImportResult? LastResult { get; set; }
        public FakeHistory History { get; set; } = new();
        public object? CoordinatorResults { get; set; }
        public Exception? ThrowOnCopy { get; set; }
        public int FailAfterCalls { get; set; } = 1;
        public int LastBatchCount { get; private set; } = 1;

        public Task<LegacyCopyResult> CopyAsync(
            LegacyTableSchema schema,
            ILegacyDataSource dataSource,
            string destinationTableName,
            long expectedRowCount,
            IProgress<LegacyCopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Destinations.Add(destinationTableName);
            if (ThrowOnCopy is not null && Calls >= FailAfterCalls)
            {
                throw ThrowOnCopy;
            }

            progress?.Report(new LegacyCopyProgress(expectedRowCount, expectedRowCount, LastBatchCount));
            return Task.FromResult(new LegacyCopyResult(expectedRowCount, LastBatchCount));
        }
    }

    private sealed class FakeHistory : ILegacyImportHistoryPort
    {
        public HashSet<(string Table, string Hash)> Imported { get; } = [];

        public Task<bool> WasDataImportedAsync(
            string tableName,
            string dataHash,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Imported.Contains((tableName, dataHash)));
        }

        public Task<long> RecordStartAsync(TableImportResult draft, CancellationToken cancellationToken)
        {
            return Task.FromResult(1L);
        }

        public Task RecordFinishAsync(
            long id,
            TableImportResult result,
            CancellationToken cancellationToken)
        {
            if (result.DataStatus == DataStatus.Imported && result.DataHash is not null)
            {
                Imported.Add((result.TableName, result.DataHash));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = Directory.CreateTempSubdirectory().FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
